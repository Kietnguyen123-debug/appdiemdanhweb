using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PayrollController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IPayrollService _payrollService;
        private readonly IPayrollPolicyService _payrollPolicyService;

        public PayrollController(AppDbContext context, IPayrollService payrollService, IPayrollPolicyService payrollPolicyService)
        {
            _context = context;
            _payrollService = payrollService;
            _payrollPolicyService = payrollPolicyService;
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpGet("policy")]
        [ProducesResponseType(typeof(PayrollPolicyResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetActivePayrollPolicy([FromQuery] DateTime? payPeriodDate = null)
        {
            var policy = await _payrollPolicyService.GetActivePolicy(payPeriodDate?.Date ?? DateTime.Today);
            return policy == null
                ? NotFound(new { message = $"Chưa có chính sách Thuế/BHXH hiệu lực cho ngày {(payPeriodDate?.Date ?? DateTime.Today):yyyy-MM-dd}." })
                : Ok(ToPolicyResponse(policy));
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpPost("policy")]
        [ProducesResponseType(typeof(PayrollPolicyResponse), StatusCodes.Status201Created)]
        public async Task<IActionResult> CreatePayrollPolicy([FromBody] SavePayrollPolicyRequest request)
        {
            if (request == null) return BadRequest(new { message = "Policy không được để trống." });
            if (!TryGetEmployeeId(out var createdBy)) return Unauthorized();

            try
            {
                var policy = await _payrollPolicyService.CreatePolicyAsync(request, createdBy);
                return CreatedAtAction(nameof(GetActivePayrollPolicy), new { payPeriodDate = policy.EffectiveDate }, ToPolicyResponse(policy));
            }
            catch (ArgumentException exception)
            {
                return BadRequest(new { message = exception.Message });
            }
            catch (DbUpdateException exception) when (exception.InnerException?.Message.Contains("UQ_PayrollPolicies_EffectiveDate", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Conflict(new { message = "Đã tồn tại policy có ngày hiệu lực này." });
            }
        }

        [HttpGet("my-history")]
        [ProducesResponseType(typeof(IReadOnlyList<PayrollHistoryItemDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyHistory()
        {
            if (!TryGetEmployeeId(out var employeeId)) return Unauthorized();
            return Ok(await _context.PayrollSnapshots.AsNoTracking().Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
                .Select(x => new PayrollHistoryItemDto { Id = x.Id, Month = x.Month, Year = x.Year, NetSalary = x.NetSalary, CreatedAt = x.CreatedAt }).ToListAsync());
        }

        [HttpGet("my-payslip/{id:long}")]
        [ProducesResponseType(typeof(PayslipResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyPayslipById(long id)
        {
            if (!TryGetEmployeeId(out var employeeId)) return Unauthorized();
            var snapshot = await _context.PayrollSnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId);
            if (snapshot == null) return NotFound();
            return Content(snapshot.PayslipJson, "application/json");
        }

        private bool TryGetEmployeeId(out int employeeId) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out employeeId) && employeeId > 0;

        /// <summary>
        /// [NHÂN VIÊN] Xem Phiếu Lương Cá Nhân Theo Tháng Trên App Mobile
        /// </summary>
        [HttpGet("my-payslip")]
        [ProducesResponseType(typeof(PayslipResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyPayslip([FromQuery] int month, [FromQuery] int year, [FromQuery] int standardWorkDays = 26)
        {
            if (month < 1 || month > 12 || year < 2000 || year > 2100 || standardWorkDays <= 0 || standardWorkDays > 31)
                return BadRequest("Tháng, năm hoặc số ngày công chuẩn không hợp lệ.");
            // Lấy EmployeeId từ JWT Token
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int employeeId))
                return Unauthorized("Token không hợp lệ!");

            try { return Ok(await _payrollService.CalculateMonthlyPayslipAsync(employeeId, month, year, standardWorkDays)); }
            catch (KeyNotFoundException exception) { return NotFound(exception.Message); }
            catch (ArgumentException exception) { return BadRequest(exception.Message); }
            catch (PayrollPolicyUnavailableException exception) { return Conflict(new { message = exception.Message }); }
        }

        /// <summary>
        /// [ADMIN / HR] Xem Bảng Lương Toàn Bộ Nhân Viên Theo Tháng
        /// </summary>
        [Authorize(Roles = "Admin,HR")]
        [HttpGet("monthly-summary")]
        [ProducesResponseType(typeof(MonthlyPayrollSummaryResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMonthlyPayrollSummary([FromQuery] int month, [FromQuery] int year, [FromQuery] int standardWorkDays = 26)
        {
            if (month < 1 || month > 12 || year < 2000 || year > 2100 || standardWorkDays <= 0 || standardWorkDays > 31)
                return BadRequest("Tháng, năm hoặc số ngày công chuẩn không hợp lệ.");
            var employees = await _context.Employees.Where(e => e.IsActive).ToListAsync();
            var payrollList = new System.Collections.Generic.List<PayslipResponse>();

            try
            {
                foreach (var emp in employees)
                {
                    var payslip = await _payrollService.CalculateMonthlyPayslipAsync(emp.Id, month, year, standardWorkDays);
                    payrollList.Add(payslip);
                }
            }
            catch (PayrollPolicyUnavailableException exception) { return Conflict(new { message = exception.Message }); }

            return Ok(new MonthlyPayrollSummaryResponse { Items = payrollList });
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpPost("finalize")]
        [ProducesResponseType(typeof(PayrollFinalizeResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> FinalizePayroll([FromBody] FinalizePayrollRequest request)
        {
            if (request == null || request.Month < 1 || request.Month > 12 || request.Year < 2000 || request.Year > 2100 || request.StandardWorkDays <= 0 || request.StandardWorkDays > 31)
                return BadRequest(new { message = "Thông tin chốt lương không hợp lệ." });
            try
            {
                int employeeCount = await _payrollService.FinalizeMonthlyPayrollAsync(
                    request.Month,
                    request.Year,
                    request.StandardWorkDays);

                return Ok(new PayrollFinalizeResponse
                {
                    Message = $"Đã chốt bảng lương tháng {request.Month:00}/{request.Year} và gửi thông báo.",
                    EmployeeCount = employeeCount
                });
            }
            catch (ArgumentException exception)
            {
                return BadRequest(new { message = exception.Message });
            }
            catch (PayrollPolicyUnavailableException exception)
            {
                return Conflict(new { message = exception.Message });
            }
        }

        /// <summary>
        /// [ADMIN / HR] Cấu Hình Mức Lương &amp; Tiền Phạt Trễ Cho Nhân Viên
        /// </summary>
        [Authorize(Roles = "Admin,HR")]
        [HttpPut("config-salary/{employeeId}")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateSalaryConfig(int employeeId, [FromBody] UpdateSalaryConfigRequest request)
        {
            if (employeeId <= 0 || request == null || request.BaseSalary < 0 || request.Allowance < 0 || request.PenaltyPerLateMinute < 0 || request.DependentCount < 0 || request.DependentCount > 20 || request.SocialInsuranceBase < 0)
                return BadRequest(new { message = "Cấu hình lương không hợp lệ." });
            try
            {
                await _payrollService.UpdateSalaryConfigAsync(employeeId, request);
                return Ok(new MessageResponse { Message = "Cập nhật cấu hình lương thành công!" });
            }
            catch (KeyNotFoundException exception)
            {
                return NotFound(exception.Message);
            }
            catch (ArgumentException exception)
            {
                return BadRequest(new { message = exception.Message });
            }
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpGet("late-penalty-policy")]
        [ProducesResponseType(typeof(LatePenaltyPolicyResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetActiveLatePenaltyPolicy()
        {
            var policy = await _context.LatePenaltyPolicies.AsNoTracking()
                .Include(x => x.Ranges)
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            return policy == null ? NotFound("Chưa có chính sách phạt đi trễ đang hoạt động.") : Ok(ToPolicyResponse(policy));
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpPut("late-penalty-policy")]
        [ProducesResponseType(typeof(LatePenaltyPolicyResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> SaveLatePenaltyPolicy([FromBody] LatePenaltyPolicyRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Tên chính sách là bắt buộc.");
            if (request.Type == PenaltyType.PerMinute && (!request.AmountPerMinute.HasValue || request.AmountPerMinute < 0))
                return BadRequest("Phạt theo phút phải có AmountPerMinute hợp lệ.");
            if (request.Type == PenaltyType.FixedByRange && (request.Ranges.Count == 0 || request.Ranges.Any(x => x.FromMinute < 0 || x.ToMinute < x.FromMinute || x.PenaltyAmount < 0)))
                return BadRequest("Khoảng phút phạt không hợp lệ.");
            if (request.Type == PenaltyType.PerMinute && request.Ranges.Count > 0)
                return BadRequest("Phạt theo phút không được khai báo dải phút.");
            var orderedRanges = request.Ranges.OrderBy(x => x.FromMinute).ToList();
            if (orderedRanges.Zip(orderedRanges.Skip(1), (left, right) => left.ToMinute >= right.FromMinute).Any(x => x))
                return BadRequest("Các dải phút phạt không được chồng lấn.");
            if (!request.IsActive)
                return BadRequest("Chính sách mới phải được kích hoạt để áp dụng tính lương.");

            await using var transaction = await _context.Database.BeginTransactionAsync();
            await _context.LatePenaltyPolicies.Where(x => x.IsActive).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsActive, false));

            var policy = new LatePenaltyPolicy
            {
                Name = request.Name.Trim(),
                Type = request.Type,
                AmountPerMinute = request.Type == PenaltyType.PerMinute ? request.AmountPerMinute : null,
                IsActive = request.IsActive
            };

            policy.Ranges = request.Ranges.Select(x => new LatePenaltyRange
            {
                FromMinute = x.FromMinute,
                ToMinute = x.ToMinute,
                PenaltyAmount = x.PenaltyAmount
            }).ToList();
            _context.LatePenaltyPolicies.Add(policy);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(ToPolicyResponse(policy));
        }

        private static LatePenaltyPolicyResponse ToPolicyResponse(LatePenaltyPolicy policy) => new()
        {
            Id = policy.Id,
            Name = policy.Name,
            Type = policy.Type,
            AmountPerMinute = policy.AmountPerMinute,
            IsActive = policy.IsActive,
            Ranges = policy.Ranges.OrderBy(x => x.FromMinute).Select(x => new LatePenaltyRangeRequest
            {
                FromMinute = x.FromMinute,
                ToMinute = x.ToMinute,
                PenaltyAmount = x.PenaltyAmount
            }).ToList()
        };

        private static PayrollPolicyResponse ToPolicyResponse(PayrollPolicy policy) => new()
        {
            Id = policy.Id,
            EffectiveDate = policy.EffectiveDate,
            ReferenceInsuranceSalary = policy.ReferenceInsuranceSalary,
            InsuranceCeiling = policy.InsuranceCeiling,
            EmployeeSocialInsuranceRate = policy.EmployeeSocialInsuranceRate,
            EmployeeHealthInsuranceRate = policy.EmployeeHealthInsuranceRate,
            EmployeeUnemploymentInsuranceRate = policy.EmployeeUnemploymentInsuranceRate,
            EmployerSocialInsuranceRate = policy.EmployerSocialInsuranceRate,
            EmployerHealthInsuranceRate = policy.EmployerHealthInsuranceRate,
            EmployerUnemploymentInsuranceRate = policy.EmployerUnemploymentInsuranceRate,
            PersonalDeduction = policy.PersonalDeduction,
            DependentDeduction = policy.DependentDeduction,
            CasualIncomeWithholdingThreshold = policy.CasualIncomeWithholdingThreshold,
            CasualIncomeWithholdingRate = policy.CasualIncomeWithholdingRate,
            MealTaxExemptLimit = policy.MealTaxExemptLimit,
            SourceReference = policy.SourceReference,
            ChangeReason = policy.ChangeReason,
            TaxBrackets = policy.TaxBrackets.OrderBy(x => x.Sequence).Select(x => new PayrollTaxBracketDto
            {
                Sequence = x.Sequence,
                UpperLimit = x.UpperLimit,
                Rate = x.Rate,
                Name = x.Name
            }).ToList()
        };

    }
}