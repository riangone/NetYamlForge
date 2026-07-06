using System;
using System.IO;
using System.IO.Compression;
using System.Data;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Dapper;
using NetYamlForge.Services;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Services.BatchJob;
using Xunit;

namespace NetYamlForge.Tests;

public class AiDocProcessorTests
{
    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TestApp";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        public Microsoft.Extensions.Hosting.IHostEnvironment Services { get; set; } = null!;
        public string EnvironmentName { get; set; } = "Test";
    }

    [Fact]
    public async Task TestBatchUploadZipHandler_LoadsAndExecutesSuccessfully()
    {
        var tempWebRoot = Path.Combine(Path.GetTempPath(), "nyf_test_" + Guid.NewGuid().ToString("N"));
        var uploadsDir = Path.Combine(tempWebRoot, "uploads", "ai-doc-processor");
        Directory.CreateDirectory(uploadsDir);

        var tempDbPath = Path.Combine(tempWebRoot, "test_db.sqlite");

        // 1. セットアップデータベース (SQLite Temp File)
        var connType = typeof(SqliteConnection);
        using var db = (IDbConnection)Activator.CreateInstance(connType, $"Data Source={tempDbPath}")!;
        db.Open();
        
        // 必要なテーブルを作成
        db.Execute(@"
            CREATE TABLE DocumentTask (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FileName TEXT,
                FilePath TEXT,
                Status TEXT,
                DocumentType TEXT,
                JsonPath TEXT,
                ExtractedTable TEXT,
                ExtractedId INTEGER,
                CreatedAt DATETIME
            );
        ");

        // 2. モックの設定
        var mockAi = new Mock<IAntigravityCliService>();
        mockAi.Setup(a => a.PromptAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<System.Threading.CancellationToken>()))
              .ReturnsAsync(@"{
                  ""document_type"": ""Invoice"",
                  ""data"": {
                      ""invoice_no"": ""INV-TEST-001"",
                      ""date"": ""2026-06-06"",
                      ""total"": ""100.00"",
                      ""vendor"": ""Test Vendor""
                  }
              }");

        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.WebRootPath).Returns(tempWebRoot);

        // 3. DI コンテナの構築
        var logs = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(mockAi.Object);
        services.AddSingleton(mockEnv.Object);
        services.AddSingleton<NetYamlForge.Services.Project.Loading.HookMetadataReferenceCache>();
        services.AddSingleton<NetYamlForge.Services.Project.Loading.CollectibleAssemblyManager>();
        services.AddSingleton<NetYamlForge.Services.Project.Loading.ProjectLoadLockRegistry>();
        services.AddSingleton<NetYamlForge.Services.Project.Loading.HookAssemblyCompiler>();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddProvider(new TestLoggerProvider(logs));
        });
        
        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        // 4. ProjectHookLoader のインスタンス化と Hooks のロード
        var mockHookRegistry = new Mock<IProjectHookRegistry>();
        var mockBizRegistry = new Mock<IProjectBusinessLogicRegistry>();
        var actionRegistry = new ProjectActionRegistry(NullLogger<ProjectActionRegistry>.Instance);
        var mockPageDispatcher = new Mock<NetYamlForge.Services.Page.IPageActionDispatcher>();
        
        var loader = new ProjectHookLoader(
            serviceProvider.GetRequiredService<ILogger<ProjectHookLoader>>(),
            scopeFactory,
            mockHookRegistry.Object,
            mockBizRegistry.Object,
            actionRegistry,
            new BatchStepHandlerRegistry(Enumerable.Empty<BatchStepHandlerRegistration>()),
            mockPageDispatcher.Object,
            serviceProvider.GetRequiredService<NetYamlForge.Services.Project.Loading.HookAssemblyCompiler>(),
            serviceProvider.GetRequiredService<NetYamlForge.Services.Project.Loading.CollectibleAssemblyManager>(),
            serviceProvider.GetRequiredService<NetYamlForge.Services.Project.Loading.ProjectLoadLockRegistry>());

        // テストプロジェクトのベースディレクトリからの相対パス
        var projectDir = "/home/ubuntu/ws/NetYamlForge/NetYamlForge/projects/ai-doc-processor";
        
        // Hooks / ActionHandlers のロードを実行
        await loader.LoadProjectActionHandlersAsync("ai-doc-processor", projectDir, actionRegistry);

        // アクションハンドラーが正常に登録されたことを検証
        var handler = actionRegistry.Find("ai-doc-processor", "batch_upload_zip_handler");
        if (handler == null)
        {
            throw new Exception("加载 ActionHandler 失败，注册日志如下：\n" + string.Join("\n", logs));
        }

        // 5. テスト用ドキュメントファイルを用意
        var testDocPath = Path.Combine(tempWebRoot, "test_invoice.pdf");
        await File.WriteAllTextAsync(testDocPath, "Mock PDF Content");

        var ctx = new CustomActionContext
        {
            Project = "ai-doc-processor",
            Entity = "document_task",
            Action = "batch_upload",
            Files = new Dictionary<string, string>(),
            MultipleFiles = new Dictionary<string, List<string>>
            {
                ["DocFiles"] = new List<string> { testDocPath }
            }
        };

        // 6. ハンドラー実行
        var result = await handler.ExecuteAsync(ctx, db, null);
        Assert.True(result.Ok);

        // 7. 非同期バックグラウンド処理の完了を監視
        bool completed = false;
        int retries = 30; // 最大 6 秒待つ
        while (retries-- > 0)
        {
            var task = db.QueryFirstOrDefault("SELECT Status FROM DocumentTask WHERE Id = 1");
            if (task != null)
            {
                string status = task.Status;
                if (status == "completed" || status == "failed")
                {
                    completed = true;
                    if (status == "failed")
                    {
                        throw new Exception("异步处理任务失败。收集到的日志如下：\n" + string.Join("\n", logs));
                    }
                    Assert.Equal("completed", status);
                    break;
                }
            }
            await Task.Delay(200);
        }
        if (!completed)
        {
            throw new Exception("AI 抽出処理がタイムアウトしました。收集到的日志如下：\n" + string.Join("\n", logs));
        }

        // 8. データベースに動的な数据表が作成され、データが保存されたことを検証
        var dynamicTableExists = db.QueryFirstOrDefault<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name = 'dynamic_invoice'");
        Assert.NotNull(dynamicTableExists);

        var dynamicData = db.QueryFirstOrDefault("SELECT * FROM dynamic_invoice WHERE DocumentTaskId = 1");
        Assert.NotNull(dynamicData);
        
        var invoiceNo = db.QueryFirstOrDefault<string>("SELECT invoice_no FROM dynamic_invoice WHERE DocumentTaskId = 1");
        Assert.Equal("INV-TEST-001", invoiceNo);

        // クリーンアップ
        try
        {
            Directory.Delete(tempWebRoot, true);
        }
        catch { }
    }

    private class TestLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _logs;
        public TestLoggerProvider(List<string> logs) => _logs = logs;

        public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, _logs);
        public void Dispose() { }
    }

    private class TestLogger : ILogger
    {
        private readonly string _category;
        private readonly List<string> _logs;

        public TestLogger(string category, List<string> logs)
        {
            _category = category;
            _logs = logs;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            if (exception != null)
            {
                msg = $"{msg} | Exception: {exception.Message}\n{exception.StackTrace}";
            }
            lock (_logs)
            {
                _logs.Add($"[{logLevel}] [{_category}] {msg}");
            }
        }
    }
}
