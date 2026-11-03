using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace AppDiemDanh.Services
{
    public interface IAttendanceExplanationService
    {
        Task<AttendanceExplanationResponse?> SubmitExplanationAsync(int employeeId, SubmitAttendanceExplanationRequest request);
        Task<IReadOnlyList<AttendanceExplanationResponse>> GetPendingExplanationsAsync();
        Task<IReadOnlyList<AttendanceExplanationResponse>> GetApprovedExplanationsAsync();
        Task<AttendanceExplanationResponse?> ReviewExplanationAsync(long explanationId, int reviewerId, string status, string? note);
    }

    public class AttendanceExplanationService : IAttendanceExplanationService
    {
        private readonly AppDbContext _context;
        private readonly IFcmService _fcmService;
        public AttendanceExplanationService(AppDbContext context, IFcmService fcmService)
        {
            _context = context;
            _fcmService = fcmService;
        }   

        public async Task<AttendanceExplanationResponse?> SubmitExplanationAsync(int employeeId, SubmitAttendanceExplanationRequest request)
        {
            DateTime targetDate = request.TargetDate.Date;
            if (targetDate == default || string.IsNullOrWhiteSpace(request.ExplanationReason))
                return null;

            if (!request.MissingCheckInTime.HasValue && !request.MissingCheckOutTime.HasValue)
                return null;

            if (request.MissingCheckInTime.HasValue && request.MissingCheckInTime.Value.Date != targetDate ||
                request.MissingCheckOutTime.HasValue && request.MissingCheckOutTime.Value.Date != targetDate)
                return null;

            bool employeeExists = await _context.Employees.AnyAsync(x => x.Id == employeeId && x.IsActive);
            if (!employeeExists)
                return null;

            bool hasPendingExplanation = await _context.AttendanceExplanations.AnyAsync(x =>
                x.EmployeeId == employeeId &&
                x.TargetDate == targetDate &&
                x.Status.ToUpper() == "PENDING");
            if (hasPendingExplanation)
                return null;

            var explanation = new AttendanceExplanation
            {
                EmployeeId = employeeId,
                TargetDate = targetDate,
                ExplanationReason = request.ExplanationReason.Trim(),
                MissingCheckInTime = request.MissingCheckInTime,
                MissingCheckOutTime = request.MissingCheckOutTime,
                Status = "PENDING",
                CreatedAt = DateTime.Now
            };

            _context.AttendanceExplanations.Add(explanation);
            await _context.SaveChangesAsync();
            return ToResponse(explanation, null);
        }

        public async Task<IReadOnlyList<AttendanceExplanationResponse>> GetPendingExplanationsAsync()
        {
            var pending = await _context.AttendanceExplanations
                .AsNoTracking()
                .Include(x => x.Employee)
                .Where(x => x.Status.ToUpper() == "PENDING")
                .OrderByDescending(x => x.TargetDate)
                .ThenByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return pending.Select(x => ToResponse(x, x.Employee?.FullName)).ToList();
        }

        public async Task<IReadOnlyList<AttendanceExplanationResponse>> GetApprovedExplanationsAsync()
        {
            var approved = await _context.AttendanceExplanations
                .AsNoTracking()
                .Include(x => x.Employee)
                .Where(x => x.Status.ToUpper() == "APPROVED")
                .OrderByDescending(x => x.TargetDate)
                .ThenByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return approved.Select(x => ToResponse(x, x.Employee?.FullName)).ToList();
        }

        public async Task<AttendanceExplanationResponse?> ReviewExplanationAsync(long explanationId, int reviewerId, string status, string? note)
        {
            if (string.IsNullOrWhiteSpace(status))
                return null;

            string normalizedStatus = status.Trim().ToUpperInvariant();
            if (normalizedStatus != "APPROVED" && normalizedStatus != "REJECTED")
                return null;

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var explanation = await _context.AttendanceExplanations
                .FirstOrDefaultAsync(x => x.Id == explanationId);

            if (explanation == null || explanation.Status != "PENDING")
                return null;

            bool reviewerExists = await _context.Employees.AnyAsync(x => x.Id == reviewerId && x.IsActive);
            if (!reviewerExists)
                return null;

            explanation.ReviewerId = reviewerId;
            explanation.ReviewNote = note?.Trim();
            explanation.Status = normalizedStatus;

            if (normalizedStatus == "APPROVED")
            {
                var employee = await _context.Employees
                    .FirstOrDefaultAsync(x => x.Id == explanation.EmployeeId);
                if (employee == null)
                    return null;

                var attendance = await _context.Attendances
                    .FirstOrDefaultAsync(x => x.EmployeeId == explanation.EmployeeId && x.WorkDate == explanation.TargetDate);

                if (attendance == null)
                {
                    if (!explanation.MissingCheckInTime.HasValue)
                        return null;

                    attendance = new Attendance
                    {
                        EmployeeId = explanation.EmployeeId,
                        BranchId = employee.BranchId,
                        WorkDate = explanation.TargetDate,
                        CheckInTime = explanation.MissingCheckInTime.Value,
                        CheckOutTime = explanation.MissingCheckOutTime,
                        Status = "PRESENT",
                        Note = "Bổ sung từ giải trình chấm công",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };
                    _context.Attendances.Add(attendance);
                }
                else
                {
                    if (explanation.MissingCheckInTime.HasValue && attendance.CheckInTime == default)
                        attendance.CheckInTime = explanation.MissingCheckInTime.Value;

                    if (explanation.MissingCheckOutTime.HasValue && !attendance.CheckOutTime.HasValue)
                        attendance.CheckOutTime = explanation.MissingCheckOutTime.Value;

                    attendance.UpdatedAt = DateTime.Now;
                }
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            try
            {
                string statusStr = normalizedStatus == "APPROVED" ? "được duyệt" : "từ chối";
                await _fcmService.SendNotificationToEmployeeAsync(
                    explanation.EmployeeId,
                    "Cập nhật giải trình chấm công",
                    $"Yêu cầu giải trình ngày {explanation.TargetDate:dd/MM/yyyy} đã {statusStr}.",
                    "General");
            }
            catch (Exception) {}

            return ToResponse(explanation, null);
        }

        private static AttendanceExplanationResponse ToResponse(AttendanceExplanation explanation, string? employeeName) => new()
        {
            Id = explanation.Id,
            EmployeeId = explanation.EmployeeId,
            EmployeeName = employeeName,
            TargetDate = explanation.TargetDate,
            ExplanationReason = explanation.ExplanationReason,
            MissingCheckInTime = explanation.MissingCheckInTime,
            MissingCheckOutTime = explanation.MissingCheckOutTime,
            Status = explanation.Status,
            ReviewerId = explanation.ReviewerId,
            ReviewNote = explanation.ReviewNote,
            CreatedAt = explanation.CreatedAt
        };
    }
}
