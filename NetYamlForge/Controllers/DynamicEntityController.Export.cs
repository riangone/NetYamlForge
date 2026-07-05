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

public partial class DynamicEntityController : BaseProjectController
{
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
            return NotFound($"PDF template '{templateName}' not found in projects/<name>/pdf-templates/");

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
        var filename = BuildPdfFilename(template.FilenameTemplate, templateName, header);
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

    private static string BuildPdfFilename(
        string? template, string fallbackName,
        IDictionary<string, object?>? header = null)
    {
        if (string.IsNullOrWhiteSpace(template))
            return $"{fallbackName}_{DateTime.Now:yyyyMMdd}.pdf";

        var result = template
            .Replace("{date:yyyyMMdd}", DateTime.Now.ToString("yyyyMMdd"))
            .Replace("{date}", DateTime.Now.ToString("yyyyMMdd"));

        if (header != null)
        {
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\{(\w+)\}", m =>
            {
                var key = m.Groups[1].Value;
                return header.TryGetValue(key, out var val) && val != null
                    ? val.ToString()!
                    : m.Value;
            });
        }

        // ファイル名に使えない文字を除去
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            result = result.Replace(c.ToString(), "_");

        return result;
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
}
