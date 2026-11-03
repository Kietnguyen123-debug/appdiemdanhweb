using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Net.Mail;
using System.Threading.Tasks;
using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using AppDiemDanh.Services.Face;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace AppDiemDanh.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private readonly IFaceRecognitionService _faceAiService;
        private readonly IAccountSecurityService _accountSecurityService;
        private readonly PasswordHasher<string> _passwordHasher;

        public AuthController(AppDbContext context, IConfiguration config, IFaceRecognitionService faceAiService, IAccountSecurityService accountSecurityService)
        {
            _context = context;
            _config = config;
            _faceAiService = faceAiService;
            _accountSecurityService = accountSecurityService;
            _passwordHasher = new PasswordHasher<string>();
        }

        [Authorize]
        [HttpGet("me")]
        [ProducesResponseType(typeof(EmployeeMeResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Me()
        {
            if (!TryGetEmployeeId(out var employeeId)) return Unauthorized();
            var employee = await _context.Employees.AsNoTracking().Where(x => x.Id == employeeId && x.IsActive).Select(x => new EmployeeMeResponse { Id = x.Id, EmployeeCode = x.EmployeeCode, FullName = x.FullName, Role = x.Role, BranchId = x.BranchId, Email = x.Email, IsEmailVerified = x.IsEmailVerified, IsActivated = x.IsActivated }).FirstOrDefaultAsync();
            return employee == null ? NotFound() : Ok(employee);
        }

        [Authorize]
        [HttpPost("change-password")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            if (!TryGetEmployeeId(out var employeeId)) return Unauthorized();
            var result = await _accountSecurityService.ChangePasswordAsync(employeeId, request?.CurrentPassword ?? "", request?.NewPassword ?? "");
            return result.Success ? Ok(new MessageResponse { Message = result.Message }) : BadRequest(new { message = result.Message });
        }

        [AllowAnonymous]
        [HttpPost("refresh-token")]
        [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.RefreshToken)) return BadRequest(new { message = "Refresh token không hợp lệ." });
            var hash = HashToken(request.RefreshToken);
            var stored = await _context.RefreshTokens.Include(x => x.Employee).FirstOrDefaultAsync(x => x.TokenHash == hash && x.RevokedAt == null && x.ExpiresAt > DateTime.UtcNow && x.Employee!.IsActive);
            if (stored == null) return Unauthorized(new { message = "Refresh token đã hết hạn hoặc đã bị thu hồi." });
            stored.RevokedAt = DateTime.UtcNow;
            string access = GenerateJwtToken(stored.Employee!);
            string refresh = await CreateRefreshTokenAsync(stored.EmployeeId);
            await _context.SaveChangesAsync();
            return Ok(new AuthTokenResponse { AccessToken = access, RefreshToken = refresh, ExpiresAt = DateTime.UtcNow.AddDays(30) });
        }

        [Authorize]
        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest? request)
        {
            if (!TryGetEmployeeId(out var employeeId)) return Unauthorized();
            if (!string.IsNullOrWhiteSpace(request?.RefreshToken))
            {
                var hash = HashToken(request.RefreshToken);
                var token = await _context.RefreshTokens.FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.TokenHash == hash && x.RevokedAt == null);
                if (token != null) token.RevokedAt = DateTime.UtcNow;
            }
            else await _context.RefreshTokens.Where(x => x.EmployeeId == employeeId && x.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [AllowAnonymous]
        [HttpPost("request-password-reset")]
        [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> RequestPasswordReset([FromBody] RequestPasswordResetRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.EmployeeCodeOrEmail))
                return BadRequest(new { success = false, message = "Vui lòng nhập mã nhân viên hoặc email." });
            try { await _accountSecurityService.RequestPasswordResetAsync(request.EmployeeCodeOrEmail); }
            catch (InvalidOperationException) { }
            return Ok(new ApiMessageResponse { Success = true, Message = "Nếu tài khoản hợp lệ và email đã xác minh, OTP đã được gửi." });
        }

        [AllowAnonymous]
        [HttpPost("verify-reset-otp")]
        [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> VerifyResetOtp([FromBody] VerifyPasswordResetRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.EmployeeCodeOrEmail) || string.IsNullOrWhiteSpace(request.Otp))
                return BadRequest(new { success = false, message = "Vui lòng nhập tài khoản và OTP." });
            var valid = await _accountSecurityService.VerifyPasswordResetOtpAsync(request.EmployeeCodeOrEmail, request.Otp);
            return valid ? Ok(new ApiMessageResponse { Success = true, Message = "OTP hợp lệ." }) : BadRequest(new { success = false, message = "OTP không đúng hoặc đã hết hạn." });
        }

        [AllowAnonymous]
        [HttpPost("reset-password")]
        [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.EmployeeCodeOrEmail) || string.IsNullOrWhiteSpace(request.Otp) || string.IsNullOrWhiteSpace(request.NewPassword))
                return BadRequest(new { success = false, message = "Vui lòng nhập đầy đủ tài khoản, OTP và mật khẩu mới." });
            var result = await _accountSecurityService.ResetPasswordAsync(request.EmployeeCodeOrEmail, request.Otp, request.NewPassword);
            return result.Success ? Ok(new ApiMessageResponse { Success = true, Message = result.Message }) : BadRequest(new { success = false, message = result.Message });
        }

        [Authorize]
        [HttpPost("request-email-verify")]
        [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> RequestEmailVerify([FromBody] RequestEmailVerificationRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email))
                return BadRequest(new { success = false, message = "Vui lòng nhập email." });
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId)) return Unauthorized();
            try
            {
                await _accountSecurityService.RequestEmailVerificationAsync(employeeId, request.Email);
                return Ok(new ApiMessageResponse { Success = true, Message = "OTP xác minh email đã được gửi." });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [Authorize]
        [HttpPost("verify-email-otp")]
        [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> VerifyEmailOtp([FromBody] VerifyEmailRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Otp))
                return BadRequest(new { success = false, message = "Vui lòng nhập OTP." });
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId)) return Unauthorized();
            var valid = await _accountSecurityService.VerifyEmailAsync(employeeId, request.Otp);
            return valid ? Ok(new ApiMessageResponse { Success = true, Message = "Email đã được xác minh." }) : BadRequest(new { success = false, message = "OTP không đúng hoặc đã hết hạn." });
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpPost("create-employee")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateEmployee([FromBody] CreateEmployeeAccountRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.EmployeeCode) || string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.TemporaryPassword))
                return BadRequest(new AuthResponse { Success = false, Message = "Mã nhân viên, họ tên và mật khẩu tạm không được để trống." });
            request.EmployeeCode = request.EmployeeCode.Trim();
            request.FullName = request.FullName.Trim();
            request.Role = request.Role?.Trim() ?? string.Empty;
            if (request.TemporaryPassword.Length < 6)
                return BadRequest(new AuthResponse { Success = false, Message = "Mật khẩu tạm phải có ít nhất 6 ký tự." });
            if (request.Role is not ("Admin" or "HR" or "Manager" or "Employee"))
                return BadRequest(new AuthResponse { Success = false, Message = "Vai trò không hợp lệ." });
            if (!string.IsNullOrWhiteSpace(request.Email) &&
                (!MailAddress.TryCreate(request.Email.Trim(), out var email) || email.Address.Length > 320))
                return BadRequest(new AuthResponse { Success = false, Message = "Email không hợp lệ." });
            request.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
            if (!await _context.Branches.AnyAsync(x => x.Id == request.BranchId && x.IsActive))
                return BadRequest(new AuthResponse { Success = false, Message = "Chi nhánh không tồn tại hoặc đã bị khóa." });
            if (await _context.Employees.AnyAsync(e => e.EmployeeCode == request.EmployeeCode))
                return BadRequest(new AuthResponse { Success = false, Message = "Mã nhân viên đã tồn tại!" });

            var employee = new Employee
            {
                EmployeeCode = request.EmployeeCode,
                FullName = request.FullName,
                BranchId = request.BranchId,
                Role = request.Role,
                IsActivated = !RequiresFirstTimeActivation(request.Role),
                PasswordHash = _passwordHasher.HashPassword(request.EmployeeCode, request.TemporaryPassword)
            };

            _context.Employees.Add(employee);
            await _context.SaveChangesAsync();

            return Ok(new AuthResponse
            {
                Success = true,
                Message = RequiresFirstTimeActivation(request.Role)
                    ? "Tạo tài khoản thành công! Hãy gửi Mã NV và Mật khẩu tạm cho nhân viên kích hoạt trên App."
                    : "Tạo tài khoản thành công! Tài khoản đã được kích hoạt và có thể đăng nhập ngay."
            });
        }

        [AllowAnonymous]
        [HttpPost("activate-first-time")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ActivateFirstTime([FromBody] FirstTimeActivationRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.EmployeeCode) || string.IsNullOrWhiteSpace(request.TemporaryPassword) || string.IsNullOrWhiteSpace(request.NewPassword) || string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.FaceImageBase64))
                return BadRequest(new AuthResponse { Success = false, Message = "Vui lòng nhập đầy đủ thông tin kích hoạt." });
            if (request.NewPassword.Length < 6)
                return BadRequest(new AuthResponse { Success = false, Message = "Mật khẩu mới phải có ít nhất 6 ký tự." });
            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.EmployeeCode == request.EmployeeCode && e.IsActive);
            if (employee == null)
                return BadRequest(new AuthResponse { Success = false, Message = "Tài khoản không tồn tại hoặc đã bị khóa!" });

            if (employee.IsActivated)
                return BadRequest(new AuthResponse { Success = false, Message = "Tài khoản này đã được kích hoạt trước đó! Vui lòng đăng nhập." });

            var passResult = _passwordHasher.VerifyHashedPassword(employee.EmployeeCode, employee.PasswordHash, request.TemporaryPassword);
            if (passResult == PasswordVerificationResult.Failed)
                return BadRequest(new AuthResponse { Success = false, Message = "Mật khẩu tạm thời không chính xác!" });

            // Trích xuất ONNX Vector khuôn mặt gốc
            float[] faceVector = await _faceAiService.ExtractEmbeddingVectorAsync(request.FaceImageBase64);
            if (faceVector == null || faceVector.Length == 0)
                return BadRequest(new AuthResponse { Success = false, Message = "Không thể nhận diện khuôn mặt trong ảnh! Vui lòng chụp lại." });

            employee.PasswordHash = _passwordHasher.HashPassword(employee.EmployeeCode, request.NewPassword);
            employee.FaceVector = JsonSerializer.Serialize(faceVector);
            employee.BoundDeviceId = request.DeviceId; 
            employee.IsActivated = true;

            await _context.SaveChangesAsync();

            string token = GenerateJwtToken(employee);
            string refreshToken = await CreateRefreshTokenAsync(employee.Id);

            return Ok(new AuthResponse
            {
                Success = true,
                Message = "Kích hoạt tài khoản và đăng ký khuôn mặt thành công!",
                Token = token,
                RefreshToken = refreshToken,
                IsActivated = true,
                UserInfo = new AuthUserInfo { Id = employee.Id, EmployeeCode = employee.EmployeeCode, FullName = employee.FullName, Role = employee.Role, BranchId = employee.BranchId }
            });
        }

        [AllowAnonymous]
        [HttpPost("login")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                if (request == null)
                    return BadRequest(new AuthResponse { Success = false, Message = "Vui lòng gửi dữ liệu đăng nhập hợp lệ." });

                var employeeCode = new[] { request.EmployeeCode, request.EmployeeCodeSnakeCase, request.Username }
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
                var deviceId = new[] { request.DeviceId, request.DeviceIdSnakeCase }
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
                if (string.IsNullOrWhiteSpace(employeeCode) || string.IsNullOrWhiteSpace(request.Password))
                    return BadRequest(new AuthResponse { Success = false, Message = "Vui lòng nhập mã nhân viên và mật khẩu." });

                var employee = await _context.Employees.FirstOrDefaultAsync(e => e.EmployeeCode == employeeCode && e.IsActive);
                if (employee == null)
                    return Unauthorized(new AuthResponse { Success = false, Message = "Mã nhân viên hoặc mật khẩu không chính xác!" });

                if (string.IsNullOrEmpty(employee.PasswordHash))
                {
                    return Unauthorized(new AuthResponse { Success = false, Message = "Tài khoản chưa được thiết lập mật khẩu. Vui lòng liên hệ Admin." });
                }

                PasswordVerificationResult passResult = PasswordVerificationResult.Failed;
                bool isLegacyPassword = false;
                try
                {
                    passResult = _passwordHasher.VerifyHashedPassword(employee.EmployeeCode, employee.PasswordHash, request.Password);
                    isLegacyPassword = employee.PasswordHash == request.Password;
                }
                catch
                {
                    isLegacyPassword = employee.PasswordHash == request.Password;
                }

                if (passResult == PasswordVerificationResult.Failed && !isLegacyPassword)
                {
                    return Unauthorized(new AuthResponse { Success = false, Message = "Mã nhân viên hoặc mật khẩu không chính xác" });
                }

                if (isLegacyPassword)
                {
                    employee.PasswordHash = _passwordHasher.HashPassword(employee.EmployeeCode, request.Password);
                    await _context.SaveChangesAsync();
                }

                if (RequiresFirstTimeActivation(employee.Role) && !employee.IsActivated)
                {
                    return BadRequest(new AuthResponse { Success = false, Message = "Tài khoản chưa được kích hoạt lần đầu trên App Mobile!", IsActivated = false });
                }

                if (employee.Role == "Employee")
                {
                    if (string.IsNullOrWhiteSpace(deviceId))
                    {
                        return BadRequest(new AuthResponse { Success = false, Message = "Thông tin thiết bị không hợp lệ." });
                    }

                    if (string.IsNullOrEmpty(employee.BoundDeviceId))
                    {
                        employee.BoundDeviceId = deviceId;
                        await _context.SaveChangesAsync();
                    }
                    else if (employee.BoundDeviceId != deviceId)
                    {
                        return BadRequest(new AuthResponse
                        {
                            Success = false,
                            Message = "Tài khoản này đã gắn liền với thiết bị khác! Vui lòng liên hệ HR để mở khóa máy (Unbind Device)."
                        });
                    }
                }

                string token = GenerateJwtToken(employee);
                string refreshToken = await CreateRefreshTokenAsync(employee.Id);

                return Ok(new AuthResponse
                {
                    Success = true,
                    Message = "Đăng nhập thành công!",
                    Token = token,
                    RefreshToken = refreshToken,
                    IsActivated = true,
                    UserInfo = new AuthUserInfo { Id = employee.Id, EmployeeCode = employee.EmployeeCode, FullName = employee.FullName, Role = employee.Role, BranchId = employee.BranchId }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new AuthResponse
                {
                    Success = false,
                    Message = $"Lỗi xử lý hệ thống Backend: {ex.Message}"
                });
            }
        }

        private static bool RequiresFirstTimeActivation(string? role) =>
            string.Equals(role?.Trim(), "Employee", StringComparison.OrdinalIgnoreCase);

        private bool TryGetEmployeeId(out int employeeId) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out employeeId) && employeeId > 0;

        private async Task<string> CreateRefreshTokenAsync(int employeeId)
        {
            try
            {
                string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
                _context.RefreshTokens.Add(new RefreshToken { EmployeeId = employeeId, TokenHash = HashToken(token), ExpiresAt = DateTime.UtcNow.AddDays(30) });
                await _context.SaveChangesAsync();
                return token;
            }
            catch
            {
                return Guid.NewGuid().ToString();
            }
        }

        private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private string GenerateJwtToken(Employee employee)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"] ?? "ChuoiBaoMatSieuCapVipPro1234567890!");

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, employee.Id.ToString()),
                new Claim(ClaimTypes.Name, employee.FullName),
                new Claim(ClaimTypes.Role, employee.Role),
                new Claim("BranchId", employee.BranchId.ToString()),
                new Claim("EmployeeCode", employee.EmployeeCode)
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddDays(30),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}