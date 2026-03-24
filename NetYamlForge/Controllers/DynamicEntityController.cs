// ファイル概要: 動的エンティティの一覧・作成・編集・削除・部分更新を処理します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using NetYamlForge.Models;
using NetYamlForge.Services;
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
    private readonly IProjectActionRegistry _actionRegistry;
    private readonly IDbConnection _db;
    private readonly IPdfExportService _pdfExport;
    private readonly IDocumentPdfService _docPdf;
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
        IProjectActionRegistry actionRegistry,
        IDbConnection db,
        IPdfExportService pdfExport,
        IDocumentPdfService docPdf,
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
        _actionRegistry = actionRegistry;
        _db = db;
        _pdfExport = pdfExport;
        _docPdf = docPdf;
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

    public async Task<IActionResult> DetailPage(string entity, string? id = null, string? returnUrl = null)
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
        var vm = new DynamicDetailViewModel(entity, meta, item, fkData, breadcrumbs, returnUrl);
        return View("DetailPage", vm);
    }

    /// <summary>
    /// YAML テンプレート（pdf-templates/*.yaml）を使用して帳票 PDF を生成します。
    /// エンティティの pdfTemplate プロパティでテンプレート名を指定します。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> DocumentPdf(string entity, string? id = null)
    {
        entity = NormalizeSingleValue(entity) ?? "";

        var meta         = _meta.Get(entity);
        var templateName = meta.PdfTemplate;
        if (string.IsNullOrWhiteSpace(templateName))
            return NotFound("pdfTemplate is not configured for entity: " + entity);

        var keyValue = _keyResolver.ResolvePrimaryKeyValue(meta, id, Request.Query);
        var record   = await _repo.GetByIdAsync(entity, keyValue ?? "");
        if (record == null) return NotFound();

        var header = ((IDictionary<string, object>)record)
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

        var projectDir = _projectScope.Current?.ProjectDir
            ?? throw new InvalidOperationException("No active project scope");

        var template = _docPdf.LoadTemplate(projectDir, templateName);
        if (template == null)
            return NotFound($"PDF template '{templateName}.yaml' not found in project pdf-templates/");

        // テンプレートのデータソースクエリを実行
        var dataSources = new Dictionary<string, IList<IDictionary<string, object?>>>();
        foreach (var (sourceName, sourceConfig) in template.DataSources)
        {
            var dynParams = BuildQueryParams(sourceConfig.Query, header);
            var rows = await _db.QueryAsync<dynamic>(sourceConfig.Query, dynParams);
            dataSources[sourceName] = rows
                .Select(r => (IDictionary<string, object?>)
                    ((IDictionary<string, object>)r)
                    .ToDictionary(kv => kv.Key, kv => (object?)kv.Value))
                .ToList();
        }

        var bytes    = _docPdf.Generate(template, header, dataSources, projectDir);
        var filename = BuildPdfFilename(template.FilenameTemplate, templateName);
        return File(bytes, "application/pdf", filename);
    }

    private static Dapper.DynamicParameters BuildQueryParams(
        string query, IDictionary<string, object?> header)
    {
        var dynParams  = new Dapper.DynamicParameters();
        var paramNames = System.Text.RegularExpressions.Regex
            .Matches(query, @"@(\w+)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in paramNames)
        {
            header.TryGetValue(name, out var val);
            dynParams.Add(name, val);
        }
        return dynParams;
    }

    private static string BuildPdfFilename(string? template, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(template))
            return $"{fallbackName}_{DateTime.Now:yyyyMMdd}.pdf";
        return template.Replace("{date:yyyyMMdd}", DateTime.Now.ToString("yyyyMMdd"))
                       .Replace("{date}", DateTime.Now.ToString("yyyyMMdd"));
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

    /// <summary>
    /// エンティティの全レコードを CSV ファイルとしてダウンロードします。
    /// 現在の検索・フィルタ条件を引き継ぎます。
    /// </summary>
    public async Task<IActionResult> ExportCsv(
        string entity,
        string? search = null,
        string? sort = null,
        string? dir = null)
    {
        entity = NormalizeSingleValue(entity) ?? "";
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
            return accessDenied;

        // 現在の検索・フィルタ条件を取得
        var filters = FilterValueParser.Build(meta, Request.Query);

        // 全件取得（最大 10 万件）
        var items = await _repo.GetAllAsync(entity, search, sort, dir, filters, page: 1, pageSize: 100000);

        // CSV 生成
        var displayColumns = meta.Columns.Where(c => !c.Value.Hidden).ToList();
        var sb = new System.Text.StringBuilder();

        // ヘッダー行
        sb.AppendLine(string.Join(",", displayColumns.Select(c => CsvEscape(c.Value.GetLabel(c.Key)))));

        // データ行
        foreach (var item in items)
        {
            var row = displayColumns.Select(c =>
            {
                object? value = null;
                try { value = ((IDictionary<string, object>)item)[c.Key]; } catch { }
                var formatted = ColumnValueFormatter.FormatValue(c.Value.Type, value, c.Value.OptionLabels);
                return CsvEscape(formatted);
            });
            sb.AppendLine(string.Join(",", row));
        }

        var fileName = $"{entity}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString()))
            .ToArray();
        return File(bytes, "text/csv", fileName);
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    /// <summary>
    /// YAML の exports セクションで定義したカスタムエクスポートをダウンロードします。
    /// format が csv/tsv の場合は区切りテキスト、json の場合は JSON 配列を返します。
    /// sqlQuery / sqlFile が指定された場合はカスタム SQL を実行し、
    /// 省略された場合は現在のフィルタ条件を引き継いだエンティティクエリを使用します。
    /// </summary>
    public async Task<IActionResult> ExportCustom(
        string entity,
        string exportKey,
        string? search = null,
        string? sort = null,
        string? dir = null)
    {
        entity = NormalizeSingleValue(entity) ?? "";
        exportKey = NormalizeSingleValue(exportKey) ?? "";

        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null) return accessDenied;

        if (!meta.Exports.TryGetValue(exportKey, out var exportDef))
            return NotFound();

        bool useCustomSql = !string.IsNullOrWhiteSpace(exportDef.SqlQuery)
                         || !string.IsNullOrWhiteSpace(exportDef.SqlFile);

        IEnumerable<dynamic> rawItems;
        if (useCustomSql)
        {
            var sql = exportDef.SqlQuery ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(exportDef.SqlFile))
            {
                var sqlPath = Path.Combine(_projectScope.Current!.ProjectDir, exportDef.SqlFile);
                sql = await System.IO.File.ReadAllTextAsync(sqlPath);
            }
            rawItems = await _db.QueryAsync(sql);
        }
        else
        {
            var filters = FilterValueParser.Build(meta, Request.Query);
            rawItems = await _repo.GetAllAsync(entity, search, sort, dir, filters, page: 1, pageSize: 100000);
        }

        var itemList = rawItems.Select(r => (IDictionary<string, object>)r).ToList();

        // 出力列を決定する
        List<(string Key, string Label)> columns;
        if (exportDef.Columns is { Count: > 0 })
        {
            columns = exportDef.Columns.Select(k =>
            {
                var label = meta.Columns.TryGetValue(k, out var col) ? col.GetLabel(k) : k;
                return (k, label);
            }).ToList();
        }
        else if (useCustomSql && itemList.Count > 0)
        {
            // カスタム SQL の場合は結果の全列をそのまま使用する
            columns = itemList[0].Keys.Select(k => (k, k)).ToList();
        }
        else
        {
            columns = meta.Columns
                .Where(c => !c.Value.Hidden)
                .Select(c => (c.Key, c.Value.GetLabel(c.Key)))
                .ToList();
        }

        var format = (exportDef.Format ?? "csv").ToLowerInvariant();
        var ext = format == "pdf" ? "pdf" : format == "json" ? "json" : format == "tsv" ? "tsv" : "csv";
        var defaultPattern = $"{entity}_{exportKey}_{{date:yyyyMMdd_HHmmss}}.{ext}";
        var filename = ResolveExportFilename(exportDef.Filename ?? defaultPattern);

        return format switch
        {
            "pdf"  => BuildPdfExport(itemList, meta, columns, exportDef.Pdf ?? new(), filename),
            "json" => BuildJsonExport(itemList, columns, filename),
            "tsv"  => BuildDelimitedExport(itemList, meta, columns, '\t', "text/tab-separated-values", filename),
            _      => BuildDelimitedExport(itemList, meta, columns, ',', "text/csv", filename),
        };
    }

    /// <summary>ファイル名パターン内の {date:format} プレースホルダーを現在日時に置換します。</summary>
    private static string ResolveExportFilename(string pattern)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            pattern,
            @"\{date:([^}]+)\}",
            m => DateTime.Now.ToString(m.Groups[1].Value));
    }

    /// <summary>CSV または TSV 形式のレスポンスを生成します。</summary>
    private IActionResult BuildDelimitedExport(
        List<IDictionary<string, object>> items,
        EntityDefinition meta,
        List<(string Key, string Label)> columns,
        char delimiter,
        string contentType,
        string filename)
    {
        string EscapeCell(string value)
        {
            if (value.Contains(delimiter) || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(delimiter, columns.Select(c => EscapeCell(c.Label))));

        foreach (var item in items)
        {
            var row = columns.Select(c =>
            {
                item.TryGetValue(c.Key, out var raw);
                var colDef = meta.Columns.GetValueOrDefault(c.Key);
                var formatted = colDef != null
                    ? ColumnValueFormatter.FormatValue(colDef.Type, raw, colDef.OptionLabels)
                    : raw?.ToString() ?? string.Empty;
                return EscapeCell(formatted);
            });
            sb.AppendLine(string.Join(delimiter, row));
        }

        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(sb.ToString()))
            .ToArray();
        return File(bytes, contentType, filename);
    }

    /// <summary>JSON 配列形式のレスポンスを生成します。</summary>
    private IActionResult BuildJsonExport(
        List<IDictionary<string, object>> items,
        List<(string Key, string Label)> columns,
        string filename)
    {
        var rows = items.Select(item =>
        {
            var dict = new Dictionary<string, object?>();
            foreach (var (key, _) in columns)
            {
                item.TryGetValue(key, out var val);
                dict[key] = val;
            }
            return dict;
        }).ToList();

        var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
        var bytes = Encoding.UTF8.GetBytes(json);
        return File(bytes, "application/json", filename);
    }

    /// <summary>PDF 形式のレスポンスを生成します。</summary>
    private IActionResult BuildPdfExport(
        List<IDictionary<string, object>> items,
        EntityDefinition meta,
        List<(string Key, string Label)> columns,
        NetYamlForge.Models.PdfExportOptions options,
        string filename)
    {
        var projectDir = _projectScope.Current?.ProjectDir;
        var bytes = _pdfExport.Generate(items, columns, meta, options, projectDir);
        return File(bytes, "application/pdf", filename);
    }

    /// <summary>
    /// アクションの file 型 inputs に対応するアップロードファイルを一時ディレクトリに保存します。
    /// 成功した場合は (inputName → 一時ファイル絶対パス) の辞書を返します。
    /// バリデーションエラーがある場合はエラーメッセージを返します。
    /// </summary>
    private static async Task<(Dictionary<string, string> Files, string? Error)> SaveActionUploadedFilesAsync(
        NetYamlForge.Models.ActionDefinition actionDef,
        IFormFileCollection formFiles)
    {
        var saved = new Dictionary<string, string>();
        if (actionDef.Inputs == null) return (saved, null);

        foreach (var input in actionDef.Inputs.Where(i => i.Type == "file"))
        {
            var formFile = formFiles.GetFile(input.Name);
            if (formFile == null || formFile.Length == 0)
            {
                if (input.Required)
                    return (saved, $"'{input.Label ?? input.Name}' は必須です。");
                continue;
            }

            // サイズ検証
            var maxBytes = input.MaxSizeBytes ?? 10L * 1024 * 1024;
            if (formFile.Length > maxBytes)
                return (saved, $"'{input.Label ?? input.Name}' のファイルサイズが上限（{maxBytes / 1024 / 1024} MB）を超えています。");

            // 拡張子検証
            if (!string.IsNullOrWhiteSpace(input.AllowedExtensions))
            {
                var allowed = input.AllowedExtensions
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(e => e.TrimStart('.').ToLowerInvariant())
                    .ToHashSet();
                var ext = Path.GetExtension(formFile.FileName).TrimStart('.').ToLowerInvariant();
                if (!allowed.Contains(ext))
                    return (saved, $"'{input.Label ?? input.Name}' の拡張子 .{ext} は許可されていません（許可: {input.AllowedExtensions}）。");
            }

            // 一時ファイルに保存
            var tempDir = Path.Combine(Path.GetTempPath(), "netyamlforge_uploads");
            Directory.CreateDirectory(tempDir);
            var safeFilename = $"{Guid.NewGuid():N}_{Path.GetFileName(formFile.FileName)}";
            var tempPath = Path.Combine(tempDir, safeFilename);

            await using var fs = System.IO.File.Create(tempPath);
            await formFile.CopyToAsync(fs);

            saved[input.Name] = tempPath;
        }

        return (saved, null);
    }

    /// <summary>アクション実行後に一時ファイルを削除します。</summary>
    private void CleanupTempFiles(Dictionary<string, string> files)
    {
        foreach (var path in files.Values)
        {
            try { System.IO.File.Delete(path); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "一時ファイルの削除に失敗しました: {Path}", path);
            }
        }
    }

    /// <summary>
    /// ヘッダーアクション入力フォームをモーダル用パーシャルとして返します。
    /// </summary>
    public IActionResult HeaderActionForm(string entity, string actionKey)
    {
        entity = NormalizeSingleValue(entity) ?? "";
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
            return accessDenied;

        if (!meta.Actions.TryGetValue(actionKey, out var actionDef))
            return NotFound($"アクション '{actionKey}' が見つかりません。");

        return PartialView("_ActionForm", new ActionFormViewModel(entity, actionKey, actionDef, null));
    }

    /// <summary>
    /// ヘッダーアクション（行に依存しない操作）を実行し、一覧パーシャルを返します。
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> InvokeHeaderAction(
        string entity,
        string actionKey,
        [FromForm] Dictionary<string, string?> form = null!,
        [FromForm] string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "";
        form ??= new Dictionary<string, string?>();

        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
            return accessDenied;

        if (!meta.Actions.TryGetValue(actionKey, out var actionDef))
            return NotFound($"アクション '{actionKey}' が見つかりません。");

        var projectName = _projectScope.Current?.Name ?? "";
        var handlerName = string.IsNullOrWhiteSpace(actionDef.Handler) ? actionKey : actionDef.Handler;
        var handler = _actionRegistry.Find(projectName, handlerName);
        if (handler == null)
            return BadRequest($"アクションハンドラー '{handlerName}' が見つかりません。");

        var inputs = form
            .Where(kv => kv.Key != "__RequestVerificationToken")
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

        // ファイル入力を一時領域に保存する
        var (savedFiles, fileError) = await SaveActionUploadedFilesAsync(actionDef, Request.Form.Files);
        if (fileError != null)
            return BadRequest(fileError);

        var ctx = new CustomActionContext
        {
            Project = projectName,
            Entity = entity,
            Action = actionKey,
            RecordId = null,
            Inputs = inputs,
            Files = savedFiles,
            UserName = User.Identity?.Name
        };

        ActionHandlerResult result;
        try
        {
            result = await _commandService.ExecuteActionAsync(handler, ctx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ヘッダーアクション '{Action}' の実行中にエラーが発生しました", actionKey);
            return StatusCode(500, "アクションの実行中にエラーが発生しました。");
        }
        finally
        {
            CleanupTempFiles(savedFiles);
        }

        if (!result.Ok)
            return BadRequest(result.ErrorMessage ?? "アクションが失敗しました。");

        var (items, total) = await _listResponseService.LoadFirstPageAfterMutationAsync(entity);
        _listHttpResponseService.SetEntityFormSavedHeaders(Response);
        return PartialView("_List",
            CreateListViewModel(entity, meta, items, null, null, null, new(), 1, total, null, 5, true, false, null, null, returnUrl));
    }

    /// <summary>
    /// 複数レコードに対して同一アクションを一括実行します。
    /// 各 ID ごとにハンドラーを呼び出し、すべて成功した場合のみコミットします。
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> InvokeBulkAction(
        string entity,
        string actionKey,
        [FromForm] string[] ids,
        [FromForm] string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "";

        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
            return accessDenied;

        if (!meta.Actions.TryGetValue(actionKey, out var actionDef))
            return NotFound($"アクション '{actionKey}' が見つかりません。");

        if (ids == null || ids.Length == 0)
            return BadRequest("操作するレコードが指定されていません。");

        var projectName = _projectScope.Current?.Name ?? "";
        var handlerName = string.IsNullOrWhiteSpace(actionDef.Handler) ? actionKey : actionDef.Handler;
        var handler = _actionRegistry.Find(projectName, handlerName);
        if (handler == null)
            return BadRequest($"アクションハンドラー '{handlerName}' が見つかりません。");

        var errors = new List<string>();
        foreach (var id in ids)
        {
            var ctx = new CustomActionContext
            {
                Project = projectName,
                Entity = entity,
                Action = actionKey,
                RecordId = id,
                Inputs = new Dictionary<string, object?>(),
                UserName = User.Identity?.Name
            };

            try
            {
                var result = await _commandService.ExecuteActionAsync(handler, ctx);
                if (!result.Ok)
                    errors.Add($"ID={id}: {result.ErrorMessage}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "一括アクション '{Action}' ID={Id} の実行中にエラーが発生しました", actionKey, id);
                errors.Add($"ID={id}: 実行エラー");
            }
        }

        if (errors.Count > 0)
            _logger.LogWarning("一括アクション '{Action}' の一部が失敗しました: {Errors}", actionKey, string.Join("; ", errors));

        var (items, total) = await _listResponseService.LoadFirstPageAfterMutationAsync(entity);
        _listHttpResponseService.SetEntityFormSavedHeaders(Response);
        return PartialView("_List",
            CreateListViewModel(entity, meta, items, null, null, null, new(), 1, total, null, 5, true, false, null, null, returnUrl));
    }

    /// <summary>
    /// カスタムアクション入力フォームをモーダル用パーシャルとして返します。
    /// inputs が空のアクションは確認ダイアログを表示せずそのまま InvokeAction を呼びます。
    /// </summary>
    public IActionResult ActionForm(string entity, string actionKey, string? id = null)
    {
        entity = NormalizeSingleValue(entity) ?? "";
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
            return accessDenied;

        if (!meta.Actions.TryGetValue(actionKey, out var actionDef))
            return NotFound($"アクション '{actionKey}' が見つかりません。");

        var keyValue = _keyResolver.ResolvePrimaryKeyValue(meta, id, Request.Query);
        return PartialView("_ActionForm", new ActionFormViewModel(entity, actionKey, actionDef, keyValue));
    }

    /// <summary>
    /// カスタムアクションを実行し、一覧パーシャルを返します。
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> InvokeAction(
        string entity,
        string actionKey,
        string? id = null,
        [FromForm] Dictionary<string, string?> form = null!,
        [FromForm] string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "";
        form ??= new Dictionary<string, string?>();

        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
            return accessDenied;

        if (!meta.Actions.TryGetValue(actionKey, out var actionDef))
            return NotFound($"アクション '{actionKey}' が見つかりません。");

        var keyValue = _keyResolver.ResolvePrimaryKeyValue(meta, id, Request.Query);
        var projectName = _projectScope.Current?.Name ?? "";

        // ハンドラー名を解決（省略時はアクションキー名）
        var handlerName = string.IsNullOrWhiteSpace(actionDef.Handler) ? actionKey : actionDef.Handler;
        var handler = _actionRegistry.Find(projectName, handlerName);
        if (handler == null)
        {
            _logger.LogWarning(
                "プロジェクト '{Project}' のアクションハンドラー '{Handler}' が見つかりません",
                projectName, handlerName);
            return BadRequest($"アクションハンドラー '{handlerName}' が見つかりません。");
        }

        // 入力値を object? 辞書に変換
        var inputs = form
            .Where(kv => kv.Key != "__RequestVerificationToken")
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

        // ファイル入力を一時領域に保存する
        var (savedFiles, fileError) = await SaveActionUploadedFilesAsync(actionDef, Request.Form.Files);
        if (fileError != null)
            return BadRequest(fileError);

        var ctx = new NetYamlForge.Services.Hooks.CustomActionContext
        {
            Project = projectName,
            Entity = entity,
            Action = actionKey,
            RecordId = keyValue,
            Inputs = inputs,
            Files = savedFiles,
            UserName = User.Identity?.Name
        };

        // before フックを実行（IEntityHook として）
        var beforeHooks = actionDef.Hooks?.Before;
        if (beforeHooks != null && beforeHooks.Count > 0)
        {
            var hookCtx = new NetYamlForge.Services.Hooks.EntityHookContext
            {
                Entity = entity,
                Operation = NetYamlForge.Services.Hooks.CrudOperation.CustomAction,
                Id = int.TryParse(keyValue, out var intId) ? intId : null,
                Values = inputs,
                UserName = User.Identity?.Name
            };
            var beforeResult = await _commandService.RunBeforeHooksForActionAsync(beforeHooks, hookCtx);
            if (beforeResult.Cancel)
                return BadRequest(beforeResult.CancelMessage ?? "前処理によりキャンセルされました。");
        }

        // ハンドラー実行（トランザクション付き）
        ActionHandlerResult result;
        try
        {
            result = await _commandService.ExecuteActionAsync(handler, ctx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アクション '{Action}' の実行中にエラーが発生しました", actionKey);
            return StatusCode(500, "アクションの実行中にエラーが発生しました。");
        }
        finally
        {
            CleanupTempFiles(ctx.Files);
        }

        if (!result.Ok)
            return BadRequest(result.ErrorMessage ?? "アクションが失敗しました。");

        var (items, total) = await _listResponseService.LoadFirstPageAfterMutationAsync(entity);
        _listHttpResponseService.SetEntityFormSavedHeaders(Response);
        return PartialView("_List",
            CreateListViewModel(entity, meta, items, null, null, null, new(), 1, total, null, 5, true, false, null, null, returnUrl));
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
