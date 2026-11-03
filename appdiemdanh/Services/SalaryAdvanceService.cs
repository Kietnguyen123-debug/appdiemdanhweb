using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Services;

public interface ISalaryAdvanceService
{
    Task<SalaryAdvance?> RequestAsync(int employeeId, SalaryAdvanceRequest request);
    Task<List<SalaryAdvance>> GetMyRequestsAsync(int employeeId);
    Task<List<SalaryAdvance>> GetPendingAsync();
    Task<SalaryAdvance?> ReviewAsync(long id, int reviewerId, SalaryAdvanceReviewRequest request);
}

public class SalaryAdvanceService : ISalaryAdvanceService
{
    private readonly AppDbContext _context;
    private readonly IFcmService _fcmService;

    public SalaryAdvanceService(AppDbContext context, IFcmService fcmService)
    {
        _context = context;
        _fcmService = fcmService;
    }

    public async Task<SalaryAdvance?> RequestAsync(int employeeId, SalaryAdvanceRequest request)
    {
        if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Reason)) return null;
        if (!await _context.Employees.AnyAsync(x => x.Id == employeeId && x.IsActive)) return null;

        var advance = new SalaryAdvance
        {
            EmployeeId = employeeId,
            Amount = request.Amount,
            Reason = request.Reason.Trim(),
            RequestDate = DateTime.Now,
            CreatedAt = DateTime.Now,
            Status = "Pending"
        };
        _context.SalaryAdvances.Add(advance);
        await _context.SaveChangesAsync();
        return advance;
    }

    public Task<List<SalaryAdvance>> GetMyRequestsAsync(int employeeId) =>
        _context.SalaryAdvances.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.RequestDate)
            .ToListAsync();

    public Task<List<SalaryAdvance>> GetPendingAsync() =>
        _context.SalaryAdvances.AsNoTracking()
            .Where(x => x.Status == "Pending")
            .OrderBy(x => x.RequestDate)
            .ToListAsync();

    public async Task<SalaryAdvance?> ReviewAsync(long id, int reviewerId, SalaryAdvanceReviewRequest request)
    {
        var advance = await _context.SalaryAdvances.FirstOrDefaultAsync(x => x.Id == id);
        if (advance == null || advance.Status != "Pending") return null;
        advance.Status = request.IsApproved ? "Approved" : "Rejected";
        advance.ApprovedById = reviewerId;
        advance.ReviewNote = request.ReviewNote;
        await _context.SaveChangesAsync();

        try
        {
            string statusStr = request.IsApproved ? "được duyệt" : "từ chối";
            await _fcmService.SendNotificationToEmployeeAsync(
                advance.EmployeeId,
                "Cập nhật yêu cầu tạm ứng lương",
                $"Yêu cầu ứng {advance.Amount:N0} VNĐ đã {statusStr}.",
                "General");
        }
        catch (Exception) {}

        return advance;
    }
}
