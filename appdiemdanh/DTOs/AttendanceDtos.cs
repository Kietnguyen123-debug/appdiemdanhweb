using System;
using System.ComponentModel.DataAnnotations;

namespace AppDiemDanh.DTOs
{
    public class AttendanceRequest
    {
        [Required]
        public string Bssid { get; set; } = string.Empty;
        [Required]
        public string DeviceId { get; set; } = string.Empty;
        [Required]
        public string FaceImageBase64 { get; set; } = string.Empty; // Ảnh chụp khuôn mặt Base64

        /// <summary>
        /// IP Public do Mobile App tự đọc và gửi lên.
        /// </summary>
        public string? ClientPublicIp { get; set; }
    }

    public class RegisterFaceRequest
    {
        [Range(1, int.MaxValue)]
        public int EmployeeId { get; set; }
        [Required]
        public string FaceImageBase64 { get; set; } = string.Empty;
    }

    public class AttendanceResponse
    {
        public bool Success { get; set; }
        public string ErrorCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Action { get; set; } = string.Empty;
        public double FaceConfidence { get; set; }
        public int OvertimeMinutes { get; set; }
        public FaceVerificationDebugDto? Debug { get; set; }
    }

    public class FaceVerificationDebugDto
    {
        public double SimilarityScore { get; set; }
        public double RequiredThreshold { get; set; }
        public int DetectedFaces { get; set; }
        public int DbVectorDimension { get; set; }
        public int AppVectorDimension { get; set; }
    }

    public class AddIpRequest
    {
        [Range(1, int.MaxValue)]
        public int BranchId { get; set; }
        public string? PublicIp { get; set; } // Để trống để backend tự nhận diện IP public của request.
        public string? Description { get; set; }
    }

    public class RegisterWifiRequest
    {
        [Range(1, int.MaxValue)]
        public int BranchId { get; set; }
        [Required, StringLength(32, MinimumLength = 1)]
        public string SSID { get; set; } = string.Empty;     // Tên Wi-Fi
        [Required]
        public string BSSID { get; set; } = string.Empty;    // MAC Address
        public bool IsAutoCaptured { get; set; } = true;     // True: Quét tự động, False: Gõ tay
    }
}
