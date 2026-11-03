using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Services
{
    public interface IFcmService
    {
        Task RegisterTokenAsync(int employeeId, RegisterFcmTokenRequest request);
        Task SendNotificationToEmployeeAsync(int employeeId, string title, string body, string type);
        Task SendNotificationToMultipleEmployeesAsync(IEnumerable<int> employeeIds, string title, string body, string type);
        Task<NotificationPageDto> GetMyNotificationsAsync(int employeeId, int page, int pageSize);
        Task<bool> MarkAsReadAsync(int employeeId, long notificationId);
    }

    public class FcmService : IFcmService
    {
        private static readonly object FirebaseLock = new();
        private static FirebaseApp? _firebaseApp;
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FcmService> _logger;

        public FcmService(
            AppDbContext context,
            IConfiguration configuration,
            ILogger<FcmService> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task RegisterTokenAsync(int employeeId, RegisterFcmTokenRequest request)
        {
            string token = request.FcmToken.Trim();
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("FCM token is required.", nameof(request));

            bool employeeExists = await _context.Employees.AnyAsync(x => x.Id == employeeId && x.IsActive);
            if (!employeeExists)
                throw new InvalidOperationException("Employee was not found.");

            string? deviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? null : request.DeviceId.Trim();
            var existing = await _context.EmployeeFcmTokens
                .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.DeviceId == deviceId);

            if (existing == null)
            {
                _context.EmployeeFcmTokens.Add(new EmployeeFcmToken
                {
                    EmployeeId = employeeId,
                    FcmToken = token,
                    DeviceId = deviceId,
                    UpdatedAt = DateTime.Now
                });
            }
            else
            {
                existing.FcmToken = token;
                existing.UpdatedAt = DateTime.Now;
            }

            await _context.SaveChangesAsync();
        }

        public Task SendNotificationToEmployeeAsync(int employeeId, string title, string body, string type)
        {
            return SendNotificationToMultipleEmployeesAsync(new[] { employeeId }, title, body, type);
        }

        public async Task SendNotificationToMultipleEmployeesAsync(
            IEnumerable<int> employeeIds,
            string title,
            string body,
            string type)
        {
            var targetEmployeeIds = employeeIds
                .Where(x => x > 0)
                .Distinct()
                .ToArray();

            if (targetEmployeeIds.Length == 0)
                return;

            string notificationType = NormalizeType(type);
            var tokens = await _context.EmployeeFcmTokens
                .Where(x => targetEmployeeIds.Contains(x.EmployeeId) && x.FcmToken != "")
                .ToListAsync();

            if (tokens.Count > 0)
            {
                try
                {
                    var tokenValues = tokens
                        .Select(x => x.FcmToken)
                        .Distinct()
                        .ToArray();

                    foreach (var tokenBatch in tokenValues.Chunk(500))
                    {
                        var message = new MulticastMessage
                        {
                            Tokens = tokenBatch.ToList(),
                            Notification = new Notification
                            {
                                Title = title,
                                Body = body
                            },
                            Data = new Dictionary<string, string>
                            {
                                ["type"] = notificationType,
                                ["title"] = title,
                                ["body"] = body
                            },
                            Android = new AndroidConfig
                            {
                                Priority = Priority.High,
                                Notification = new AndroidNotification
                                {
                                    Title = title,
                                    Body = body,
                                    ChannelId = "high_importance_channel",
                                    Sound = "default",
                                    DefaultSound = true,
                                    DefaultVibrateTimings = true,
                                    Priority = NotificationPriority.MAX,
                                    Visibility = NotificationVisibility.PUBLIC
                                }
                            },
                            Apns = new ApnsConfig
                            {
                                Aps = new Aps
                                {
                                    Alert = new ApsAlert
                                    {
                                        Title = title,
                                        Body = body
                                    },
                                    Badge = 1,
                                    Sound = "default"
                                }
                            }
                        };

                        await GetMessaging().SendEachForMulticastAsync(message);
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "FCM delivery failed for notification type {NotificationType}.", notificationType);
                }
            }

            foreach (int employeeId in targetEmployeeIds)
            {
                _context.NotificationLogs.Add(new NotificationLog
                {
                    EmployeeId = employeeId,
                    Title = title,
                    Body = body,
                    Type = notificationType,
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();
        }

        public async Task<NotificationPageDto> GetMyNotificationsAsync(int employeeId, int page, int pageSize)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _context.NotificationLogs
                .AsNoTracking()
                .Where(x => x.EmployeeId == employeeId);

            int totalItems = await query.CountAsync();
            var items = await query
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new NotificationLogDto
                {
                    Id = x.Id,
                    Title = x.Title,
                    Body = x.Body,
                    Type = x.Type,
                    IsRead = x.IsRead,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();

            return new NotificationPageDto
            {
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                Items = items
            };
        }

        public async Task<bool> MarkAsReadAsync(int employeeId, long notificationId)
        {
            var notification = await _context.NotificationLogs
                .FirstOrDefaultAsync(x => x.Id == notificationId && x.EmployeeId == employeeId);
            if (notification == null)
                return false;

            notification.IsRead = true;
            await _context.SaveChangesAsync();
            return true;
        }

        private FirebaseMessaging GetMessaging()
        {
            EnsureFirebaseApp();
            FirebaseApp firebaseApp = _firebaseApp
                ?? throw new InvalidOperationException("Firebase was not initialized.");

            return FirebaseMessaging.GetMessaging(firebaseApp);
        }

        private void EnsureFirebaseApp()
        {
            if (_firebaseApp != null)
                return;

            lock (FirebaseLock)
            {
                if (_firebaseApp != null)
                    return;

                try
                {
                    _firebaseApp = FirebaseApp.DefaultInstance;
                }
                catch (InvalidOperationException)
                {
                    string? configuredPath = _configuration["Firebase:CredentialFilePath"];
                    if (string.IsNullOrWhiteSpace(configuredPath))
                        throw new InvalidOperationException("Firebase:CredentialFilePath is not configured.");

                    string credentialPath = Path.IsPathRooted(configuredPath)
                        ? configuredPath
                        : Path.Combine(AppContext.BaseDirectory, configuredPath);
                    if (!File.Exists(credentialPath))
                        throw new FileNotFoundException("Firebase credential file was not found.", credentialPath);

                    _firebaseApp = FirebaseApp.Create(new AppOptions
                    {
                        Credential = GoogleCredential.FromFile(credentialPath)
                    });
                }
            }
        }

        private static string NormalizeType(string type)
        {
            string normalized = string.IsNullOrWhiteSpace(type) ? "General" : type.Trim();
            return normalized switch
            {
                "Leave" or "Payroll" or "CheckInReminder" or "CheckOutReminder" or "General" => normalized,
                _ => "General"
            };
        }
    }
}
