using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Services;

public interface IOvertimeService
{
    Task<OvertimeRecord?> LogAsync(int employeeId, OvertimeRequest request);
    Task<OvertimeRecord?> ReviewAsync(long id, OvertimeReviewRequest request);
}

public class OvertimeService : IOvertimeService
{
    private readonly AppDbContext _context;
    private readonly IFcmService _fcmService;

    public OvertimeService(AppDbContext context, IFcmService fcmService)
    {
        _context = context;
        _fcmService = fcmService;
    }

    public async Task<OvertimeRecord?> LogAsync(int employeeId, OvertimeRequest request)
    {
        if (request.WorkDate == default || request.Hours <= 0 || request.Multiplier <= 0) return null;
        if (!await _context.Employees.AnyAsync(x => x.Id == employeeId && x.IsActive)) return null;

        var record = new OvertimeRecord
        {
            EmployeeId = employeeId,
            WorkDate = request.WorkDate.Date,
            Hours = request.Hours,
            Multiplier = request.Multiplier,
            Status = "Pending"
        };
        _context.OvertimeRecords.Add(record);
        await _context.SaveChangesAsync();
        return record;
    }

    public async Task<OvertimeRecord?> ReviewAsync(long id, OvertimeReviewRequest request)
    {
        var record = await _context.OvertimeRecords.FirstOrDefaultAsync(x => x.Id == id);
        if (record == null || record.Status != "Pending") return null;
        record.Status = request.IsApproved ? "Approved" : "Rejected";
        await _context.SaveChangesAsync();

        try
        {
            string statusStr = request.IsApproved ? "được duyệt" : "từ chối";
            await _fcmService.SendNotificationToEmployeeAsync(
                record.EmployeeId,
                "Cập nhật đăng ký tăng ca",
                $"Đơn làm tăng ca {record.Hours}h ngày {record.WorkDate:dd/MM/yyyy} đã {statusStr}.",
                "General");
        }
        catch (Exception) {}

        return record;
    }
}
