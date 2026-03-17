// ファイル概要: 動的エンティティの一覧・作成・編集・削除・部分更新を処理します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using NetYamlForge.Models;
using NetYamlForge.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Controllers;

[Authorize]
[Route("{project}/DynamicEntity/{action}")]
public class DynamicEntityController : BaseProjectController
{
    private readonly IDynamicCrudRepository _repo;
    private readonly IEntityMetadataProvider _meta;
    private readonly DynamicEntityCommandService _commandService;
    private readonly DynamicEntityKeyResolverService _keyResolver;
    private readonly DynamicEntityListResponseService _listResponseService;
    private readonly DynamicEntityListQueryService _listQueryService;
    private readonly DynamicEntityForeignKeyDataService _foreignKeyDataService;
    private readonly DynamicEntityFormViewModelFactory _formVmFactory;
    private readonly DynamicEntityListHttpResponseService _listHttpResponseService;
    private readonly DynamicEntityNavigationService _navigationService;
    private readonly DynamicEntityConfigDiagnosticsService _configDiagnosticsService;
    private readonly DynamicEntityFormValidationService _formValidationService;
    private readonly CommandErrorHttpMapper _commandErrorHttpMapper;
    private readonly ProjectScope _projectScope;
    private readonly IFileUploadService _fileUploadService;
    private readonly ILogger<DynamicEntityController> _logger;

    public DynamicEntityController(
        IDynamicCrudRepository repo,
        IEntityMetadataProvider meta,
        DynamicEntityCommandService commandService,
        DynamicEntityKeyResolverService keyResolver,
        DynamicEntityListResponseService listResponseService,
        DynamicEntityListQueryService listQueryService,
        DynamicEntityForeignKeyDataService foreignKeyDataService,
        DynamicEntityFormViewModelFactory formVmFactory,
        DynamicEntityListHttpResponseService listHttpResponseService,
        DynamicEntityNavigationService navigationService,
        DynamicEntityConfigDiagnosticsService configDiagnosticsService,
        DynamicEntityFormValidationService formValidationService,
        CommandErrorHttpMapper commandErrorHttpMapper,
        ProjectScope projectScope,
        IFileUploadService fileUploadService,
        ILogger<DynamicEntityController> logger)
    {
        _repo = repo;
        _meta = meta;
        _commandService = commandService;
        _keyResolver = keyResolver;
        _listResponseService = listResponseService;
        _listQueryService = listQueryService;
        _foreignKeyDataService = foreignKeyDataService;
        _formVmFactory = formVmFactory;
        _listHttpResponseService = listHttpResponseService;
        _navigationService = navigationService;
        _configDiagnosticsService = configDiagnosticsService;
        _formValidationService = formValidationService;
        _commandErrorHttpMapper = commandErrorHttpMapper;
        _projectScope = projectScope;
        _fileUploadService = fileUploadService;
        _logger = logger;
    }

    public async Task<IActionResult> Index(
        string entity = "customer",
        string? search = null,
        string? sort = null,
        string? dir = null,
        int? pageSize = null,
        string? count = null,
        string? clear = null,
        string? cursor = null,
        string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";
        count = NormalizeSingleValue(count);
        clear = NormalizeSingleValue(clear);

        // 初期画面表示。メタデータから検索条件を解釈し、一覧表示モデルを構築します。
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        var queryResult = await _listQueryService.LoadAsync(
            entity,
            meta,
            search,
            sort,
            dir,
            page: 1,
            pageSize,
            count,
            clear,
            cursor,
            Request.Query,
            foreignKeysForForm: false);
        var page = 1;

        return View("Index",
            CreateListViewModel(
                entity,
                meta,
                queryResult.Items,
                queryResult.EffectiveSearch,
                sort,
                dir,
                queryResult.ForeignKeyData,
                page,
                queryResult.Total,
                queryResult.Filters,
                queryResult.PageSize,
                queryResult.IncludeCount,
                queryResult.HasMore,
                queryResult.NextCursor,
                cursor,
                returnUrl));
    }

    public async Task<IActionResult> ListPartial(
        string entity = "customer",
        string? search = null,
        string? sort = null,
        string? dir = null,
        int page = 1,
        int? pageSize = null,
        string? count = null,
        string? clear = null,
        string? cursor = null,
        string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";
        count = NormalizeSingleValue(count);
        clear = NormalizeSingleValue(clear);

        // HTMXによる一覧部分更新。count有無・keyset有無をここで切り替えます。
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        var queryResult = await _listQueryService.LoadAsync(
            entity,
            meta,
            search,
            sort,
            dir,
            page,
            pageSize,
            count,
            clear,
            cursor,
            Request.Query,
            foreignKeysForForm: true);
        _listHttpResponseService.TrySetPushUrl(
            Response,
            Url.Action(nameof(Index), "DynamicEntity"),
            Request.Query,
            entity,
            returnUrl);

        return PartialView("_List",
            CreateListViewModel(
                entity,
                meta,
                queryResult.Items,
                queryResult.EffectiveSearch,
                sort,
                dir,
                queryResult.ForeignKeyData,
                page,
                queryResult.Total,
                queryResult.Filters,
                queryResult.PageSize,
                queryResult.IncludeCount,
                queryResult.HasMore,
                queryResult.NextCursor,
                cursor,
                returnUrl));
    }

    public async Task<IActionResult> CreateForm(string entity = "customer", string mode = "modal")
    {
        entity = NormalizeSingleValue(entity) ?? "customer";

        // 新規作成フォームの描画（モーダル/ページ両対応）。
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        var fkData = await _foreignKeyDataService.LoadForFormAsync(meta);
        var vm = _formVmFactory.Build(entity, meta, null, fkData, mode: mode);
        return PartialView("_Form", vm);
    }

    public async Task<IActionResult> CreatePage(string entity = "customer", string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";

        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        var fkData = await _foreignKeyDataService.LoadForFormAsync(meta);
        var breadcrumbs = _navigationService.BuildBreadcrumbChain(returnUrl);
        var vm = _formVmFactory.Build(entity, meta, null, fkData, mode: "page", breadcrumbChain: breadcrumbs);
        return View("FormPage", vm);
    }

    [HttpPost]
    public async Task<IActionResult> Create(string entity, [FromForm] Dictionary<string, string?> form, [FromForm] IFormFileCollection? files = null, string mode = "modal", [FromForm] string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";

        // 登録処理。CRUD本体と監査ログを同一トランザクションで実行します。
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        var (values, errors) = _formValidationService.ConvertAndValidate(meta, form);
        var isPageMode = mode.Equals("page", StringComparison.OrdinalIgnoreCase);

        if (errors.Any())
            return await RenderFormWithErrorsAsync(entity, meta, mode, form, errors);

        var beforeCreateHooks = meta.Hooks?.GetExpandedHookList(h => h.BeforeCreate, msg => _logger.LogWarning("{Message}", msg));
        var afterCreateHooks = meta.Hooks?.GetExpandedHookList(h => h.AfterCreate, msg => _logger.LogWarning("{Message}", msg));
        var createResult = await _commandService.CreateAsync(
            entity,
            values,
            beforeCreateHooks,
            afterCreateHooks,
            User.Identity?.Name);
        if (!createResult.Ok)
        {
            errors["__hook"] = createResult.Error?.Message ?? "前処理によりキャンセルされました。";
            return await RenderFormWithErrorsAsync(entity, meta, mode, form, errors);
        }
        if (isPageMode)
            return Redirect(returnUrl ?? Url.Action(nameof(Index), new { entity })!);

        return await ReturnListAfterSaveAsync(entity, meta, returnUrl);
    }

    public async Task<IActionResult> EditForm(string entity, string? id = null, string mode = "modal")
    {
        entity = NormalizeSingleValue(entity) ?? "customer";

        // 既存レコードを読み込み、編集フォームを返します。
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        
        var keyValue = _keyResolver.ResolvePrimaryKeyValue(meta, id, Request.Query);
        
        var item = await _repo.GetByIdAsync(entity, keyValue ?? "");
        var fkData = await _foreignKeyDataService.LoadForFormAsync(meta);
        var vm = _formVmFactory.Build(entity, meta, item, fkData, mode: mode);
        return PartialView("_Form", vm);
    }

    public async Task<IActionResult> EditPage(string entity, string? id = null, string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";

        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        
        var keyValue = _keyResolver.ResolvePrimaryKeyValue(meta, id, Request.Query);
        
        var item = await _repo.GetByIdAsync(entity, keyValue ?? "");
        var fkData = await _foreignKeyDataService.LoadForFormAsync(meta);
        var breadcrumbs = _navigationService.BuildBreadcrumbChain(returnUrl);
        var vm = _formVmFactory.Build(entity, meta, item, fkData, mode: "page", breadcrumbChain: breadcrumbs);
        return View("FormPage", vm);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(string entity, string? id = null, [FromForm] Dictionary<string, string?>? form = null, [FromForm] IFormFileCollection? files = null, string mode = "modal", [FromForm] string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";

        // 更新処理。登録同様に監査ログと整合性を取るためTx内で実行します。
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        
        var pkColumns = meta.GetPrimaryKeyColumns();
        var keyValue = _keyResolver.ResolvePrimaryKeyValue(meta, id, Request.Query);
        
        form ??= new Dictionary<string, string?>();
        var (values, errors) = _formValidationService.ConvertAndValidate(meta, form);
        var isPageMode = mode.Equals("page", StringComparison.OrdinalIgnoreCase);

        if (errors.Any())
            return await RenderFormWithErrorsAsync(entity, meta, mode, form, errors, keyValue);

        var beforeUpdateHooks = meta.Hooks?.GetExpandedHookList(h => h.BeforeUpdate, msg => _logger.LogWarning("{Message}", msg));
        var afterUpdateHooks = meta.Hooks?.GetExpandedHookList(h => h.AfterUpdate, msg => _logger.LogWarning("{Message}", msg));
        var updateResult = await _commandService.UpdateAsync(
            entity,
            pkColumns[0],
            keyValue,
            values,
            beforeUpdateHooks,
            afterUpdateHooks,
            User.Identity?.Name);
        if (!updateResult.Ok)
        {
            errors["__hook"] = updateResult.Error?.Message ?? "前処理によりキャンセルされました。";
            return await RenderFormWithErrorsAsync(entity, meta, mode, form, errors, keyValue);
        }
        if (isPageMode)
            return Redirect(returnUrl ?? Url.Action(nameof(Index), new { entity })!);

        return await ReturnListAfterSaveAsync(entity, meta, returnUrl);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(string entity, string? id = null, [FromForm] string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";

        // 削除処理（論理/物理はRepository側で自動分岐）。
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        
        var pkColumns = meta.GetPrimaryKeyColumns();
        var keyValue = _keyResolver.ResolvePrimaryKeyValue(meta, id, Request.Query);

        var beforeDeleteHooks = meta.Hooks?.GetExpandedHookList(h => h.BeforeDelete, msg => _logger.LogWarning("{Message}", msg));
        var afterDeleteHooks = meta.Hooks?.GetExpandedHookList(h => h.AfterDelete, msg => _logger.LogWarning("{Message}", msg));
        var deleteResult = await _commandService.DeleteAsync(
            entity,
            pkColumns[0],
            keyValue,
            beforeDeleteHooks,
            afterDeleteHooks,
            User.Identity?.Name);
        if (!deleteResult.Ok)
        {
            if (_commandErrorHttpMapper.IsConflict(deleteResult.Error))
            {
                return Conflict(deleteResult.Error?.Message ?? "対象データが更新済みか、既に削除されています。");
            }

            return BadRequest(deleteResult.Error?.Message ?? "前処理により削除がキャンセルされました。");
        }
        var (items, total) = await _listResponseService.LoadFirstPageAfterMutationAsync(entity);
        return PartialView("_List",
            CreateListViewModel(
                entity,
                meta,
                items,
                null,
                null,
                null,
                new(),
                1,
                total,
                null,
                5,
                true,
                false,
                null,
                null,
                returnUrl));
    }

    // エンティティ定義（フィールド・フォーム・フィルタ等）をページで表示します（Admin のみ）
    [Authorize(Roles = "Admin")]
    public IActionResult Definition(string entity = "customer")
    {
        entity = NormalizeSingleValue(entity) ?? "customer";
        var meta = _meta.Get(entity);
        return View("Definition", new EntityDefinitionViewModel(entity, meta));
    }

    // 全エンティティ定義の概要一覧を表示します（Admin のみ）
    [Authorize(Roles = "Admin")]
    public IActionResult AllDefinitions()
    {
        var all = _meta.GetAll();
        return View("AllDefinitions", new AllDefinitionsViewModel(all));
    }

    // 現在プロジェクトの有効エンティティ設定を診断表示します（Admin のみ）
    [Authorize(Roles = "Admin")]
    public IActionResult ConfigDiagnostics(string entity = "customer", bool onlyChanged = true)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";
        var all = _meta.GetAll();
        var projectName = _projectScope.Current?.Name ?? "";
        var diagnostics = _configDiagnosticsService.Build(entity, all, onlyChanged);

        return View("ConfigDiagnostics", new ConfigDiagnosticsViewModel(
            projectName,
            diagnostics.SelectedEntity,
            diagnostics.Entities,
            diagnostics.BaseJson,
            diagnostics.EffectiveJson,
            diagnostics.DiffLines,
            onlyChanged,
            diagnostics.ChangedCount));
    }

    // エンティティ選択ピッカーモーダル用の一覧を返します
    public async Task<IActionResult> PickerList(
        string entity,
        string targetField = "",
        string displayColumn = "Id",
        string[]? displayColumns = null,
        string? query = null,
        bool multi = false,
        string? search = null,
        int page = 1,
        int pageSize = 10)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }

        var displayCols = new List<string>();
        if (displayColumns != null && displayColumns.Length > 0)
        {
            foreach (var item in displayColumns)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                displayCols.AddRange(
                    item.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            }
        }

        var fk = new ForeignKeyDefinition
        {
            Entity = entity,
            DisplayColumn = displayColumn,
            DisplayColumns = displayCols.Count > 0 ? displayCols.Distinct(StringComparer.OrdinalIgnoreCase).ToList() : null,
            Query = query
        };
        var itemsRaw = await _repo.GetAllForEntityAsync(entity, fk, search, page, pageSize, fetchOneExtra: true);
        var list = itemsRaw.ToList();
        var hasMore = list.Count > pageSize;
        if (hasMore) list = list.Take(pageSize).ToList();

        return PartialView("_Picker", new PickerViewModel(entity, meta, list, targetField, fk.GetDisplayColumns().ToList(), multi, search, page, pageSize, hasMore));
    }

    private DynamicListViewModel CreateListViewModel(
        string entity,
        EntityDefinition meta,
        IEnumerable<dynamic> items,
        string? search,
        string? sort,
        string? dir,
        Dictionary<string, IEnumerable<dynamic>> fkData,
        int page,
        int total,
        Dictionary<string, string?>? filters,
        int pageSize,
        bool includeCount,
        bool hasMore,
        string? nextCursor,
        string? cursor,
        string? returnUrl)
    {
        var returnEntity = _navigationService.ExtractEntityFromReturnUrl(returnUrl);
        string? returnDisplayName = null;
        if (!string.IsNullOrEmpty(returnEntity) && _meta.TryGet(returnEntity!, out var previousMeta))
        {
            returnDisplayName = previousMeta.GetDisplayName();
        }

        var breadcrumbChain = _navigationService.BuildBreadcrumbChain(returnUrl);

        return new DynamicListViewModel(
            entity,
            meta,
            items,
            search,
            sort,
            dir,
            fkData,
            page,
            total,
            filters,
            pageSize,
            includeCount,
            hasMore,
            nextCursor,
            cursor,
            returnUrl,
            returnEntity,
            returnDisplayName,
            breadcrumbChain);
    }

    private IActionResult RenderFormByMode(bool isPageMode, DynamicFormViewModel vm)
        => isPageMode ? View("FormPage", vm) : PartialView("_Form", vm);

    /// <summary>
    /// バリデーション/フックエラー時にフォームを再描画する共通処理。
    /// keyValue が指定されている場合は既存レコードを読み込んで渡す（Edit 用）。
    /// </summary>
    private async Task<IActionResult> RenderFormWithErrorsAsync(
        string entity,
        EntityDefinition meta,
        string mode,
        Dictionary<string, string?> form,
        Dictionary<string, string> errors,
        string? keyValue = null)
    {
        var isPageMode = mode.Equals("page", StringComparison.OrdinalIgnoreCase);
        dynamic? item = keyValue != null ? await _repo.GetByIdAsync(entity, keyValue) : null;
        var fkData = await _foreignKeyDataService.LoadForFormAsync(meta);
        var vm = _formVmFactory.Build(entity, meta, item, fkData, errors, mode, submittedValues: form);
        return RenderFormByMode(isPageMode, vm);
    }

    /// <summary>
    /// Create/Edit 成功後にリスト先頭ページを返す共通処理（モーダルモード用）。
    /// </summary>
    private async Task<IActionResult> ReturnListAfterSaveAsync(
        string entity,
        EntityDefinition meta,
        string? returnUrl)
    {
        var (items, total) = await _listResponseService.LoadFirstPageAfterMutationAsync(entity);
        _listHttpResponseService.SetEntityFormSavedHeaders(Response);
        return PartialView("_List",
            CreateListViewModel(entity, meta, items, null, null, null, new(), 1, total, null, 5, true, false, null, null, returnUrl));
    }

}

public record BreadcrumbItem(string Label, string Url);

public record DynamicListViewModel(
    string Entity,
    EntityDefinition Meta,
    IEnumerable<dynamic> Items,
    string? Search,
    string? Sort,
    string? Dir,
    Dictionary<string, IEnumerable<dynamic>> ForeignKeyData,
    int Page,
    int Total,
    Dictionary<string, string?>? Filters = null,
    int PageSize = 5,
    bool CountEnabled = true,
    bool HasMore = false,
    string? NextCursor = null,
    string? Cursor = null,
    string? ReturnUrl = null,
    string? ReturnEntity = null,
    string? ReturnDisplayName = null,
    IReadOnlyList<BreadcrumbItem>? BreadcrumbChain = null);

public record DynamicFormViewModel(
    string Entity,
    EntityDefinition Meta,
    dynamic? Item,
    Dictionary<string, IEnumerable<dynamic>> ForeignKeyData,
    Dictionary<string, string> Errors,
    string Mode = "modal",
    IReadOnlyList<BreadcrumbItem>? BreadcrumbChain = null,
    /// <summary>
    /// バリデーション/フックエラー時に渡す送信値。
    /// フォーム再描画時にフィールドの値を保持するために使用します。
    /// </summary>
    Dictionary<string, string?>? SubmittedValues = null,
    Dictionary<string, ColumnDefinition>? ColumnDefinitions = null);

public record PickerViewModel(
    string Entity,
    EntityDefinition Meta,
    IEnumerable<dynamic> Items,
    string TargetField,
    IReadOnlyList<string> DisplayColumns,
    bool Multi,
    string? Search,
    int Page,
    int PageSize,
    bool HasMore);

public record DynamicFormFieldViewModel(
    string FieldName,
    FormDefinition Def,
    object? Value,
    string Label,
    string? Error,
    Dictionary<string, IEnumerable<dynamic>> ForeignKeyData,
    ColumnDefinition? ColDef = null);

// ColumnDefinition 拡張プロパティ（ビューで使用）
public static class DynamicFormFieldViewModelExtensions
{
    public static string GetColType(this DynamicFormFieldViewModel vm) => vm.ColDef?.Type ?? "string";
    public static List<string>? GetColOptions(this DynamicFormFieldViewModel vm) => vm.ColDef?.Options;
    public static bool GetColEditable(this DynamicFormFieldViewModel vm) => vm.ColDef?.Editable ?? true;
    public static string? GetColCurrency(this DynamicFormFieldViewModel vm) => vm.ColDef?.Currency;
    public static string? GetColLocale(this DynamicFormFieldViewModel vm) => vm.ColDef?.Locale;
    public static int? GetColPrecision(this DynamicFormFieldViewModel vm) => vm.ColDef?.Precision;
    public static string? GetColUploadPath(this DynamicFormFieldViewModel vm) => vm.ColDef?.UploadPath;
    public static string? GetColAllowedExtensions(this DynamicFormFieldViewModel vm) => vm.ColDef?.AllowedExtensions;
    public static long? GetColMaxFileSize(this DynamicFormFieldViewModel vm) => vm.ColDef?.MaxFileSize;
    public static Dictionary<string, string>? GetColOptionLabels(this DynamicFormFieldViewModel vm) => vm.ColDef?.OptionLabels;
    public static string? GetColPlaceholder(this DynamicFormFieldViewModel vm) => vm.ColDef?.Placeholder;
}

public record EntityDefinitionViewModel(string Entity, EntityDefinition Meta);

public record AllDefinitionsViewModel(IReadOnlyDictionary<string, EntityDefinition> Entities);

public record ConfigDiagnosticsViewModel(
    string ProjectName,
    string Entity,
    IReadOnlyList<string> Entities,
    string BaseJson,
    string EffectiveJson,
    IReadOnlyList<string> DiffLines,
    bool OnlyChanged,
    int ChangedCount);
