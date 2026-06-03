// ファイル概要: CRUD 主経路・Hook 実行順序の統合テストです。
// EntityCrudExecutionService を通じた Create/Update/Delete の主要経路を検証します。
// Hook チェーン（複数フック）の実行順・AbortAll 伝播・AfterHook 実行も確認します。

using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Dapper;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.Hooks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NetYamlForge.Tests;

/// <summary>
/// CRUD 主経路と Hook 実行順序の統合テスト。
/// インメモリ SQLite + EntityCrudExecutionService を直接テストします。
/// </summary>
public class CrudMainPathTests
{
    // ── Hook 実行順序テスト ─────────────────────────────────────────────────

    [Fact]
    public async Task HookChain_MultipleBeforeHooks_ExecutedInOrder()
    {
        // 複数の BeforeHook が宣言順に実行されることを確認
        var executionOrder = new List<string>();

        var registry = new OrderedHookRegistry(new Dictionary<string, IEntityHook>
        {
            ["hook_a"] = new DelegateHook("hook_a", _ => { executionOrder.Add("A"); return HookResult.Continue(); }),
            ["hook_b"] = new DelegateHook("hook_b", _ => { executionOrder.Add("B"); return HookResult.Continue(); }),
            ["hook_c"] = new DelegateHook("hook_c", _ => { executionOrder.Add("C"); return HookResult.Continue(); })
        });

        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var sut = CreateSut(db, registry);

        var ctx = new EntityHookContext { Entity = "item", Operation = CrudOperation.Create };
        await sut.RunBeforeHookAsync(new List<string> { "hook_a", "hook_b", "hook_c" }, ctx);

        // A → B → C の順で実行される
        Assert.Equal(new[] { "A", "B", "C" }, executionOrder);
    }

    [Fact]
    public async Task HookChain_AbortStopsRemainingHooks()
    {
        // 先頭フックが Abort を返した場合、後続フックは実行されない
        var bCalled = false;

        var registry = new OrderedHookRegistry(new Dictionary<string, IEntityHook>
        {
            ["hook_a"] = new DelegateHook("hook_a", _ => HookResult.Abort("最初で中断")),
            ["hook_b"] = new DelegateHook("hook_b", _ => { bCalled = true; return HookResult.Continue(); })
        });

        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var sut = CreateSut(db, registry);

        var ctx = new EntityHookContext { Entity = "item", Operation = CrudOperation.Create };
        var result = await sut.RunBeforeHookAsync(new List<string> { "hook_a", "hook_b" }, ctx);

        Assert.True(result.Cancel);
        Assert.Equal("最初で中断", result.CancelMessage);
        Assert.False(bCalled);   // hook_b は実行されない
    }

    [Fact]
    public async Task HookChain_AfterHooks_ExecutedAfterTransaction()
    {
        // AfterHook は DB 書き込み成功後に実行されることを確認
        var afterCalled = false;

        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        await db.ExecuteAsync("CREATE TABLE item (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL)");

        var afterRegistry = new OrderedHookRegistry(new Dictionary<string, IEntityHook>
        {
            ["notify"] = new DelegateHook("notify", after: async _ =>
            {
                // AfterHook 実行時点でレコードが DB に存在する
                var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM item");
                Assert.Equal(1, count);
                afterCalled = true;
            })
        });

        var sut = CreateSut(db, afterRegistry);
        var ctx = new EntityHookContext { Entity = "item", Operation = CrudOperation.Create };

        await sut.ExecuteCrudTransactionAsync(async tx =>
        {
            await db.ExecuteAsync("INSERT INTO item (Name) VALUES ('test')", transaction: tx);
        });
        await sut.RunAfterHookAsync(new List<string> { "notify" }, ctx, null!);

        Assert.True(afterCalled);
    }

    [Fact]
    public async Task HookChain_MixedHooks_OnlyContinueHooksPassThrough()
    {
        // Continue / Abort 混在: 最初の Continue、2番目が Abort の場合
        var aCalled = false;

        var registry = new OrderedHookRegistry(new Dictionary<string, IEntityHook>
        {
            ["validate_1"] = new DelegateHook("validate_1", _ => { aCalled = true; return HookResult.Continue(); }),
            ["validate_2"] = new DelegateHook("validate_2", _ => HookResult.Abort("バリデーション失敗"))
        });

        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var sut = CreateSut(db, registry);

        var ctx = new EntityHookContext { Entity = "order", Operation = CrudOperation.Update };
        var result = await sut.RunBeforeHookAsync(new List<string> { "validate_1", "validate_2" }, ctx);

        Assert.True(aCalled);           // validate_1 は実行される
        Assert.True(result.Cancel);     // validate_2 の Abort が伝播
        Assert.Equal("バリデーション失敗", result.CancelMessage);
    }

    // ── トランザクション主経路テスト ─────────────────────────────────────────

    [Fact]
    public async Task Transaction_CommitsOnSuccess()
    {
        // 正常系: トランザクションがコミットされてデータが永続化される
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        await db.ExecuteAsync("CREATE TABLE item (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL)");

        var sut = CreateSut(db, new OrderedHookRegistry());

        await sut.ExecuteCrudTransactionAsync(async tx =>
        {
            await db.ExecuteAsync("INSERT INTO item (Name) VALUES ('committed')", transaction: tx);
        });

        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM item WHERE Name='committed'");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Transaction_RollsBackOnException()
    {
        // 例外時: トランザクションがロールバックされてデータが残らない
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        await db.ExecuteAsync("CREATE TABLE item (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL)");

        var sut = CreateSut(db, new OrderedHookRegistry());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteCrudTransactionAsync(async tx =>
            {
                await db.ExecuteAsync("INSERT INTO item (Name) VALUES ('to_rollback')", transaction: tx);
                throw new InvalidOperationException("強制ロールバック");
            }));

        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM item");
        Assert.Equal(0, count);
    }

    // ── プロジェクト Hook 優先度テスト ───────────────────────────────────────

    [Fact]
    public async Task ProjectHook_TakesPriorityOverFrameworkHook()
    {
        // プロジェクト固有フックがフレームワーク標準フックより優先実行される
        var frameworkCalled = false;
        var projectCalled = false;

        var frameworkRegistry = new OrderedHookRegistry(new Dictionary<string, IEntityHook>
        {
            ["normalize"] = new DelegateHook("normalize", _ => { frameworkCalled = true; return HookResult.Continue(); })
        });
        var projectRegistry = new TestProjectHookRegistry();
        projectRegistry.Register("myproject", new DelegateHook("normalize", _ => { projectCalled = true; return HookResult.Continue(); }));

        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var sut = CreateSutWithProjectRegistry(db, frameworkRegistry, projectRegistry, "myproject");

        var ctx = new EntityHookContext { Entity = "customer", Operation = CrudOperation.Create };
        await sut.RunBeforeHookAsync(new List<string> { "normalize" }, ctx);

        Assert.True(projectCalled);
        Assert.False(frameworkCalled);  // フレームワークフックは上書きされる
    }

    [Fact]
    public async Task UnknownHook_IsSkipped_WithoutError()
    {
        // 未登録のフック名が指定されても例外にならずスキップされる
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var sut = CreateSut(db, new OrderedHookRegistry());

        var ctx = new EntityHookContext { Entity = "item", Operation = CrudOperation.Create };
        var result = await sut.RunBeforeHookAsync(new List<string> { "nonexistent_hook" }, ctx);

        Assert.False(result.Cancel);   // 中断しない
    }

    // ── ヘルパー ─────────────────────────────────────────────────────────────

    private static EntityCrudExecutionService CreateSut(
        IDbConnection db,
        IEntityHookRegistry frameworkRegistry)
    {
        return CreateSutWithProjectRegistry(db, frameworkRegistry, new TestProjectHookRegistry(), "p1");
    }

    private static EntityCrudExecutionService CreateSutWithProjectRegistry(
        IDbConnection db,
        IEntityHookRegistry frameworkRegistry,
        IProjectHookRegistry projectRegistry,
        string projectName)
    {
        var scope = new ProjectScope();
        var method = typeof(ProjectScope).GetMethod("Set", BindingFlags.Instance | BindingFlags.NonPublic);
        method!.Invoke(scope, new object[]
        {
            new ProjectInfo
            {
                Name = projectName,
                DisplayName = projectName,
                ProjectDir = ".",
                DatabaseType = "sqlite",
                ConnectionString = "Data Source=:memory:",
                EntityMetadata = new NullEntityMetadataProvider(),
                DashboardConfig = new NullDashboardConfigProvider(),
                PageMetadata = NullPageMetadataProvider.Instance
            }
        });

        return new EntityCrudExecutionService(
            db,
            new NullAuditLogService(),
            frameworkRegistry,
            projectRegistry,
            new NullTelemetry(),
            scope,
            NullLogger<EntityCrudExecutionService>.Instance);
    }

    private sealed class DelegateHook : IEntityHook
    {
        private readonly Func<EntityHookContext, HookResult> _before;
        private readonly Func<EntityHookContext, Task>? _afterAsync;
        public string Name { get; }

        public DelegateHook(string name,
            Func<EntityHookContext, HookResult>? before = null,
            Func<EntityHookContext, Task>? after = null)
        {
            Name = name;
            _before = before ?? (_ => HookResult.Continue());
            _afterAsync = after;
        }

        public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) =>
            Task.FromResult(_before(ctx));

        public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) =>
            _afterAsync != null ? _afterAsync(ctx) : Task.CompletedTask;
    }

    private sealed class OrderedHookRegistry : IEntityHookRegistry
    {
        private readonly Dictionary<string, IEntityHook> _hooks;

        public OrderedHookRegistry(Dictionary<string, IEntityHook>? hooks = null)
        {
            _hooks = hooks ?? new Dictionary<string, IEntityHook>(StringComparer.OrdinalIgnoreCase);
        }

        public IEntityHook? Find(string hookNameWithConfig, EntityHookContext? context = null)
        {
            var name = hookNameWithConfig.Split(':', 2)[0];
            return _hooks.TryGetValue(name, out var h) ? h : null;
        }
    }

    private sealed class TestProjectHookRegistry : IProjectHookRegistry
    {
        private readonly Dictionary<string, Dictionary<string, IEntityHook>> _hooks =
            new(StringComparer.OrdinalIgnoreCase);

        public IEntityHook? Find(string project, string hook, EntityHookContext? ctx = null)
        {
            var name = hook.Split(':', 2)[0];
            return _hooks.TryGetValue(project, out var byName) &&
                   byName.TryGetValue(name, out var h) ? h : null;
        }

        public IEnumerable<string> GetHookNames(string project) =>
            _hooks.TryGetValue(project, out var d) ? d.Keys : Enumerable.Empty<string>();

        public IEnumerable<IEntityHook> GetAllHooks(string project) =>
            _hooks.TryGetValue(project, out var d) ? d.Values : Enumerable.Empty<IEntityHook>();

        public void Register(string project, IEntityHook hook)
        {
            if (!_hooks.TryGetValue(project, out var byName))
                _hooks[project] = byName = new Dictionary<string, IEntityHook>(StringComparer.OrdinalIgnoreCase);
            byName[hook.Name] = hook;
        }

        public void Clear(string project)
        {
            _hooks.Remove(project);
        }
    }

    private sealed class NullEntityMetadataProvider : IEntityMetadataProvider
    {
        public EntityDefinition Get(string e) => throw new KeyNotFoundException(e);
        public IReadOnlyDictionary<string, EntityDefinition> GetAll() => new Dictionary<string, EntityDefinition>();
        public bool TryGet(string e, [NotNullWhen(true)] out EntityDefinition? def) { def = null; return false; }
    }

    private sealed class NullDashboardConfigProvider : IDashboardConfigProvider
    {
        public DashboardConfig GetConfig() => new();
    }

    private sealed class NullAuditLogService : IAuditLogService
    {
        public Task WriteAsync(string action, string? entity = null, string? detail = null,
            string? userName = null, IDbConnection? connection = null, IDbTransaction? transaction = null)
            => Task.CompletedTask;
    }

    private sealed class NullTelemetry : IHookExecutionTelemetry
    {
        public void Record(HookExecutionTelemetryEvent evt) { }
    }
}
