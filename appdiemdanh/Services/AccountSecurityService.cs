using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Services;

public interface IAccountSecurityService
{
    Task RequestPasswordResetAsync(string identifier);
    Task<bool> VerifyPasswordResetOtpAsync(string identifier, string otp);
    Task<(bool Success, string Message)> ResetPasswordAsync(string identifier, string otp, string newPassword);
    Task RequestEmailVerificationAsync(int employeeId, string email);
    Task SendEmailVerificationOtpAsync(int employeeId);
    Task<bool> VerifyEmailAsync(int employeeId, string otp);
    Task<(bool Success, string Message)> ChangePasswordAsync(int employeeId, string currentPassword, string newPassword);
}

public class AccountSecurityService : IAccountSecurityService
{
    private const string PasswordResetPurpose = "PASSWORD_RESET";
    private const string EmailVerificationPurpose = "EMAIL_VERIFY";
    private const int OtpLifetimeMinutes = 10;
    private const int MaxFailedAttempts = 5;
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountSecurityService> _logger;
    private readonly PasswordHasher<string> _passwordHasher = new();

    public AccountSecurityService(AppDbContext context, IConfiguration configuration, ILogger<AccountSecurityService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task RequestPasswordResetAsync(string identifier)
    {
        var employee = await FindEmployeeAsync(identifier);
        if (employee?.IsActive != true || string.IsNullOrWhiteSpace(employee.Email) || !employee.IsEmailVerified) return;
        await CreateAndSendOtpAsync(employee, employee.Email, PasswordResetPurpose, "Mã OTP đặt lại mật khẩu");
    }

    public async Task<bool> VerifyPasswordResetOtpAsync(string identifier, string otp)
    {
        var employee = await FindEmployeeAsync(identifier);
        return employee != null && await ValidateOtpAsync(employee, employee.Email ?? string.Empty, PasswordResetPurpose, otp, false);
    }

    public async Task<(bool Success, string Message)> ResetPasswordAsync(string identifier, string otp, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            return (false, "Mật khẩu mới phải có ít nhất 6 ký tự.");

        var employee = await FindEmployeeAsync(identifier);
        if (employee == null || string.IsNullOrWhiteSpace(employee.Email) || !employee.IsEmailVerified)
            return (false, "Tài khoản không tồn tại hoặc email chưa được xác minh.");

        if (!await ValidateOtpAsync(employee, employee.Email, PasswordResetPurpose, otp, true))
            return (false, "OTP không đúng, đã hết hạn hoặc đã được sử dụng.");

        employee.PasswordHash = _passwordHasher.HashPassword(employee.EmployeeCode, newPassword);
        await _context.SaveChangesAsync();
        await SendCredentialEmailAsync(employee, newPassword);
        return (true, "Đặt lại mật khẩu thành công. Thông tin tài khoản đã được gửi tới email đã xác minh.");
    }

    public async Task RequestEmailVerificationAsync(int employeeId, string email)
    {
        var employee = await _context.Employees.FirstOrDefaultAsync(x => x.Id == employeeId && x.IsActive);
        if (employee == null) throw new InvalidOperationException("Không tìm thấy tài khoản.");
        email = NormalizeEmail(email);
        if (!MailAddress.TryCreate(email, out _)) throw new ArgumentException("Email không hợp lệ.");
        if (await _context.Employees.AnyAsync(x => x.Id != employeeId && x.Email == email)) throw new InvalidOperationException("Email đã được sử dụng.");
        employee.Email = email;
        employee.IsEmailVerified = false;
        await _context.SaveChangesAsync();
        await CreateAndSendOtpAsync(employee, email, EmailVerificationPurpose, "Mã OTP xác minh email");
    }

    public async Task<bool> VerifyEmailAsync(int employeeId, string otp)
    {
        var employee = await _context.Employees.FirstOrDefaultAsync(x => x.Id == employeeId && x.IsActive);
        if (employee == null || string.IsNullOrWhiteSpace(employee.Email)) return false;
        var valid = await ValidateOtpAsync(employee, employee.Email, EmailVerificationPurpose, otp, true);
        if (!valid) return false;
        employee.IsEmailVerified = true;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<(bool Success, string Message)> ChangePasswordAsync(int employeeId, string currentPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6) return (false, "Mật khẩu mới phải có ít nhất 6 ký tự.");
        var employee = await _context.Employees.FirstOrDefaultAsync(x => x.Id == employeeId && x.IsActive);
        if (employee == null) return (false, "Không tìm thấy tài khoản.");
        var result = _passwordHasher.VerifyHashedPassword(employee.EmployeeCode, employee.PasswordHash, currentPassword);
        if (result == PasswordVerificationResult.Failed && employee.PasswordHash != currentPassword) return (false, "Mật khẩu hiện tại không chính xác.");
        employee.PasswordHash = _passwordHasher.HashPassword(employee.EmployeeCode, newPassword);
        await _context.SaveChangesAsync();
        return (true, "Đổi mật khẩu thành công.");
    }

    public async Task SendEmailVerificationOtpAsync(int employeeId)
    {
        var employee = await _context.Employees.FirstOrDefaultAsync(x => x.Id == employeeId && x.IsActive);
        if (employee == null) throw new InvalidOperationException("Không tìm thấy tài khoản.");
        if (string.IsNullOrWhiteSpace(employee.Email)) throw new InvalidOperationException("Nhân viên chưa có email.");
        await CreateAndSendOtpAsync(employee, employee.Email, EmailVerificationPurpose, "Mã OTP xác minh email");
    }

    private async Task<Employee?> FindEmployeeAsync(string identifier)
    {
        identifier = identifier.Trim();
        return await _context.Employees.FirstOrDefaultAsync(x => x.IsActive &&
            (x.EmployeeCode == identifier || x.Email == identifier));
    }

    private async Task CreateAndSendOtpAsync(Employee employee, string email, string purpose, string subject)
    {
        var activeOtps = await _context.AccountOtps.Where(x => x.EmployeeId == employee.Id && x.Purpose == purpose && !x.IsUsed).ToListAsync();
        foreach (var item in activeOtps) item.IsUsed = true;
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var otp = new AccountOtp
        {
            EmployeeId = employee.Id,
            Email = email,
            CodeHash = HashOtp(code),
            Purpose = purpose,
            ExpiresAt = DateTime.UtcNow.AddMinutes(OtpLifetimeMinutes),
            CreatedAt = DateTime.UtcNow
        };
        _context.AccountOtps.Add(otp);
        await _context.SaveChangesAsync();
        var htmlBody = $"""
            <div style='font-family:Arial,sans-serif;line-height:1.6;color:#222'>
                <p>Xin chào <strong>{WebUtility.HtmlEncode(employee.FullName)}</strong>,</p>
                <p>Vui lòng sử dụng mã xác thực dưới đây để tiếp tục:</p>
                <p style='font-size:28px;letter-spacing:6px;font-weight:bold;color:#2563eb'>{code}</p>
                <p>Mã xác thực có hiệu lực trong <strong>{OtpLifetimeMinutes} phút</strong> và chỉ được sử dụng một lần.</p>
                <p>Nếu bạn không yêu cầu mã này, hãy bỏ qua email. Vui lòng không chia sẻ mã xác thực với bất kỳ ai.</p>
                <p>Trân trọng,<br/><strong>Hệ thống chấm công</strong></p>
            </div>
            """;
        await SendEmailAsync(email, subject, htmlBody);
    }

    private async Task<bool> ValidateOtpAsync(Employee employee, string email, string purpose, string code, bool consume)
    {
        var otp = await _context.AccountOtps.Where(x => x.EmployeeId == employee.Id && x.Email == email && x.Purpose == purpose && !x.IsUsed).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
        if (otp == null || otp.ExpiresAt < DateTime.UtcNow || otp.FailedAttempts >= MaxFailedAttempts) return false;
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(otp.CodeHash), Convert.FromHexString(HashOtp(code))))
        {
            otp.FailedAttempts++;
            await _context.SaveChangesAsync();
            return false;
        }
        if (consume) { otp.IsUsed = true; await _context.SaveChangesAsync(); }
        return true;
    }

    private string HashOtp(string code)
    {
        var secret = _configuration["Security:OtpSecret"] ?? throw new InvalidOperationException("Security:OtpSecret chưa được cấu hình.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{secret}:{code}")));
    }

    private async Task SendCredentialEmailAsync(Employee employee, string newPassword) =>
        await SendEmailAsync(employee.Email!, "Thông tin đăng nhập của bạn", $"""
            <div style='font-family:Arial,sans-serif;line-height:1.6;color:#222'>
                <p>Xin chào <strong>{WebUtility.HtmlEncode(employee.FullName)}</strong>,</p>
                <p>Mật khẩu của bạn đã được đặt lại thành công.</p>
                <p>Tên tài khoản: <strong>{WebUtility.HtmlEncode(employee.EmployeeCode)}</strong><br/>
                Mật khẩu: <strong>{WebUtility.HtmlEncode(newPassword)}</strong></p>
                <p>Vui lòng đăng nhập và đổi mật khẩu ngay sau lần đăng nhập đầu tiên.</p>
                <p>Nếu bạn không thực hiện yêu cầu này, hãy liên hệ quản trị viên.</p>
                <p>Trân trọng,<br/><strong>Hệ thống chấm công</strong></p>
            </div>
            """);

    private async Task SendEmailAsync(string recipient, string subject, string htmlBody)
    {
        var host = _configuration["Smtp:Host"];
        var port = _configuration.GetValue<int?>("Smtp:Port") ?? 587;
        var username = _configuration["Smtp:Username"];
        var password = _configuration["Smtp:Password"];
        var from = _configuration["Smtp:From"] ?? username;
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("SMTP chưa được cấu hình.");
        using var message = new MailMessage(from, recipient, subject, htmlBody) { IsBodyHtml = true };
        using var client = new SmtpClient(host, port) { EnableSsl = _configuration.GetValue("Smtp:EnableSsl", true), Credentials = new NetworkCredential(username, password) };
        await client.SendMailAsync(message);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
