using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AppDiemDanh.DTOs
{
    // Admin/HR tạo tài khoản nhân viên ban đầu
    public class CreateEmployeeAccountRequest
    {
        [Required, StringLength(50, MinimumLength = 1)]
        public string EmployeeCode { get; set; } = string.Empty;
        [Required, StringLength(200, MinimumLength = 1)]
        public string FullName { get; set; } = string.Empty;
        [Range(1, int.MaxValue)]
        public int BranchId { get; set; }
        public int? DepartmentId { get; set; }
        [Required, StringLength(128, MinimumLength = 6)]
        public string TemporaryPassword { get; set; } = string.Empty;
        [Required]
        public string Role { get; set; } = "Employee"; // Admin, HR, Manager, Employee
        [EmailAddress, StringLength(320)]
        public string? Email { get; set; }
    }

    // Nhân viên kích hoạt lần đầu trên App Mobile
    public class FirstTimeActivationRequest
    {
        public string EmployeeCode { get; set; } = string.Empty;
        public string TemporaryPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;          // Khóa ID thiết bị
        public string FaceImageBase64 { get; set; } = string.Empty;   // Ảnh chụp mặt để trích xuất Vector ONNX
    }

    // Đăng nhập chuẩn
    public class LoginRequest
    {
        public string EmployeeCode { get; set; } = string.Empty;
        [JsonPropertyName("username")]
        public string? Username { get; set; }
        [JsonPropertyName("employee_code")]
        public string? EmployeeCodeSnakeCase { get; set; }
        public string Password { get; set; } = string.Empty;
        public string? DeviceId { get; set; }                         // Bắt buộc đối với Role Employee
        [JsonPropertyName("device_id")]
        public string? DeviceIdSnakeCase { get; set; }
    }

    public class AuthResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public bool IsActivated { get; set; }
        public AuthUserInfo? UserInfo { get; set; }
    }

    public class AuthUserInfo
    {
        public int Id { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int BranchId { get; set; }
        public int? DepartmentId { get; set; }
    }

    public class UpdateEmployeeRequest
    {
        [Required, StringLength(200, MinimumLength = 1)]
        public string FullName { get; set; } = string.Empty;
        [Range(1, int.MaxValue)]
        public int BranchId { get; set; }
        public int? DepartmentId { get; set; }
        [Required]
        public string Role { get; set; } = "Employee"; // Admin, HR, Manager, Employee
    }

    public class AdminResetPasswordRequest
    {
        [Required, StringLength(128, MinimumLength = 6)]
        public string NewTemporaryPassword { get; set; } = string.Empty;
    }

    public class RequestPasswordResetRequest
    {
        public string EmployeeCodeOrEmail { get; set; } = string.Empty;
    }

    public class VerifyPasswordResetRequest
    {
        public string EmployeeCodeOrEmail { get; set; } = string.Empty;
        public string Otp { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        public string EmployeeCodeOrEmail { get; set; } = string.Empty;
        public string Otp { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class RequestEmailVerificationRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public class VerifyEmailRequest
    {
        public string Otp { get; set; } = string.Empty;
    }
}