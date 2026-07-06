// ファイル概要: 動的エンティティの一覧・作成・編集・削除・部分更新を処理します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.DynamicEntity;
using NetYamlForge.Services.Hooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using System.Data;
using System.Text;
using System.Text.Json;

namespace NetYamlForge.Controllers;

[Authorize]
[Route("{project}/DynamicEntity/{action}")]
public partial class DynamicEntityController : BaseProjectController
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
    private readonly DynamicEntitySchemaMigrationService _schemaMigrationService;
    private readonly DynamicEntityFormValidationService _formValidationService;
    private readonly CommandErrorHttpMapper _commandErrorHttpMapper;
    private readonly ProjectScope _projectScope;
    private readonly IFileUploadService _fileUploadService;
    private readonly IProjectActionRegistry _actionRegistry;
    private readonly IDbConnection _db;
    private readonly IPdfExportService _pdfExport;
    private readonly IDocumentPdfService _docPdf;
    private readonly Microsoft.AspNetCore.Hosting.IWebHostEnvironment _env;
    private readonly IEntityHooksService _entityHooks;
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
        DynamicEntitySchemaMigrationService schemaMigrationService,
        DynamicEntityFormValidationService formValidationService,
        CommandErrorHttpMapper commandErrorHttpMapper,
        ProjectScope projectScope,
        IFileUploadService fileUploadService,
        IProjectActionRegistry actionRegistry,
        IDbConnection db,
        IPdfExportService pdfExport,
        IDocumentPdfService docPdf,
        Microsoft.AspNetCore.Hosting.IWebHostEnvironment env,
        IEntityHooksService entityHooks,
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
        _schemaMigrationService = schemaMigrationService;
        _formValidationService = formValidationService;
        _commandErrorHttpMapper = commandErrorHttpMapper;
        _projectScope = projectScope;
        _fileUploadService = fileUploadService;
        _actionRegistry = actionRegistry;
        _db = db;
        _pdfExport = pdfExport;
        _docPdf = docPdf;
        _env = env;
        _entityHooks = entityHooks;
        _logger = logger;
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

public record ActionFormViewModel(
    string Entity,
    string Action,
    ActionDefinition ActionDef,
    string? RecordId);

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

public record DynamicDetailViewModel(
    string Entity,
    EntityDefinition Meta,
    dynamic? Item,
    Dictionary<string, IEnumerable<dynamic>> ForeignKeyData,
    IReadOnlyList<BreadcrumbItem>? BreadcrumbChain = null,
    string? ReturnUrl = null);

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

public record SchemaMigrationViewModel(
    string ProjectName,
    string Entity,
    IReadOnlyList<string> Entities,
    EntityDefinition Meta,
    IReadOnlyList<ColumnSchemaInfo> PhysicalColumns,
    MigrationPlan Plan,
    IReadOnlyList<string> UpSql,
    IReadOnlyList<string> DownSql,
    string BackupTableName,
    IReadOnlyList<MigrationRecord> History);
