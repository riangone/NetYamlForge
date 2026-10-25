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

public class AppManifestationServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public AppManifestationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "nyf-manifest-test-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task ManifestAppAsync_ExtractsAndMaterializesFilesCorrectly()
    {
        // 插入测试任务，附带 Markdown 代码块
        using (var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO agent_task (id, title, description, developer_output, status)
                VALUES (10, '创建书签应用 (Bookmark App)', '收藏夹管理', 
                '以下是交付的文件：
```yaml:projects/bookmark-app/project.yaml
name: bookmark-app
displayName: ""书签管理系统""
database:
  type: sqlite
  path: database/bookmark-app.db
```

### File: projects/bookmark-app/database/init_schema.sql
```sql
CREATE TABLE IF NOT EXISTS bookmark (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    url TEXT NOT NULL
);
```

### File: projects/bookmark-app/entities/bookmark.yml
```yaml
entities:
  bookmark:
    table: bookmark
    key: id
    displayName: ""书签""
    actions:
      verify_link:
        label: ""校验链接""
        handler: check_link_handler
```
', 'Completed');";
            await cmd.ExecuteNonQueryAsync();
        }

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

        var hookRegistryMock = new Mock<IProjectHookRegistry>();
        var hookLoaderMock = new Mock<IProjectHookLoader>();
        var bizRegistryMock = new Mock<IProjectBusinessLogicRegistry>();
        var actionRegistryMock = new Mock<IProjectActionRegistry>();

        var projectManager = new ProjectManager(
            NullLogger<ProjectManager>.Instance,
            hookRegistryMock.Object,
            hookLoaderMock.Object,
            bizRegistryMock.Object,
            actionRegistryMock.Object);

        var service = new AppManifestationService(
            connManagerMock.Object,
            projectManager,
            envMock.Object,
            NullLogger<AppManifestationService>.Instance);

        var request = new ManifestationRequest(
            TaskId: 10,
            TargetProjectName: "bookmark-app",
            ForceOverwrite: true,
            AutoScaffoldHooks: true,
            ReloadInMemory: false
        );

        var result = await service.ManifestAppAsync(request);

        Assert.True(result.Success, $"Manifest failed with error: {result.Error}");
        Assert.Equal("bookmark-app", result.ProjectName);
        Assert.True(result.DatabaseInitialized);
        Assert.True(result.HooksScaffolded);

        var targetProjectDir = Path.Combine(_tempDir, "projects", "bookmark-app");
        Assert.True(File.Exists(Path.Combine(targetProjectDir, "project.yaml")));
        Assert.True(File.Exists(Path.Combine(targetProjectDir, "database", "init_schema.sql")));
        Assert.True(File.Exists(Path.Combine(targetProjectDir, "database", "bookmark-app.db")));
        Assert.True(File.Exists(Path.Combine(targetProjectDir, "entities", "bookmark.yml")));

        // 验证缺失的 action handler 是否自动补全了桩代码
        var stubFile = Path.Combine(targetProjectDir, "Hooks", "CheckLinkHandlerHandler.cs");
        Assert.True(File.Exists(stubFile));
        var stubContent = await File.ReadAllTextAsync(stubFile);
        Assert.Contains("Name => \"check_link_handler\"", stubContent);
    }

    [Fact]
    public async Task ManifestAppAsync_ZeroTrustSandbox_BlocksDisallowedFilesAndUnsafeHooks()
    {
        // 构造包含非法脚本扩展名、未授权根文件以及恶意 C# Hook 的交付任务
        using (var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO agent_task (id, title, description, developer_output, status)
                VALUES (20, '恶意应用 (Evil App)', '测试逃逸拦截', 
                '以下是恶意产物：
```yaml:projects/evil-app/project.yaml
name: evil-app
displayName: ""恶意应用""
database:
  type: sqlite
  path: database/evil-app.db
```

### File: projects/evil-app/evil.sh
```bash
#!/bin/bash
rm -rf /
```

### File: projects/evil-app/.hidden_payload
```txt
secret payload
```

### File: projects/evil-app/Hooks/EvilHook.cs
```csharp
using System.Diagnostics;
public class EvilHook {
    public void Run() {
        Process.Start(""calc.exe"");
    }
}
```

### File: projects/evil-app/entities/safe.yml
```yaml
entities:
  safe:
    table: safe
    key: id
```
', 'Completed');";
            await cmd.ExecuteNonQueryAsync();
        }

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

        var projectManager = new ProjectManager(
            NullLogger<ProjectManager>.Instance,
            new Mock<IProjectHookRegistry>().Object,
            new Mock<IProjectHookLoader>().Object,
            new Mock<IProjectBusinessLogicRegistry>().Object,
            new Mock<IProjectActionRegistry>().Object);

        var service = new AppManifestationService(
            connManagerMock.Object,
            projectManager,
            envMock.Object,
            NullLogger<AppManifestationService>.Instance);

        var request = new ManifestationRequest(
            TaskId: 20,
            TargetProjectName: "evil-app",
            ForceOverwrite: true,
            AutoScaffoldHooks: false,
            ReloadInMemory: false
        );

        var result = await service.ManifestAppAsync(request);

        Assert.True(result.Success);
        var evilDir = Path.Combine(_tempDir, "projects", "evil-app");

        // 验证白名单文件成功落盘
        Assert.True(File.Exists(Path.Combine(evilDir, "project.yaml")));
        Assert.True(File.Exists(Path.Combine(evilDir, "entities", "safe.yml")));

        // 验证零信任安全网闸拦截：非法脚本、隐藏文件、危险 C# 钩子均被拒绝
        Assert.False(File.Exists(Path.Combine(evilDir, "evil.sh")), "evil.sh 应被文件类型白名单拦截");
        Assert.False(File.Exists(Path.Combine(evilDir, ".hidden_payload")), "隐藏文件应被拦截");
        Assert.False(File.Exists(Path.Combine(evilDir, "Hooks", "EvilHook.cs")), "恶意 C# Hook 应被静态安全网闸拦截");
    }

    [Fact]
    public async Task ManifestAppAsync_ZeroTrustSandbox_BlocksDangerousSqlSchemaDirectives()
    {
        // 构造包含 ATTACH DATABASE 逃逸攻击的 init_schema.sql
        using (var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO agent_task (id, title, description, developer_output, status)
                VALUES (30, 'SQL逃逸应用 (Sql Escape App)', '测试跨数据库注入', 
                '产物：
```yaml:projects/sql-escape/project.yaml
name: sql-escape
displayName: ""SQL逃逸""
database:
  type: sqlite
  path: database/sql-escape.db
```

### File: projects/sql-escape/database/init_schema.sql
```sql
CREATE TABLE safe_table (id INTEGER PRIMARY KEY);
ATTACH DATABASE ''/tmp/stolen.db'' AS stolen;
```
', 'Completed');";
            await cmd.ExecuteNonQueryAsync();
        }

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

        var projectManager = new ProjectManager(
            NullLogger<ProjectManager>.Instance,
            new Mock<IProjectHookRegistry>().Object,
            new Mock<IProjectHookLoader>().Object,
            new Mock<IProjectBusinessLogicRegistry>().Object,
            new Mock<IProjectActionRegistry>().Object);

        var service = new AppManifestationService(
            connManagerMock.Object,
            projectManager,
            envMock.Object,
            NullLogger<AppManifestationService>.Instance);

        var request = new ManifestationRequest(
            TaskId: 30,
            TargetProjectName: "sql-escape",
            ForceOverwrite: true,
            AutoScaffoldHooks: false,
            ReloadInMemory: false
        );

        var result = await service.ManifestAppAsync(request);

        // 验证物理交付引擎捕获安全异常，且数据库未被非法初始化
        Assert.False(result.Success);
        Assert.Contains("Unsafe SQL directive detected", result.Error);
    }

    [Fact]
    public void ProjectManager_DatabasePathTraversal_IsBlockedByPathSafetyGuard()
    {
        var projectDir = Path.Combine(_tempDir, "projects", "tenant-alpha");
        Directory.CreateDirectory(projectDir);

        // 尝试逃逸到项目目录之外
        var maliciousPath = "../../system.db";

        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            PathSafetyGuard.NormalizeAndValidatePath(
                maliciousPath,
                projectDir,
                "Project 'tenant-alpha' database.path");
        });
    }
}
