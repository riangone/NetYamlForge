// ファイル概要：MCP（Model Context Protocol）ツールから呼び出される、エンティティ CRUD / メタデータ /
// カスタムアクションのビジネスロジックを提供するサービスです。
// `ApiEntityController` の判定・変換ロジック（ValidateApiAccess / ToApiDto / ConvertValue）を
// MCP コンテキスト向けに再実装しています。MCP 工具呼び出しにはルートの {project} セグメントが
// 存在しないため、各メソッドの最初の引数 `project` を使って `ProjectScope` を明示的に bind します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using Microsoft.Extensions.DependencyInjection;
using NetYamlForge.Models;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Services.Mcp;

/// <summary>
/// MCP ツール呼び出しの戻り値を表す統一フォーマット。
/// `Ok=false` の場合 `Error` にエラーメッセージを設定し、ツール側はこれをそのまま
/// クライアントに返す（例外をスローしない）。
/// </summary>
public sealed class McpToolResult
{
    public bool    Ok    { get; init; }
    public string? Error { get; init; }
    public object? Data  { get; init; }

    public static McpToolResult Success(object? data) => new() { Ok = true, Data = data };
    public static McpToolResult Failure(string error)  => new() { Ok = false, Error = error };
}

/// <summary>
/// MCP ツール（<see cref="EntityMcpTools"/>）が委譲する実処理を提供するスコープサービス。
/// `IDynamicCrudRepository` / `IEntityMetadataProvider` / `DynamicEntityCommandService` /
/// `DynamicEntityFormValidationService` / `ProjectManager` / `ProjectScope` /
/// `IProjectActionRegistry` を組み合わせて、`ApiEntityController` と同等の CRUD 操作を提供します。
/// </summary>
public sealed class EntityToolService
{
    /// <summary>
    /// プロジェクトバインド後に解決されるスコープ依存の集合。
    /// `IEntityMetadataProvider`（<c>ProjectAwareEntityMetadataProvider</c>）はコンストラクタ内で
    /// `ProjectScope.Current` を参照するため、`ProjectScope.Set()` 実行前に解決すると例外が発生します。
    /// そのため本サービスのコンストラクタでは直接注入せず、<see cref="BindProject"/> 完了後に
    /// <see cref="IServiceProvider"/> から都度解決します。
    /// </summary>
    private readonly record struct ScopedServices(
        IDynamicCrudRepository             Repo,
        IEntityMetadataProvider            Meta,
        DynamicEntityCommandService        CommandService,
        DynamicEntityFormValidationService FormValidationService);

    private readonly IServiceProvider           _serviceProvider;
    private readonly ProjectManager             _projectManager;
    private readonly ProjectScope               _projectScope;
    private readonly IProjectActionRegistry     _actionRegistry;
    private readonly IAuditLogService           _audit;
    private readonly IEntityHooksService        _entityHooks;
    private readonly ILogger<EntityToolService> _logger;

    public EntityToolService(
        IServiceProvider             serviceProvider,
        ProjectManager               projectManager,
        ProjectScope                 projectScope,
        IProjectActionRegistry       actionRegistry,
        IAuditLogService             audit,
        IEntityHooksService          entityHooks,
        ILogger<EntityToolService>   logger)
    {
        _serviceProvider = serviceProvider;
        _projectManager  = projectManager;
        _projectScope    = projectScope;
        _actionRegistry  = actionRegistry;
        _audit           = audit;
        _entityHooks     = entityHooks;
        _logger          = logger;
    }

    // ─── プロジェクトバインド ──────────────────────────────────────────────

    /// <summary>
    /// 指定された <paramref name="projectName"/> を <see cref="ProjectScope"/> に bind します。
    /// 見つからない場合は <see cref="McpToolResult.Failure"/> を返します。
    /// </summary>
    private McpToolResult? BindProject(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            return McpToolResult.Failure("Parameter 'project' is required.");

        if (!_projectManager.TryGet(projectName, out var info) || info == null)
            return McpToolResult.Failure($"Project '{projectName}' was not found.");

        _projectScope.Set(info);
        return null;
    }

    /// <summary>
    /// <see cref="BindProject"/> 完了後に呼び出し、プロジェクトに紐づくスコープ依存を解決します。
    /// </summary>
    private ScopedServices ResolveScopedServices() => new(
        Repo:                  _serviceProvider.GetRequiredService<IDynamicCrudRepository>(),
        Meta:                  _serviceProvider.GetRequiredService<IEntityMetadataProvider>(),
        CommandService:        _serviceProvider.GetRequiredService<DynamicEntityCommandService>(),
        FormValidationService: _serviceProvider.GetRequiredService<DynamicEntityFormValidationService>());

    /// <summary>
    /// `meta.Api` を確認し、`disabled`/`readonly` の場合は権限エラーを返します。
    /// </summary>
    private static McpToolResult? ValidateApiAccess(EntityDefinition meta, bool writeRequired)
    {
        var apiMode = (meta.Api ?? "disabled").ToLowerInvariant();
        if (apiMode == "disabled")
            return McpToolResult.Failure($"API access to entity '{meta.Table}' is disabled by configuration.");

        if (writeRequired && apiMode == "readonly")
            return McpToolResult.Failure($"API access to entity '{meta.Table}' is read-only.");

        return null;
    }

    /// <summary>
    /// `entity` を解決し、存在しない/非公開/API無効の場合はエラーを返します。
    /// 成功時は `(meta, null)` を返します。
    /// </summary>
    private static (EntityDefinition? meta, McpToolResult? error) ResolveEntity(
        IEntityMetadataProvider metaProvider, string entity, bool writeRequired)
    {
        if (!metaProvider.TryGet(entity, out var meta) || meta == null)
            return (null, McpToolResult.Failure($"Entity '{entity}' was not found."));

        var denied = ValidateApiAccess(meta, writeRequired);
        if (denied != null)
            return (null, denied);

        return (meta, null);
    }

    // ─── list_projects ─────────────────────────────────────────────────────

    /// <summary>登録されている全てのテナントプロジェクトを列挙します。</summary>
    public McpToolResult ListProjects()
    {
        var projects = _projectManager.GetAll()
            .Select(p => new { name = p.Name, displayName = p.DisplayName })
            .OrderBy(p => p.name)
            .ToList();

        return McpToolResult.Success(projects);
    }

    // ─── list_entities ──────────────────────────────────────────────────────

    /// <summary>指定プロジェクト内で API アクセスが許可されている（`api != disabled`）エンティティを列挙します。</summary>
    public McpToolResult ListEntities(string project)
    {
        var bindError = BindProject(project);
        if (bindError != null) return bindError;

        var svc = ResolveScopedServices();
        var entities = svc.Meta.GetAll()
            .Where(kv => !string.Equals(kv.Value.Api ?? "disabled", "disabled", StringComparison.OrdinalIgnoreCase))
            .Select(kv => new
            {
                entity      = kv.Key,
                table       = kv.Value.Table,
                displayName = kv.Value.DisplayName,
                api         = kv.Value.Api ?? "disabled"
            })
            .OrderBy(e => e.entity)
            .ToList();

        return McpToolResult.Success(entities);
    }

    // ─── get_entity_meta ────────────────────────────────────────────────────

    /// <summary>指定エンティティのカラム定義・フォーム定義・主キー情報を取得します。</summary>
    public McpToolResult GetEntityMeta(string project, string entity)
    {
        var bindError = BindProject(project);
        if (bindError != null) return bindError;

        var svc = ResolveScopedServices();
        var (meta, error) = ResolveEntity(svc.Meta, entity, writeRequired: false);
        if (error != null) return error;

        var columns = meta!.Columns.ToDictionary(
            kv => kv.Key,
            kv => new
            {
                type     = kv.Value.Type,
                label    = kv.Value.Label ?? string.Empty,
                required = kv.Value.Required,
                editable = kv.Value.Editable,
                identity = kv.Value.Identity,
                options  = kv.Value.Options ?? new List<string>()
            });

        var forms = meta.Forms.ToDictionary(
            kv => kv.Key,
            kv => new
            {
                type     = kv.Value.Type,
                label    = kv.Value.Label ?? string.Empty,
                required = kv.Value.Required,
                editable = kv.Value.Editable,
                options  = kv.Value.Options ?? new List<string>()
            });

        return McpToolResult.Success(new
        {
            entity,
            table             = meta.Table,
            displayName       = meta.DisplayName,
            primaryKeyColumns = meta.GetPrimaryKeyColumns().ToList(),
            api               = meta.Api ?? "disabled",
            columns,
            forms,
            actions = meta.Actions.Keys.ToList()
        });
    }

    // ─── list_entity_records ────────────────────────────────────────────────

    /// <summary>エンティティのレコード一覧をページネーション・検索・ソート・フィルタ付きで取得します。</summary>
    public async Task<McpToolResult> ListRecordsAsync(
        string project, string entity, string? search, string? sort, string? dir, int page, int pageSize,
        Dictionary<string, string?>? filters = null)
    {
        var bindError = BindProject(project);
        if (bindError != null) return bindError;

        var svc = ResolveScopedServices();
        var (meta, error) = ResolveEntity(svc.Meta, entity, writeRequired: false);
        if (error != null) return error;

        var filterDict = filters ?? new Dictionary<string, string?>();

        var items = await svc.Repo.GetAllAsync(
            entity:   entity,
            search:   search,
            sort:     sort,
            dir:      dir ?? "asc",
            filters:  filterDict,
            page:     page,
            pageSize: pageSize);

        var total = await svc.Repo.CountAsync(entity, search, filterDict);

        var data = items.Select(item => ToApiDto((IDictionary<string, object?>)item, meta!)).ToList();

        return McpToolResult.Success(new
        {
            data,
            page,
            pageSize,
            total,
            totalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    // ─── get_entity_record ──────────────────────────────────────────────────

    /// <summary>主キーを指定して単一レコードを取得します。</summary>
    public async Task<McpToolResult> GetRecordAsync(string project, string entity, string id)
    {
        var bindError = BindProject(project);
        if (bindError != null) return bindError;

        var svc = ResolveScopedServices();
        var (meta, error) = ResolveEntity(svc.Meta, entity, writeRequired: false);
        if (error != null) return error;

        var item = await svc.Repo.GetByIdAsync(entity, id);
        if (item == null)
            return McpToolResult.Failure($"Entity '{entity}' with id '{id}' was not found.");

        return McpToolResult.Success(ToApiDto(item, meta!));
    }

    // ─── create_entity_record ───────────────────────────────────────────────

    /// <summary>新しいレコードを作成します。`data` は列名→値のマップです。</summary>
    public async Task<McpToolResult> CreateRecordAsync(string project, string entity, Dictionary<string, object?> data)
    {
        var bindError = BindProject(project);
        if (bindError != null) return bindError;

        var svc = ResolveScopedServices();
        var (meta, error) = ResolveEntity(svc.Meta, entity, writeRequired: true);
        if (error != null) return error;

        var stringForm = data.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
        var (values, errors) = await svc.FormValidationService.ConvertAndValidateAsync(meta!, stringForm, project);
        if (errors.Count > 0)
            return McpToolResult.Failure(string.Join("; ", errors.Select(e => $"{e.Key}: {e.Value}")));

        var beforeHooks = meta!.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.BeforeCreate, msg => _logger.LogWarning("{Message}", msg)) : null;
        var afterHooks  = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.AfterCreate,  msg => _logger.LogWarning("{Message}", msg)) : null;

        var result = await svc.CommandService.CreateAsync(entity, values, beforeHooks, afterHooks, userName: null);
        if (!result.Ok)
            return McpToolResult.Failure(result.Error?.Message ?? "Failed to create entity");

        var created = await svc.Repo.GetByIdAsync(entity, result.Value.ToString()!);
        await _audit.WriteAsync("mcp.create", entity, $"id={result.Value}", userName: null);
        return McpToolResult.Success(ToApiDto(created!, meta));
    }

    // ─── update_entity_record ───────────────────────────────────────────────

    /// <summary>既存レコードをフィールド単位で更新します（PATCH 相当）。</summary>
    public async Task<McpToolResult> UpdateRecordAsync(string project, string entity, string id, Dictionary<string, object?> data)
    {
        var bindError = BindProject(project);
        if (bindError != null) return bindError;

        var svc = ResolveScopedServices();
        var (meta, error) = ResolveEntity(svc.Meta, entity, writeRequired: true);
        if (error != null) return error;

        if (await svc.Repo.GetByIdAsync(entity, id) == null)
            return McpToolResult.Failure($"Entity '{entity}' with id '{id}' was not found.");

        var stringForm = data.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
        var (values, errors) = await svc.FormValidationService.ConvertAndValidateAsync(meta!, stringForm, project);
        if (errors.Count > 0)
            return McpToolResult.Failure(string.Join("; ", errors.Select(e => $"{e.Key}: {e.Value}")));

        var beforeHooks = meta!.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.BeforeUpdate, msg => _logger.LogWarning("{Message}", msg)) : null;
        var afterHooks  = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.AfterUpdate,  msg => _logger.LogWarning("{Message}", msg)) : null;

        var result = await svc.CommandService.UpdateAsync(
            entity, meta.GetPrimaryKeyColumns()[0], id, values, beforeHooks, afterHooks, userName: null);

        if (!result.Ok)
            return McpToolResult.Failure(result.Error?.Message ?? "Failed to update entity");

        var updated = await svc.Repo.GetByIdAsync(entity, id);
        await _audit.WriteAsync("mcp.update", entity, $"id={id}", userName: null);
        return McpToolResult.Success(ToApiDto(updated!, meta));
    }

    // ─── delete_entity_record ───────────────────────────────────────────────

    /// <summary>主キーを指定してレコードを削除します。</summary>
    public async Task<McpToolResult> DeleteRecordAsync(string project, string entity, string id)
    {
        var bindError = BindProject(project);
        if (bindError != null) return bindError;

        var svc = ResolveScopedServices();
        var (meta, error) = ResolveEntity(svc.Meta, entity, writeRequired: true);
        if (error != null) return error;

        if (await svc.Repo.GetByIdAsync(entity, id) == null)
            return McpToolResult.Failure($"Entity '{entity}' with id '{id}' was not found.");

        var beforeHooks = meta!.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.BeforeDelete, msg => _logger.LogWarning("{Message}", msg)) : null;
        var afterHooks  = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.AfterDelete,  msg => _logger.LogWarning("{Message}", msg)) : null;

        var result = await svc.CommandService.DeleteAsync(
            entity, meta.GetPrimaryKeyColumns()[0], id, beforeHooks, afterHooks, userName: null);

        if (!result.Ok)
            return McpToolResult.Failure(result.Error?.Message ?? "Failed to delete entity");

        await _audit.WriteAsync("mcp.delete", entity, $"id={id}", userName: null);
        return McpToolResult.Success(new { deleted = true, id });
    }

    // ─── invoke_entity_action ───────────────────────────────────────────────

    /// <summary>エンティティに定義されたカスタムアクション（entities.yml の `actions`）を実行します。</summary>
    public async Task<McpToolResult> InvokeActionAsync(
        string project, string entity, string id, string actionKey, Dictionary<string, object?>? inputs)
    {
        var bindError = BindProject(project);
        if (bindError != null) return bindError;

        var svc = ResolveScopedServices();
        var (meta, error) = ResolveEntity(svc.Meta, entity, writeRequired: true);
        if (error != null) return error;

        if (!meta!.Actions.TryGetValue(actionKey, out var actionDef))
            return McpToolResult.Failure($"Action '{actionKey}' was not found.");

        var projectName = _projectScope.Current?.Name ?? "";
        var handlerName = string.IsNullOrWhiteSpace(actionDef.Handler) ? actionKey : actionDef.Handler;
        var handler = _actionRegistry.Find(projectName, handlerName);
        if (handler == null)
        {
            _logger.LogWarning("Action handler '{Handler}' not found for project '{Project}'", handlerName, projectName);
            return McpToolResult.Failure($"Action handler '{handlerName}' was not found.");
        }

        var actionInputs = inputs ?? new Dictionary<string, object?>();

        var ctx = new CustomActionContext
        {
            Project       = projectName,
            Entity        = entity,
            Action        = actionKey,
            RecordId      = id,
            Inputs        = actionInputs,
            Files         = new Dictionary<string, string>(),
            MultipleFiles = new Dictionary<string, List<string>>(),
            UserName      = null
        };

        var beforeHooks = actionDef.Hooks?.Before;
        if (beforeHooks != null && beforeHooks.Count > 0)
        {
            var hookCtx = new EntityHookContext
            {
                Entity    = entity,
                Operation = CrudOperation.CustomAction,
                Id        = int.TryParse(id, out var intId) ? intId : null,
                Values    = actionInputs,
                UserName  = null
            };
            var beforeResult = await svc.CommandService.RunBeforeHooksForActionAsync(beforeHooks, hookCtx);
            if (beforeResult.Cancel)
                return McpToolResult.Failure(beforeResult.CancelMessage ?? "Cancelled by pre-action hook.");
        }

        ActionHandlerResult result;
        try
        {
            result = await svc.CommandService.ExecuteActionAsync(handler, ctx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing action '{Action}'", actionKey);
            return McpToolResult.Failure("An error occurred during action execution.");
        }

        if (!result.Ok)
            return McpToolResult.Failure(result.ErrorMessage ?? "Action execution failed.");

        await _audit.WriteAsync("mcp.action", entity, $"id={id} action={actionKey}", userName: null);
        return McpToolResult.Success(new { success = true, message = result.ErrorMessage ?? "Action executed successfully." });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> ToApiDto(IDictionary<string, object?> item, EntityDefinition meta)
    {
        var pkCol = meta.GetPrimaryKeyColumns()[0];
        var dto = new Dictionary<string, object?>
        {
            ["id"]   = item.TryGetValue(pkCol, out var idVal) ? idVal?.ToString() : null,
            ["data"] = item.Where(kv => meta.Columns.ContainsKey(kv.Key))
                            .ToDictionary(kv => kv.Key, kv => ConvertValue(kv.Value, meta.Columns[kv.Key].Type))
        };
        return dto;
    }

    private static object? ConvertValue(object? value, string type)
    {
        if (value == null || value == DBNull.Value) return null;
        return type.ToLowerInvariant() switch
        {
            "int" or "integer" or "long"                 => Convert.ToInt64(value),
            "double" or "decimal" or "float" or "number" => Convert.ToDecimal(value),
            "bool" or "boolean"                          => Convert.ToBoolean(value),
            _                                             => value.ToString()
        };
    }
}
