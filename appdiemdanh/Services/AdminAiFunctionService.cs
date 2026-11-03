using System.Globalization;
using System.Text;
using System.Text.Json;
using appdiemdanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace appdiemdanh.Services;

public interface IAdminAiFunctionService
{
    Task<string> ExecuteFunctionAsync(string functionName, JsonElement args, UserJwtContext adminContext, string promptContent);
}

public sealed class AdminAiFunctionService : IAdminAiFunctionService
{
    private readonly AppDbContext _context;
    private readonly ILogger<AdminAiFunctionService> _logger;

    public AdminAiFunctionService(AppDbContext context, ILogger<AdminAiFunctionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<string> ExecuteFunctionAsync(string functionName, JsonElement args, UserJwtContext adminContext, string promptContent)
    {
        try
        {
            object result = functionName switch
            {
                "GetCompanyAttendanceSummary" => await GetCompanyAttendanceSummaryAsync(args),
                "GetPendingRequests" => await GetPendingRequestsAsync(args),
                "ApproveRequestsBulk" => await ApproveRequestsBulkAsync(args, adminContext.EmployeeId, promptContent),
                "SearchEmployees" => await SearchEmployeesAsync(args),
                "GetPayrollOverview" => await GetPayrollOverviewAsync(args),
                _ => new { status = "not_supported", message = "Công cụ quản trị không được hỗ trợ." }
            };
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ADMIN TOOL ERROR] Lỗi khi thực thi hàm {FunctionName}", functionName);
            return JsonSerializer.Serialize(new { status = "error", message = $"Không thể xử lý yêu cầu quản trị lúc này. Chi tiết: {ex.Message}" });
        }
    }

    private async Task<object> GetCompanyAttendanceSummaryAsync(JsonElement args)
    {
        var date = GetDateSafe(args, DateTime.Today, "date", "Date", "targetDate", "workDate").Date;
        var nextDate = date.AddDays(1);
        var departmentId = GetNullableIntSafe(args, "departmentId", "DepartmentId", "deptId");

        var employeesQuery = _context.Employees.AsNoTracking()
            .Where(x => x.IsActive && (!departmentId.HasValue || x.DepartmentId == departmentId.Value));

        var employeeIds = await employeesQuery.Select(x => x.Id).ToListAsync();
        if (employeeIds.Count == 0)
        {
            return new
            {
                status = "success",
                date = date.ToString("yyyy-MM-dd"),
                departmentId,
                totalEmployees = 0,
                present = 0,
                late = 0,
                approvedLeave = 0,
                absentWithoutReason = 0,
                checkedOut = 0
            };
        }

        var attendance = await _context.Attendances.AsNoTracking()
            .Where(x => employeeIds.Contains(x.EmployeeId) && x.WorkDate >= date && x.WorkDate < nextDate)
            .ToListAsync();

        var leaveCount = await _context.LeaveRequests.AsNoTracking()
            .CountAsync(x => employeeIds.Contains(x.EmployeeId)
                && x.Status.ToUpper() == "APPROVED"
                && x.StartDate.Date <= date
                && x.EndDate.Date >= date);

        var present = attendance.Count(x => x.CheckInTime != default);

        return new
        {
            status = "success",
            date = date.ToString("yyyy-MM-dd"),
            departmentId,
            totalEmployees = employeeIds.Count,
            present,
            late = attendance.Count(x => x.LateMinutes > 0),
            approvedLeave = leaveCount,
            absentWithoutReason = Math.Max(0, employeeIds.Count - present - leaveCount),
            checkedOut = attendance.Count(x => x.CheckOutTime.HasValue)
        };
    }

    private async Task<object> GetPendingRequestsAsync(JsonElement args)
    {
        var type = NormalizeRequestType(GetSafeString(args, "requestType", "RequestType", "type", "kind"));
        if (string.IsNullOrEmpty(type))
        {
            return new { status = "error", message = "RequestType không hợp lệ. Vui lòng truyền Leave, Overtime hoặc SalaryAdvance." };
        }

        return type switch
        {
            "Leave" => new
            {
                status = "success",
                requestType = type,
                items = await _context.LeaveRequests.AsNoTracking()
                    .Where(x => x.Status.ToUpper() == "PENDING")
                    .Select(x => new
                    {
                        x.Id,
                        x.EmployeeId,
                        EmployeeName = x.Employee != null ? x.Employee.FullName : "N/A",
                        EmployeeCode = x.Employee != null ? x.Employee.EmployeeCode : "N/A",
                        StartDate = x.StartDate.ToString("dd/MM/yyyy"),
                        EndDate = x.EndDate.ToString("dd/MM/yyyy"),
                        x.LeaveType,
                        x.Reason
                    }).ToListAsync()
            },
            "Overtime" => new
            {
                status = "success",
                requestType = type,
                items = await _context.OvertimeRecords.AsNoTracking()
                    .Where(x => x.Status.ToUpper() == "PENDING")
                    .Select(x => new
                    {
                        x.Id,
                        x.EmployeeId,
                        EmployeeName = x.Employee != null ? x.Employee.FullName : "N/A",
                        EmployeeCode = x.Employee != null ? x.Employee.EmployeeCode : "N/A",
                        WorkDate = x.WorkDate.ToString("dd/MM/yyyy"),
                        x.Hours,
                        x.Multiplier
                    }).ToListAsync()
            },
            "SalaryAdvance" => new
            {
                status = "success",
                requestType = type,
                items = await _context.SalaryAdvances.AsNoTracking()
                    .Where(x => x.Status.ToUpper() == "PENDING")
                    .Select(x => new
                    {
                        x.Id,
                        x.EmployeeId,
                        EmployeeName = x.Employee != null ? x.Employee.FullName : "N/A",
                        EmployeeCode = x.Employee != null ? x.Employee.EmployeeCode : "N/A",
                        x.Amount,
                        x.Reason,
                        RequestDate = x.RequestDate.ToString("dd/MM/yyyy")
                    }).ToListAsync()
            },
            _ => new { status = "error", message = "RequestType phải là Leave, Overtime hoặc SalaryAdvance." }
        };
    }

    private async Task<object> ApproveRequestsBulkAsync(JsonElement args, int adminId, string promptContent)
    {
        bool confirmed = GetBoolSafe(args, "confirmed", "isConfirmed", "confirmation", "is_confirmed", "confirm");
        if (!confirmed)
        {
            return new { status = "confirmation_required", message = "Cần xác nhận rõ bằng 'Xác nhận Duyệt' hoặc truyền confirmed=true trước khi thực hiện." };
        }

        var type = NormalizeRequestType(GetSafeString(args, "requestType", "RequestType", "type"));
        if (string.IsNullOrEmpty(type))
        {
            return new { status = "error", message = "RequestType không hợp lệ (Cần Leave, Overtime hoặc SalaryAdvance)." };
        }

        var ids = GetIntListSafe(args, "requestIds", "RequestIds", "ids", "requestId", "targetIds");
        if (ids.Count == 0)
        {
            return new { status = "error", message = "Danh sách RequestIds không được rỗng." };
        }

        bool approved = GetBoolSafe(args, "isApproved", "IsApproved", "approved");
        if (!HasProperty(args, "isApproved", "IsApproved", "approved"))
        {
            approved = true;
        }

        string note = GetSafeString(args, "note", "Note", "reason", "reviewNote") ?? string.Empty;
        var targetIds = new List<long>();

        await using var transaction = await _context.Database.BeginTransactionAsync();
        switch (type)
        {
            case "Leave":
                var leaves = await _context.LeaveRequests
                    .Where(x => ids.Contains((int)x.Id) && x.Status.ToUpper() == "PENDING")
                    .ToListAsync();
                foreach (var item in leaves)
                {
                    item.Status = approved ? "APPROVED" : "REJECTED";
                    item.ApprovedByEmployeeId = adminId;
                    item.RejectReason = note;
                    targetIds.Add(item.Id);
                }
                break;

            case "Overtime":
                var overtime = await _context.OvertimeRecords
                    .Where(x => ids.Contains((int)x.Id) && x.Status.ToUpper() == "PENDING")
                    .ToListAsync();
                foreach (var item in overtime)
                {
                    item.Status = approved ? "APPROVED" : "REJECTED";
                    targetIds.Add(item.Id);
                }
                break;

            case "SalaryAdvance":
                var advances = await _context.SalaryAdvances
                    .Where(x => ids.Contains((int)x.Id) && x.Status.ToUpper() == "PENDING")
                    .ToListAsync();
                foreach (var item in advances)
                {
                    item.Status = approved ? "APPROVED" : "REJECTED";
                    item.ApprovedById = adminId;
                    item.ReviewNote = note;
                    targetIds.Add(item.Id);
                }
                break;
        }

        if (targetIds.Count == 0)
        {
            return new { status = "not_found", message = "Không tìm thấy đơn PENDING phù hợp với danh sách ID đã cung cấp." };
        }

        _context.AdminAuditLogs.Add(new AdminAuditLog
        {
            AdminId = adminId,
            Action = approved ? "ApproveRequestsBulk" : "RejectRequestsBulk",
            TargetId = string.Join(",", targetIds),
            PromptContent = string.IsNullOrWhiteSpace(promptContent) ? "N/A" : (promptContent.Length > 4000 ? promptContent[..4000] : promptContent)
        });

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return new
        {
            status = "success",
            requestType = type,
            approved,
            updatedCount = targetIds.Count,
            requestIds = targetIds,
            message = approved ? $"Đã duyệt thành công {targetIds.Count} đơn!" : $"Đã từ chối thành công {targetIds.Count} đơn!"
        };
    }

    private async Task<object> SearchEmployeesAsync(JsonElement args)
    {
        var keyword = GetSafeString(args, "keyword", "Keyword")?.Trim();
        var departmentId = GetNullableIntSafe(args, "departmentId", "DepartmentId");
        var status = GetSafeString(args, "status", "Status");

        var query = _context.Employees.AsNoTracking().Where(x =>
            (!departmentId.HasValue || x.DepartmentId == departmentId.Value)
            && (string.IsNullOrWhiteSpace(status) || (status.Equals("Active", StringComparison.OrdinalIgnoreCase) ? x.IsActive : !x.IsActive))
            && (string.IsNullOrWhiteSpace(keyword) || x.FullName.Contains(keyword) || x.EmployeeCode.Contains(keyword) || (x.Email != null && x.Email.Contains(keyword))));

        return new
        {
            status = "success",
            items = await query.Take(100).Select(x => new
            {
                x.Id,
                x.EmployeeCode,
                x.FullName,
                x.Email,
                x.DepartmentId,
                DepartmentName = x.Department != null ? x.Department.Name : "N/A",
                x.Role,
                x.IsActive
            }).ToListAsync()
        };
    }

    private async Task<object> GetPayrollOverviewAsync(JsonElement args)
    {
        var month = Math.Clamp(GetIntSafe(args, DateTime.Today.Month, "month", "Month"), 1, 12);
        var year = Math.Clamp(GetIntSafe(args, DateTime.Today.Year, "year", "Year"), 2000, 2100);

        var snapshots = _context.PayrollSnapshots.AsNoTracking().Where(x => x.Month == month && x.Year == year);
        var advances = _context.SalaryAdvances.AsNoTracking().Where(x => x.RequestDate.Month == month && x.RequestDate.Year == year && x.Status.ToUpper() == "APPROVED");

        return new
        {
            status = "success",
            month,
            year,
            totalPayroll = await snapshots.SumAsync(x => (decimal?)x.NetSalary) ?? 0m,
            totalApprovedAdvances = await advances.SumAsync(x => (decimal?)x.Amount) ?? 0m,
            finalizedPayslips = await snapshots.CountAsync()
        };
    }

    #region Helper Safe Methods
    private static string NormalizeRequestType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var clean = value.Replace("_", "").Replace("-", "").Replace(" ", "").Trim().ToLowerInvariant();

        if (clean.Contains("leave") || clean.Contains("nghi") || clean.Contains("phep")) return "Leave";
        if (clean.Contains("overtime") || clean.Contains("ot") || clean.Contains("tangca")) return "Overtime";
        if (clean.Contains("advance") || clean.Contains("ungluong") || clean.Contains("salary")) return "SalaryAdvance";

        return string.Empty;
    }

    private static string? GetSafeString(JsonElement args, params string[] names)
    {
        if (args.ValueKind != JsonValueKind.Object) return null;

        foreach (var key in names.SelectMany(GetKeyVariants).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!args.TryGetProperty(key, out var prop) || prop.ValueKind == JsonValueKind.Null) continue;

            if (prop.ValueKind == JsonValueKind.String)
            {
                var text = prop.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            else if (prop.ValueKind != JsonValueKind.Undefined)
            {
                var raw = prop.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(raw)) return raw;
            }
        }
        return null;
    }

    private static IEnumerable<string> GetKeyVariants(string keyName)
    {
        yield return keyName;
        yield return keyName.ToLowerInvariant();
        yield return keyName.ToUpperInvariant();

        if (keyName.Contains('_'))
        {
            yield return string.Concat(keyName.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select((part, index) => index == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
        }
        else
        {
            var snakeCase = new StringBuilder();
            foreach (var character in keyName)
            {
                if (char.IsUpper(character)) snakeCase.Append('_');
                snakeCase.Append(char.ToLowerInvariant(character));
            }
            yield return snakeCase.ToString();
        }
    }

    private static bool HasProperty(JsonElement args, params string[] names)
    {
        if (args.ValueKind != JsonValueKind.Object) return false;
        return names.SelectMany(GetKeyVariants).Any(k => args.TryGetProperty(k, out var p) && p.ValueKind != JsonValueKind.Null);
    }

    private static bool GetBoolSafe(JsonElement args, params string[] names)
    {
        if (args.ValueKind != JsonValueKind.Object) return false;

        foreach (var key in names.SelectMany(GetKeyVariants).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!args.TryGetProperty(key, out var prop) || prop.ValueKind == JsonValueKind.Null) continue;

            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
            if (prop.ValueKind == JsonValueKind.Number) return prop.GetInt32() != 0;
            if (prop.ValueKind == JsonValueKind.String)
            {
                var str = prop.GetString()?.Trim().ToLowerInvariant();
                if (str == "true" || str == "1" || str == "yes" || str == "y" || str == "xac nhan" || str == "xác nhận")
                    return true;
            }
        }
        return false;
    }

    private static List<int> GetIntListSafe(JsonElement args, params string[] names)
    {
        var result = new List<int>();
        if (args.ValueKind != JsonValueKind.Object) return result;

        foreach (var key in names.SelectMany(GetKeyVariants).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!args.TryGetProperty(key, out var prop) || prop.ValueKind == JsonValueKind.Null) continue;

            if (prop.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prop.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var num))
                        result.Add(num);
                    else if (int.TryParse(item.ToString(), out var parsed))
                        result.Add(parsed);
                }
            }
            else if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var singleNum))
            {
                result.Add(singleNum);
            }
            else if (prop.ValueKind == JsonValueKind.String)
            {
                var str = prop.GetString();
                if (!string.IsNullOrWhiteSpace(str))
                {
                    var parts = str.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                    {
                        if (int.TryParse(p.Trim(), out var parsed))
                            result.Add(parsed);
                    }
                }
            }

            if (result.Count > 0) break;
        }

        return result.Where(x => x > 0).Distinct().ToList();
    }

    private static int GetIntSafe(JsonElement args, int fallback, params string[] names)
    {
        var raw = GetSafeString(args, names);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private static int? GetNullableIntSafe(JsonElement args, params string[] names)
    {
        var raw = GetSafeString(args, names);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
    }

    private static DateTime GetDateSafe(JsonElement args, DateTime fallback, params string[] names)
    {
        var raw = GetSafeString(args, names);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        string[] formats = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "yyyy/MM/dd", "dd-MM-yyyy" };
        if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact;

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var generic) ? generic : fallback;
    }
    #endregion
}