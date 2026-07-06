using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using NetYamlForge.Controllers;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.DynamicEntity;
using NetYamlForge.Services.Hooks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NetYamlForge.Tests;

public class DynamicEntityControllerTests
{
    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TestApp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        public Microsoft.Extensions.Hosting.IHostEnvironment Services { get; set; } = null!;
        public string EnvironmentName { get; set; } = "Test";
    }
    [Fact]
    public async Task Create_ReturnsPartialForm_WhenValidationFailsInModalMode()
    {
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var (repo, controller) = CreateDefaultSetup(db);

        var result = await controller.Create(
            "customer",
            new Dictionary<string, string?> { ["Amount"] = "abc" },
            mode: "modal");

        var partial = Assert.IsType<PartialViewResult>(result);
        Assert.Equal("_Form", partial.ViewName);
        var vm = Assert.IsType<DynamicFormViewModel>(partial.Model);
        Assert.Equal("abc", vm.SubmittedValues!["Amount"]);
        Assert.Equal("Invalid integer", vm.Errors["Amount"]);
    }

    [Fact]
    public async Task Create_ReturnsPageForm_WhenValidationFailsInPageMode()
    {
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var (_, controller) = CreateDefaultSetup(db);

        var result = await controller.Create(
            "customer",
            new Dictionary<string, string?> { ["Amount"] = "abc" },
            mode: "page");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("FormPage", view.ViewName);
        var vm = Assert.IsType<DynamicFormViewModel>(view.Model);
        Assert.Equal("page", vm.Mode);
        Assert.Equal("Invalid integer", vm.Errors["Amount"]);
    }

    [Fact]
    public async Task Create_RedirectsToReturnUrl_WhenPageModeSucceeds()
    {
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var (repo, controller) = CreateDefaultSetup(db);

        var result = await controller.Create(
            "customer",
            new Dictionary<string, string?> { ["Amount"] = "12" },
            mode: "page",
            returnUrl: "/p/DynamicEntity/Index?entity=customer");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/p/DynamicEntity/Index?entity=customer", redirect.Url);
        Assert.Equal(1, repo.InsertCallCount);
    }

    [Fact]
    public async Task Create_ReturnsListAndSetsHtmxHeaders_WhenModalSucceeds()
    {
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var (repo, controller) = CreateDefaultSetup(db);

        var result = await controller.Create(
            "customer",
            new Dictionary<string, string?> { ["Amount"] = "22" },
            mode: "modal");

        var partial = Assert.IsType<PartialViewResult>(result);
        Assert.Equal("_List", partial.ViewName);
        Assert.Equal(1, repo.InsertCallCount);
        Assert.Equal(1, repo.CountCallCount);
        Assert.Equal("#list-container", controller.Response.Headers["HX-Retarget"].ToString());
        Assert.Equal("entity-form-saved", controller.Response.Headers["HX-Trigger"].ToString());
    }

    [Fact]
    public async Task Edit_ReturnsPartialForm_WhenValidationFailsInModalMode()
    {
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var (_, controller) = CreateDefaultSetup(db);

        var result = await controller.Edit(
            "customer",
            id: "7",
            form: new Dictionary<string, string?> { ["Amount"] = "abc" },
            mode: "modal");

        var partial = Assert.IsType<PartialViewResult>(result);
        Assert.Equal("_Form", partial.ViewName);
        var vm = Assert.IsType<DynamicFormViewModel>(partial.Model);
        Assert.Equal("Invalid integer", vm.Errors["Amount"]);
    }

    [Fact]
    public async Task Edit_RedirectsToReturnUrl_WhenPageModeSucceeds()
    {
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var (repo, controller) = CreateDefaultSetup(db);

        var result = await controller.Edit(
            "customer",
            id: "9",
            form: new Dictionary<string, string?> { ["Amount"] = "33" },
            mode: "page",
            returnUrl: "/p/DynamicEntity/Index?entity=customer");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/p/DynamicEntity/Index?entity=customer", redirect.Url);
        Assert.Equal(1, repo.UpdateCallCount);
        Assert.Equal("9", repo.LastUpdatedId);
    }

    [Fact]
    public async Task Edit_ReturnsListAndSetsHtmxHeaders_WhenModalSucceeds()
    {
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var (repo, controller) = CreateDefaultSetup(db);

        var result = await controller.Edit(
            "customer",
            id: "9",
            form: new Dictionary<string, string?> { ["Amount"] = "44" },
            mode: "modal");

        var partial = Assert.IsType<PartialViewResult>(result);
        Assert.Equal("_List", partial.ViewName);
        Assert.Equal(1, repo.UpdateCallCount);
        Assert.Equal(1, repo.CountCallCount);
        Assert.Equal("#list-container", controller.Response.Headers["HX-Retarget"].ToString());
        Assert.Equal("entity-form-saved", controller.Response.Headers["HX-Trigger"].ToString());
    }

    [Fact]
    public async Task Delete_ReturnsBadRequest_WhenBeforeHookRejects()
    {
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var repo = new FakeRepo();
        var hooks = new Dictionary<string, IEntityHook>(StringComparer.OrdinalIgnoreCase)
        {
            ["block_delete"] = new DelegateHook("block_delete", _ => HookResult.Abort("blocked by hook"))
        };
        var metaProvider = new StubEntityMetadataProvider(CreateEntityMeta(
            hooks: new EntityHooksDefinition { BeforeDelete = new List<string> { "block_delete" } }));
        var controller = CreateController(db, repo, metaProvider, new DictionaryEntityHookRegistry(hooks));

        var result = await controller.Delete("customer", id: "3");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("blocked by hook", badRequest.Value);
        Assert.Equal(0, repo.DeleteCallCount);
    }

    [Fact]
    public async Task Delete_ReturnsListPartial_WhenDeleteSucceeds()
    {
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var (repo, controller) = CreateDefaultSetup(db);

        var result = await controller.Delete("customer", id: "12");

        var partial = Assert.IsType<PartialViewResult>(result);
        Assert.Equal("_List", partial.ViewName);
        Assert.Equal(1, repo.DeleteCallCount);
    }

    [Fact]
    public async Task Delete_ReturnsConflict_WhenNoRowsAffected()
    {
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var (repo, controller) = CreateDefaultSetup(db);
        repo.NextDeleteAffectedRows = 0;

        var result = await controller.Delete("customer", id: "12");

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("既に削除", conflict.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigDiagnostics_FallsBackToAvailableEntity_AndReturnsViewModel()
    {
        var db = new SqliteConnection("Data Source=:memory:");
        db.Open();
        try
        {
            var customer = CreateEntityMeta();
            var order = CreateEntityMeta();
            order.DisplayName = "Order";
            var metadata = new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer"] = customer,
                ["order"] = order
            };
            var controller = CreateController(
                db,
                new FakeRepo(),
                new DictionaryEntityMetadataProvider(metadata));

            var result = controller.ConfigDiagnostics(entity: "missing-entity", onlyChanged: true);

            var view = Assert.IsType<ViewResult>(result);
            Assert.Equal("ConfigDiagnostics", view.ViewName);
            var vm = Assert.IsType<ConfigDiagnosticsViewModel>(view.Model);
            Assert.Equal("p1", vm.ProjectName);
            Assert.Equal("customer", vm.Entity);
            Assert.Contains("customer", vm.Entities);
            Assert.Contains("order", vm.Entities);
        }
        finally
        {
            db.Dispose();
        }
    }

    [Fact]
    public async Task ListPartial_SetsHxPushUrlHeader()
    {
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var (_, controller) = CreateDefaultSetup(db);
        controller.HttpContext.Request.QueryString = new QueryString("?entity=customer&search=abc&sort=Amount&dir=asc");

        var result = await controller.ListPartial(
            entity: "customer",
            search: "abc",
            sort: "Amount",
            dir: "asc",
            page: 1,
            count: "true");

        var partial = Assert.IsType<PartialViewResult>(result);
        Assert.Equal("_List", partial.ViewName);
        Assert.True(controller.Response.Headers.TryGetValue("HX-Push-Url", out var pushUrl));
        Assert.Contains("entity=customer", pushUrl.ToString(), StringComparison.Ordinal);
        Assert.Contains("search=abc", pushUrl.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Index_SkipsCount_WhenCountIsZero()
    {
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var (repo, controller) = CreateDefaultSetup(db);

        var result = await controller.Index(entity: "customer", search: "abc", count: "0");

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<DynamicListViewModel>(view.Model);
        Assert.Equal(-1, vm.Total);
        Assert.Equal(0, repo.CountCallCount);
        Assert.Equal("abc", repo.LastSearch);
        Assert.True(repo.LastFetchOneExtra);
    }

    [Fact]
    public async Task ListPartial_ClearsSearch_WhenClearRequested()
    {
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        var (repo, controller) = CreateDefaultSetup(db);

        var result = await controller.ListPartial(
            entity: "customer",
            search: "abc",
            clear: "1",
            count: "true");

        var partial = Assert.IsType<PartialViewResult>(result);
        Assert.Equal("_List", partial.ViewName);
        Assert.Null(repo.LastSearch);
    }

    /// <summary>
    /// 最も一般的なテストシナリオ向けの簡易セットアップ。
    /// db は呼び出し元で await using 管理すること。
    /// </summary>
    private static (FakeRepo repo, DynamicEntityController controller) CreateDefaultSetup(
        System.Data.IDbConnection db,
        EntityHooksDefinition? hooks = null)
    {
        var repo = new FakeRepo();
        var metaProvider = new StubEntityMetadataProvider(CreateEntityMeta(hooks));
        return (repo, CreateController(db, repo, metaProvider));
    }

    private static DynamicEntityController CreateController(
        IDbConnection db,
        IDynamicCrudRepository repo,
        IEntityMetadataProvider metaProvider,
        IEntityHookRegistry? frameworkHooks = null,
        IBaseEntityMetadataProvider? baseMetadataProvider = null)
    {
        var scope = new ProjectScope();
        SetProjectScope(scope, metaProvider);
        var execution = new EntityCrudExecutionService(
            db,
            new NoopAuditLogService(),
            frameworkHooks ?? new StubEntityHookRegistry(),
            new StubProjectHookRegistry(),
            new NoopHookExecutionTelemetry(),
            scope,
            NullLogger<EntityCrudExecutionService>.Instance);
        var commandService = new DynamicEntityCommandService(repo, execution);
        var diagnosticsService = new DynamicEntityConfigDiagnosticsService(
            baseMetadataProvider ?? new StubBaseEntityMetadataProvider(),
            new DynamicEntityConfigDiffService(),
            NullLogger<DynamicEntityConfigDiagnosticsService>.Instance);
        var foreignKeyDataService = new DynamicEntityForeignKeyDataService(repo);
        var listQueryService = new DynamicEntityListQueryService(repo, foreignKeyDataService);
        var controller = new DynamicEntityController(
            repo,
            metaProvider,
            commandService,
            new DynamicEntityKeyResolverService(),
            new DynamicEntityListResponseService(repo),
            listQueryService,
            foreignKeyDataService,
            new DynamicEntityFormViewModelFactory(),
            new DynamicEntityListHttpResponseService(),
            new DynamicEntityNavigationService(metaProvider),
            diagnosticsService,
            new DynamicEntitySchemaMigrationService(NullLogger<DynamicEntitySchemaMigrationService>.Instance),
            new DynamicEntityFormValidationService(new FormValueValidationService(new ValueConverter())),
            new CommandErrorHttpMapper(),
            scope,
            new FileUploadService(
                new TestWebHostEnvironment(),
                NullLogger<FileUploadService>.Instance),
            new StubProjectActionRegistry(),
            new SqliteConnection("Data Source=:memory:"),
            new StubPdfExportService(),
            new StubDocumentPdfService(),
            new TestWebHostEnvironment(),
            new EntityHooksService(),
            NullLogger<DynamicEntityController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Url = new StubUrlHelper("/p1/DynamicEntity/Index");

        return controller;
    }

    private static void SetProjectScope(ProjectScope scope, IEntityMetadataProvider metaProvider)
    {
        var method = typeof(ProjectScope).GetMethod("Set", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(scope, new object[]
        {
            new ProjectInfo
            {
                Name = "p1",
                DisplayName = "p1",
                ProjectDir = ".",
                DatabaseType = "sqlite",
                ConnectionString = "Data Source=:memory:",
                EntityMetadata = metaProvider,
                DashboardConfig = new DummyDashboardConfigProvider(),
                PageMetadata = NullPageMetadataProvider.Instance
            }
        });
    }

    private static EntityDefinition CreateEntityMeta(EntityHooksDefinition? hooks = null)
    {
        return new EntityDefinition
        {
            Table = "Customer",
            Key = "Id",
            DisplayName = "Customer",
            IsPublic = true,
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["Amount"] = new() { Type = "int" }
            },
            Forms = new Dictionary<string, FormDefinition>
            {
                ["Amount"] = new() { Type = "int" }
            },
            Hooks = hooks
        };
    }

    private sealed class FakeRepo : IDynamicCrudRepository
    {
        public int InsertCallCount { get; private set; }
        public int UpdateCallCount { get; private set; }
        public int DeleteCallCount { get; private set; }
        public int NextUpdateAffectedRows { get; set; } = 1;
        public int NextDeleteAffectedRows { get; set; } = 1;
        public int CountCallCount { get; private set; }
        public string? LastSearch { get; private set; }
        public bool LastFetchOneExtra { get; private set; }
        public string? LastUpdatedId { get; private set; }

        public Task<IEnumerable<dynamic>> GetAllAsync(string entity, string? search, string? sort, string? dir, Dictionary<string, string?>? filters = null, int page = 1, int? pageSize = null, string? cursor = null, bool keyset = false, bool fetchOneExtra = false)
        {
            LastSearch = search;
            LastFetchOneExtra = fetchOneExtra;
            return Task.FromResult<IEnumerable<dynamic>>(new List<dynamic>());
        }

        public Task<int> CountAsync(string entity, string? search, Dictionary<string, string?>? filters = null)
        {
            CountCallCount++;
            return Task.FromResult(0);
        }

        public Task<dynamic?> GetByIdAsync(string entity, object id) => Task.FromResult<dynamic?>(null);
        public Task<dynamic?> GetByIdAsync(string entity, IDictionary<string, object?> keyValues) => Task.FromResult<dynamic?>(null);

        public Task<int> InsertAsync(string entity, IDictionary<string, object?> values, IDbTransaction? tx = null)
        {
            InsertCallCount++;
            return Task.FromResult(1);
        }

        public Task<int> UpdateAsync(string entity, object id, IDictionary<string, object?> values, IDbTransaction? tx = null)
        {
            UpdateCallCount++;
            LastUpdatedId = id.ToString();
            return Task.FromResult(NextUpdateAffectedRows);
        }

        public Task<int> UpdateAsync(string entity, IDictionary<string, object?> keyValues, IDictionary<string, object?> values, IDbTransaction? tx = null)
            => Task.FromResult(1);

        public Task<int> DeleteAsync(string entity, object id, IDbTransaction? tx = null)
        {
            DeleteCallCount++;
            return Task.FromResult(NextDeleteAffectedRows);
        }

        public Task<int> DeleteAsync(string entity, IDictionary<string, object?> keyValues, IDbTransaction? tx = null)
            => Task.FromResult(1);

        public Task<IEnumerable<dynamic>> GetAllForEntityAsync(string entity, ForeignKeyDefinition? foreignKey = null, string? search = null, int page = 1, int? pageSize = null, bool fetchOneExtra = false)
            => Task.FromResult<IEnumerable<dynamic>>(new List<dynamic>());
    }

    private sealed class StubEntityMetadataProvider : IEntityMetadataProvider
    {
        private readonly EntityDefinition _entity;

        public StubEntityMetadataProvider(EntityDefinition entity)
        {
            _entity = entity;
        }

        public EntityDefinition Get(string entityName) => _entity;

        public IReadOnlyDictionary<string, EntityDefinition> GetAll() =>
            new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase) { ["customer"] = _entity };

        public bool TryGet(string entityName, [NotNullWhen(true)] out EntityDefinition? definition)
        {
            definition = _entity;
            return true;
        }
    }

    private sealed class StubBaseEntityMetadataProvider : IBaseEntityMetadataProvider
    {
        public bool TryGet(string entityName, out EntityDefinition? definition)
        {
            definition = null;
            return false;
        }
    }

    private sealed class DictionaryEntityMetadataProvider : IEntityMetadataProvider
    {
        private readonly IReadOnlyDictionary<string, EntityDefinition> _entities;

        public DictionaryEntityMetadataProvider(IReadOnlyDictionary<string, EntityDefinition> entities)
        {
            _entities = entities;
        }

        public EntityDefinition Get(string entityName) => _entities[entityName];

        public IReadOnlyDictionary<string, EntityDefinition> GetAll() => _entities;

        public bool TryGet(string entityName, [NotNullWhen(true)] out EntityDefinition? definition)
        {
            return _entities.TryGetValue(entityName, out definition);
        }
    }

    private sealed class StubEntityHookRegistry : IEntityHookRegistry
    {
        public IEntityHook? Find(string hookNameWithConfig, EntityHookContext? context = null) => null;
    }

    private sealed class DictionaryEntityHookRegistry : IEntityHookRegistry
    {
        private readonly IReadOnlyDictionary<string, IEntityHook> _hooks;

        public DictionaryEntityHookRegistry(IReadOnlyDictionary<string, IEntityHook> hooks)
        {
            _hooks = hooks;
        }

        public IEntityHook? Find(string hookNameWithConfig, EntityHookContext? context = null)
        {
            var name = hookNameWithConfig.Split(':', 2)[0];
            return _hooks.TryGetValue(name, out var hook) ? hook : null;
        }
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

    private sealed class StubProjectHookRegistry : IProjectHookRegistry
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

    private sealed class DummyDashboardConfigProvider : IDashboardConfigProvider
    {
        public DashboardConfig GetConfig() => new();
    }

    private sealed class StubProjectActionRegistry : IProjectActionRegistry
    {
        public ICustomActionHandler? Find(string projectName, string handlerName) => null;
        public void Register(string projectName, ICustomActionHandler handler) { }
        public void Clear(string projectName) { }
    }

    private sealed class StubPdfExportService : IPdfExportService
    {
        public byte[] Generate(
            List<IDictionary<string, object>> rows,
            List<(string Key, string Label)> columns,
            EntityDefinition meta,
            PdfExportOptions options,
            string? projectDir = null) => [];
    }

    private sealed class StubDocumentPdfService : IDocumentPdfService
    {
        public PdfTemplateConfig? LoadTemplate(string projectDir, string templateName) => null;
        public PdfTemplateConfig? LoadGlobalTemplate(string templateName) => null;

        public byte[] Generate(
            PdfTemplateConfig template,
            IDictionary<string, object?> header,
            IDictionary<string, IList<IDictionary<string, object?>>> dataSources,
            string? projectDir = null) => [];
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        private readonly string _actionUrl;

        public StubUrlHelper(string actionUrl)
        {
            _actionUrl = actionUrl;
        }

        public ActionContext ActionContext { get; } = new(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());

        public string? Action(UrlActionContext actionContext) => _actionUrl;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => _actionUrl;
        public string? RouteUrl(UrlRouteContext routeContext) => _actionUrl;
    }
}
