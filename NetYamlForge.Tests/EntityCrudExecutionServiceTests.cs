using System.Data;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.Hooks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NetYamlForge.Tests;

public class EntityCrudExecutionServiceTests
{
    [Fact]
    public async Task RunBeforeHookAsync_PrioritizesProjectHookOverFrameworkHook()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var ctx = new EntityHookContext { Entity = "customer", Operation = CrudOperation.Create };
        var frameworkCalled = false;
        var projectCalled = false;

        var frameworkRegistry = new TestEntityHookRegistry(new Dictionary<string, IEntityHook>
        {
            ["normalize_name"] = new DelegateHook(
                "normalize_name",
                before: _ =>
                {
                    frameworkCalled = true;
                    return HookResult.Continue();
                })
        });
        var projectRegistry = new TestProjectHookRegistry();
        projectRegistry.Register("p1", new DelegateHook(
            "normalize_name",
            before: _ =>
            {
                projectCalled = true;
                return HookResult.Abort("stop");
            }));

        var sut = CreateSut(conn, frameworkRegistry, projectRegistry, "p1");

        var result = await sut.RunBeforeHookAsync(new List<string> { "normalize_name" }, ctx);

        Assert.True(projectCalled);
        Assert.False(frameworkCalled);
        Assert.True(result.Cancel);
        Assert.Equal("stop", result.CancelMessage);
    }

    [Fact]
    public async Task ExecuteCrudTransactionAsync_RollsBackOnException()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("CREATE TABLE T(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);");

        var sut = CreateSut(conn, new TestEntityHookRegistry(), new TestProjectHookRegistry(), "p1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteCrudTransactionAsync(async tx =>
            {
                await conn.ExecuteAsync("INSERT INTO T(Id, Name) VALUES(1, 'a')", transaction: tx);
                throw new InvalidOperationException("boom");
            }));

        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM T");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task TryWriteHookRejectAuditAsync_WritesAuditEntry()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var audit = new CaptureAuditLogService();
        var sut = CreateSut(conn, new TestEntityHookRegistry(), new TestProjectHookRegistry(), "p1", audit);

        await sut.TryWriteHookRejectAuditAsync(
            "customer",
            CrudOperation.Update,
            "10",
            new List<string> { "validate_email" },
            "メール形式不正",
            "alice");

        Assert.Single(audit.Writes);
        Assert.Equal("hook_rejected", audit.Writes[0].Action);
        Assert.Equal("customer", audit.Writes[0].Entity);
        Assert.Equal("alice", audit.Writes[0].UserName);
        Assert.Contains("\"reasonCode\":\"", audit.Writes[0].Detail);
    }

    [Fact]
    public async Task RunBeforeHookAsync_EmitsTelemetryEvent()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var telemetry = new CaptureHookExecutionTelemetry();
        var frameworkRegistry = new TestEntityHookRegistry(new Dictionary<string, IEntityHook>
        {
            ["normalize_name"] = new DelegateHook("normalize_name", _ => HookResult.Continue())
        });
        var scope = new ProjectScope();
        SetProjectScope(scope, "p1");
        var sut = new EntityCrudExecutionService(
            conn,
            new CaptureAuditLogService(),
            frameworkRegistry,
            new TestProjectHookRegistry(),
            telemetry,
            scope,
            NullLogger<EntityCrudExecutionService>.Instance);

        await sut.RunBeforeHookAsync(
            new List<string> { "normalize_name" },
            new EntityHookContext { Entity = "customer", Operation = CrudOperation.Create });

        Assert.Single(telemetry.Events);
        Assert.Equal("before", telemetry.Events[0].Phase);
        Assert.Equal("framework", telemetry.Events[0].Source);
        Assert.Equal("normalize_name", telemetry.Events[0].HookName);
        Assert.Equal("continue", telemetry.Events[0].Result);
    }

    private static EntityCrudExecutionService CreateSut(
        IDbConnection db,
        IEntityHookRegistry frameworkRegistry,
        IProjectHookRegistry projectRegistry,
        string projectName,
        IAuditLogService? audit = null)
    {
        var scope = new ProjectScope();
        SetProjectScope(scope, projectName);

        return new EntityCrudExecutionService(
            db,
            audit ?? new CaptureAuditLogService(),
            frameworkRegistry,
            projectRegistry,
            new CaptureHookExecutionTelemetry(),
            scope,
            NullLogger<EntityCrudExecutionService>.Instance);
    }

    private static void SetProjectScope(ProjectScope scope, string projectName)
    {
        var method = typeof(ProjectScope).GetMethod("Set", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(scope, new object[]
        {
            new ProjectInfo
            {
                Name = projectName,
                DisplayName = projectName,
                ProjectDir = ".",
                DatabaseType = "sqlite",
                ConnectionString = "Data Source=:memory:",
                EntityMetadata = new DummyEntityMetadataProvider(),
                DashboardConfig = new DummyDashboardConfigProvider(),
                PageMetadata = NullPageMetadataProvider.Instance
            }
        });
    }

    private sealed class DelegateHook : IEntityHook
    {
        private readonly Func<EntityHookContext, HookResult> _before;
        private readonly Func<EntityHookContext, Task> _after;
        public string Name { get; }

        public DelegateHook(string name, Func<EntityHookContext, HookResult>? before = null, Func<EntityHookContext, Task>? after = null)
        {
            Name = name;
            _before = before ?? (_ => HookResult.Continue());
            _after = after ?? (_ => Task.CompletedTask);
        }

        public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) =>
            Task.FromResult(_before(ctx));

        public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => _after(ctx);
    }

    private sealed class TestEntityHookRegistry : IEntityHookRegistry
    {
        private readonly Dictionary<string, IEntityHook> _hooks;

        public TestEntityHookRegistry(Dictionary<string, IEntityHook>? hooks = null)
        {
            _hooks = hooks ?? new Dictionary<string, IEntityHook>(StringComparer.OrdinalIgnoreCase);
        }

        public IEntityHook? Find(string hookNameWithConfig, EntityHookContext? context = null)
        {
            var name = hookNameWithConfig.Split(':', 2)[0];
            _hooks.TryGetValue(name, out var hook);
            return hook;
        }
    }

    private sealed class TestProjectHookRegistry : IProjectHookRegistry
    {
        private readonly Dictionary<string, Dictionary<string, IEntityHook>> _hooks = new(StringComparer.OrdinalIgnoreCase);

        public IEntityHook? Find(string projectName, string hookName, EntityHookContext? context = null)
        {
            if (_hooks.TryGetValue(projectName, out var byName) &&
                byName.TryGetValue(hookName.Split(':', 2)[0], out var hook))
            {
                return hook;
            }

            return null;
        }

        public IEnumerable<string> GetHookNames(string projectName) =>
            _hooks.TryGetValue(projectName, out var byName) ? byName.Keys : Enumerable.Empty<string>();

        public IEnumerable<IEntityHook> GetAllHooks(string projectName) =>
            _hooks.TryGetValue(projectName, out var byName) ? byName.Values : Enumerable.Empty<IEntityHook>();

        public void Register(string projectName, IEntityHook hook)
        {
            if (!_hooks.TryGetValue(projectName, out var byName))
            {
                byName = new Dictionary<string, IEntityHook>(StringComparer.OrdinalIgnoreCase);
                _hooks[projectName] = byName;
            }

            byName[hook.Name] = hook;
        }
    }

    private sealed class CaptureAuditLogService : IAuditLogService
    {
        public List<(string Action, string? Entity, string? Detail, string? UserName)> Writes { get; } = new();

        public Task WriteAsync(
            string action,
            string? entity = null,
            string? detail = null,
            string? userName = null,
            IDbConnection? connection = null,
            IDbTransaction? transaction = null)
        {
            Writes.Add((action, entity, detail, userName));
            return Task.CompletedTask;
        }
    }

    private sealed class CaptureHookExecutionTelemetry : IHookExecutionTelemetry
    {
        public List<HookExecutionTelemetryEvent> Events { get; } = new();

        public void Record(HookExecutionTelemetryEvent evt) => Events.Add(evt);
    }

    private sealed class DummyEntityMetadataProvider : IEntityMetadataProvider
    {
        public EntityDefinition Get(string entityName) => throw new KeyNotFoundException(entityName);
        public IReadOnlyDictionary<string, EntityDefinition> GetAll() => new Dictionary<string, EntityDefinition>();
        public bool TryGet(string entityName, [NotNullWhen(true)] out EntityDefinition? definition)
        {
            definition = null;
            return false;
        }
    }

    private sealed class DummyDashboardConfigProvider : IDashboardConfigProvider
    {
        public DashboardConfig GetConfig() => new();
    }
}
