using appdiemdanh.Services;
using AppDiemDanh.Models;
using AppDiemDanh.Services;
using AppDiemDanh.Services.Face;
using AppDiemDanh.Swagger;
using Google;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. CẤU HÌNH DATA PROTECTION (SỬA LỖI 400 BAD REQUEST)
// ==========================================
var configuredKeysFolder = builder.Configuration["DataProtection:KeysPath"];
var keysFolder = string.IsNullOrWhiteSpace(configuredKeysFolder)
    ? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "Keys")
    : Path.GetFullPath(configuredKeysFolder);
if (!Directory.Exists(keysFolder))
{
    Directory.CreateDirectory(keysFolder);
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("AppDiemDanh");

// ==========================================
// 2. DATABASE & SERVICES REGISTER
// ==========================================
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            /*
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
            sqlOptions.CommandTimeout(60);
            */
        }));

builder.Services.AddSingleton<IFaceRecognitionService, OnnxFaceRecognitionService>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<IShiftService, ShiftService>();
builder.Services.AddScoped<IHybridScheduleService, HybridScheduleService>();
builder.Services.AddScoped<ILeaveService, LeaveService>();
builder.Services.AddScoped<IAttendanceExplanationService, AttendanceExplanationService>();
builder.Services.AddScoped<IFcmService, FcmService>();
builder.Services.AddScoped<IPayrollService, PayrollService>();
builder.Services.AddScoped<IPayrollPolicyService, PayrollPolicyService>();
builder.Services.AddSingleton<PayrollFormulaEvaluator>();
builder.Services.AddSingleton<PayrollCalculationEngine>();
builder.Services.AddSingleton<PayrollWorkdayCalculator>();
builder.Services.AddScoped<ILatePenaltyService, LatePenaltyService>();
builder.Services.AddScoped<ISalaryAdvanceService, SalaryAdvanceService>();
builder.Services.AddScoped<IOvertimeService, OvertimeService>();
builder.Services.AddScoped<IAccountSecurityService, AccountSecurityService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddSingleton<IClientIpResolver, ClientIpResolver>();
builder.Services.AddHostedService<ShiftAttendanceReminderService>();
builder.Services.AddScoped<IAiFunctionService, AiFunctionService>();
builder.Services.AddScoped<IAdminAiFunctionService, AdminAiFunctionService>();

builder.Services.AddHttpClient<IAdminAiService, AdminAiService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddHttpClient<IGeminiChatService, GeminiChatService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddHttpContextAccessor();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
    options.KnownNetworks.Clear();
});

// ==========================================
// 3. AUTHENTICATION (KẾT HỢP JWT & COOKIE FOR ADMIN)
// ==========================================
var jwtKey = builder.Configuration["Jwt:Key"] ?? "ChuoiBaoMatSieuCapVipPro1234567890!";
var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(options =>
{
    // Mặc định API dùng JWT Bearer
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(5)
    };
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.Cookie.Name = ".AppDiemDanh.Admin.Auth";
    options.LoginPath = "/Admin/Login";
    options.LogoutPath = "/Admin/Logout";
    options.AccessDeniedPath = "/Admin/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.MaxAge = TimeSpan.FromHours(8);
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// ==========================================
// 4. SESSION CONFIGURATION
// ==========================================
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".AppDiemDanh.Admin.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.MaxAge = TimeSpan.FromHours(8);
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.IdleTimeout = TimeSpan.FromHours(8);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AppDiemDanh API", Version = "v1" });

    c.TagActionsBy(api => new[]
    {
        api.ActionDescriptor.RouteValues.TryGetValue("controller", out var controller) && !string.IsNullOrWhiteSpace(controller)
            ? controller.Replace("AdminDashboard", "Dashboard")
                .Replace("AttendanceExplanation", "AttendanceExplanation")
            : "API"
    });

    var xmlFile = "appdiemdanh.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);

    c.OperationFilter<ApiOperationFilter>();
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Chỉ dán nguyên chuỗi Token (KHÔNG gõ chữ 'Bearer ' ở đầu). Ví dụ: eyJhbGci...",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
});

var app = builder.Build();

app.UseDeveloperExceptionPage();
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AppDiemDanh API v1");
    c.RoutePrefix = "swagger";
});

app.UseForwardedHeaders();
app.UseStaticFiles();
app.UseCors("AllowAll");

// ==========================================
// 5. MIDDLEWARE PIPELINE (ĐÚNG THỨ TỰ)
// ==========================================
app.UseRouting();

// UseSession PHẢI đứng trước Authentication & Authorization
app.UseSession();

// Session đang dùng memory cache nên có thể mất sau khi app recycle hoặc đổi worker.
// Khôi phục lại session quản trị từ cookie đã ký để không phát sinh 403 giả.
app.Use(async (context, next) =>
{
    if (!context.Session.GetInt32("AdminEmployeeId").HasValue)
    {
        var cookieAuth = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = cookieAuth.Principal;
        var employeeId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = principal?.FindFirstValue(ClaimTypes.Role);
        var jwtToken = principal?.FindFirstValue("AdminJwtToken");

        if (cookieAuth.Succeeded && int.TryParse(employeeId, out var id) && id > 0 &&
            role is not null && (role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                role.Equals("HR", StringComparison.OrdinalIgnoreCase) ||
                role.Equals("Manager", StringComparison.OrdinalIgnoreCase)))
        {
            context.Session.SetInt32("AdminEmployeeId", id);
            context.Session.SetString("AdminEmployeeName", principal?.Identity?.Name ?? string.Empty);
            context.Session.SetString("AdminRole", role);
            if (!string.IsNullOrWhiteSpace(jwtToken))
                context.Session.SetString("AdminJwtToken", jwtToken);
        }
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();
app.MapGet("/", () => Results.Redirect("/Admin/Login")).AllowAnonymous().ExcludeFromDescription();

// ==========================================
// 6. MIGRATION & SEED DATA
// ==========================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        context.Database.Migrate();
        await services.GetRequiredService<IPermissionService>().SeedAsync();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Lỗi xảy ra khi tự động Migrate Database trên Server.");
    }
}

app.Run();