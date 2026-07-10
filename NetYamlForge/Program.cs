// ファイル概要：アプリケーションのエントリポイント。DI、認証、ローカライズ、ログ、ルーティングを初期化します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using NetYamlForge.Services.Tenant;
using NetYamlForge.Services.Auth;
using System.Globalization;
using NetYamlForge.Data;
using NetYamlForge.Data.Schemas;
using NetYamlForge.Extensions;
using NetYamlForge.Middleware;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Connection;
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



if (args.Any(a => a.Equals("--migrate-data", StringComparison.OrdinalIgnoreCase) ||
                   a.Equals("--migrate-data-status", StringComparison.OrdinalIgnoreCase) ||
                   a.Equals("--migrate-data-rollback", StringComparison.OrdinalIgnoreCase)))
{
    var isStatus = args.Any(a => a.Equals("--migrate-data-status", StringComparison.OrdinalIgnoreCase));
    var isRollback = args.Any(a => a.Equals("--migrate-data-rollback", StringComparison.OrdinalIgnoreCase));
    var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
    var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
    var versionArg = args.FirstOrDefault(a => a.StartsWith("--version=", StringComparison.OrdinalIgnoreCase));
    var versionStr = versionArg?.Split('=', 2).ElementAtOrDefault(1);

    if (string.IsNullOrWhiteSpace(projectName))
    {
        Console.Error.WriteLine("--project=<name> is required.");
        Environment.Exit(1);
        return;
    }

    var projectDir = Path.Combine(Directory.GetCurrentDirectory(), "projects", projectName);
    if (!Directory.Exists(projectDir))
    {
        projectDir = Path.Combine(Directory.GetCurrentDirectory(), "NetYamlForge", "projects", projectName);
    }
    if (!Directory.Exists(projectDir))
    {
        Console.Error.WriteLine($"Project directory not found: {projectDir}");
        Environment.Exit(1);
        return;
    }

    var basePath = Directory.GetCurrentDirectory();
    if (Directory.Exists(Path.Combine(basePath, "NetYamlForge")))
    {
        basePath = Path.Combine(basePath, "NetYamlForge");
    }

    var configBuilder = new ConfigurationBuilder()
        .SetBasePath(basePath)
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args);
    var config = configBuilder.Build();
    var connectionString = config.GetConnectionString("DefaultConnection")
        ?? "Data Source=system.db";

    var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
    var runner = new ProjectDataMigrationRunner(loggerFactory.CreateLogger<ProjectDataMigrationRunner>());

    if (isRollback)
    {
        if (string.IsNullOrWhiteSpace(versionStr) || !long.TryParse(versionStr, out var rollbackVersion))
        {
            Console.Error.WriteLine("--version=<number> is required for rollback.");
            Environment.Exit(1);
            return;
        }
        await runner.RollbackAsync(projectName, projectDir, connectionString, rollbackVersion, CancellationToken.None);
        Console.WriteLine($"Rolled back migration version {rollbackVersion} for project '{projectName}'.");
    }
    else if (isStatus)
    {
        var records = await runner.GetStatusAsync(projectName, projectDir, connectionString, CancellationToken.None);
        if (records.Count == 0)
        {
            Console.WriteLine($"No migrations found for project '{projectName}'.");
        }
        else
        {
            Console.WriteLine($"Migration status for '{projectName}':");
            foreach (var r in records)
            {
                var status = r.Applied ? (r.RolledBackAt != null ? "ROLLED_BACK" : "APPLIED") : "PENDING";
                Console.WriteLine($"  {r.Version}  {status,-12}  {r.Name}");
            }
        }
    }
    else
    {
        var summary = await runner.ApplyPendingAsync(projectName, projectDir, connectionString, CancellationToken.None);
        Console.WriteLine($"Applied {summary.AppliedCount} migration(s), skipped {summary.SkippedCount} for project '{projectName}'.");
        foreach (var r in summary.Records)
        {
            var status = r.Applied ? (r.RolledBackAt != null ? "ROLLED_BACK" : "APPLIED") : "FAILED";
            Console.WriteLine($"  {r.Version}  {status,-12}  {r.Name}");
        }
    }

    Environment.Exit(0);
    return;
}

// R2-01 PR-4: スキーマ検証 CLI 入口（CI 用）。
// Web ホストを起動せず SchemaValidationRunner のみ実行し、GitHub Actions 注釈形式で出力、
// 違反あり=1 / なし=0 で終了する。既存の起動時検証（YamlConfigStartupValidator）とは独立に呼べる。
if (args.Any(a => a.Equals("--validate-schemas", StringComparison.OrdinalIgnoreCase)))
{
    var exitCode = NetYamlForge.Services.Validation.SchemaValidationCli.Run(args);
    Environment.Exit(exitCode);
    return;
}

var builder = WebApplication.CreateBuilder(args);
Directory.SetCurrentDirectory(builder.Environment.ContentRootPath);

// Paths configuration mapping for database and runtime directories
var dataDir = builder.Configuration["Paths:Data"];
if (!string.IsNullOrEmpty(dataDir))
{
    var fullDataDir = Path.Combine(Directory.GetCurrentDirectory(), dataDir);
    if (!Directory.Exists(fullDataDir))
    {
        Directory.CreateDirectory(fullDataDir);
    }

    var oldSystemDb = Path.Combine(Directory.GetCurrentDirectory(), "system.db");
    var newSystemDb = Path.Combine(fullDataDir, "system.db");
    if (File.Exists(oldSystemDb) && !File.Exists(newSystemDb))
    {
        File.Copy(oldSystemDb, newSystemDb);
    }

    var oldChinookDb = Path.Combine(Directory.GetCurrentDirectory(), "chinook.db");
    var newChinookDb = Path.Combine(fullDataDir, "chinook.db");
    if (File.Exists(oldChinookDb) && !File.Exists(newChinookDb))
    {
        File.Copy(oldChinookDb, newChinookDb);
    }

    builder.Configuration["SystemDbPath"] = newSystemDb;
    var defaultConn = builder.Configuration.GetConnectionString("DefaultConnection");
    if (defaultConn != null && defaultConn.Contains("chinook.db") && !defaultConn.Contains(dataDir))
    {
        builder.Configuration["ConnectionStrings:DefaultConnection"] = defaultConn.Replace("chinook.db", Path.Combine(dataDir, "chinook.db"));
    }
}

// Windows サービスとして実行する場合
if (useWindowsService)
{
    builder.Host.UseWindowsService();
}

builder.Host.UseSerilog((context, cfg) =>
{
    var logDir = context.Configuration["Paths:Log"] ?? "logs";
    var logFile = Path.Combine(logDir, "app-.log");
    cfg.ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(logFile, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
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

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
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
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, NetYamlForge.Services.Auth.ApiTokenAuthenticationHandler>("ApiToken", null);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    // FallbackPolicy はブラウザ向けページ用なので Cookie のみ。
    // API エンドポイントは各コントローラーの [Authorize(AuthenticationSchemes = "...,ApiToken")] で個別指定。
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
        .Build();
});

// ===== サービス登録（Extensions/ServiceCollectionExtensions.cs に委譲）=====
// グループ別の詳細は AddNetYamlForge の各メソッドを参照してください。
builder.Services.AddNetYamlForge(builder.Configuration);

foreach (var sd in builder.Services.Where(s => s.ServiceType.Name.Contains("FormForge") || (s.ImplementationType != null && s.ImplementationType.Name.Contains("FormForge"))))
{
    Console.WriteLine($"[FormForge-DI-Check] Service: {sd.ServiceType.FullName}, Lifetime: {sd.Lifetime}");
}


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "NetYamlForge API",
        Version = "v1"
    });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "Enter API Token here",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });
    c.DocumentFilter<NetYamlForge.Services.Auth.DynamicEntitySwaggerFilter>();
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (System.IO.File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
});

// Dapper: enable snake_case column mapping
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

// 显式确保 ITenantUserService 已注册（解决 AccountController 的 DI 错误）
builder.Services.AddScoped<ITenantUserService, TenantUserService>();
builder.Services.AddScoped<IJpcsUserSyncService, JpcsUserSyncService>();

// MCP（Model Context Protocol）サーバー：/mcp で動的エンティティ CRUD ツールを公開する。
builder.Services.AddScoped<NetYamlForge.Services.Mcp.EntityToolService>();
builder.Services.AddScoped<NetYamlForge.Services.Mcp.EntityMcpTools>();
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<NetYamlForge.Services.Mcp.EntityMcpTools>();

var app = builder.Build();

app.UseStaticFiles();
app.UsePathBase("/nyf");
app.Use(async (context, next) =>
{
    // 独立ドメイン (nyf.0101.click) 経由のアクセスは PathBase 不要
    var forwardedHost = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? "";
    var hasDirectHeader = context.Request.Headers.ContainsKey("X-Direct-Domain")
                         || string.Equals(context.Request.Headers["X-Direct-Domain"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);
    var isDirectDomain = hasDirectHeader
                         || forwardedHost.StartsWith("nyf.", StringComparison.OrdinalIgnoreCase)
                         || context.Request.Host.Host.Equals("nyf.0101.click", StringComparison.OrdinalIgnoreCase);
    if (isDirectDomain)
    {
        context.Request.PathBase = PathString.Empty;
        Serilog.Log.Information("[DirectDomain] Detected direct domain access. Host: {Host}, ForwardedHost: {ForwardedHost}, PathBase set to empty.", 
            context.Request.Host.Value, forwardedHost);
        await next();
        return;
    }

    // Caddy の handle_path で /nyf が剥がされた場合に PathBase を補正する。
    if (!context.Request.PathBase.HasValue)
    {
        var forwardedPrefix = context.Request.Headers["X-Forwarded-Prefix"].FirstOrDefault();
        context.Request.PathBase = new PathString(
            !string.IsNullOrWhiteSpace(forwardedPrefix) ? forwardedPrefix : "/nyf");
        Serilog.Log.Information("[PathBaseFix] Corrected PathBase to: {PathBase}", context.Request.PathBase.Value);
    }

    await next();
});

// プロジェクト定義の非同期初期化
var projectManager = app.Services.GetRequiredService<ProjectManager>();
await projectManager.InitializeAsync(app.Environment);

// PDFフォントの非同期事前ロード
await PdfFontLoader.LoadFontsAsync();

await DbInitializer.InitializeAsync(app.Services, app.Configuration);

// projects/ を実際にスキャンした結果を system.db の projects / app_user_project_role に反映する。
// これを怠ると、新規に追加したサブプロジェクト（例: --ai-scaffold で生成したもの）が
// 物理的には正常ロードされていても「マイホーム」の一覧に永久に出てこない（admin にロールが付与されないため）。
{
    var syncLogger = app.Services.GetRequiredService<ILogger<Program>>();
    await SystemDatabaseInitializer.SyncProjectsAsync(projectManager.GetAll(), syncLogger);
}

// データマイグレーション適用
{
    var migrationLogger = app.Services.GetRequiredService<ILogger<ProjectDataMigrationRunner>>();
    var migrationRunner = new ProjectDataMigrationRunner(migrationLogger);
    foreach (var proj in projectManager.GetAll())
    {
        try
        {
            var projDir = proj.ProjectDir;
            if (!Directory.Exists(projDir)) continue;
            var dbConfig = app.Configuration.GetSection($"Projects:{proj.Name}");
            var connStr = dbConfig["Connection"] ?? app.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=system.db";
            await migrationRunner.ApplyPendingAsync(proj.Name, projDir, connStr, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "データマイグレーション適用中にエラー: {Project}", proj.Name);
        }
    }
}

var supportedCultures = new[] { "en-US", "zh-CN", "ja-JP", "ko-KR" }
    .Select(x => new CultureInfo(x))
    .ToList();

var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en-US"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
};
localizationOptions.RequestCultureProviders.Insert(2, new NetYamlForge.Localization.UserPreferredLanguageProvider());


if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseMiddleware<NetYamlForge.Services.ApiExceptionHandlingMiddleware>();
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
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("v1/swagger.json", "NetYamlForge API v1");
});
app.UseRouting();
app.UseMiddleware<ProjectMiddleware>(); // UseRouting 後・UseAuthentication 前に配置
app.UseAuthentication();
app.UseRequestLocalization(localizationOptions);
app.UseMiddleware<ProjectScopeMiddleware>(); // プロジェクト別アクセス制御（認証後に配置）
app.UseMiddleware<NetYamlForge.Services.Tenant.TenantResolverMiddleware>();
app.UseMiddleware<NetYamlForge.Services.Api.DynamicRateLimitingMiddleware>();
app.Use(async (context, next) =>
{
    var endpoint = context.GetEndpoint();
    if (context.Request.Path.Value.Contains("photo-file") || context.Request.Path.Value.Contains("photo/"))
    {
        if (endpoint != null)
        {
            var allowAnonymous = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>();
            var authorizeData = endpoint.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>();
            Console.WriteLine($"[DEBUG-ENDPOINT] Path: {context.Request.Path}, Endpoint: {endpoint.DisplayName}, AllowAnonymous: {allowAnonymous != null}, AuthDataCount: {authorizeData?.Count}");
        }
        else
        {
            Console.WriteLine($"[DEBUG-ENDPOINT] Path: {context.Request.Path}, Endpoint is NULL");
        }
    }
    await next();
});
app.UseConnectionPreloading(); // 接続プリロード中間件
app.UseAuthorization();

app.MapGet("/trigger-test", async (string? jobId, string? project, NetYamlForge.Services.BatchJob.IBatchJobScheduler scheduler) =>
{
    var targetJob = jobId ?? "japan_it_news_briefing";
    var targetProject = project ?? "blog";
    await scheduler.TriggerJobNowAsync(targetProject, targetJob);
    return $"Triggered: {targetProject}/{targetJob}";
}).AllowAnonymous();

// MCP サーバーエンドポイント：/mcp（/api/{project}/{entity} と同じ認証スキームを要求）
app.MapMcp("/mcp")
    .RequireAuthorization(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme, "ApiToken")
        .Build());

// ユーザー個人ホーム：/userhome（プロジェクトルートより先に評価）
app.MapControllerRoute(
    name: "userhome",
    pattern: "userhome/{action=Index}",
    defaults: new { controller = "UserHome", project = "userhome" });

// 博客公开首页快捷路由：/blog -> /blog/Page/BlogHome
app.MapControllerRoute(
    name: "blog-public",
    pattern: "blog",
    defaults: new { controller = "Page", action = "Index", project = "blog", pageName = "BlogHome" });

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

/// <summary>
/// WebApplicationFactory（統合テスト）から参照できるようにするための partial 宣言。
/// トップレベルステートメントで生成される Program クラスを public にします。
/// </summary>
public partial class Program { }
