// AI チャットサービス 独立 Web ホスト
// 主应用通过 HTTP API + SignalR と通信

using Serilog;
using NetYamlForge.AI;
using NetYamlForge.AI.Hubs;
using NetYamlForge.AI.Controllers;
using NetYamlForge.AI.Controllers.Api;

var builder = WebApplication.CreateBuilder(args);

// ユーザーが管理画面から変更した AI 設定を読み込む
builder.Configuration.AddJsonFile("ai-user-settings.json", optional: true, reloadOnChange: true);

// Serilog 設定
builder.Host.UseSerilog((context, cfg) =>
{
    cfg.ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/ai-service-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
});

// ===== AI サービス登録 =====
builder.Services.AddNetYamlForgeAI(builder.Configuration);

// ===== MVC コントローラー =====
builder.Services.AddControllers()
    .AddApplicationPart(typeof(NetYamlForge.AI.Controllers.AIController).Assembly);

// ===== CORS (主アプリからのアクセス許可) =====
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// ===== ミドルウェア =====
app.UseSerilogRequestLogging();
app.UseCors();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// ===== SignalR Hubs =====
app.MapHub<AIChatHub>("/aiChatHub");
app.MapHub<AIProgressHub>("/aiProgressHub");
app.MapHub<AIDebateHub>("/aiDebateHub");
app.MapHub<NaturalLanguageQueryHub>("/nlQueryHub");

// ===== MVC コントローラー =====
app.MapControllers();

// ===== タスクキュー開始 =====
var taskQueue = app.Services.GetService<NetYamlForge.AI.Services.TaskQueueService>();
taskQueue?.StartProcessing();

app.Run();
