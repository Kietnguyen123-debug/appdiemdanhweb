using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using appdiemdanh.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace appdiemdanh.Services;

public interface IAdminAiService
{
    Task<ChatResponseDto> ChatAsync(ChatRequestDto request, UserJwtContext adminContext);
}

public sealed class AdminAiService : IAdminAiService
{
    private const string DefaultModel = "gemini-3.5-flash-lite";
    private const string DefaultFallbackModel = "gemini-3.1-flash-lite";
    private const string OverloadMessage = "Máy chủ AI hiện đang quá tải. Anh/Chị vui lòng thử lại sau vài giây nhé!";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IAdminAiFunctionService _functionService;
    private readonly ILogger<AdminAiService> _logger;

    public AdminAiService(
        HttpClient httpClient,
        IConfiguration configuration,
        IAdminAiFunctionService functionService,
        ILogger<AdminAiService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _functionService = functionService;
        _logger = logger;
    }

    public async Task<ChatResponseDto> ChatAsync(ChatRequestDto request, UserJwtContext adminContext)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
                return new ChatResponseDto { Success = false, Reply = "Tin nhắn không được để trống." };

            var key = _configuration["Gemini:ApiKey"];
            if (string.IsNullOrWhiteSpace(key))
                return new ChatResponseDto { Success = false, Reply = "Gemini API key chưa được cấu hình." };

            var model = _configuration["Gemini:Model"] ?? DefaultModel;
            var fallbackModel = _configuration["Gemini:FallbackModel"] ?? DefaultFallbackModel;
            var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={key}";
            var contents = PrepareContents(request.History, request.Message);
            var actions = new List<string>();

            const int maxLoop = 3;
            for (var round = 0; round < maxLoop; round++)
            {
                if (round > 0)
                    await Task.Delay(1000);

                var responseNode = await SendToGeminiAsync(endpoint, fallbackModel, BuildRequest(contents, BuildSystemInstruction(adminContext)));
                var parts = responseNode?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
                var calls = parts?.Where(x => x?["functionCall"] != null).ToList();

                // Nếu Gemini không gọi Tool -> Trả lời bằng văn bản
                if (calls == null || calls.Count == 0)
                {
                    return new ChatResponseDto
                    {
                        Success = true,
                        Reply = GetText(responseNode) ?? "Em chưa nhận được kết quả tổng hợp.",
                        ExecutedAction = actions.Count == 0 ? null : string.Join("; ", actions.Distinct().Select(TranslateActionToVietnamese))
                    };
                }

                // Đóng gói lượt trả lời của Model chứa functionCall
                contents.Add(new JsonObject { ["role"] = "model", ["parts"] = parts.DeepClone() });

                // Lượt trả lời kết quả hàm của User (Chỉ chứa các functionResponse parts)
                var functionResponses = new JsonArray();
                foreach (var call in calls)
                {
                    var functionCall = call!["functionCall"]!;
                    var name = functionCall["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    try
                    {
                        var args = JsonSerializer.Deserialize<JsonElement>(functionCall["args"]?.ToJsonString() ?? "{}");
                        _logger.LogInformation("[ADMIN AI TOOL EXECUTE] Function: {FunctionName}; Args: {Args}", name, args);
                        var result = await _functionService.ExecuteFunctionAsync(name, args, adminContext, request.Message);

                        actions.Add(name);
                        functionResponses.Add(new JsonObject
                        {
                            ["functionResponse"] = new JsonObject
                            {
                                ["name"] = name,
                                ["response"] = ParseToResponseObject(result)
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[ADMIN AI TOOL ERROR] Function: {FunctionName}", name);
                        return new ChatResponseDto
                        {
                            Success = false,
                            Reply = $"[LỖI TOOL] {ex.Message}" + (ex.InnerException != null ? $" | Inner: {ex.InnerException.Message}" : ""),
                            ExecutedAction = actions.Count == 0 ? null : string.Join("; ", actions.Distinct().Select(TranslateActionToVietnamese))
                        };
                    }
                }

                if (functionResponses.Count == 0) break;

                contents.Add(new JsonObject { ["role"] = "user", ["parts"] = functionResponses.DeepClone() });

                // Ở vòng lặp cuối cùng, gọi API lần cuối không kèm tools để ép Gemini tổng hợp văn bản
                if (round == maxLoop - 1)
                {
                    var finalResponse = await SendToGeminiAsync(endpoint, fallbackModel, BuildRequest(contents, BuildSystemInstruction(adminContext), includeTools: false));
                    return new ChatResponseDto
                    {
                        Success = true,
                        Reply = GetText(finalResponse) ?? "Em đã xử lý các tác vụ quản trị nhưng chưa nhận được nội dung tổng hợp.",
                        ExecutedAction = actions.Count == 0 ? null : string.Join("; ", actions.Distinct().Select(TranslateActionToVietnamese))
                    };
                }
            }

            return new ChatResponseDto
            {
                Success = false,
                Reply = "Trợ lý AI Admin cần quá nhiều bước để hoàn tất yêu cầu. Vui lòng thử lại với câu hỏi ngắn gọn hơn.",
                ExecutedAction = actions.Count == 0 ? null : string.Join("; ", actions.Distinct().Select(TranslateActionToVietnamese))
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Admin Gemini request timed out");
            return new ChatResponseDto { Success = false, Reply = OverloadMessage };
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Admin Gemini request timed out");
            return new ChatResponseDto { Success = false, Reply = OverloadMessage };
        }
        catch (InvalidOperationException ex) when (ex.Message == OverloadMessage)
        {
            _logger.LogError(ex, "Admin Gemini is overloaded after retries and fallback");
            return new ChatResponseDto { Success = false, Reply = OverloadMessage };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin Gemini chatbot failed");
            return new ChatResponseDto
            {
                Success = false,
                Reply = $"[LỖI SERVER] {ex.Message}" + (ex.InnerException != null ? $" | Inner: {ex.InnerException.Message}" : "")
            };
        }
    }

    private static string BuildSystemInstruction(UserJwtContext context) => $@"
Bạn là Trợ lý Quản trị HR Thông minh, chuyên hỗ trợ Ban Giám đốc và Phòng Nhân sự theo dõi chỉ số, duyệt hồ sơ và quản lý nhân sự.
Người dùng: {context.FullName}; AdminUserId: {context.EmployeeId}; Role: {context.Role}; thời gian: {DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}.

=== NGUYÊN TẮC QUẢN TRỊ ===
1. PHONG CÁCH TRÌNH BÀY: Chuyên nghiệp, ngắn gọn, báo cáo số liệu chính xác bằng Bảng Markdown hoặc Danh sách gạch đầu dòng rõ ràng.
2. TRA CỨU & THỐNG KÊ: Khi Admin hỏi báo cáo, điểm danh, quỹ lương, đơn từ hay tìm nhân viên, hãy gọi ngay Function Tool tương ứng và tổng hợp kết quả thành bảng biểu trực quan.
3. PHÊ DUYỆT / HÀNH ĐỘNG HÀNG LOẠT:
   - Với các lệnh duyệt/từ chối đơn hàng loạt: Trước tiên hãy liệt kê danh sách các đơn bị tác động và yêu cầu Admin xác nhận rõ ràng bằng cụm từ ""Xác nhận Duyệt"" (hoặc ""Xác nhận Từ chối"").
   - Chỉ gọi `ApproveRequestsBulk` khi người dùng ĐÃ XÁC NHẬN. Khi gọi hàm này, BẮT BUỘC truyền tham số `confirmed = true`.
4. TUYỆT ĐỐI KHÔNG DÙNG CÚ PHÁP LATEX:
   - Cấm các ký hiệu `$$`, `$`, `\text{{}}`, `\times`. Mọi công thức hoặc số liệu tài chính viết bằng Tiếng Việt thuần túy và Markdown in đậm.
5. Bảo mật dữ liệu nội bộ, không tự tạo dữ liệu giả mạo.
";

    private static JsonObject BuildRequest(JsonArray contents, string instruction, bool includeTools = true)
    {
        var request = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = instruction } } },
            ["generationConfig"] = new JsonObject { ["maxOutputTokens"] = 2048 },
            ["contents"] = contents.DeepClone()
        };

        if (includeTools)
            request["tools"] = JsonSerializer.SerializeToNode(GetTools());

        return request;
    }

    private async Task<JsonNode?> SendToGeminiAsync(string endpoint, string fallbackModel, JsonObject request)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(endpoint, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                return string.IsNullOrWhiteSpace(responseText) ? null : JsonNode.Parse(responseText);

            var transient = response.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;

            if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests or HttpStatusCode.NotFound)
            {
                var fallbackEndpoint = BuildFallbackEndpoint(endpoint, fallbackModel);
                var fallbackResponseText = string.Empty;
                if (fallbackEndpoint != null)
                {
                    using var fallbackContent = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
                    using var fallbackResponse = await _httpClient.PostAsync(fallbackEndpoint, fallbackContent);
                    fallbackResponseText = await fallbackResponse.Content.ReadAsStringAsync();

                    if (fallbackResponse.IsSuccessStatusCode)
                        return string.IsNullOrWhiteSpace(fallbackResponseText) ? null : JsonNode.Parse(fallbackResponseText);
                }

                _logger.LogError("[ADMIN AI FALLBACK] Gemini returned HTTP {StatusCode}; fallback model {FallbackModel} failed. Response: {Response}", response.StatusCode, fallbackModel, fallbackResponseText);
                if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests)
                    throw new InvalidOperationException(OverloadMessage);

                throw new InvalidOperationException("HTTP " + (int)response.StatusCode + " (" + response.StatusCode + "): " + fallbackResponseText);
            }

            if (!transient || attempt == maxAttempts)
                throw new InvalidOperationException("HTTP " + (int)response.StatusCode + " (" + response.StatusCode + "): " + responseText);

            _logger.LogWarning("[ADMIN AI RETRY] Attempt {Attempt} due to status {StatusCode}", attempt, response.StatusCode);
            await Task.Delay(TimeSpan.FromSeconds(attempt == 1 ? 1.5 : 3));
        }

        throw new InvalidOperationException("Không thể kết nối Gemini.");
    }

    private static string? BuildFallbackEndpoint(string endpoint, string fallbackModel)
    {
        const string modelMarker = "/models/";
        const string actionMarker = ":generateContent";

        var modelStart = endpoint.IndexOf(modelMarker, StringComparison.Ordinal);
        if (modelStart < 0)
            return null;

        modelStart += modelMarker.Length;
        var modelEnd = endpoint.IndexOf(actionMarker, modelStart, StringComparison.Ordinal);
        if (modelEnd < 0)
            return null;

        var currentModel = endpoint[modelStart..modelEnd];
        if (currentModel.Equals(fallbackModel, StringComparison.OrdinalIgnoreCase))
            return null;

        return endpoint[..modelStart] + fallbackModel + endpoint[modelEnd..];
    }

    private static object GetTools() => new[]
    {
        new
        {
            functionDeclarations = new object[]
            {
                Tool("GetCompanyAttendanceSummary", "Báo cáo điểm danh công ty theo ngày và phòng ban", new
                {
                    date = new { type = "STRING", description = "Ngày cần báo cáo (YYYY-MM-DD)" },
                    departmentId = new { type = "INTEGER", description = "ID phòng ban (tùy chọn)" }
                }),
                Tool("GetPendingRequests", "Liệt kê danh sách đơn đang chờ duyệt theo loại", new
                {
                    requestType = new { type = "STRING", description = "Loại đơn: Leave, Overtime hoặc SalaryAdvance" }
                }),
                Tool("ApproveRequestsBulk", "Duyệt hoặc từ chối nhiều đơn sau khi Admin đã xác nhận", new
                {
                    requestType = new { type = "STRING", description = "Loại đơn: Leave, Overtime hoặc SalaryAdvance" },
                    requestIds = new { type = "ARRAY", items = new { type = "INTEGER" }, description = "Danh sách ID các đơn cần duyệt/từ chối" },
                    isApproved = new { type = "BOOLEAN", description = "true = Duyệt, false = Từ chối" },
                    note = new { type = "STRING", description = "Ghi chú phê duyệt" },
                    confirmed = new { type = "BOOLEAN", description = "Bắt buộc truyền true khi Admin đã xác nhận" }
                }),
                Tool("SearchEmployees", "Tìm kiếm nhân viên theo tên, mã, email, phòng ban và trạng thái", new
                {
                    keyword = new { type = "STRING", description = "Tên, mã nhân viên hoặc email" },
                    departmentId = new { type = "INTEGER", description = "ID phòng ban" },
                    status = new { type = "STRING", description = "Active hoặc Inactive" }
                }),
                Tool("GetPayrollOverview", "Tổng quan quỹ lương, tiền ứng và phiếu lương đã chốt theo tháng/năm", new
                {
                    month = new { type = "INTEGER", description = "Tháng (1-12)" },
                    year = new { type = "INTEGER", description = "Năm" }
                })
            }
        }
    };

    private static object Tool(string name, string description, object properties) => new { name, description, parameters = new { type = "OBJECT", properties } };

    private static JsonArray PrepareContents(IEnumerable<ChatMessageHistoryDto>? history, string message)
    {
        var contents = new JsonArray();
        var validHistory = (history ?? Enumerable.Empty<ChatMessageHistoryDto>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Text) && !IsSystemError(item.Text))
            .Where(item => item.Role != null && (item.Role.Equals("user", StringComparison.OrdinalIgnoreCase) || item.Role.Equals("model", StringComparison.OrdinalIgnoreCase) || item.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)))
            .TakeLast(8)
            .SkipWhile(item => item.Role.Equals("model", StringComparison.OrdinalIgnoreCase) || item.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase));

        foreach (var item in validHistory)
        {
            var role = item.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user" : "model";
            AppendTextTurn(contents, role, item.Text.Trim());
        }

        AppendTextTurn(contents, "user", message.Trim());
        return contents;
    }

    private static void AppendTextTurn(JsonArray contents, string role, string text)
    {
        if (contents.LastOrDefault() is JsonObject last && last["role"]?.ToString() == role)
        {
            var existing = last["parts"]?[0]?["text"]?.ToString();
            last["parts"] = new JsonArray { new JsonObject { ["text"] = $"{existing}\n{text}" } };
            return;
        }

        contents.Add(new JsonObject { ["role"] = role, ["parts"] = new JsonArray { new JsonObject { ["text"] = text } } });
    }

    private static bool IsSystemError(string text) =>
        text.Contains("⚠️", StringComparison.Ordinal) ||
        text.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Rất tiếc", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Lỗi", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("[system error]", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("[error]", StringComparison.OrdinalIgnoreCase);

    private static string? GetText(JsonNode? response)
    {
        var parts = response?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
        var text = parts == null
            ? null
            : string.Join("\n", parts
                .Where(part => part?["text"] != null && !string.IsNullOrWhiteSpace(part["text"]!.ToString()))
                .Select(part => part!["text"]!.ToString())
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        if (string.IsNullOrWhiteSpace(text)) return null;

        return CleanLatexArtifacts(text);
    }

    private static string CleanLatexArtifacts(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var cleaned = text;
        cleaned = Regex.Replace(cleaned, @"\\times", "x");
        cleaned = Regex.Replace(cleaned, @"\\cdot", "*");
        cleaned = Regex.Replace(cleaned, @"\\text\{([^}]*)\}", "$1");
        cleaned = Regex.Replace(cleaned, @"\\frac\{([^}]*)\}\{([^}]*)\}", "($1 / $2)"); cleaned = cleaned.Replace("$$", "").Replace("$", "");

        return cleaned;
    }

    private static JsonObject ParseToResponseObject(string jsonResult)
    {
        var node = JsonNode.Parse(jsonResult);
        if (node is JsonObject responseObject)
            return responseObject;

        return new JsonObject { ["result"] = node };
    }

    private static string TranslateActionToVietnamese(string functionName) => functionName switch
    {
        "GetCompanyAttendanceSummary" => "Báo cáo điểm danh công ty",
        "GetPendingRequests" => "Tra cứu danh sách đơn chờ duyệt",
        "ApproveRequestsBulk" => "Duyệt/Từ chối đơn hàng loạt",
        "SearchEmployees" => "Tìm kiếm hồ sơ nhân viên",
        "GetPayrollOverview" => "Tổng quan quỹ lương & ứng lương",
        _ => functionName
    };
}