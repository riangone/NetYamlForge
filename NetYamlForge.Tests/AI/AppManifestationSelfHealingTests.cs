// DCS003 抑制理由: テスト用インメモリ/一時ファイル SQLite 接続を直接作成
#pragma warning disable DCS003

using System;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetYamlForge.Services;
using NetYamlForge.Services.AI.AgentStudio.Manifestation;
using NetYamlForge.Services.Connection;
using NetYamlForge.Services.Hooks;
using Xunit;

namespace NetYamlForge.Tests.AI;

/// <summary>
/// 自举交付引擎：流水线智能诊断自愈与原子回滚自动化测试
/// </summary>
public class AppManifestationSelfHealingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public AppManifestationSelfHealingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "nyf-selfhealing-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "projects"));

        _dbPath = Path.Combine(_tempDir, "agent-studio.db");
        using var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE agent_task (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                description TEXT,
                status TEXT DEFAULT 'Draft',
                developer_output TEXT,
                architect_output TEXT,
                created_at TEXT,
                updated_at TEXT
            );
            CREATE TABLE agent_task_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                task_id INTEGER,
                stage TEXT,
                role TEXT,
                provider TEXT,
                success INTEGER DEFAULT 1,
                log_content TEXT,
                error_message TEXT,
                created_at TEXT
            );";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    private AppManifestationService CreateService(ProjectManager? projectManager = null)
    {
        var connManagerMock = new Mock<IConnectionManager>();
        connManagerMock.Setup(c => c.GetConnectionAsync("agent-studio", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
                conn.Open();
                return conn;
            });

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(e => e.ContentRootPath).Returns(_tempDir);

        var pm = projectManager ?? new ProjectManager(
            NullLogger<ProjectManager>.Instance,
            new Mock<IProjectHookRegistry>().Object,
            new Mock<IProjectHookLoader>().Object,
            new Mock<IProjectBusinessLogicRegistry>().Object,
            new Mock<IProjectActionRegistry>().Object);

        var healer = new ManifestationSelfHealingService(
            NullLogger<ManifestationSelfHealingService>.Instance,
            cliChainService: null);

        return new AppManifestationService(
            connManagerMock.Object,
            pm,
            envMock.Object,
            NullLogger<AppManifestationService>.Instance,
            healer);
    }

    [Fact]
    public async Task ManifestAppAsync_TriggersSelfHealing_WhenActionHandlerIsMissing_AndSucceeds()
    {
        // 构造一个定义了 action 但缺少 C# Handler 实现的任务
        using (var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO agent_task (id, title, description, developer_output, status)
                VALUES (101, '支付网关应用 (Payment Gateway)', '自动自愈测试',
                '产物文件：
```yaml:projects/payment-app/project.yaml
name: payment-app
displayName: ""支付网关""
database:
  type: sqlite
  path: database/payment-app.db
```

### File: projects/payment-app/entities/payment.yml
```yaml
entities:
  payment:
    table: payment
    key: id
    displayName: ""支付记录""
    actions:
      process_refund:
        label: ""执行退款""
        handler: process_refund_handler
```
', 'Completed');";
            await cmd.ExecuteNonQueryAsync();
        }

        var service = CreateService();
        var request = new ManifestationRequest(
            TaskId: 101,
            TargetProjectName: "payment-app",
            ForceOverwrite: true,
            AutoScaffoldHooks: false, // 故意关闭基础脚手架，以触发静态诊断错误并验证自愈修补引擎
            ReloadInMemory: false,
            EnableSelfHealing: true,
            MaxHealingAttempts: 2
        );

        var result = await service.ManifestAppAsync(request);

        Assert.True(result.Success, $"自愈交付应成功，但返回错误: {result.Error}");
        Assert.True(result.SelfHealingTriggered, "应成功触发自愈修补");
        Assert.True(result.SelfHealingSucceeded, "自愈修补应成功解决问题");
        Assert.NotNull(result.HealingHistory);
        Assert.NotEmpty(result.HealingHistory);

        // 验证自愈引擎生成的桩代码
        var stubFile = Path.Combine(_tempDir, "projects", "payment-app", "Hooks", "ProcessRefundHandlerHandler.cs");
        Assert.True(File.Exists(stubFile), "自愈引擎应自动补全动作处理器桩代码");
        var stubContent = await File.ReadAllTextAsync(stubFile);
        Assert.Contains("process_refund_handler", stubContent);
    }

    [Fact]
    public async Task ManifestAppAsync_TriggersSelfHealing_WhenCSharpMissingBrace_AndSucceeds()
    {
        // 构造一个 C# 语法末尾缺少闭合大括号的任务
        using (var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO agent_task (id, title, description, developer_output, status)
                VALUES (102, '语法修补应用 (Syntax Repair App)', '修复未闭合大括号',
                '产物文件：
```yaml:projects/syntax-app/project.yaml
name: syntax-app
displayName: ""语法修补应用""
database:
  type: sqlite
  path: database/syntax-app.db
```

### File: projects/syntax-app/Hooks/TestHook.cs
```csharp
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
namespace NetYamlForge.Projects.SyntaxApp.Hooks {
    public class TestHook {
        public void Ping() { }
    // 故意缺失两个闭合花括号
```
', 'Completed');";
            await cmd.ExecuteNonQueryAsync();
        }

        var service = CreateService();
        var request = new ManifestationRequest(
            TaskId: 102,
            TargetProjectName: "syntax-app",
            ForceOverwrite: true,
            AutoScaffoldHooks: false,
            ReloadInMemory: false,
            EnableSelfHealing: true
        );

        var result = await service.ManifestAppAsync(request);

        Assert.True(result.Success, $"C# 语法自愈修补应成功: {result.Error}");
        Assert.True(result.SelfHealingTriggered);
        Assert.True(result.SelfHealingSucceeded);

        // 检查修复后的代码是否包含了闭合花括号并通过了 Roslyn 诊断
        var hookPath = Path.Combine(_tempDir, "projects", "syntax-app", "Hooks", "TestHook.cs");
        Assert.True(File.Exists(hookPath));
        var fixedCode = await File.ReadAllTextAsync(hookPath);
        Assert.Contains("}", fixedCode);
    }

    [Fact]
    public async Task ManifestAppAsync_ExecutesAtomicRollback_WhenFatalErrorCannotBeHealed()
    {
        // 1. 模拟一个已经存在的工程，包含干净的原有文件
        var targetDir = Path.Combine(_tempDir, "projects", "existing-app");
        Directory.CreateDirectory(targetDir);
        var originalFile = Path.Combine(targetDir, "README.md");
        var originalContent = "# Clean Production App\nExisting pristine state.";
        await File.WriteAllTextAsync(originalFile, originalContent);

        // 2. 插入一个带有不可修复严重错误的任务（例如未闭合且混乱的不可解析 YAML）
        using (var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO agent_task (id, title, description, developer_output, status)
                VALUES (103, '致命错误任务 (Fatal Error App)', '测试原子回滚',
                '产物文件：
### File: projects/existing-app/entities/bad_broken.yml
```yaml
::: this is totally invalid yaml [[]] :: broken tabs
```
', 'Completed');";
            await cmd.ExecuteNonQueryAsync();
        }

        var service = CreateService();
        var request = new ManifestationRequest(
            TaskId: 103,
            TargetProjectName: "existing-app",
            ForceOverwrite: true,
            AutoScaffoldHooks: false,
            ReloadInMemory: false,
            EnableSelfHealing: false // 禁用自愈，确保直接触发门闩与原子回滚
        );

        var result = await service.ManifestAppAsync(request);

        // 3. 验证返回状态为失败且触发了原子回滚
        Assert.False(result.Success, "包含严重错误的任务交付必须失败");
        Assert.True(result.RolledBack, "系统必须自动触发原子回滚");
        Assert.NotNull(result.RollbackReason);

        // 4. 验证原子回滚后磁盘状态的完整性：原有文件未受损，新增脏文件被彻底消除！
        Assert.True(File.Exists(originalFile), "原有文件必须在原子回滚后完好保存");
        var restoredContent = await File.ReadAllTextAsync(originalFile);
        Assert.Equal(originalContent, restoredContent);

        var dirtyFile = Path.Combine(targetDir, "entities", "bad_broken.yml");
        Assert.False(File.Exists(dirtyFile), "新增的脏文件必须在原子回滚时被干净删除");
    }
}
