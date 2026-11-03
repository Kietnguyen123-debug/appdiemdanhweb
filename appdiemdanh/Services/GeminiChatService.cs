using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using appdiemdanh.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace appdiemdanh.Services
{
    public class GeminiChatService : IGeminiChatService
    {
        private const string DefaultModel = "gemini-3.5-flash-lite";
        private const string DefaultFallbackModel = "gemini-3.1-flash-lite";
        private const string OverloadMessage = "Máy chủ AI của Google hiện đang quá tải chốc lát. Anh/Chị vui lòng thử lại sau vài giây nhé!";

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly IAiFunctionService _functionService;
        private readonly ILogger<GeminiChatService> _logger;
        private readonly IHostEnvironment _environment;

        public GeminiChatService(
            HttpClient httpClient,
            IConfiguration config,
            IAiFunctionService functionService,
            ILogger<GeminiChatService> logger,
            IHostEnvironment environment)
        {
            _httpClient = httpClient;
            _config = config;
            _functionService = functionService;
            _logger = logger;
            _environment = environment;
        }

        public async Task<ChatResponseDto> ChatAsync(ChatRequestDto request, UserJwtContext userContext)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Message))
                {
                    return new ChatResponseDto { Success = false, Reply = "Vui lòng nhập nội dung cần hỗ trợ." };
                }

                string apiKey = _config["Gemini:ApiKey"] ?? throw new InvalidOperationException("Gemini API key chưa được cấu hình.");
                string model = _config["Gemini:Model"] ?? DefaultModel;
                string fallbackModel = _config["Gemini:FallbackModel"] ?? DefaultFallbackModel;
                string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

                var contentsArray = PrepareContents(request.History, request.Message);
                var requestBody = BuildRequestBody(contentsArray, BuildSystemInstruction(userContext));
                var executedActions = new List<string>();

                const int maxLoop = 3;
                for (var round = 0; round < maxLoop; round++)
                {
                    if (round > 0)
                        await Task.Delay(1000);

                    var responseNode = await SendToGeminiApiAsync(endpoint, fallbackModel, requestBody);
                    var partsArray = responseNode?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
                    var functionCallParts = partsArray?.Where(part => part?["functionCall"] != null).ToList();

                    // Nếu Gemini không gọi Tool nào -> Trả lời bằng Text trực tiếp
                    if (functionCallParts == null || functionCallParts.Count == 0)
                    {
                        return new ChatResponseDto
                        {
                            Success = true,
                            Reply = GetTextReply(responseNode) ?? "Em chưa hiểu rõ ý anh/chị, vui lòng mô tả chi tiết hơn nhé.",
                            ExecutedAction = executedActions.Count == 0 ? null : string.Join("; ", executedActions.Distinct().Select(TranslateActionToVietnamese))
                        };
                    }

                    // Đóng gói lượt trả lời của Model chứa functionCall vào History
                    contentsArray.Add(new JsonObject
                    {
                        ["role"] = "model",
                        ["parts"] = partsArray!.DeepClone()
                    });

                    // Tạo lượt phản hồi kết quả Tool từ User
                    var functionResponseParts = new JsonArray();
                    foreach (var part in functionCallParts)
                    {
                        var functionCall = part!["functionCall"]!;
                        var functionName = functionCall["name"]?.ToString();
                        if (string.IsNullOrWhiteSpace(functionName)) continue;

                        string jsonResult;
                        try
                        {
                            var argsElement = JsonSerializer.Deserialize<JsonElement>(functionCall["args"]?.ToJsonString() ?? "{}");
                            _logger.LogInformation("[AI TOOL EXECUTE] Function: {FunctionName}; Args: {Args}", functionName, argsElement);
                            jsonResult = await _functionService.ExecuteFunctionAsync(functionName, argsElement, userContext);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[AI TOOL ERROR] Function: {FunctionName}", functionName);
                            jsonResult = JsonSerializer.Serialize(new
                            {
                                status = "warning",
                                message = "Đã ghi nhận thông tin nhưng cần bạn kiểm tra lại chi tiết."
                            });
                        }

                        executedActions.Add(functionName);

                        functionResponseParts.Add(new JsonObject
                        {
                            ["functionResponse"] = new JsonObject
                            {
                                ["name"] = functionName,
                                ["response"] = ParseToResponseObject(jsonResult)
                            }
                        });
                    }

                    if (functionResponseParts.Count == 0) break;

                    // Thêm lượt trả lời kết quả hàm của user (Chỉ chứa functionResponse parts)
                    contentsArray.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["parts"] = functionResponseParts.DeepClone()
                    });

                    // Ở vòng lặp cuối cùng, tắt tools để ép Gemini tổng hợp văn bản
                    bool isLastRound = (round == maxLoop - 1);
                    requestBody = BuildRequestBody(contentsArray, BuildSystemInstruction(userContext), includeTools: !isLastRound);
                }

                // Trường hợp gọi lại API sau khi xử lý xong các lượt Tool
                var finalResponse = await SendToGeminiApiAsync(
                    endpoint,
                    fallbackModel,
                    BuildRequestBody(contentsArray, BuildSystemInstruction(userContext), includeTools: false));

                return new ChatResponseDto
                {
                    Success = true,
                    Reply = GetTextReply(finalResponse) ?? "Em đã xử lý các tác vụ nhưng chưa nhận được nội dung tổng hợp.",
                    ExecutedAction = executedActions.Count == 0 ? null : string.Join("; ", executedActions.Distinct().Select(TranslateActionToVietnamese))
                };
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Gemini request timed out");
                return new ChatResponseDto { Success = false, Reply = OverloadMessage };
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "Gemini request timed out");
                return new ChatResponseDto { Success = false, Reply = OverloadMessage };
            }
            catch (InvalidOperationException ex) when (ex.Message == OverloadMessage)
            {
                _logger.LogError(ex, "Gemini is overloaded after retries and fallback");
                return new ChatResponseDto { Success = false, Reply = OverloadMessage };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gemini Exception: {Message} | Inner: {Inner}", ex.Message, ex.InnerException?.Message);
                var detail = _environment.IsDevelopment() || _environment.IsStaging() ? $" Chi tiết: {ex.Message}" : string.Empty;
                return new ChatResponseDto { Success = false, Reply = $"Rất tiếc, đã xảy ra lỗi trong quá trình kết nối với trợ lý AI. Vui lòng thử lại sau ít phút!{detail}" };
            }
        }

        private string BuildSystemInstruction(UserJwtContext userContext)
        {
            var now = DateTime.Now;
            var previousMonth = now.AddMonths(-1);
            return $@"
Bạn là SmartHR AI - chuyên gia trợ lý Nhân sự cao cấp và quản trị doanh nghiệp của AppDiemDanh.
Thời gian hệ thống: {now:dd/MM/yyyy HH:mm} ({now.ToString("dddd", new CultureInfo("vi-VN"))}).
Người dùng: {userContext.FullName}; Mã nhân viên: {userContext.EmployeeCode}; ID: {userContext.EmployeeId}; Role: {userContext.Role}; Chi nhánh ID: {userContext.BranchId}.

=== NGUYÊN TẮC XỬ LÝ & TRÌNH BÀY VĂN BẢN ===
1. GIỮ NGUYÊN NGỮ CẢNH ĐA LƯỢT (MULTI-TURN):
   - Luôn đọc lại lịch sử để theo dõi ngữ cảnh. Khi người dùng bổ sung thông tin ngắn (như 'lý do ốm nha', 'đổi sang ngày mai', 'xác nhận gửi'), BẮT BUỘC kết hợp với các thông tin đã có ở lượt thoại trước (ngày bắt đầu, ngày kết thúc, loại nghỉ) để truyền đầy đủ các tham số khi gọi Tool.
   - Khi truyền ngày tháng vào Tool Call, BẮT BUỘC dùng định dạng ISO `YYYY-MM-DD` (Ví dụ: {now:yyyy-MM-dd}).

2. PHÂN BIỆT CÂU HỎI QUY ĐỊNH VỚI HÀNH ĐỘNG:
   - Với các câu hỏi về QUY ĐỊNH, CHÍNH SÁCH, LUẬT LAO ĐỘNG, BHXH, THAI SẢN, QUY TRÌNH NGHỈ PHÉP, CÁCH TÍNH TIỀN TĂNG CA (VD: 'Quy định tăng ca', 'Chính sách thai sản', 'Luật bảo hiểm'): TUYỆT ĐỐI KHÔNG GỌI CÁC TOOL TẠO ĐƠN (`SubmitOvertimeRequest`, `CreateLeaveRequest`...). Trả lời trực tiếp bằng văn bản Markdown dựa trên tri thức tích hợp sẵn.
   - CHỈ GỌI TOOL TẠO ĐƠN khi người dùng thể hiện rõ ý định YÊU CẦU ĐĂNG KÝ hoặc NỘP ĐƠN cho bản thân.

3. TUYỆT ĐỐI KHÔNG SỬ DỤNG CÚ PHÁP LATEX:
   - Không dùng cặp dấu $$,$, không dùng các ký hiệu lệnh LaTeX như \text{{}}, \times, \frac{{}}{{}}, \sum, \cdot,...
   - Trình bày công thức tính toán bằng tiếng Việt thuần túy kết hợp in đậm Markdown và phép toán thông thường (+, -, x, /, %).

4. QUY ĐỔI THỜI GIAN:
   - Hôm nay = {now:yyyy-MM-dd}; Ngày mai = {now.AddDays(1):yyyy-MM-dd}; Hôm qua = {now.AddDays(-1):yyyy-MM-dd}; Tháng này = {now:MM/yyyy}; Tháng trước = {previousMonth:MM/yyyy}.

5. Xưng 'Em', gọi người dùng lịch sự, trả lời bằng tiếng Việt thuần, Markdown ngắn gọn, trình bày mạch lạc.
";
        }

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

        private JsonObject BuildRequestBody(JsonArray contents, string systemInstruction, bool includeTools = true)
        {
            var body = new JsonObject
            {
                ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = systemInstruction } } },
                ["generationConfig"] = new JsonObject { ["maxOutputTokens"] = 2048 },
                ["contents"] = contents.DeepClone()
            };

            if (includeTools)
                body["tools"] = JsonSerializer.SerializeToNode(GetToolsDefinition());

            return body;
        }

        private static string? GetTextReply(JsonNode? response)
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
            try
            {
                var node = JsonNode.Parse(jsonResult);
                if (node is JsonObject responseObject)
                    return responseObject;

                return new JsonObject { ["result"] = node };
            }
            catch (JsonException)
            {
                return new JsonObject { ["result"] = jsonResult };
            }
        }

        private static string TranslateActionToVietnamese(string functionName) => functionName switch
        {
            "GetMyAttendanceSummary" => "Xem báo cáo chấm công",
            "GetMyLeaveBalance" => "Kiểm tra số dư phép năm",
            "GetMyShiftSchedule" => "Xem lịch làm việc",
            "GetMyPayslip" => "Xem phiếu lương",
            "GetAdminDashboardOverview" => "Xem tổng quan điểm danh",
            "CreateLeaveRequest" => "Tạo đơn xin nghỉ phép",
            "RequestSalaryAdvance" => "Yêu cầu tạm ứng lương",
            "SubmitAttendanceExplanation" => "Giải trình quên chấm công",
            "SubmitOvertimeRequest" => "Đăng ký làm tăng ca",
            _ => functionName
        };

        private async Task<JsonNode?> SendToGeminiApiAsync(string url, string fallbackModel, JsonObject body)
        {
            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    return JsonNode.Parse(responseString);
                }

                var isTransient = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable;
                var errorBody = await response.Content.ReadAsStringAsync();

                if (response.StatusCode is System.Net.HttpStatusCode.ServiceUnavailable
                    or System.Net.HttpStatusCode.TooManyRequests
                    or System.Net.HttpStatusCode.NotFound)
                {
                    var fallbackUrl = BuildFallbackEndpoint(url, fallbackModel);
                    var fallbackResponseBody = string.Empty;
                    if (fallbackUrl != null)
                    {
                        using var fallbackContent = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                        using var fallbackResponse = await _httpClient.PostAsync(fallbackUrl, fallbackContent);
                        fallbackResponseBody = await fallbackResponse.Content.ReadAsStringAsync();

                        if (fallbackResponse.IsSuccessStatusCode)
                            return JsonNode.Parse(fallbackResponseBody);
                    }

                    _logger.LogError("[GEMINI FALLBACK] Gemini returned HTTP {StatusCode}; fallback model {FallbackModel} failed. Response: {Response}", response.StatusCode, fallbackModel, fallbackResponseBody);
                    if (response.StatusCode is System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.TooManyRequests)
                        throw new InvalidOperationException(OverloadMessage);

                    throw new InvalidOperationException($"Gemini trả về HTTP {(int)response.StatusCode} ({response.StatusCode}). {fallbackResponseBody}");
                }

                if (!isTransient)
                    throw new InvalidOperationException($"Gemini trả về HTTP {(int)response.StatusCode} ({response.StatusCode}). {errorBody}");

                if (attempt == maxAttempts)
                    throw new HttpRequestException($"Gemini trả về HTTP {(int)response.StatusCode} ({response.StatusCode}). {errorBody}");

                _logger.LogWarning("[GEMINI RETRY] Attempt {Attempt} due to status {StatusCode}", attempt + 1, response.StatusCode);
                await Task.Delay(TimeSpan.FromSeconds(attempt == 1 ? 1.5 : 3));
            }

            throw new InvalidOperationException("Gemini request không thể hoàn tất.");
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

        private static object GetToolsDefinition()
        {
            return new[]
            {
                new
                {
                    // Đã sửa: Phải dùng camelCase 'functionDeclarations' theo chuẩn Gemini REST API v1beta
                    functionDeclarations = new object[]
                    {
                        // ================= TRA CỨU DỮ LIỆU =================
                        new {
                            name = "GetMyAttendanceSummary",
                            description = "Xem tổng quan báo cáo chấm công (số ngày làm, phút trễ, về sớm) theo tháng/năm",
                            parameters = new {
                                type = "OBJECT",
                                properties = new {
                                    month = new { type = "INTEGER", description = "Tháng cần tra cứu (1-12)" },
                                    year = new { type = "INTEGER", description = "Năm cần tra cứu" }
                                },
                                required = new[] { "month", "year" }
                            }
                        },
                        new {
                            name = "GetMyLeaveBalance",
                            description = "Xem số ngày phép năm còn lại, đã dùng và tổng quỹ phép",
                            parameters = new { type = "OBJECT", properties = new { } }
                        },
                        new {
                            name = "GetMyShiftSchedule",
                            description = "Xem ca làm việc hôm nay hoặc lịch làm việc sắp tới",
                            parameters = new { type = "OBJECT", properties = new { } }
                        },
                        new {
                            name = "GetMyPayslip",
                            description = "Xem chi tiết phiếu lương cá nhân theo tháng/năm",
                            parameters = new {
                                type = "OBJECT",
                                properties = new {
                                    month = new { type = "INTEGER", description = "Tháng cần xem lương (1-12)" },
                                    year = new { type = "INTEGER", description = "Năm cần xem lương" },
                                    standardWorkDays = new { type = "INTEGER", description = "Số ngày công tiêu chuẩn (mặc định 26)" }
                                },
                                required = new[] { "month", "year" }
                            }
                        },
                        new {
                            name = "GetAdminDashboardOverview",
                            description = "Xem tổng quan tình hình điểm danh toàn công ty dành cho Quản lý/HR/Admin",
                            parameters = new { type = "OBJECT", properties = new { } }
                        },

                        // ================= TẠO ĐƠN & THỰC THI HÀNH ĐỘNG =================
                        new {
                            name = "CreateLeaveRequest",
                            description = "CHỈ GỌI HÀM NÀY khi người dùng YÊU CẦU NỘP/TẠO ĐƠN xin nghỉ phép chính thức. TUYỆT ĐỐI KHÔNG GỌI HÀM NÀY khi người dùng hỏi quy định, chính sách hay cách tính phép.",
                            parameters = new {
                                type = "OBJECT",
                                properties = new {
                                    startDate = new { type = "STRING", description = "Ngày bắt đầu nghỉ, định dạng YYYY-MM-DD" },
                                    endDate = new { type = "STRING", description = "Ngày kết thúc nghỉ, định dạng YYYY-MM-DD" },
                                    leaveType = new { type = "STRING", description = "Loại nghỉ: Phép năm, Nghỉ ốm, Nghỉ không lương" },
                                    reason = new { type = "STRING", description = "Lý do nghỉ phép" }
                                },
                                required = new[] { "startDate" }
                            }
                        },
                        new {
                            name = "RequestSalaryAdvance",
                            description = "CHỈ GỌI HÀM NÀY khi người dùng YÊU CẦU TẠM ỨNG LƯƠNG cụ thể. TUYỆT ĐỐI KHÔNG GỌI HÀM NÀY khi hỏi quy định.",
                            parameters = new {
                                type = "OBJECT",
                                properties = new {
                                    amount = new { type = "NUMBER", description = "Số tiền tạm ứng tính bằng VNĐ (Ví dụ: 2000000)" },
                                    reason = new { type = "STRING", description = "Lý do tạm ứng" }
                                },
                                required = new[] { "amount" }
                            }
                        },
                        new {
                            name = "SubmitAttendanceExplanation",
                            description = "CHỈ GỌI HÀM NÀY khi người dùng YÊU CẦU GIẢI TRÌNH quên chấm công.",
                            parameters = new {
                                type = "OBJECT",
                                properties = new {
                                    targetDate = new { type = "STRING", description = "Ngày cần giải trình (YYYY-MM-DD)" },
                                    reason = new { type = "STRING", description = "Lý do quên chấm công" }
                                },
                                required = new[] { "targetDate" }
                            }
                        },
                        new {
                            name = "SubmitOvertimeRequest",
                            description = "CHỈ GỌI HÀM NÀY khi người dùng YÊU CẦU ĐĂNG KÝ hoặc NỘP ĐƠN làm tăng ca (OT). TUYỆT ĐỐI KHÔNG GỌI HÀM NÀY khi người dùng hỏi quy định, chính sách, cách tính tiền tăng ca hay tra cứu.",
                            parameters = new {
                                type = "OBJECT",
                                properties = new {
                                    workDate = new { type = "STRING", description = "Ngày làm tăng ca (YYYY-MM-DD)" },
                                    hours = new { type = "NUMBER", description = "Số giờ làm tăng ca" }
                                },
                                required = new[] { "workDate" }
                            }
                        }
                    }
                }
            };
        }
    }
}