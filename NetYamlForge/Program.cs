// ファイル概要：アプリケーションのエントリポイント。DI、認証、ローカライズ、ログ、ルーティングを初期化します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using NetYamlForge.Services.Tenant;
using NetYamlForge.Services.Auth;
using System.Globalization;
using NetYamlForge.Data;
using NetYamlForge.Extensions;
using NetYamlForge.Middleware;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Cli;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;

var jsonMode = args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase));

// Windows サービスとして実行するかどうか（--run-as-service フラグまたは環境変数）
var useWindowsService = args.Any(a => a.Equals("--run-as-service", StringComparison.OrdinalIgnoreCase))
    || Environment.GetEnvironmentVariable("DOTNET_RUNNING_AS_WINDOWS_SERVICE") == "true";

if (useWindowsService)
{
    args = args.Where(a => a != "--run-as-service").ToArray();
}

if (args.Any(a => a.Equals("--scaffold-entities", StringComparison.OrdinalIgnoreCase)))
{
    var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
    var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
    var overwrite = !args.Any(a => a.Equals("--no-overwrite", StringComparison.OrdinalIgnoreCase));
    var outputDirArg = args.FirstOrDefault(a => a.StartsWith("--output-dir=", StringComparison.OrdinalIgnoreCase));
    var outputDirName = outputDirArg?.Split('=', 2).ElementAtOrDefault(1);
    var withLabelKeys = args.Any(a => a.Equals("--with-label-keys", StringComparison.OrdinalIgnoreCase));
    var scaffoldResult = new CliScaffoldResult { Command = "scaffold-entities" };
    if (jsonMode) Console.SetOut(TextWriter.Null);
    var exitCode = EntityYamlScaffolder.Run(
        Directory.GetCurrentDirectory(),
        projectName,
        overwrite,
        string.IsNullOrWhiteSpace(outputDirName) ? "entities.generated" : outputDirName,
        withLabelKeys,
        scaffoldResult);
    if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
    Environment.Exit(exitCode);
    return;
}

if (args.Any(a => a.Equals("--scaffold-hook", StringComparison.OrdinalIgnoreCase)))
{
    var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
    var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
    var nameArg = args.FirstOrDefault(a => a.StartsWith("--name=", StringComparison.OrdinalIgnoreCase));
    var hookName = nameArg?.Split('=', 2).ElementAtOrDefault(1);
    var withTests = args.Any(a => a.Equals("--with-tests", StringComparison.OrdinalIgnoreCase));
    var scaffoldResult = new CliScaffoldResult { Command = "scaffold-hook" };
    if (jsonMode) Console.SetOut(TextWriter.Null);
    var exitCode = HookScaffolder.Run(
        Directory.GetCurrentDirectory(),
        projectName,
        hookName,
        withTests,
        scaffoldResult);
    if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
    Environment.Exit(exitCode);
    return;
}

if (args.Any(a => a.Equals("--upgrade-entity-yaml", StringComparison.OrdinalIgnoreCase)))
{
    var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
    var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
    var scaffoldResult = new CliScaffoldResult { Command = "upgrade-entity-yaml" };
    if (jsonMode) Console.SetOut(TextWriter.Null);
    var exitCode = EntityYamlModernizer.Run(Directory.GetCurrentDirectory(), projectName, scaffoldResult);
    if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
    Environment.Exit(exitCode);
    return;
}

if (args.Any(a => a.Equals("--init-project", StringComparison.OrdinalIgnoreCase)))
{
    var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
    var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
    var displayNameArg = args.FirstOrDefault(a => a.StartsWith("--display-name=", StringComparison.OrdinalIgnoreCase));
    var displayName = displayNameArg?.Split('=', 2).ElementAtOrDefault(1);
    var dbTypeArg = args.FirstOrDefault(a => a.StartsWith("--db-type=", StringComparison.OrdinalIgnoreCase));
    var dbType = dbTypeArg?.Split('=', 2).ElementAtOrDefault(1);
    var dbPathArg = args.FirstOrDefault(a => a.StartsWith("--db-path=", StringComparison.OrdinalIgnoreCase));
    var dbPath = dbPathArg?.Split('=', 2).ElementAtOrDefault(1);
    var dbConnectionArg = args.FirstOrDefault(a => a.StartsWith("--db-connection=", StringComparison.OrdinalIgnoreCase));
    var dbConnection = dbConnectionArg?.Split('=', 2).ElementAtOrDefault(1);
    var i18nFallbackModeArg = args.FirstOrDefault(a => a.StartsWith("--i18n-fallback-mode=", StringComparison.OrdinalIgnoreCase));
    var i18nFallbackMode = i18nFallbackModeArg?.Split('=', 2).ElementAtOrDefault(1);
    var autoScaffold = !args.Any(a => a.Equals("--no-auto-scaffold", StringComparison.OrdinalIgnoreCase));
    var force = args.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase));
    var scaffoldResult = new CliScaffoldResult { Command = "init-project" };
    if (jsonMode) Console.SetOut(TextWriter.Null);
    var exitCode = ProjectTemplateScaffolder.Run(
        Directory.GetCurrentDirectory(),
        projectName,
        displayName,
        force,
        dbType,
        dbPath,
        dbConnection,
        autoScaffold,
        i18nFallbackMode,
        scaffoldResult);
    if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
    Environment.Exit(exitCode);
    return;
}

if (args.Any(a => a.Equals("--scaffold-batch-job", StringComparison.OrdinalIgnoreCase)))
{
    var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
    var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
    var nameArg = args.FirstOrDefault(a => a.StartsWith("--name=", StringComparison.OrdinalIgnoreCase));
    var jobName = nameArg?.Split('=', 2).ElementAtOrDefault(1);
    var scaffoldResult = new CliScaffoldResult { Command = "scaffold-batch-job" };
    if (jsonMode) Console.SetOut(TextWriter.Null);
    var exitCode = BatchJobScaffolder.Run(
        Directory.GetCurrentDirectory(),
        projectName,
        jobName ?? "sample_job",
        scaffoldResult);
    if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
    Environment.Exit(exitCode);
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Windows サービスとして実行する場合
if (useWindowsService)
{
    builder.Host.UseWindowsService();
}

builder.Host.UseSerilog((context, cfg) =>
{
    cfg.ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/app-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
});

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddControllersWithViews()
    .AddViewLocalization();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Path = "/";
});
builder.Services.Configure<Microsoft.AspNetCore.Mvc.Razor.RazorViewEngineOptions>(options =>
{
    options.ViewLocationExpanders.Add(new NetYamlForge.Services.ProjectViewLocationExpander());
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ===== サービス登録（Extensions/ServiceCollectionExtensions.cs に委譲）=====
// グループ別の詳細は AddNetYamlForge の各メソッドを参照してください。
builder.Services.AddNetYamlForge();

// 显式确保 ITenantUserService 已注册（解决 AccountController 的 DI 错误）
builder.Services.AddScoped<ITenantUserService, TenantUserService>();
builder.Services.AddScoped<IJpcsUserSyncService, JpcsUserSyncService>();

var app = builder.Build();

app.UsePathBase("/nyf");
app.Use(async (context, next) =>
{
    // Caddy の handle_path で /nyf が剥がされると UsePathBase だけでは PathBase が空のままになる。
    // その場合でも Razor/Url.Content が正しく /nyf を含む URL を生成できるように補正する。
    if (!context.Request.PathBase.HasValue)
    {
        var forwardedPrefix = context.Request.Headers["X-Forwarded-Prefix"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedPrefix))
        {
            context.Request.PathBase = new PathString(forwardedPrefix);
        }
        else
        {
            context.Request.PathBase = new PathString("/nyf");
        }
    }

    await next();
});

await DbInitializer.InitializeAsync(app.Services, app.Configuration);

var supportedCultures = new[] { "en-US", "zh-CN", "ja-JP", "ko-KR" }
    .Select(x => new CultureInfo(x))
    .ToList();

var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en-US"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
};

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseMiddleware<RequestTraceMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
        var project = httpContext.GetRouteValue("project")?.ToString();
        if (!string.IsNullOrWhiteSpace(project))
        {
            diagnosticContext.Set("Project", project);
        }
    };
});
app.UseRequestLocalization(localizationOptions);
app.UseStaticFiles();
app.UseRouting();
app.UseMiddleware<ProjectMiddleware>(); // UseRouting 後・UseAuthentication 前に配置
app.UseAuthentication();
app.UseAuthorization();

// ユーザー個人ホーム：/userhome（プロジェクトルートより先に評価）
app.MapControllerRoute(
    name: "userhome",
    pattern: "userhome/{action=Index}",
    defaults: new { controller = "UserHome", project = "userhome" });

// プロジェクトホーム：/{project}
app.MapControllerRoute(
    name: "project-home",
    pattern: "{project}",
    defaults: new { controller = "Home", action = "Project" });

// プロジェクトルート：/{project}/{controller}/{action}
app.MapControllerRoute(
    name: "project",
    pattern: "{project}/{controller=Dashboard}/{action=Index}/{id?}");

// ルートルート：/{controller}/{action}（デフォルトは Home）
app.MapControllerRoute(
    name: "root",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
