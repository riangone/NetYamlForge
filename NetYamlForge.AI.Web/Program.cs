// NetYamlForge AI - 独立 AI チャット製品
// NetYamlForge メインアプリと完全分離した単独 Web サービス

using Serilog;
using NetYamlForge.AI;
using NetYamlForge.AI.Hubs;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ユーザーが UI から変更した設定を優先読み込み
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile("ai-user-settings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// ===== Serilog =====
builder.Host.UseSerilog((context, cfg) =>
{
    cfg.ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/ai-service-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14);
});

// ===== Cookie 認証（NetYamlForge Identity 不要）=====
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath        = "/Account/Login";
        options.LogoutPath       = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.Cookie.Name      = "nyf_ai_auth";
        options.Cookie.HttpOnly  = true;
        options.Cookie.SameSite  = SameSiteMode.Lax;
        options.ExpireTimeSpan   = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

// ===== AI サービス（NetYamlForge.AI ライブラリ）=====
builder.Services.AddNetYamlForgeAI(builder.Configuration);

// ===== MVC + Razor Views =====
builder.Services.AddControllersWithViews()
    .AddApplicationPart(typeof(NetYamlForge.AI.Controllers.AIController).Assembly);

// ===== CORS（組み込みモードで主アプリからアクセスする場合）=====
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseSerilogRequestLogging();
app.UseCors("AllowAll");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// ===== SignalR Hubs =====
app.MapHub<AIChatHub>("/aiChatHub");
app.MapHub<AIProgressHub>("/aiProgressHub");
app.MapHub<AIDebateHub>("/aiDebateHub");
app.MapHub<NaturalLanguageQueryHub>("/nlQueryHub");

// ===== MVC ルート =====
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ===== バックグラウンドタスクキュー起動 =====
var taskQueue = app.Services.GetService<NetYamlForge.AI.Services.TaskQueueService>();
taskQueue?.StartProcessing();

app.Logger.LogInformation("NetYamlForge AI Chat Service started on {Urls}", string.Join(", ", builder.WebHost.GetSetting("urls") ?? "http://localhost:5200"));

app.Run();
