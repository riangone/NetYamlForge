using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.Hooks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NetYamlForge.Tests;

public class DynamicEntityCommandServiceTests
{
    [Fact]
    public async Task CreateAsync_ReturnsCancel_WhenBeforeHookAborts()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var repo = new FakeRepo();
        var commandService = CreateCommandService(
            conn,
            repo,
            hookName: "block_create",
            hook: new DelegateHook("block_create", _ => HookResult.Abort("blocked")));

        var result = await commandService.CreateAsync(
            "customer",
            new Dictionary<string, object?> { ["Name"] = "A" },
            new List<string> { "block_create" },
            new List<string> { "after_any" },
            "alice");

        Assert.False(result.Ok);
        Assert.Equal(CommandErrorCodes.HookRejectedBeforeCreate, result.Error?.Code);
        Assert.Equal("blocked", result.Error?.Message);
        Assert.Equal(0, repo.InsertCallCount);
    }

    [Fact]
    public async Task UpdateAsync_CallsRepository_WhenHooksPass()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var repo = new FakeRepo();
        var commandService = CreateCommandService(conn, repo);

        var result = await commandService.UpdateAsync(
            "customer",
            "CustomerId",
            "10",
            new Dictionary<string, object?> { ["Name"] = "Updated" },
            beforeHooks: null,
            afterHooks: null,
            userName: "alice");

        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.Equal(1, repo.UpdateCallCount);
        Assert.Equal("10", repo.LastId);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsConcurrencyConflict_WhenNoRowsAffected()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var repo = new FakeRepo { NextUpdateAffectedRows = 0 };
        var commandService = CreateCommandService(conn, repo);

        var result = await commandService.UpdateAsync(
            "customer",
            "CustomerId",
            "10",
            new Dictionary<string, object?> { ["Name"] = "Updated" },
            beforeHooks: null,
            afterHooks: null,
            userName: "alice");

        Assert.False(result.Ok);
        Assert.Equal(CommandErrorCodes.ConcurrencyConflictOrNotFound, result.Error?.Code);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsConcurrencyConflict_WhenNoRowsAffected()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var repo = new FakeRepo { NextDeleteAffectedRows = 0 };
        var commandService = CreateCommandService(conn, repo);

        var result = await commandService.DeleteAsync(
            "customer",
            "CustomerId",
            "10",
            beforeHooks: null,
            afterHooks: null,
            userName: "alice");

        Assert.False(result.Ok);
        Assert.Equal(CommandErrorCodes.ConcurrencyConflictOrNotFound, result.Error?.Code);
    }

    private static DynamicEntityCommandService CreateCommandService(
        IDbConnection db,
        IDynamicCrudRepository repo,
        string? hookName = null,
        IEntityHook? hook = null)
    {
        var scope = new ProjectScope();
        SetProjectScope(scope, "p1");

        var frameworkRegistry = new TestEntityHookRegistry();
        if (!string.IsNullOrWhiteSpace(hookName) && hook != null)
        {
            frameworkRegistry.Register(hookName!, hook);
        }

        var execution = new EntityCrudExecutionService(
            db,
            new NoopAuditLogService(),
            frameworkRegistry,
            new TestProjectHookRegistry(),
            new NoopHookExecutionTelemetry(),
            scope,
            NullLogger<EntityCrudExecutionService>.Instance);

        return new DynamicEntityCommandService(repo, execution);
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

    private sealed class FakeRepo : IDynamicCrudRepository
    {
        public int InsertCallCount { get; private set; }
        public int UpdateCallCount { get; private set; }
        public int DeleteCallCount { get; private set; }
        public int NextUpdateAffectedRows { get; set; } = 1;
        public int NextDeleteAffectedRows { get; set; } = 1;
        public string? LastId { get; private set; }

        public Task<IEnumerable<dynamic>> GetAllAsync(string entity, string? search, string? sort, string? dir, Dictionary<string, string?>? filters = null, int page = 1, int? pageSize = null, string? cursor = null, bool keyset = false, bool fetchOneExtra = false) => throw new NotImplementedException();
        public Task<dynamic?> GetByIdAsync(string entity, object id) => throw new NotImplementedException();
        public Task<dynamic?> GetByIdAsync(string entity, IDictionary<string, object?> keyValues) => throw new NotImplementedException();
        public Task<int> InsertAsync(string entity, IDictionary<string, object?> values, IDbTransaction? tx = null)
        {
            InsertCallCount++;
            return Task.FromResult(123);
        }

        public Task<int> UpdateAsync(string entity, object id, IDictionary<string, object?> values, IDbTransaction? tx = null)
        {
            UpdateCallCount++;
            LastId = id.ToString();
            return Task.FromResult(NextUpdateAffectedRows);
        }

        public Task<int> UpdateAsync(string entity, IDictionary<string, object?> keyValues, IDictionary<string, object?> values, IDbTransaction? tx = null)
            => throw new NotImplementedException();

        public Task<int> DeleteAsync(string entity, object id, IDbTransaction? tx = null)
        {
            DeleteCallCount++;
            LastId = id.ToString();
            return Task.FromResult(NextDeleteAffectedRows);
        }

        public Task<int> DeleteAsync(string entity, IDictionary<string, object?> keyValues, IDbTransaction? tx = null)
            => throw new NotImplementedException();

        public Task<IEnumerable<dynamic>> GetAllForEntityAsync(string entity, ForeignKeyDefinition? foreignKey = null, string? search = null, int page = 1, int? pageSize = null, bool fetchOneExtra = false)
            => throw new NotImplementedException();

        public Task<int> CountAsync(string entity, string? search, Dictionary<string, string?>? filters = null)
            => throw new NotImplementedException();
    }

    private sealed class DelegateHook : IEntityHook
    {
        private readonly Func<EntityHookContext, HookResult> _before;
        public string Name { get; }

        public DelegateHook(string name, Func<EntityHookContext, HookResult> before)
        {
            Name = name;
            _before = before;
        }

        public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) =>
            Task.FromResult(_before(ctx));

        public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
    }

    private sealed class TestEntityHookRegistry : IEntityHookRegistry
    {
        private readonly Dictionary<string, IEntityHook> _hooks = new(StringComparer.OrdinalIgnoreCase);
        public void Register(string name, IEntityHook hook) => _hooks[name] = hook;
        public IEntityHook? Find(string hookNameWithConfig, EntityHookContext? context = null)
        {
            var key = hookNameWithConfig.Split(':', 2)[0];
            _hooks.TryGetValue(key, out var hook);
            return hook;
        }
    }

    private sealed class TestProjectHookRegistry : IProjectHookRegistry
    {
        public IEntityHook? Find(string projectName, string hookName, EntityHookContext? context = null) => null;
        public IEnumerable<string> GetHookNames(string projectName) => Enumerable.Empty<string>();
        public IEnumerable<IEntityHook> GetAllHooks(string projectName) => Enumerable.Empty<IEntityHook>();
        public void Register(string projectName, IEntityHook hook) { }
        public void Clear(string projectName) { }
    }

    private sealed class NoopAuditLogService : IAuditLogService
    {
        public Task WriteAsync(string action, string? entity = null, string? detail = null, string? userName = null, IDbConnection? connection = null, IDbTransaction? transaction = null)
            => Task.CompletedTask;
    }

    private sealed class NoopHookExecutionTelemetry : IHookExecutionTelemetry
    {
        public void Record(HookExecutionTelemetryEvent evt) { }
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
