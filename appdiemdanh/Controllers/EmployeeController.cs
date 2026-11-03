using System.Linq;
using System.Threading.Tasks;
using AppDiemDanh.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppDiemDanh.DTOs;
using System.Net.Mail;

namespace AppDiemDanh.Controllers
{
    [Authorize] 
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeeController : ControllerBase
    {
        private readonly AppDbContext _context;

        public EmployeeController(AppDbContext context) => _context = context;

        [Authorize(Roles = "Admin,HR")]
        [HttpGet("{id:int}")]
        public async Task<ActionResult<EmployeeDetailResponse>> GetById(int id)
        {
            if (id <= 0) return BadRequest(new { message = "Mã nhân viên không hợp lệ." });
            var employee = await _context.Employees.AsNoTracking().Include(x => x.Branch).Include(x => x.Department).Where(x => x.Id == id).Select(x => new EmployeeDetailResponse
            {
                Id = x.Id,
                EmployeeCode = x.EmployeeCode,
                FullName = x.FullName,
                Email = x.Email,
                BranchId = x.BranchId,
                DepartmentId = x.DepartmentId,
                BranchName = x.Branch == null ? string.Empty : x.Branch.Name,
                DepartmentName = x.Department == null ? null : x.Department.Name,
                Role = x.Role,
                IsActive = x.IsActive,
                IsActivated = x.IsActivated,
                IsEmailVerified = x.IsEmailVerified,
                HasBoundDevice = x.BoundDeviceId != null,
                HasFaceRegistered = x.FaceVector != null,
                BaseSalary = x.BaseSalary,
                Allowance = x.Allowance,
                PenaltyPerLateMinute = x.PenaltyPerLateMinute,
                DependentCount = x.DependentCount,
                SocialInsuranceBase = x.SocialInsuranceBase,
                TotalAnnualLeave = x.TotalAnnualLeave,
                UsedAnnualLeave = x.UsedAnnualLeave,
                RemainingAnnualLeave = Math.Max(0, x.TotalAnnualLeave - x.UsedAnnualLeave)
            }).FirstOrDefaultAsync();
            return employee == null ? NotFound() : Ok(employee);
        }

        [Authorize(Roles = "Admin,HR,Manager,Employee")]
        [HttpGet]
        public async Task<ActionResult<EmployeeListResponse>> GetAll([FromQuery] int? branchId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            if (branchId is <= 0) return BadRequest("Mã chi nhánh không hợp lệ.");
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var query = _context.Employees.Include(e => e.Branch).Include(e => e.Department).AsQueryable();

            if (branchId.HasValue)
                query = query.Where(e => e.BranchId == branchId.Value);

            var totalItems = await query.CountAsync();
            var employees = await query.Skip((page - 1) * pageSize)
                                       .Take(pageSize)
                                       .Select(e => new EmployeeListItemResponse
                                       {
                                           Id = e.Id,
                                           EmployeeCode = e.EmployeeCode,
                                           FullName = e.FullName,
                                           BranchId = e.BranchId,
                                           BranchName = e.Branch != null ? e.Branch.Name : "",
                                           DepartmentId = e.DepartmentId,
                                           DepartmentName = e.Department != null ? e.Department.Name : null,
                                           HasBoundDevice = e.BoundDeviceId != null,
                                           HasFaceRegistered = !string.IsNullOrEmpty(e.FaceVector),
                                           IsActive = e.IsActive
                                       }).ToListAsync();

            return Ok(new EmployeeListResponse { TotalItems = totalItems, Page = page, PageSize = pageSize, TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize), Data = employees });
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpPost]
        public async Task<ActionResult<EmployeeMessageResponse>> Create([FromBody] CreateEmployeeProfileRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.EmployeeCode) || string.IsNullOrWhiteSpace(request.FullName) || request.BranchId <= 0)
                return BadRequest("Mã nhân viên và họ tên không được để trống");
            request.EmployeeCode = request.EmployeeCode.Trim();
            request.FullName = request.FullName.Trim();
            request.Role = string.IsNullOrWhiteSpace(request.Role) ? "Employee" : request.Role.Trim();
            if (request.Role is not ("Admin" or "HR" or "Manager" or "Employee"))
                return BadRequest("Vai trò không hợp lệ");
            if (request.BaseSalary < 0 || request.Allowance < 0 || request.PenaltyPerLateMinute < 0 || request.DependentCount is < 0 or > 20 || request.SocialInsuranceBase < 0)
                return BadRequest("Thông tin lương hoặc số người phụ thuộc không hợp lệ");
            if (!string.IsNullOrWhiteSpace(request.Email) && !MailAddress.TryCreate(request.Email.Trim(), out _))
                return BadRequest("Email không hợp lệ");
            request.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
            if (!await _context.Branches.AnyAsync(x => x.Id == request.BranchId && x.IsActive))
                return BadRequest("Chi nhánh không tồn tại hoặc đã bị khóa");
            if (request.DepartmentId.HasValue && !await _context.Departments.AnyAsync(x => x.Id == request.DepartmentId.Value && x.IsActive))
                return BadRequest("Phòng ban không tồn tại hoặc đã bị khóa");
            if (await _context.Employees.AnyAsync(e => e.EmployeeCode == request.EmployeeCode))
                return BadRequest("Mã nhân viên đã tồn tại");

            var model = new Employee { EmployeeCode = request.EmployeeCode, FullName = request.FullName, BranchId = request.BranchId, DepartmentId = request.DepartmentId, Role = request.Role, Email = request.Email, BaseSalary = request.BaseSalary, Allowance = request.Allowance, PenaltyPerLateMinute = request.PenaltyPerLateMinute, DependentCount = request.DependentCount, SocialInsuranceBase = request.SocialInsuranceBase, TotalAnnualLeave = request.TotalAnnualLeave };
            _context.Employees.Add(model);
            await _context.SaveChangesAsync();
            return Ok(new EmployeeMessageResponse { Message = "Thêm nhân viên thành công", EmployeeId = model.Id });
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpPost("{id}/unbind-device")]
        public async Task<ActionResult<MessageResponse>> UnbindDevice(int id)
        {
            if (id <= 0) return BadRequest("Mã nhân viên không hợp lệ");
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound("Không tìm thấy nhân viên");

            employee.BoundDeviceId = null;
            await _context.SaveChangesAsync();
            return Ok(new MessageResponse { Message = "Đã hủy liên kết thiết bị cũ. Nhân viên có thể đăng nhập trên máy mới." });
        }

        // Vô hiệu hóa/Kích hoạt tài khoản - Chỉ Admin, HR
        [Authorize(Roles = "Admin,HR")]
        [HttpPut("{id}/toggle-status")]
        [ProducesResponseType(typeof(StatusMessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            if (id <= 0) return BadRequest("Mã nhân viên không hợp lệ");
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();

            employee.IsActive = !employee.IsActive;
            await _context.SaveChangesAsync();
            return Ok(new StatusMessageResponse { Message = "Cập nhật trạng thái thành công", IsActive = employee.IsActive });
        }

        // Cập nhật thông tin cá nhân & Đổi Phân quyền - Chỉ Admin, HR
        [Authorize(Roles = "Admin,HR")]
        [HttpPut("{id}")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateEmployee(int id, [FromBody] UpdateEmployeeRequest request)
        {
            if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Role))
                return BadRequest("Thông tin cập nhật không hợp lệ");
            if (request.Role is not ("Admin" or "HR" or "Manager" or "Employee"))
                return BadRequest("Vai trò không hợp lệ");
            request.FullName = request.FullName.Trim();
            if (!await _context.Branches.AnyAsync(x => x.Id == request.BranchId && x.IsActive))
                return BadRequest("Chi nhánh không tồn tại hoặc đã bị khóa");
            if (request.DepartmentId.HasValue && !await _context.Departments.AnyAsync(x => x.Id == request.DepartmentId.Value && x.IsActive))
                return BadRequest("Phòng ban không tồn tại hoặc đã bị khóa");
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound("Không tìm thấy nhân viên");

            employee.FullName = request.FullName;
            employee.BranchId = request.BranchId;
            employee.DepartmentId = request.DepartmentId;
            employee.Role = request.Role;

            await _context.SaveChangesAsync();
            return Ok(new MessageResponse { Message = "Cập nhật thông tin và cấp quyền thành công!" });
        }

        // Reset mật khẩu tạm - Chỉ Admin, HR
        [Authorize(Roles = "Admin,HR")]
        [HttpPost("{id}/reset-password")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ResetPassword(int id, [FromBody] AdminResetPasswordRequest request)
        {
            if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.NewTemporaryPassword) || request.NewTemporaryPassword.Length < 6)
                return BadRequest("Mã nhân viên hoặc mật khẩu tạm không hợp lệ");
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound("Không tìm thấy nhân viên");

            var passwordHasher = new Microsoft.AspNetCore.Identity.PasswordHasher<string>();
            employee.PasswordHash = passwordHasher.HashPassword(employee.EmployeeCode, request.NewTemporaryPassword);
            employee.IsActivated = employee.Role.Equals("Employee", StringComparison.OrdinalIgnoreCase);

            await _context.SaveChangesAsync();
            return Ok(new MessageResponse { Message = "Đã reset mật khẩu tạm thời. Nhân viên cần kích hoạt lại trên App." });
        }
    }
}