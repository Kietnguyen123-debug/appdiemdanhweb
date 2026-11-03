using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Services
{
    public class ShiftAttendanceReminderService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ShiftAttendanceReminderService> _logger;

        public ShiftAttendanceReminderService(
            IServiceScopeFactory scopeFactory,
            ILogger<ShiftAttendanceReminderService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessRemindersAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Error while processing shift attendance reminders.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task ProcessRemindersAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fcmService = scope.ServiceProvider.GetRequiredService<IFcmService>();
            DateTime now = DateTime.Now;
            DateTime today = now.Date;
            DateTime tomorrow = today.AddDays(1);

            var assignments = await context.EmployeeShifts
                .AsNoTracking()
                .Include(x => x.Employee)
                .Include(x => x.Shift)
                .Where(x => x.Employee != null && x.Employee.IsActive &&
                            x.Shift != null && x.Shift.IsActive &&
                            x.EffectiveFromDate <= today &&
                            (!x.EffectiveToDate.HasValue || x.EffectiveToDate.Value >= today))
                .OrderByDescending(x => x.EffectiveFromDate)
                .ToListAsync(cancellationToken);

            foreach (var assignment in assignments.GroupBy(x => x.EmployeeId).Select(x => x.First()))
            {
                if (assignment.Employee == null || assignment.Shift == null)
                    continue;

                DateTime shiftStart = today.Add(assignment.Shift.StartTime);
                DateTime shiftEnd = today.Add(assignment.Shift.EndTime);
                if (assignment.Shift.EndTime < assignment.Shift.StartTime)
                    shiftEnd = shiftEnd.AddDays(1);

                var attendance = await context.Attendances
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.EmployeeId == assignment.EmployeeId && x.WorkDate == today, cancellationToken);

                if (now >= shiftStart.AddMinutes(-15) && now <= shiftStart.AddMinutes(10) &&
                    (attendance == null || attendance.CheckInTime == default) &&
                    !await HasReminderAsync(context, assignment.EmployeeId, "CheckInReminder", today, cancellationToken))
                {
                    await fcmService.SendNotificationToEmployeeAsync(
                        assignment.EmployeeId,
                        "Nhắc nhở chấm công",
                        $"Bạn có ca {assignment.Shift.ShiftName} lúc {assignment.Shift.StartTime:hh\\:mm}. Vui lòng check-in đúng giờ.",
                        "CheckInReminder");
                }

                if (now >= shiftEnd && now <= shiftEnd.AddMinutes(20) &&
                    attendance != null && attendance.CheckInTime != default && !attendance.CheckOutTime.HasValue &&
                    !await HasReminderAsync(context, assignment.EmployeeId, "CheckOutReminder", today, cancellationToken))
                {
                    await fcmService.SendNotificationToEmployeeAsync(
                        assignment.EmployeeId,
                        "Nhắc nhở check-out",
                        $"Ca {assignment.Shift.ShiftName} đã kết thúc. Vui lòng check-out để hoàn tất công.",
                        "CheckOutReminder");
                }
            }
        }

        private static Task<bool> HasReminderAsync(
            AppDbContext context,
            int employeeId,
            string type,
            DateTime today,
            CancellationToken cancellationToken)
        {
            return context.NotificationLogs.AnyAsync(x =>
                x.EmployeeId == employeeId &&
                x.Type == type &&
                x.CreatedAt >= today &&
                x.CreatedAt < today.AddDays(1), cancellationToken);
        }
    }
}
