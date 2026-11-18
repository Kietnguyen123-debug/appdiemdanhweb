namespace AppDiemDanh.Services;

public sealed record PayrollLeavePeriod(DateTime StartDate, DateTime EndDate, bool IsPaidLeave);

public sealed record PayrollWorkdaySummary(
    IReadOnlySet<DateTime> ScheduledWorkingDates,
    IReadOnlySet<DateTime> ActualWorkDates,
    IReadOnlySet<DateTime> PaidLeaveDates)
{
    public decimal ActualWorkDays => ActualWorkDates.Count;
    public decimal PaidLeaveDays => PaidLeaveDates.Count;
    public decimal TotalInsuranceDays => ActualWorkDates.Count + PaidLeaveDates.Count;
}

public sealed class PayrollWorkdayCalculator
{
    public IReadOnlySet<DateTime> CreateLegacyWeekdaySchedule(DateTime from, DateTime to)
    {
        var start = from.Date;
        var end = to.Date;
        if (end < start) throw new ArgumentException("Payroll schedule end date cannot be before its start date.");

        return Enumerable.Range(0, (end - start).Days + 1)
            .Select(offset => start.AddDays(offset))
            .Where(date => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            .ToHashSet();
    }

    public PayrollWorkdaySummary Calculate(
        IEnumerable<DateTime> scheduledWorkDates,
        IEnumerable<DateTime> attendanceDates,
        IEnumerable<PayrollLeavePeriod> leavePeriods,
        IEnumerable<DateTime> holidayDates)
    {
        ArgumentNullException.ThrowIfNull(scheduledWorkDates);
        ArgumentNullException.ThrowIfNull(attendanceDates);
        ArgumentNullException.ThrowIfNull(leavePeriods);
        ArgumentNullException.ThrowIfNull(holidayDates);

        var holidays = holidayDates.Select(x => x.Date).ToHashSet();
        var scheduled = scheduledWorkDates
            .Select(x => x.Date)
            .Where(x => x.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !holidays.Contains(x))
            .ToHashSet();
        var attendance = attendanceDates.Select(x => x.Date).ToHashSet();
        var actualWork = scheduled.Where(attendance.Contains).ToHashSet();
        var paidLeave = new HashSet<DateTime>();

        foreach (var leave in leavePeriods.Where(x => x.IsPaidLeave))
        {
            var from = leave.StartDate.Date;
            var to = leave.EndDate.Date;
            if (to < from) continue;

            foreach (var date in scheduled)
            {
                if (date >= from && date <= to && !actualWork.Contains(date))
                    paidLeave.Add(date);
            }
        }

        return new PayrollWorkdaySummary(scheduled, actualWork, paidLeave);
    }
}
