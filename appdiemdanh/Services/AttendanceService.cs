using System.Text.Json;
using AppDiemDanh.DTOs;
using AppDiemDanh.Extensions;
using AppDiemDanh.Models;
using AppDiemDanh.Services.Face;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AppDiemDanh.Services
{
    public interface IAttendanceService
    {
        Task<AttendanceResponse> ProcessCheckInAsync(int employeeId, string clientIp, AttendanceRequest request);
        Task<AttendanceResponse> ProcessCheckOutAsync(int employeeId, string clientIp, AttendanceRequest request);
        Task<bool> RegisterFaceAsync(RegisterFaceRequest request);
    }

    public class AttendanceService : IAttendanceService
    {
        private readonly AppDbContext _context;
        private readonly IFaceRecognitionService _faceAiService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AttendanceService> _logger;

        public AttendanceService(
            AppDbContext context,
            IFaceRecognitionService faceAiService,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AttendanceService> logger)
        {
            _context = context;
            _faceAiService = faceAiService;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        private DateTime GetVietnamTime()
        {
            var utcNow = DateTime.UtcNow;
            try
            {
                var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(utcNow, vnTimeZone);
            }
            catch
            {
                try
                {
                    var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
                    return TimeZoneInfo.ConvertTimeFromUtc(utcNow, vnTimeZone);
                }
                catch
                {
                    return utcNow.AddHours(7); 
                }
            }
        }

        private string CleanIp(string ip) => IpHelper.NormalizeIp(ip);
        private string CleanBssid(string bssid) => (bssid ?? string.Empty).Trim().ToLowerInvariant().Replace("-", ":");

        public async Task<bool> RegisterFaceAsync(RegisterFaceRequest request)
        {
            var employee = await _context.Employees.FindAsync(request.EmployeeId);
            if (employee == null) return false;

            float[] vector = await _faceAiService.ExtractEmbeddingVectorAsync(request.FaceImageBase64);
            employee.FaceVector = JsonSerializer.Serialize(vector);

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<AttendanceResponse> ProcessCheckInAsync(int employeeId, string clientIp, AttendanceRequest request)
        {
            DateTime now = GetVietnamTime();
            DateTime today = now.Date;

            string safeClientIp = ResolveAttendanceIp(clientIp, request);
            string normalizedBssid = CleanBssid(request?.Bssid);
            string deviceId = request?.DeviceId ?? string.Empty;

            // 1. Check Employee & Device Binding
            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.Id == employeeId && e.IsActive);
            if (employee == null)
                return await RecordAttemptAndReject(employeeId, null, safeClientIp, normalizedBssid, 0, deviceId, "EMPLOYEE_NOT_FOUND");

            if (!string.IsNullOrEmpty(employee.BoundDeviceId) && employee.BoundDeviceId != deviceId)
                return await RecordAttemptAndReject(employeeId, employee.BranchId, safeClientIp, normalizedBssid, 0, deviceId, "UNAUTHORIZED_DEVICE");

            int branchId = employee.BranchId;

            // 2. Kiểm tra BSSID WiFi
            bool isBssidValid = await _context.BranchWifis
                .AnyAsync(w => w.BranchId == branchId && w.BSSID.ToLower().Replace("-", ":") == normalizedBssid && w.IsActive);

            if (!isBssidValid)
                return await RecordAttemptAndReject(employeeId, branchId, safeClientIp, normalizedBssid, 0, deviceId, "BSSID_NOT_ALLOWED");

            // 3. Chỉ so khớp IP active thuộc chi nhánh của nhân viên.
            var validIps = await GetActiveBranchIpsAsync(branchId);
            if (!IsIpAllowed(validIps, safeClientIp))
            {
                LogIpMismatch(employeeId, branchId, safeClientIp, validIps);
                return await RecordAttemptAndReject(employeeId, branchId, safeClientIp, normalizedBssid, 0, deviceId, "PUBLIC_IP_NOT_ALLOWED", $"IP mạng hiện tại ({safeClientIp}) không khớp với danh sách IP của chi nhánh.");
            }

            // 4. Kiểm tra quyền thuộc chi nhánh
            if (employee.BranchId != branchId)
                return await RecordAttemptAndReject(employeeId, branchId, safeClientIp, normalizedBssid, 0, deviceId, "EMPLOYEE_BRANCH_MISMATCH");

            // 5. Xác thực ONNX Face AI
            if (string.IsNullOrEmpty(employee.FaceVector))
                return await RecordAttemptAndReject(employeeId, branchId, safeClientIp, normalizedBssid, 0, deviceId, "NO_REGISTERED_FACE_VECTOR", "Tài khoản này chưa đăng ký khuôn mặt trên hệ thống");

            var faceResult = await _faceAiService.VerifyFaceAsync(request.FaceImageBase64, employee.FaceVector);
            if (!faceResult.IsSuccess)
                return await RecordAttemptAndReject(employeeId, branchId, safeClientIp, normalizedBssid, faceResult.Similarity, deviceId, faceResult.ErrorCode, faceResult.ErrorMessage, ToFaceDebug(faceResult));

            // 6. Kiểm tra đã Check-in hôm nay chưa
            var existingAttendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.WorkDate == today);

            if (existingAttendance != null)
                return await RecordAttemptAndReject(employeeId, branchId, safeClientIp, normalizedBssid, faceResult.Similarity, deviceId, "ALREADY_CHECKED_IN");

            var assignedShift = await GetShiftForDateAsync(employeeId, today);
            DateTime? expectedStart = assignedShift == null ? null : today.Add(assignedShift.StartTime);
            int lateMinutes = expectedStart.HasValue && now > expectedStart.Value
                ? (int)(now - expectedStart.Value).TotalMinutes
                : 0;

            // 7. Lưu Attendance thành công
            var attendance = new Attendance
            {
                EmployeeId = employeeId,
                BranchId = branchId,
                WorkDate = today,
                CheckInTime = now,
                CheckInIp = safeClientIp,
                CheckInBssid = normalizedBssid,
                CheckInFaceConfidence = faceResult.Similarity,
                CheckInDeviceId = deviceId,
                LateMinutes = lateMinutes,
                Status = "PRESENT",
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.Attendances.Add(attendance);

            _context.AttendanceAttempts.Add(new AttendanceAttempt
            {
                EmployeeId = employeeId,
                BranchId = branchId,
                AttemptTime = now,
                PublicIp = safeClientIp,
                Bssid = normalizedBssid,
                FaceConfidence = faceResult.Similarity,
                DeviceId = deviceId,
                Status = "SUCCESS",
                RejectReason = "NONE"
            });

            await _context.SaveChangesAsync();

            return new AttendanceResponse
            {
                Success = true,
                Message = "Check-in thành công!",
                Timestamp = now,
                Action = "CHECK_IN",
                FaceConfidence = faceResult.Similarity
            };
        }

        public async Task<AttendanceResponse> ProcessCheckOutAsync(int employeeId, string clientIp, AttendanceRequest request)
        {
            DateTime now = GetVietnamTime();
            DateTime today = now.Date;

            string safeClientIp = ResolveAttendanceIp(clientIp, request);
            string normalizedBssid = CleanBssid(request?.Bssid);
            string deviceId = request?.DeviceId ?? string.Empty;

            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.Id == employeeId && e.IsActive);
            if (employee == null)
                return await RecordAttemptAndReject(employeeId, null, safeClientIp, normalizedBssid, 0, deviceId, "EMPLOYEE_NOT_FOUND");

            int branchId = employee.BranchId;
            bool isBssidValid = await _context.BranchWifis.AnyAsync(w => w.BranchId == branchId && w.BSSID.ToLower().Replace("-", ":") == normalizedBssid && w.IsActive);
            if (!isBssidValid)
                return await RecordAttemptAndReject(employeeId, branchId, safeClientIp, normalizedBssid, 0, deviceId, "BSSID_NOT_ALLOWED");

            var validIps = await GetActiveBranchIpsAsync(branchId);
            if (!IsIpAllowed(validIps, safeClientIp))
            {
                LogIpMismatch(employeeId, branchId, safeClientIp, validIps);
                return await RecordAttemptAndReject(employeeId, branchId, safeClientIp, normalizedBssid, 0, deviceId, "PUBLIC_IP_NOT_ALLOWED", $"IP mạng hiện tại ({safeClientIp}) không khớp với danh sách IP của chi nhánh.");
            }

            var faceResult = await _faceAiService.VerifyFaceAsync(request.FaceImageBase64, employee.FaceVector ?? "");
            if (!faceResult.IsSuccess)
                return await RecordAttemptAndReject(employeeId, branchId, safeClientIp, normalizedBssid, faceResult.Similarity, deviceId, faceResult.ErrorCode, faceResult.ErrorMessage, ToFaceDebug(faceResult));

            var attendance = await _context.Attendances.FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.WorkDate == today && a.CheckOutTime == null);
            if (attendance == null)
            {
                var previousWorkDate = today.AddDays(-1);
                var previousShift = await GetShiftForDateAsync(employeeId, previousWorkDate);
                if (previousShift != null && previousShift.EndTime < previousShift.StartTime)
                {
                    var previousAttendance = await _context.Attendances.FirstOrDefaultAsync(a =>
                        a.EmployeeId == employeeId && a.WorkDate == previousWorkDate && a.CheckOutTime == null);
                    DateTime previousShiftEnd = previousWorkDate.Add(previousShift.EndTime).AddDays(1);
                    if (previousAttendance != null && now >= previousShiftEnd)
                        attendance = previousAttendance;
                }
            }
            if (attendance == null)
                return await RecordAttemptAndReject(employeeId, branchId, safeClientIp, normalizedBssid, faceResult.Similarity, deviceId, "NO_OPEN_CHECKIN_FOUND");

            attendance.CheckOutTime = now;
            attendance.CheckOutIp = safeClientIp;
            attendance.CheckOutBssid = normalizedBssid;
            attendance.CheckOutFaceConfidence = faceResult.Similarity;
            attendance.CheckOutDeviceId = deviceId;
            attendance.WorkedMinutes = (int)(now - attendance.CheckInTime).TotalMinutes;

            var assignedShift = await GetShiftForDateAsync(employeeId, attendance.WorkDate);
            int overtimeMinutes = 0;
            if (assignedShift != null)
            {
                DateTime expectedEnd = attendance.WorkDate.Add(assignedShift.EndTime);
                if (assignedShift.EndTime < assignedShift.StartTime)
                    expectedEnd = expectedEnd.AddDays(1);

                const int minOvertimeThresholdMinutes = 15;
                if (now > expectedEnd.AddMinutes(minOvertimeThresholdMinutes))
                    overtimeMinutes = Math.Max(0, (int)Math.Round((now - expectedEnd).TotalMinutes, MidpointRounding.AwayFromZero));

                attendance.EarlyLeaveMinutes = now < expectedEnd
                    ? Math.Max(0, (int)(expectedEnd - now).TotalMinutes)
                    : 0;
            }
            attendance.OvertimeMinutes = overtimeMinutes;

            if (overtimeMinutes > 0)
            {
                var overtimeRecord = await _context.OvertimeRecords
                    .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.WorkDate == attendance.WorkDate);
                double overtimeHours = Math.Round(overtimeMinutes / 60.0, 1, MidpointRounding.AwayFromZero);

                if (overtimeRecord == null)
                {
                    _context.OvertimeRecords.Add(new OvertimeRecord
                    {
                        EmployeeId = employeeId,
                        WorkDate = attendance.WorkDate,
                        Hours = overtimeHours,
                        Multiplier = 1.5,
                        Status = "Pending"
                    });
                }
                else if (overtimeRecord.Status == "Pending")
                {
                    overtimeRecord.Hours = overtimeHours;
                    overtimeRecord.Multiplier = 1.5;
                }
            }

            attendance.UpdatedAt = now;

            _context.AttendanceAttempts.Add(new AttendanceAttempt
            {
                EmployeeId = employeeId,
                BranchId = branchId,
                AttemptTime = now,
                PublicIp = safeClientIp,
                Bssid = normalizedBssid,
                FaceConfidence = faceResult.Similarity,
                DeviceId = deviceId,
                Status = "SUCCESS",
                RejectReason = "NONE"
            });

            await _context.SaveChangesAsync();

            return new AttendanceResponse
            {
                Success = true,
                Message = overtimeMinutes > 0
                    ? $"Check-out thành công! Đã ghi nhận {overtimeMinutes} phút ({overtimeMinutes / 60.0:F1} giờ) tăng ca, chờ quản lý duyệt."
                    : "Check-out thành công!",
                Timestamp = now,
                Action = "CHECK_OUT",
                FaceConfidence = faceResult.Similarity,
                OvertimeMinutes = overtimeMinutes
            };
        }

        private string ResolveAttendanceIp(string serverIp, AttendanceRequest request)
        {
            var clientIp = IpHelper.NormalizeIp(request?.ClientPublicIp);
            if (!string.IsNullOrEmpty(clientIp))
                return clientIp;

            var fallbackIp = IpHelper.NormalizeIp(serverIp);
            if (!string.IsNullOrEmpty(fallbackIp))
                return fallbackIp;

            var httpContext = _httpContextAccessor.HttpContext;
            var forwardedIp = IpHelper.NormalizeIp(httpContext?.Request.Headers["X-Forwarded-For"].FirstOrDefault());
            return !string.IsNullOrEmpty(forwardedIp)
                ? forwardedIp
                : IpHelper.NormalizeIp(httpContext?.Connection.RemoteIpAddress?.ToString());
        }

        private async Task<List<string>> GetActiveBranchIpsAsync(int branchId)
        {
            return await _context.BranchPublicIps
                .Where(ip => ip.BranchId == branchId && ip.IsActive && ip.Branch != null && ip.Branch.IsActive)
                .Select(ip => ip.PublicIp)
                .ToListAsync();
        }

        private static bool IsIpAllowed(IEnumerable<string> validIps, string clientIp)
        {
            var normalizedClientIp = IpHelper.NormalizeIp(clientIp);
            return !string.IsNullOrEmpty(normalizedClientIp) && validIps
                .Select(IpHelper.NormalizeIp)
                .Any(dbIp => string.Equals(dbIp, normalizedClientIp, StringComparison.OrdinalIgnoreCase));
        }

        private void LogIpMismatch(int employeeId, int branchId, string clientIp, IEnumerable<string> validIps)
        {
            _logger.LogWarning(
                "IP do Client gửi ({ClientIp}) không khớp với DB Chi nhánh {BranchId} của Nhân viên {EmployeeId}. XForwardedFor: {XForwardedFor}; ValidIps: {ValidIps}",
                clientIp,
                branchId,
                employeeId,
                _httpContextAccessor.HttpContext?.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? string.Empty,
                string.Join(", ", validIps));
        }

        private static FaceVerificationDebugDto ToFaceDebug(FaceVerificationResult result) => new()
        {
            SimilarityScore = result.Similarity,
            RequiredThreshold = result.Threshold,
            DetectedFaces = result.DetectedFaces,
            DbVectorDimension = result.DbVectorDimension,
            AppVectorDimension = result.AppVectorDimension
        };

        private async Task<AttendanceResponse> RecordAttemptAndReject(int employeeId, int? branchId, string ip, string bssid, double confidence, string deviceId, string reason, string? message = null, FaceVerificationDebugDto? debug = null)
        {
            DateTime now = GetVietnamTime();

            _context.AttendanceAttempts.Add(new AttendanceAttempt
            {
                EmployeeId = employeeId,
                BranchId = branchId,
                AttemptTime = now,
                PublicIp = ip,
                Bssid = bssid,
                FaceConfidence = confidence,
                DeviceId = deviceId,
                Status = "FAILED",
                RejectReason = reason
            });

            await _context.SaveChangesAsync();

            return new AttendanceResponse
            {
                Success = false,
                ErrorCode = reason,
                Message = message ?? $"Từ chối điểm danh: {reason}",
                Timestamp = now,
                FaceConfidence = confidence,
                Debug = debug
            };
        }

        private Task<Shift?> GetShiftForDateAsync(int employeeId, DateTime workDate)
        {
            DateTime date = workDate.Date;
            return _context.EmployeeShifts
                .Where(x => x.EmployeeId == employeeId &&
                            x.EffectiveFromDate <= date &&
                            (!x.EffectiveToDate.HasValue || x.EffectiveToDate.Value >= date) &&
                            x.Shift != null && x.Shift.IsActive)
                .OrderByDescending(x => x.EffectiveFromDate)
                .Select(x => x.Shift)
                .FirstOrDefaultAsync();
        }
    }
}
