namespace AppDiemDanh.Models;

public sealed class PayrollHoliday
{
    public long Id { get; set; }
    public DateTime HolidayDate { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
