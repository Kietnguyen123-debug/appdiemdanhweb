using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace AppDiemDanh.Services
{
    public interface ILeaveService
    {
        Task<LeaveRequest?> CreateLeaveRequestAsync(int employeeId, CreateLeaveRequest request);
        Task<LeaveBalanceDto?> GetBalanceAsync(int employeeId);
        Task<LeaveApprovalResult> ApproveLeaveAsync(long leaveId, int reviewerId, bool isApproved, string? rejectReason);
    }

    public class LeaveService : ILeaveService
    {
        private readonly AppDbContext _context;
        private readonly IFcmService _fcmService;
        private readonly ILogger<LeaveService> _logger;

        public LeaveService(
            AppDbContext context,
            IFcmService fcmService,
            ILogger<LeaveService> logger)
        {
            _context = context;
            _fcmService = fcmService;
            _logger = logger;
        }

        public async Task<LeaveRequest?> CreateLeaveRequestAsync(int employeeId, CreateLeaveRequest request)
        {
            if (employeeId <= 0 || request == null || string.IsNullOrWhiteSpace(request.LeaveType) ||
                request.LeaveType.Trim().ToUpperInvariant() is not ("ANNUAL" or "SICK" or "UNPAID" or "MATERNITY") ||
                string.IsNullOrWhiteSpace(request.Reason))
                return null;
            DateTime startDate = request.StartDate.Date;
            DateTime endDate = request.EndDate.Date;
            if (startDate == default || endDate < startDate)
                return null;

            var employee = await _context.Employees
                .FirstOrDefaultAsync(x => x.Id == employeeId && x.IsActive);
            if (employee == null)
                return null;

            int leaveDays = GetLeaveDays(startDate, endDate);
            int remainingDays = Math.Max(0, employee.TotalAnnualLeave - employee.UsedAnnualLeave);
            var leave = new LeaveRequest
            {
                EmployeeId = employeeId,
                StartDate = startDate,
                EndDate = endDate,
                LeaveType = string.IsNullOrWhiteSpace(request.LeaveType) ? "ANNUAL" : request.LeaveType.Trim(),
                Reason = request.Reason?.Trim() ?? string.Empty,
                Status = "PENDING",
                IsPaidLeave = remainingDays >= leaveDays,
                CreatedAt = DateTime.Now
            };

            _context.LeaveRequests.Add(leave);
            await _context.SaveChangesAsync();
            return leave;
        }

        public async Task<LeaveBalanceDto?> GetBalanceAsync(int employeeId)
        {
            var employee = await _context.Employees
                .AsNoTracking()
                .Where(x => x.Id == employeeId && x.IsActive)
                .Select(x => new LeaveBalanceDto
                {
                    TotalAnnualLeave = x.TotalAnnualLeave,
                    UsedAnnualLeave = x.UsedAnnualLeave,
                    RemainingAnnualLeave = Math.Max(0, x.TotalAnnualLeave - x.UsedAnnualLeave)
                })
                .FirstOrDefaultAsync();

            return employee;
        }

        public async Task<LeaveApprovalResult> ApproveLeaveAsync(long leaveId, int reviewerId, bool isApproved, string? rejectReason)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var leave = await _context.LeaveRequests
                .FirstOrDefaultAsync(x => x.Id == leaveId);

            if (leave == null)
                return Failure("Không tìm thấy đơn xin nghỉ.");

            if (!string.Equals(leave.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
                return Failure("Đơn xin nghỉ đã được xử lý trước đó.");

            leave.ApprovedByEmployeeId = reviewerId;
            leave.RejectReason = rejectReason;

            if (!isApproved)
            {
                leave.Status = "REJECTED";
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                await NotifyLeaveDecisionAsync(leave, false);
                return Success("Đã từ chối đơn.", leave.Status);
            }

            if (leave.IsPaidLeave)
            {
                var employee = await _context.Employees
                    .FirstOrDefaultAsync(x => x.Id == leave.EmployeeId);
                if (employee == null)
                    return Failure("Không tìm thấy nhân viên của đơn nghỉ.");

                int leaveDays = GetLeaveDays(leave.StartDate.Date, leave.EndDate.Date);
                int remainingDays = Math.Max(0, employee.TotalAnnualLeave - employee.UsedAnnualLeave);
                if (remainingDays < leaveDays)
                    return Failure("Quỹ phép hiện tại không đủ để duyệt đơn nghỉ hưởng lương.");

                employee.UsedAnnualLeave += leaveDays;
            }

            leave.Status = "APPROVED";
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            await NotifyLeaveDecisionAsync(leave, true);
            return Success("Đã duyệt đơn.", leave.Status);
        }

        private async Task NotifyLeaveDecisionAsync(LeaveRequest leave, bool approved)
        {
            try
            {
                string decision = approved ? "đã được duyệt" : "đã bị từ chối";
                await _fcmService.SendNotificationToEmployeeAsync(
                    leave.EmployeeId,
                    "Cập nhật đơn nghỉ phép",
                    $"Đơn nghỉ từ {leave.StartDate:dd/MM/yyyy} đến {leave.EndDate:dd/MM/yyyy} {decision}.",
                    "Leave");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not send leave decision notification for leave request {LeaveId}.", leave.Id);
            }
        }

        private static int GetLeaveDays(DateTime startDate, DateTime endDate) =>
            (endDate.Date - startDate.Date).Days + 1;

        private static LeaveApprovalResult Success(string message, string status) => new()
        {
            Success = true,
            Message = message,
            Status = status
        };

        private static LeaveApprovalResult Failure(string message) => new()
        {
            Success = false,
            Message = message
        };
    }
}
