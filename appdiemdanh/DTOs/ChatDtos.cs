namespace appdiemdanh.DTOs
{
    public class ChatRequestDto
    {
        public string Message { get; set; } = string.Empty;

        public List<ChatMessageHistoryDto>? History { get; set; }
    }

    public class ChatMessageHistoryDto
    {
        public string Role { get; set; } = string.Empty; 
        public string Text { get; set; } = string.Empty;
    }

    public class ChatResponseDto
    {
        public bool Success { get; set; } = true;
        public string Reply { get; set; } = string.Empty;
        public string? ExecutedAction { get; set; }
    }

    public class UserJwtContext
    {
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int BranchId { get; set; }
    }
}