using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Models
{
    public class Branch
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        public ICollection<BranchPublicIp> PublicIps { get; set; } = new List<BranchPublicIp>();
        public ICollection<BranchWifi> Wifis { get; set; } = new List<BranchWifi>();
    }

    public class AttendanceAdjustment
    {
        public long Id { get; set; }
        public long AttendanceId { get; set; }
        public int AdminEmployeeId { get; set; }
        public DateTime? PreviousCheckInTime { get; set; }
        public DateTime? PreviousCheckOutTime { get; set; }
        public DateTime? NewCheckInTime { get; set; }
        public DateTime? NewCheckOutTime { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Attendance? Attendance { get; set; }
        public Employee? AdminEmployee { get; set; }
    }

    public class BranchPublicIp
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public string PublicIp { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        public Branch? Branch { get; set; }
        public ICollection<EmployeeShift> EmployeeShifts { get; set; } = new List<EmployeeShift>();
        public ICollection<AttendanceExplanation> AttendanceExplanations { get; set; } = new List<AttendanceExplanation>();
        public ICollection<EmployeeFcmToken> FcmTokens { get; set; } = new List<EmployeeFcmToken>();
        public ICollection<NotificationLog> NotificationLogs { get; set; } = new List<NotificationLog>();
    }

    public class BranchWifi
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public string SSID { get; set; } = string.Empty;
        public string BSSID { get; set; } = string.Empty; // Luôn lưu chữ thường
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        public Branch? Branch { get; set; }
    }

    public class Department
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    }

    public class Employee
    {
        public int Id { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int BranchId { get; set; }
        public int? DepartmentId { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = "Employee";
        public int? RoleId { get; set; }
        public bool IsActivated { get; set; } = false; // Trạng thái kích hoạt lần đầu
        public string? BoundDeviceId { get; set; }
        public string? FaceVector { get; set; }
        public string? Email { get; set; }
        public bool IsEmailVerified { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public decimal BaseSalary { get; set; } = 0;               // Lương cơ bản tháng
        public decimal Allowance { get; set; } = 0;                // Phụ cấp cố định
        public decimal PenaltyPerLateMinute { get; set; } = 2000;
        public int DependentCount { get; set; } = 0;
        public decimal? SocialInsuranceBase { get; set; }
        public int TotalAnnualLeave { get; set; } = 12;
        public int UsedAnnualLeave { get; set; } = 0;

        public Branch? Branch { get; set; }
        public Department? Department { get; set; }
        public ICollection<EmployeeShift> EmployeeShifts { get; set; } = new List<EmployeeShift>();
        public ICollection<AttendanceExplanation> AttendanceExplanations { get; set; } = new List<AttendanceExplanation>();
        public ICollection<EmployeeFcmToken> FcmTokens { get; set; } = new List<EmployeeFcmToken>();
        public ICollection<NotificationLog> NotificationLogs { get; set; } = new List<NotificationLog>();
        public ICollection<SalaryAdvance> SalaryAdvances { get; set; } = new List<SalaryAdvance>();
        public ICollection<SalaryAdvance> ApprovedSalaryAdvances { get; set; } = new List<SalaryAdvance>();
        public ICollection<OvertimeRecord> OvertimeRecords { get; set; } = new List<OvertimeRecord>();
        public ICollection<AccountOtp> AccountOtps { get; set; } = new List<AccountOtp>();
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
        public ICollection<PayrollSnapshot> PayrollSnapshots { get; set; } = new List<PayrollSnapshot>();
        public ICollection<EmployeeSalaryConfig> SalaryConfigs { get; set; } = new List<EmployeeSalaryConfig>();
        public Role? PermissionRole { get; set; }
    }

    public class Role
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsSystemDefault { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Employee> Employees { get; set; } = new List<Employee>();
        public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    }

    public class Permission
    {
        public int Id { get; set; }
        public string Module { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    }

    public class RolePermission
    {
        public int RoleId { get; set; }
        public int PermissionId { get; set; }

        public Role Role { get; set; } = null!;
        public Permission Permission { get; set; } = null!;
    }

    public class EmployeeSalaryConfig
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public decimal BaseSalary { get; set; }
        public decimal TotalAllowances { get; set; }
        public decimal InsuranceSalary { get; set; }
        public decimal PenaltyPerLateMinute { get; set; }
        public int DependentCount { get; set; }

        // Bổ sung các trường thông tin thanh toán
        public string PaymentMethod { get; set; } = "BankTransfer";
        public string? BankName { get; set; }
        public string? BankAccountNo { get; set; }
        public string? BankAccountHolder { get; set; }

        public DateTime EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public bool IsActive { get; set; } = true;

        public Employee? Employee { get; set; }
    }

    public enum PenaltyType
    {
        FixedByRange = 1,
        PerMinute = 2
    }

    public class LatePenaltyPolicy
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public PenaltyType Type { get; set; }
        public decimal? AmountPerMinute { get; set; }
        public bool IsActive { get; set; }

        public ICollection<LatePenaltyRange> Ranges { get; set; } = new List<LatePenaltyRange>();
    }

    public class LatePenaltyRange
    {
        public int Id { get; set; }
        public int PolicyId { get; set; }
        public int FromMinute { get; set; }
        public int ToMinute { get; set; }
        public decimal PenaltyAmount { get; set; }

        public LatePenaltyPolicy? Policy { get; set; }
    }

    public class RefreshToken
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RevokedAt { get; set; }

        public Employee? Employee { get; set; }
    }

    public class PayrollSnapshot
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int Month { get; set; }
        public int Year { get; set; }
        public int StandardWorkDays { get; set; }
        public decimal ActualWorkDays { get; set; }

        public decimal BaseSalary { get; set; }
        public decimal GrossSalary { get; set; }
        public decimal OvertimeSalary { get; set; }
        public decimal Allowance { get; set; }
        public decimal InsuranceDeduction { get; set; }
        public decimal Pit { get; set; }
        public decimal SalaryAdvanceDeduction { get; set; }
        public decimal NetSalary { get; set; }

        // Bổ sung các trường thông tin thanh toán lưu vết
        public string PaymentMethod { get; set; } = "BankTransfer";
        public string? BankName { get; set; }
        public string? BankAccountNo { get; set; }
        public string? BankAccountHolder { get; set; }

        public string Status { get; set; } = "Approved";
        public string? Note { get; set; }
        public string PayslipJson { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int? CreatedBy { get; set; }
        public DateTime? PaidAt { get; set; }

        public Employee? Employee { get; set; }
    }

    public class AccountOtp
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string CodeHash { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public int FailedAttempts { get; set; }
        public bool IsUsed { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Employee? Employee { get; set; }
    }

    public class SalaryAdvance
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime RequestDate { get; set; } = DateTime.Now;
        public string Status { get; set; } = "Pending";
        public int? ApprovedById { get; set; }
        public string? ReviewNote { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Employee? Employee { get; set; }
        public Employee? ApprovedBy { get; set; }
    }

    public class OvertimeRecord
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public DateTime WorkDate { get; set; }
        public double Hours { get; set; }
        public double Multiplier { get; set; } = 1.5;
        public string Status { get; set; } = "Pending";

        public Employee? Employee { get; set; }
    }

    public class Attendance
    {
        public long AttendanceId { get; set; }
        public int EmployeeId { get; set; }
        public int BranchId { get; set; }
        public DateTime WorkDate { get; set; }

        // Check-in
        public DateTime CheckInTime { get; set; }
        public string CheckInIp { get; set; } = string.Empty;
        public string CheckInBssid { get; set; } = string.Empty;
        public double CheckInFaceConfidence { get; set; }
        public string CheckInDeviceId { get; set; } = string.Empty;

        // Check-out (Nullable)
        public DateTime? CheckOutTime { get; set; }
        public string? CheckOutIp { get; set; }
        public string? CheckOutBssid { get; set; }
        public double? CheckOutFaceConfidence { get; set; }
        public string? CheckOutDeviceId { get; set; }

        // Computed Fields
        public int LateMinutes { get; set; }
        public int EarlyLeaveMinutes { get; set; }
        public int WorkedMinutes { get; set; }
        public int OvertimeMinutes { get; set; }

        public string Status { get; set; } = "PRESENT";
        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public Employee? Employee { get; set; }
        public Branch? Branch { get; set; }
    }

    public class AttendanceAttempt
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public int? BranchId { get; set; }
        public DateTime AttemptTime { get; set; } = DateTime.Now;
        public string PublicIp { get; set; } = string.Empty;
        public string Bssid { get; set; } = string.Empty;
        public double FaceConfidence { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // SUCCESS / FAILED
        public string RejectReason { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class Shift
    {
        public int Id { get; set; }
        public string ShiftCode { get; set; } = string.Empty;
        public string ShiftName { get; set; } = string.Empty;
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public bool IsActive { get; set; } = true;

        public ICollection<EmployeeShift> EmployeeShifts { get; set; } = new List<EmployeeShift>();
    }

    public class EmployeeShift
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public int ShiftId { get; set; }
        public DateTime EffectiveFromDate { get; set; }
        public DateTime? EffectiveToDate { get; set; }

        public Employee? Employee { get; set; }
        public Shift? Shift { get; set; }
    }

    public class LeaveRequest
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string LeaveType { get; set; } = "ANNUAL"; // ANNUAL, SICK, UNPAID
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = "PENDING";   // PENDING, APPROVED, REJECTED
        public bool IsPaidLeave { get; set; }
        public int? ApprovedByEmployeeId { get; set; }
        public string? RejectReason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Employee? Employee { get; set; }
    }

    public class AttendanceExplanation
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public DateTime TargetDate { get; set; }
        public string ExplanationReason { get; set; } = string.Empty;
        public DateTime? MissingCheckInTime { get; set; }
        public DateTime? MissingCheckOutTime { get; set; }
        public string Status { get; set; } = "PENDING";
        public int? ReviewerId { get; set; }
        public string? ReviewNote { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Employee? Employee { get; set; }
        public Employee? Reviewer { get; set; }
    }

    public class AdminAuditLog
    {
        public long Id { get; set; }
        public int AdminId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string PromptContent { get; set; } = string.Empty;

        public Employee? Admin { get; set; }
    }

    public class EmployeeFcmToken
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public string FcmToken { get; set; } = string.Empty;
        public string? DeviceId { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public Employee? Employee { get; set; }
    }

    public class NotificationLog
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Type { get; set; } = "General";
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Employee? Employee { get; set; }
    }

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Branch> Branches { get; set; }
        public DbSet<BranchPublicIp> BranchPublicIps { get; set; }
        public DbSet<BranchWifi> BranchWifis { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<Attendance> Attendances { get; set; }
        public DbSet<AttendanceAttempt> AttendanceAttempts { get; set; }
        public DbSet<AttendanceAdjustment> AttendanceAdjustments { get; set; }
        public DbSet<Shift> Shifts { get; set; }
        public DbSet<EmployeeShift> EmployeeShifts { get; set; }
        public DbSet<LeaveRequest> LeaveRequests { get; set; }
        public DbSet<AttendanceExplanation> AttendanceExplanations { get; set; }
        public DbSet<EmployeeFcmToken> EmployeeFcmTokens { get; set; }
        public DbSet<NotificationLog> NotificationLogs { get; set; }
        public DbSet<SalaryAdvance> SalaryAdvances { get; set; }
        public DbSet<OvertimeRecord> OvertimeRecords { get; set; }
        public DbSet<AccountOtp> AccountOtps { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<PayrollSnapshot> PayrollSnapshots { get; set; }
        public DbSet<EmployeeSalaryConfig> EmployeeSalaryConfigs { get; set; }
        public DbSet<LatePenaltyPolicy> LatePenaltyPolicies { get; set; }
        public DbSet<LatePenaltyRange> LatePenaltyRanges { get; set; }
        public DbSet<AdminAuditLog> AdminAuditLogs { get; set; }
        public DbSet<PayrollPolicy> PayrollPolicies { get; set; }
        public DbSet<PayrollTaxBracket> PayrollTaxBrackets { get; set; }
        public DbSet<PayrollHoliday> PayrollHolidays { get; set; }
        public DbSet<WorkGroup> WorkGroups { get; set; }
        public DbSet<GroupMember> GroupMembers { get; set; }
        public DbSet<GroupSchedule> GroupSchedules { get; set; }
        public DbSet<ShiftGroup> ShiftGroups { get; set; }
        public DbSet<ShiftGroupEmployee> ShiftGroupEmployees { get; set; }
        public DbSet<EmployeeShiftException> EmployeeShiftExceptions { get; set; }
        public DbSet<SalaryComponent> SalaryComponents { get; set; }
        public DbSet<SalaryTemplate> SalaryTemplates { get; set; }
        public DbSet<SalaryTemplateDetail> SalaryTemplateDetails { get; set; }
        public DbSet<EmployeeSalary> EmployeeSalaries { get; set; }
        public DbSet<EmployeeSalaryDetail> EmployeeSalaryDetails { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BranchPublicIp>().HasIndex(x => x.PublicIp);
            modelBuilder.Entity<BranchWifi>().HasIndex(x => x.BSSID);
            modelBuilder.Entity<Attendance>().HasIndex(x => new { x.EmployeeId, x.WorkDate });

            modelBuilder.Entity<AttendanceAdjustment>(entity =>
            {
                entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
                entity.HasOne(x => x.Attendance).WithMany().HasForeignKey(x => x.AttendanceId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.AdminEmployee).WithMany().HasForeignKey(x => x.AdminEmployeeId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.AttendanceId, x.CreatedAt });
            });

            modelBuilder.Entity<SalaryComponent>(entity =>
            {
                entity.Property(x => x.Code).HasMaxLength(80).IsRequired();
                entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Category).HasMaxLength(30).IsRequired();
                entity.Property(x => x.CalculationType).HasMaxLength(30).IsRequired();
                entity.Property(x => x.Formula).HasMaxLength(2000);
                entity.Property(x => x.TaxTreatment).HasMaxLength(30).IsRequired();
                entity.Property(x => x.TaxExemptLimit).HasPrecision(19, 2);
                entity.Property(x => x.InsuranceTreatment).HasMaxLength(30).IsRequired();
                entity.HasIndex(x => x.Code).IsUnique();
            });

            modelBuilder.Entity<SalaryTemplate>(entity =>
            {
                entity.Property(x => x.Code).HasMaxLength(80).IsRequired();
                entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                entity.Property(x => x.PositionGroup).HasMaxLength(120);
                entity.HasIndex(x => x.Code).IsUnique();
            });

            modelBuilder.Entity<SalaryTemplateDetail>(entity =>
            {
                entity.Property(x => x.DefaultValue).HasPrecision(19, 2);
                entity.Property(x => x.Formula).HasMaxLength(2000);
                entity.HasIndex(x => new { x.SalaryTemplateId, x.SalaryComponentId }).IsUnique();
                entity.HasOne(x => x.SalaryTemplate).WithMany(x => x.Details).HasForeignKey(x => x.SalaryTemplateId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.SalaryComponent).WithMany(x => x.TemplateDetails).HasForeignKey(x => x.SalaryComponentId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<EmployeeSalary>(entity =>
            {
                entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
                entity.Property(x => x.IsCasualIncome).HasDefaultValue(false);
                entity.Property(x => x.Reason).HasMaxLength(1000);
                entity.HasIndex(x => new { x.EmployeeId, x.EffectiveDate });
                entity.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.SalaryTemplate).WithMany().HasForeignKey(x => x.SalaryTemplateId).OnDelete(DeleteBehavior.SetNull);
                entity.HasOne<Employee>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<Employee>().WithMany().HasForeignKey(x => x.ApprovedBy).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<EmployeeSalaryDetail>(entity =>
            {
                entity.Property(x => x.Amount).HasPrecision(19, 2);
                entity.Property(x => x.Formula).HasMaxLength(2000);
                entity.HasIndex(x => new { x.EmployeeSalaryId, x.SalaryComponentId }).IsUnique();
                entity.HasOne(x => x.EmployeeSalary).WithMany(x => x.Details).HasForeignKey(x => x.EmployeeSalaryId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.SalaryComponent).WithMany().HasForeignKey(x => x.SalaryComponentId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<WorkGroup>(entity =>
            {
                entity.Property(x => x.Code).HasMaxLength(80).IsRequired();
                entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                entity.HasIndex(x => x.Code).IsUnique();
                entity.HasIndex(x => new { x.DepartmentId, x.IsActive });
                entity.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(x => x.DefaultShift).WithMany().HasForeignKey(x => x.DefaultShiftId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<ShiftGroup>(entity =>
            {
                entity.Property(x => x.Code).HasMaxLength(80).IsRequired();
                entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Description).HasMaxLength(1000);
                entity.HasIndex(x => x.Code).IsUnique();
            });

            modelBuilder.Entity<ShiftGroupEmployee>(entity =>
            {
                entity.HasKey(x => new { x.ShiftGroupId, x.EmployeeId });
                entity.HasOne(x => x.ShiftGroup).WithMany(x => x.Employees).HasForeignKey(x => x.ShiftGroupId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(x => x.EmployeeId);
            });

            modelBuilder.Entity<GroupMember>(entity =>
            {
                entity.HasIndex(x => new { x.EmployeeId, x.EffectiveDate, x.EndDate });
                entity.HasIndex(x => new { x.WorkGroupId, x.EffectiveDate, x.EndDate });
                entity.HasOne(x => x.WorkGroup).WithMany(x => x.Members).HasForeignKey(x => x.WorkGroupId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<GroupSchedule>(entity =>
            {
                entity.HasIndex(x => new { x.WorkGroupId, x.WorkDate }).IsUnique();
                entity.HasIndex(x => new { x.WorkDate, x.WorkGroupId });
                entity.HasOne(x => x.WorkGroup).WithMany(x => x.Schedules).HasForeignKey(x => x.WorkGroupId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<EmployeeShiftException>(entity =>
            {
                entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
                entity.HasIndex(x => new { x.EmployeeId, x.WorkDate }).IsUnique();
                entity.HasIndex(x => new { x.WorkDate, x.EmployeeId });
                entity.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.OverrideShift).WithMany().HasForeignKey(x => x.OverrideShiftId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PayrollPolicy>(entity =>
            {
                entity.Property(x => x.ReferenceInsuranceSalary).HasPrecision(19, 2).IsRequired();
                entity.Property(x => x.InsuranceCeiling).HasPrecision(19, 2).IsRequired();
                entity.Property(x => x.EmployeeSocialInsuranceRate).HasPrecision(9, 6).IsRequired();
                entity.Property(x => x.EmployeeHealthInsuranceRate).HasPrecision(9, 6).IsRequired();
                entity.Property(x => x.EmployeeUnemploymentInsuranceRate).HasPrecision(9, 6).IsRequired();
                entity.Property(x => x.EmployerSocialInsuranceRate).HasPrecision(9, 6).IsRequired();
                entity.Property(x => x.EmployerHealthInsuranceRate).HasPrecision(9, 6).IsRequired();
                entity.Property(x => x.EmployerUnemploymentInsuranceRate).HasPrecision(9, 6).IsRequired();
                entity.Property(x => x.PersonalDeduction).HasPrecision(19, 2).IsRequired();
                entity.Property(x => x.DependentDeduction).HasPrecision(19, 2).IsRequired();
                entity.Property(x => x.CasualIncomeWithholdingThreshold).HasPrecision(19, 2).IsRequired();
                entity.Property(x => x.CasualIncomeWithholdingRate).HasPrecision(9, 6).IsRequired();
                entity.Property(x => x.MealTaxExemptLimit).HasPrecision(19, 2).IsRequired();
                entity.Property(x => x.SourceReference).HasMaxLength(500).IsRequired();
                entity.Property(x => x.ChangeReason).HasMaxLength(1000);
                entity.HasIndex(x => x.EffectiveDate).IsUnique();
            });

            modelBuilder.Entity<PayrollTaxBracket>(entity =>
            {
                entity.Property(x => x.UpperLimit).HasPrecision(19, 2);
                entity.Property(x => x.Rate).HasPrecision(9, 6).IsRequired();
                entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                entity.HasIndex(x => new { x.PayrollPolicyId, x.Sequence }).IsUnique();
                entity.HasOne(x => x.PayrollPolicy)
                    .WithMany(x => x.TaxBrackets)
                    .HasForeignKey(x => x.PayrollPolicyId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PayrollHoliday>(entity =>
            {
                entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                entity.HasIndex(x => x.HolidayDate).IsUnique();
            });

            modelBuilder.Entity<AdminAuditLog>(entity =>
            {
                entity.Property(x => x.Action).HasMaxLength(200).IsRequired();
                entity.Property(x => x.TargetId).HasMaxLength(200).IsRequired();
                entity.Property(x => x.PromptContent).HasMaxLength(4000).IsRequired();
                entity.HasIndex(x => new { x.AdminId, x.Timestamp });
                entity.HasOne(x => x.Admin).WithMany().HasForeignKey(x => x.AdminId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<EmployeeSalaryConfig>(entity =>
            {
                entity.Property(x => x.BaseSalary).HasPrecision(18, 2).IsRequired();
                entity.Property(x => x.TotalAllowances).HasPrecision(18, 2).IsRequired();
                entity.Property(x => x.InsuranceSalary).HasPrecision(18, 2).IsRequired();
                entity.Property(x => x.PenaltyPerLateMinute).HasPrecision(18, 2).IsRequired();

                // Fluent API cho thông tin thanh toán
                entity.Property(x => x.PaymentMethod).HasMaxLength(50).HasDefaultValue("BankTransfer");
                entity.Property(x => x.BankName).HasMaxLength(100);
                entity.Property(x => x.BankAccountNo).HasMaxLength(50);
                entity.Property(x => x.BankAccountHolder).HasMaxLength(150);

                entity.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom });
                entity.HasOne(x => x.Employee)
                    .WithMany(x => x.SalaryConfigs)
                    .HasForeignKey(x => x.EmployeeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<LatePenaltyPolicy>(entity =>
            {
                entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                entity.Property(x => x.AmountPerMinute).HasPrecision(18, 2);
                entity.HasIndex(x => x.IsActive).HasFilter("[IsActive] = 1").IsUnique();
            });

            modelBuilder.Entity<LatePenaltyRange>(entity =>
            {
                entity.Property(x => x.PenaltyAmount).HasPrecision(18, 2).IsRequired();
                entity.HasIndex(x => new { x.PolicyId, x.FromMinute, x.ToMinute }).IsUnique();
                entity.HasOne(x => x.Policy)
                    .WithMany(x => x.Ranges)
                    .HasForeignKey(x => x.PolicyId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Attendance>()
                .HasOne(a => a.Employee)
                .WithMany()
                .HasForeignKey(a => a.EmployeeId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Attendance>()
                .HasOne(a => a.Branch)
                .WithMany()
                .HasForeignKey(a => a.BranchId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Employee>(entity =>
            {
                entity.Property(x => x.TotalAnnualLeave).HasDefaultValue(12);
                entity.Property(x => x.UsedAnnualLeave).HasDefaultValue(0);
                entity.Property(x => x.DependentCount).HasDefaultValue(0);
                entity.Property(x => x.BaseSalary).HasPrecision(18, 2);
                entity.Property(x => x.Allowance).HasPrecision(18, 2);
                entity.Property(x => x.PenaltyPerLateMinute).HasPrecision(18, 2);
                entity.Property(x => x.SocialInsuranceBase).HasPrecision(18, 2);
                entity.Property(x => x.Email).HasMaxLength(320);
                entity.HasIndex(x => x.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
                entity.HasOne(x => x.Department)
                    .WithMany(x => x.Employees)
                    .HasForeignKey(x => x.DepartmentId)
                    .OnDelete(DeleteBehavior.SetNull);
                entity.HasIndex(x => x.RoleId);
                entity.HasOne(x => x.PermissionRole)
                    .WithMany(x => x.Employees)
                    .HasForeignKey(x => x.RoleId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Role>(entity =>
            {
                entity.ToTable("Roles");
                entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
                entity.HasIndex(x => x.Name).IsUnique();
            });

            modelBuilder.Entity<Permission>(entity =>
            {
                entity.ToTable("Permissions");
                entity.Property(x => x.Module).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Action).HasMaxLength(50).IsRequired();
                entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
                entity.HasIndex(x => new { x.Module, x.Action }).IsUnique();
            });

            modelBuilder.Entity<RolePermission>(entity =>
            {
                entity.ToTable("Role_Permissions");
                entity.HasKey(x => new { x.RoleId, x.PermissionId });
                entity.HasOne(x => x.Role).WithMany(x => x.RolePermissions)
                    .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.Permission).WithMany(x => x.RolePermissions)
                    .HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Department>(entity =>
            {
                entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                entity.HasIndex(x => x.Name).IsUnique();
            });

            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
                entity.HasIndex(x => x.TokenHash).IsUnique();
                entity.HasOne(x => x.Employee).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PayrollSnapshot>(entity =>
            {
                entity.Property(x => x.EmployeeCode).HasMaxLength(50);
                entity.Property(x => x.FullName).HasMaxLength(150);
                entity.Property(x => x.BaseSalary).HasPrecision(18, 2);
                entity.Property(x => x.GrossSalary).HasPrecision(18, 2);
                entity.Property(x => x.OvertimeSalary).HasPrecision(18, 2);
                entity.Property(x => x.Allowance).HasPrecision(18, 2);
                entity.Property(x => x.InsuranceDeduction).HasPrecision(18, 2);
                entity.Property(x => x.Pit).HasPrecision(18, 2);
                entity.Property(x => x.SalaryAdvanceDeduction).HasPrecision(18, 2);
                entity.Property(x => x.NetSalary).HasPrecision(18, 2);

                // Fluent API cho thông tin thanh toán snapshot
                entity.Property(x => x.PaymentMethod).HasMaxLength(50).HasDefaultValue("BankTransfer");
                entity.Property(x => x.BankName).HasMaxLength(100);
                entity.Property(x => x.BankAccountNo).HasMaxLength(50);
                entity.Property(x => x.BankAccountHolder).HasMaxLength(150);
                entity.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("Approved");

                entity.Property(x => x.PayslipJson).IsRequired();
                entity.HasIndex(x => new { x.EmployeeId, x.Year, x.Month }).IsUnique();
                entity.HasIndex(x => new { x.Month, x.Year, x.Status });
                entity.HasOne(x => x.Employee).WithMany(x => x.PayrollSnapshots).HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AccountOtp>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
                entity.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
                entity.Property(x => x.Purpose).HasMaxLength(40).IsRequired();
                entity.HasIndex(x => new { x.EmployeeId, x.Purpose, x.CreatedAt });
                entity.HasOne(x => x.Employee)
                    .WithMany(x => x.AccountOtps)
                    .HasForeignKey(x => x.EmployeeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<LeaveRequest>(entity =>
            {
                entity.Property(x => x.IsPaidLeave).HasDefaultValue(false);
            });

            modelBuilder.Entity<AttendanceExplanation>(entity =>
            {
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.Property(x => x.ExplanationReason).HasMaxLength(1000).IsRequired();
                entity.HasIndex(x => new { x.EmployeeId, x.TargetDate });
                entity.HasOne(x => x.Employee)
                    .WithMany(x => x.AttendanceExplanations)
                    .HasForeignKey(x => x.EmployeeId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.Reviewer)
                    .WithMany()
                    .HasForeignKey(x => x.ReviewerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<EmployeeFcmToken>(entity =>
            {
                entity.Property(x => x.FcmToken).HasMaxLength(500).IsRequired();
                entity.Property(x => x.DeviceId).HasMaxLength(200);
                entity.HasIndex(x => new { x.EmployeeId, x.DeviceId }).IsUnique();
                entity.HasOne(x => x.Employee)
                    .WithMany(x => x.FcmTokens)
                    .HasForeignKey(x => x.EmployeeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<NotificationLog>(entity =>
            {
                entity.Property(x => x.Title).HasMaxLength(250).IsRequired();
                entity.Property(x => x.Body).HasMaxLength(1000).IsRequired();
                entity.Property(x => x.Type).HasMaxLength(50).IsRequired();
                entity.Property(x => x.IsRead).HasDefaultValue(false);
                entity.HasIndex(x => new { x.EmployeeId, x.CreatedAt });
                entity.HasOne(x => x.Employee)
                    .WithMany(x => x.NotificationLogs)
                    .HasForeignKey(x => x.EmployeeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Shift>(entity =>
            {
                entity.Property(x => x.ShiftCode).HasMaxLength(50).IsRequired();
                entity.Property(x => x.ShiftName).HasMaxLength(100).IsRequired();
                entity.HasIndex(x => x.ShiftCode).IsUnique();
            });

            modelBuilder.Entity<EmployeeShift>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.HasIndex(x => new { x.EmployeeId, x.EffectiveFromDate });
                entity.HasOne(x => x.Employee)
                    .WithMany(x => x.EmployeeShifts)
                    .HasForeignKey(x => x.EmployeeId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.Shift)
                    .WithMany(x => x.EmployeeShifts)
                    .HasForeignKey(x => x.ShiftId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<SalaryAdvance>(entity =>
            {
                entity.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
                entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.Property(x => x.ReviewNote).HasMaxLength(1000);
                entity.HasIndex(x => new { x.EmployeeId, x.RequestDate });
                entity.HasIndex(x => x.Status);
                entity.HasOne(x => x.Employee)
                    .WithMany(x => x.SalaryAdvances)
                    .HasForeignKey(x => x.EmployeeId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.ApprovedBy)
                    .WithMany(x => x.ApprovedSalaryAdvances)
                    .HasForeignKey(x => x.ApprovedById)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<OvertimeRecord>(entity =>
            {
                entity.Property(x => x.Hours).HasPrecision(8, 2).IsRequired();
                entity.Property(x => x.Multiplier).HasPrecision(5, 2).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.HasIndex(x => new { x.EmployeeId, x.WorkDate });
                entity.HasOne(x => x.Employee)
                    .WithMany(x => x.OvertimeRecords)
                    .HasForeignKey(x => x.EmployeeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}