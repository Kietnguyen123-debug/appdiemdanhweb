using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Services
{
    public interface IShiftService
    {
        Task<IReadOnlyList<ShiftDto>> GetAllAsync();
        Task<ShiftDto?> GetByIdAsync(int id);
        Task<ShiftDto?> CreateAsync(CreateShiftRequest request);
        Task<ShiftDto?> UpdateAsync(int id, CreateShiftRequest request);
        Task<bool> DeleteAsync(int id);
        Task<long?> AssignShiftToEmployeeAsync(AssignShiftRequest request);
    }

    public class ShiftService : IShiftService
    {
        private readonly AppDbContext _context;
        private readonly IFcmService _fcmService;

        public ShiftService(AppDbContext context, IFcmService fcmService)
        {
            _context = context;
            _fcmService = fcmService;
        }

        public async Task<IReadOnlyList<ShiftDto>> GetAllAsync()
        {
            return await _context.Shifts
                .AsNoTracking()
                .OrderBy(x => x.ShiftCode)
                .Select(x => new ShiftDto
                {
                    Id = x.Id,
                    ShiftCode = x.ShiftCode,
                    ShiftName = x.ShiftName,
                    StartTime = x.StartTime,
                    EndTime = x.EndTime,
                    IsActive = x.IsActive
                })
                .ToListAsync();
        }

        public async Task<ShiftDto?> GetByIdAsync(int id)
        {
            return await _context.Shifts
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new ShiftDto
                {
                    Id = x.Id,
                    ShiftCode = x.ShiftCode,
                    ShiftName = x.ShiftName,
                    StartTime = x.StartTime,
                    EndTime = x.EndTime,
                    IsActive = x.IsActive
                })
                .FirstOrDefaultAsync();
        }

        public async Task<ShiftDto?> CreateAsync(CreateShiftRequest request)
        {
            if (!IsValid(request) || await _context.Shifts.AnyAsync(x => x.ShiftCode == request.ShiftCode.Trim()))
                return null;

            var shift = new Shift
            {
                ShiftCode = request.ShiftCode.Trim(),
                ShiftName = request.ShiftName.Trim(),
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                IsActive = request.IsActive
            };

            _context.Shifts.Add(shift);
            await _context.SaveChangesAsync();
            return ToDto(shift);
        }

        public async Task<ShiftDto?> UpdateAsync(int id, CreateShiftRequest request)
        {
            if (id <= 0 || request == null || !IsValid(request))
                return null;

            var shift = await _context.Shifts.FindAsync(id);
            if (shift == null || await _context.Shifts.AnyAsync(x => x.Id != id && x.ShiftCode == request.ShiftCode.Trim()))
                return null;

            shift.ShiftCode = request.ShiftCode.Trim();
            shift.ShiftName = request.ShiftName.Trim();
            shift.StartTime = request.StartTime;
            shift.EndTime = request.EndTime;
            shift.IsActive = request.IsActive;
            await _context.SaveChangesAsync();
            return ToDto(shift);
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var shift = await _context.Shifts.FindAsync(id);
            if (shift == null)
                return false;

            _context.Shifts.Remove(shift);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<long?> AssignShiftToEmployeeAsync(AssignShiftRequest request)
        {
            if (request == null || request.EmployeeId <= 0 || request.ShiftId <= 0 ||
                request.EffectiveFromDate == default ||
                request.EffectiveToDate.HasValue && request.EffectiveToDate.Value.Date < request.EffectiveFromDate.Date ||
                !await _context.Employees.AnyAsync(x => x.Id == request.EmployeeId && x.IsActive) ||
                !await _context.Shifts.AnyAsync(x => x.Id == request.ShiftId && x.IsActive))
                return null;

            DateTime from = request.EffectiveFromDate.Date;
            DateTime? to = request.EffectiveToDate?.Date;
            var overlaps = await _context.EmployeeShifts.Where(x =>
                x.EmployeeId == request.EmployeeId &&
                x.EffectiveFromDate <= (to ?? DateTime.MaxValue) &&
                (!x.EffectiveToDate.HasValue || x.EffectiveToDate.Value >= from))
                .ToListAsync();

            await using var transaction = await _context.Database.BeginTransactionAsync();
            foreach (var existing in overlaps)
            {
                var existingFrom = existing.EffectiveFromDate.Date;
                var existingTo = existing.EffectiveToDate?.Date;
                var hasLeftPart = existingFrom < from;
                var hasRightPart = to.HasValue && existingTo.HasValue && existingTo.Value > to.Value;

                _context.EmployeeShifts.Remove(existing);
                if (hasLeftPart)
                {
                    _context.EmployeeShifts.Add(new EmployeeShift
                    {
                        EmployeeId = existing.EmployeeId,
                        ShiftId = existing.ShiftId,
                        EffectiveFromDate = existingFrom,
                        EffectiveToDate = from.AddDays(-1)
                    });
                }
                if (hasRightPart)
                {
                    _context.EmployeeShifts.Add(new EmployeeShift
                    {
                        EmployeeId = existing.EmployeeId,
                        ShiftId = existing.ShiftId,
                        EffectiveFromDate = to!.Value.AddDays(1),
                        EffectiveToDate = existingTo
                    });
                }
            }

            var assignment = new EmployeeShift
            {
                EmployeeId = request.EmployeeId,
                ShiftId = request.ShiftId,
                EffectiveFromDate = from,
                EffectiveToDate = to
            };

            _context.EmployeeShifts.Add(assignment);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            try
            {
                var shift = await _context.Shifts.FindAsync(request.ShiftId);
                string shiftName = shift?.ShiftName ?? "Mới";
                await _fcmService.SendNotificationToEmployeeAsync(
                    request.EmployeeId,
                    "Phân ca làm việc mới",
                    $"Bạn đã được phân ca {shiftName} từ ngày {from:dd/MM/yyyy}.",
                    "General");
            }
            catch (Exception) {}

            return assignment.Id;
        }

        private static bool IsValid(CreateShiftRequest request) =>
            request != null &&
            !string.IsNullOrWhiteSpace(request.ShiftCode) &&
            !string.IsNullOrWhiteSpace(request.ShiftName) &&
            request.StartTime >= TimeSpan.Zero && request.StartTime < TimeSpan.FromDays(1) &&
            request.EndTime >= TimeSpan.Zero && request.EndTime < TimeSpan.FromDays(1) &&
            request.StartTime != request.EndTime;

        private static ShiftDto ToDto(Shift shift) => new()
        {
            Id = shift.Id,
            ShiftCode = shift.ShiftCode,
            ShiftName = shift.ShiftName,
            StartTime = shift.StartTime,
            EndTime = shift.EndTime,
            IsActive = shift.IsActive
        };
    }
}
