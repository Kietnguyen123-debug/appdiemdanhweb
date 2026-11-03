using System;
using System.ComponentModel.DataAnnotations;

namespace AppDiemDanh.DTOs
{
    public class ShiftDto
    {
        public int Id { get; set; }
        public string ShiftCode { get; set; } = string.Empty;
        public string ShiftName { get; set; } = string.Empty;
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public bool IsActive { get; set; }
    }

    public class CreateShiftRequest
    {
        [Required, StringLength(50, MinimumLength = 1)]
        public string ShiftCode { get; set; } = string.Empty;
        [Required, StringLength(200, MinimumLength = 1)]
        public string ShiftName { get; set; } = string.Empty;
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class AssignShiftRequest
    {
        [Range(1, int.MaxValue)]
        public int EmployeeId { get; set; }
        [Range(1, int.MaxValue)]
        public int ShiftId { get; set; }
        [Required]
        public DateTime EffectiveFromDate { get; set; }
        public DateTime? EffectiveToDate { get; set; }
    }
}
