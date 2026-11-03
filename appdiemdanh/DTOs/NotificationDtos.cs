namespace AppDiemDanh.DTOs
{
    public class RegisterFcmTokenRequest
    {
        public string FcmToken { get; set; } = string.Empty;
        public string? DeviceId { get; set; }
    }

    public class NotificationLogDto
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class NotificationPageDto
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public IReadOnlyList<NotificationLogDto> Items { get; set; } = Array.Empty<NotificationLogDto>();
    }

    public class FinalizePayrollRequest
    {
        public int Month { get; set; }
        public int Year { get; set; }
        public int StandardWorkDays { get; set; } = 26;
    }
}
