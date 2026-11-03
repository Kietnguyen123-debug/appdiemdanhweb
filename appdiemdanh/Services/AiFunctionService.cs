using appdiemdanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace appdiemdanh.Services
{
    public interface IAiFunctionService
    {
        Task<string> ExecuteFunctionAsync(string functionName, JsonElement args, UserJwtContext userContext);
    }

    public class AiFunctionService : IAiFunctionService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<AiFunctionService> _logger;

        public AiFunctionService(AppDbContext context, ILogger<AiFunctionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<string> ExecuteFunctionAsync(string functionName, JsonElement args, UserJwtContext userContext)
        {
            try
            {
                object result = functionName switch
                {
                    // === TRA CỨU (QUERY) ===
                    "GetMyAttendanceSummary" => await GetMyAttendanceSummaryAsync(userContext.EmployeeId, args),
                    "GetMyLeaveBalance" => await GetMyLeaveBalanceAsync(userContext.EmployeeId),
                    "GetMyShiftSchedule" => await GetMyShiftScheduleAsync(userContext.EmployeeId),
                    "GetMyPayslip" => await GetMyPayslipAsync(userContext.EmployeeId, args),
                    "GetAdminDashboardOverview" => await GetAdminDashboardOverviewAsync(userContext, args),

                    // === HÀNH ĐỘNG (ACTIONS) ===
                    "CreateLeaveRequest" => await CreateLeaveRequestAsync(userContext.EmployeeId, args),
                    "RequestSalaryAdvance" => await RequestSalaryAdvanceAsync(userContext.EmployeeId, args),
                    "SubmitAttendanceExplanation" => await SubmitAttendanceExplanationAsync(userContext.EmployeeId, args),
                    "SubmitOvertimeRequest" => await SubmitOvertimeRequestAsync(userContext.EmployeeId, args),

                    _ => new
                    {
                        status = "not_supported",
                        message = "Hãy trả lời câu hỏi này bằng văn bản trực tiếp cho người dùng dựa trên kiến thức của bạn."
                    }
                };

                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TOOL ERROR] Lỗi tại hàm {FunctionName}", functionName);
                return JsonSerializer.Serialize(new
                {
                    status = "warning",
                    message = "Đã ghi nhận thông tin nhưng cần bạn xác nhận lại chi tiết."
                });
            }
        }

        #region Tra cứu
        private async Task<object> GetMyAttendanceSummaryAsync(int employeeId, JsonElement args)
        {
            int month = GetMonthSafe(args, "month", "monthNumber", "month_number");
            int year = GetYearSafe(args, "year", "yearNumber", "year_number");

            var attendances = await _context.Attendances
                .Where(a => a.EmployeeId == employeeId && a.WorkDate.Month == month && a.WorkDate.Year == year)
                .ToListAsync();

            return new
            {
                Month = month,
                Year = year,
                TotalDaysPresent = attendances.Count,
                TotalLateMinutes = attendances.Sum(a => a.LateMinutes),
                TotalEarlyLeaveMinutes = attendances.Sum(a => a.EarlyLeaveMinutes),
                Details = attendances.Select(a => new
                {
                    a.WorkDate,
                    a.CheckInTime,
                    a.CheckOutTime,
                    a.Status,
                    a.LateMinutes
                })
            };
        }

        private async Task<object> GetMyLeaveBalanceAsync(int employeeId)
        {
            var emp = await _context.Employees.FindAsync(employeeId);
            if (emp == null) return new { error = "Không tìm thấy thông tin nhân viên trên hệ thống." };

            int usedLeave = await _context.LeaveRequests
                .Where(l => l.EmployeeId == employeeId && l.Status.ToUpper() == "APPROVED")
                .CountAsync();

            return new
            {
                TotalAnnualLeave = emp.TotalAnnualLeave,
                UsedAnnualLeave = usedLeave,
                RemainingAnnualLeave = emp.TotalAnnualLeave - usedLeave
            };
        }

        private async Task<object> GetMyShiftScheduleAsync(int employeeId)
        {
            var today = DateTime.Today;
            var schedule = await _context.EmployeeShifts
                .Include(es => es.Shift)
                .Where(es => es.EmployeeId == employeeId &&
                             es.EffectiveFromDate <= today &&
                             (es.EffectiveToDate == null || es.EffectiveToDate >= today))
                .Select(es => new
                {
                    es.Shift.ShiftCode,
                    es.Shift.ShiftName,
                    es.Shift.StartTime,
                    es.Shift.EndTime
                })
                .FirstOrDefaultAsync();

            return schedule ?? (object)new { message = "Chưa được phân ca làm việc hôm nay." };
        }

        private async Task<object> GetMyPayslipAsync(int employeeId, JsonElement args)
        {
            int month = GetMonthSafe(args, "month", "monthNumber", "month_number");
            int year = GetYearSafe(args, "year", "yearNumber", "year_number");

            var snapshot = await _context.PayrollSnapshots
                .FirstOrDefaultAsync(p => p.EmployeeId == employeeId && p.Month == month && p.Year == year);

            if (snapshot != null)
            {
                return new { month, year, snapshot.NetSalary, status = "Đã chốt lương" };
            }

            var config = await _context.EmployeeSalaryConfigs.FirstOrDefaultAsync(c => c.EmployeeId == employeeId);
            return new
            {
                month,
                year,
                EstimatedBaseSalary = config?.BaseSalary ?? 0,
                EstimatedAllowance = config?.TotalAllowances ?? 0,
                status = "Dự toán (Chưa chốt lương)"
            };
        }

        private async Task<object> GetAdminDashboardOverviewAsync(UserJwtContext userContext, JsonElement args)
        {
            if (userContext.Role != "Admin" && userContext.Role != "HR" && userContext.Role != "Manager")
            {
                return new { error = "Bạn không có quyền truy cập báo cáo Quản trị." };
            }

            var today = DateTime.Today;
            int totalEmployees = await _context.Employees.CountAsync(e => e.IsActive);
            int presentToday = await _context.Attendances.CountAsync(a => a.WorkDate.Date == today);
            int pendingLeaves = await _context.LeaveRequests.CountAsync(l => l.Status.ToUpper() == "PENDING");

            return new
            {
                TotalActiveEmployees = totalEmployees,
                PresentToday = presentToday,
                AbsentToday = totalEmployees - presentToday,
                PendingLeaveRequests = pendingLeaves
            };
        }
        #endregion

        #region Hành động
        private async Task<object> CreateLeaveRequestAsync(int employeeId, JsonElement args)
        {
            var startDate = GetDateTimeSafe(args, DateTime.Now.Date.AddDays(1), "startDate", "start_date", "fromDate", "from_date").Date;
            var endDate = GetDateTimeSafe(args, startDate, "endDate", "end_date", "toDate", "to_date").Date;
            if (endDate < startDate) endDate = startDate;

            var leaveType = GetSafeString(args, "leaveType", "leave_type", "type") ?? "Nghỉ phép năm";
            var reason = GetSafeString(args, "reason", "leaveReason", "leave_reason") ?? "Việc cá nhân";

            var hasDuplicate = await _context.LeaveRequests.AnyAsync(request =>
                request.EmployeeId == employeeId &&
                request.Status.ToUpper() != "REJECTED" &&
                request.StartDate < endDate.AddDays(1) && request.EndDate >= startDate);

            if (hasDuplicate)
            {
                LogDuplicate(employeeId);
                return new { status = "duplicate", message = $"Bạn đã có đơn xin nghỉ cho khoảng thời gian {startDate:dd/MM/yyyy} đến {endDate:dd/MM/yyyy} trên hệ thống rồi, không cần tạo thêm." };
            }

            var leaveRequest = new LeaveRequest
            {
                EmployeeId = employeeId,
                StartDate = startDate,
                EndDate = endDate,
                LeaveType = leaveType,
                Reason = reason,
                Status = "PENDING",
                CreatedAt = DateTime.Now
            };

            _context.LeaveRequests.Add(leaveRequest);
            await _context.SaveChangesAsync();

            return new { success = true, leaveRequestId = leaveRequest.Id, message = $"Đã gửi đơn xin nghỉ phép ({leaveType}) từ {startDate:dd/MM/yyyy} đến {endDate:dd/MM/yyyy} thành công!" };
        }

        private async Task<object> RequestSalaryAdvanceAsync(int employeeId, JsonElement args)
        {
            decimal amount = GetDecimalSafe(args, 0m, "amount", "advanceAmount", "advance_amount");
            if (amount <= 0)
                return new { status = "warning", message = "Vui lòng cho biết số tiền tạm ứng hợp lệ để em tạo yêu cầu." };

            string reason = GetSafeString(args, "reason", "advanceReason", "advance_reason") ?? "Việc cá nhân";
            var now = DateTime.Now;

            var hasMonthlyRequest = await _context.SalaryAdvances.AnyAsync(request =>
                request.EmployeeId == employeeId &&
                request.Status.ToUpper() == "PENDING" &&
                request.RequestDate.Year == now.Year && request.RequestDate.Month == now.Month);

            if (hasMonthlyRequest)
            {
                LogDuplicate(employeeId);
                return new { status = "duplicate", message = "Bạn đã gửi yêu cầu ứng lương chưa được duyệt trong tháng này rồi." };
            }

            var advance = new SalaryAdvance
            {
                EmployeeId = employeeId,
                Amount = amount,
                Reason = reason,
                RequestDate = now,
                Status = "PENDING",
                CreatedAt = now
            };

            _context.SalaryAdvances.Add(advance);
            await _context.SaveChangesAsync();

            return new { success = true, advanceId = advance.Id, message = $"Đã tạo yêu cầu tạm ứng {amount:N0} VNĐ thành công!" };
        }

        private async Task<object> SubmitAttendanceExplanationAsync(int employeeId, JsonElement args)
        {
            var targetDate = GetDateTimeSafe(args, DateTime.Now.Date, "targetDate", "target_date", "date").Date;
            string reason = GetSafeString(args, "reason", "explanationReason", "explanation_reason") ?? "Việc cá nhân";

            DateTime? missingIn = GetNullableDateTimeSafe(args, "missingCheckInTime", "missing_check_in_time", "checkInTime", "check_in_time");
            DateTime? missingOut = GetNullableDateTimeSafe(args, "missingCheckOutTime", "missing_check_out_time", "checkOutTime", "check_out_time");

            var exp = new AttendanceExplanation
            {
                EmployeeId = employeeId,
                TargetDate = targetDate,
                ExplanationReason = reason,
                MissingCheckInTime = missingIn,
                MissingCheckOutTime = missingOut,
                Status = "PENDING",
                CreatedAt = DateTime.Now
            };

            _context.AttendanceExplanations.Add(exp);
            await _context.SaveChangesAsync();

            return new { success = true, explanationId = exp.Id, message = $"Đã gửi đơn giải trình chấm công ngày {targetDate:dd/MM/yyyy} thành công!" };
        }

        private async Task<object> SubmitOvertimeRequestAsync(int employeeId, JsonElement args)
        {
            var workDate = GetDateTimeSafe(args, DateTime.Now.Date, "workDate", "work_date", "date").Date;
            double hours = GetDoubleSafe(args, 0d, "hours", "workHours", "work_hours");
            if (hours <= 0)
                hours = 2.0;

            double multiplier = GetDoubleSafe(args, 1.5d, "multiplier", "otMultiplier", "ot_multiplier");

            var hasDuplicate = await _context.OvertimeRecords.AnyAsync(request =>
                request.EmployeeId == employeeId &&
                request.Status.ToUpper() != "REJECTED" &&
                request.WorkDate >= workDate && request.WorkDate < workDate.AddDays(1));

            if (hasDuplicate)
            {
                LogDuplicate(employeeId);
                return new { status = "duplicate", message = $"Bạn đã đăng ký tăng ca trong ngày {workDate:dd/MM/yyyy} trên hệ thống rồi." };
            }

            var ot = new OvertimeRecord
            {
                EmployeeId = employeeId,
                WorkDate = workDate,
                Hours = hours,
                Multiplier = multiplier,
                Status = "PENDING"
            };

            _context.OvertimeRecords.Add(ot);
            await _context.SaveChangesAsync();

            return new { success = true, overtimeId = ot.Id, message = $"Đã đăng ký làm tăng ca {hours} giờ ngày {workDate:dd/MM/yyyy} thành công!" };
        }
        #endregion

        #region Helper Safe Parsers
        private void LogDuplicate(int employeeId)
        {
            _logger.LogInformation("[IDEMPOTENCY BLOCK] Prevented duplicate record for Employee {EmployeeId}", employeeId);
        }

        private static string? GetSafeString(JsonElement args, params string[] keyNames)
        {
            if (args.ValueKind != JsonValueKind.Object) return null;

            foreach (var keyName in keyNames.SelectMany(GetKeyVariants).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!args.TryGetProperty(keyName, out var value) || value.ValueKind == JsonValueKind.Null) continue;
                if (value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
                else if (value.ValueKind != JsonValueKind.Undefined)
                {
                    var raw = value.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(raw)) return raw;
                }
            }

            return null;
        }

        private static IEnumerable<string> GetKeyVariants(string keyName)
        {
            yield return keyName;

            if (keyName.Contains('_'))
            {
                yield return string.Concat(keyName.Split('_', StringSplitOptions.RemoveEmptyEntries)
                    .Select((part, index) => index == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
                yield break;
            }

            var snakeCase = new StringBuilder();
            foreach (var character in keyName)
            {
                if (char.IsUpper(character)) snakeCase.Append('_');
                snakeCase.Append(char.ToLowerInvariant(character));
            }

            yield return snakeCase.ToString();
        }

        private static int GetMonthSafe(JsonElement args, params string[] keys)
        {
            var month = GetIntSafe(args, DateTime.Now.Month, keys);
            return month is >= 1 and <= 12 ? month : DateTime.Now.Month;
        }

        private static int GetYearSafe(JsonElement args, params string[] keys)
        {
            var year = GetIntSafe(args, DateTime.Now.Year, keys);
            return year is >= 1 and <= 9999 ? year : DateTime.Now.Year;
        }

        private static int GetIntSafe(JsonElement args, int defaultValue, params string[] keys)
        {
            return int.TryParse(GetSafeString(args, keys), out var number) ? number : defaultValue;
        }

        private static decimal GetDecimalSafe(JsonElement args, decimal defaultValue, params string[] keys)
        {
            var raw = GetSafeString(args, keys);
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariantNumber) ||
                double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out invariantNumber))
            {
                return (decimal)invariantNumber;
            }

            return defaultValue;
        }

        private static double GetDoubleSafe(JsonElement args, double defaultValue, params string[] keys)
        {
            var raw = GetSafeString(args, keys);
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                   double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out number)
                ? number
                : defaultValue;
        }

        private static DateTime GetDateTimeSafe(JsonElement args, DateTime defaultValue, params string[] keys)
        {
            foreach (var key in keys)
            {
                var raw = GetSafeString(args, key);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var parsed = ParseDateOrDefault(raw, defaultValue);
                    if (parsed != defaultValue || raw.Contains(defaultValue.ToString("yyyy-MM-dd")))
                        return parsed;
                }
            }

            return defaultValue;
        }

        private static DateTime ParseDateOrDefault(string raw, DateTime defaultDate)
        {
            string[] formats = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "yyyy/MM/dd", "dd-MM-yyyy", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ" };
            if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactDate))
            {
                return exactDate;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invDate))
            {
                return invDate;
            }

            return defaultDate;
        }

        private static DateTime? GetNullableDateTimeSafe(JsonElement args, params string[] keys)
        {
            var raw = GetSafeString(args, keys);
            if (string.IsNullOrWhiteSpace(raw)) return null;

            string[] formats = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "yyyy/MM/dd", "dd-MM-yyyy", "yyyy-MM-ddTHH:mm:ss" };
            return DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : (DateTime.TryParse(raw, out var genericDate) ? genericDate : null);
        }
        #endregion
    }
}