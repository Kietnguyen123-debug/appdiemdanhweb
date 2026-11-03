using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Services;

public interface ILatePenaltyService
{
    Task<decimal> CalculatePenaltyAmountAsync(int lateMinutes, CancellationToken cancellationToken = default);
}

public sealed class LatePenaltyService : ILatePenaltyService
{
    private readonly AppDbContext _context;

    public LatePenaltyService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<decimal> CalculatePenaltyAmountAsync(int lateMinutes, CancellationToken cancellationToken = default)
    {
        if (lateMinutes <= 0)
            return 0m;

        var policy = await _context.LatePenaltyPolicies
            .AsNoTracking()
            .Include(x => x.Ranges)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (policy == null)
            return 0m;

        if (policy.Type == PenaltyType.PerMinute)
            return Math.Round(lateMinutes * (policy.AmountPerMinute ?? 0m), 0);

        var range = policy.Ranges.FirstOrDefault(x => x.FromMinute <= lateMinutes && lateMinutes <= x.ToMinute);
        return range?.PenaltyAmount ?? 0m;
    }
}
