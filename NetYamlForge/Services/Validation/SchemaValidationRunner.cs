// ファイル概要: R2-01 のスキーマ検証オーケストレーター。
// projects ルート配下の YAML を glob で走査し、ファイル配置規約で対応スキーマを選び、
// 例外を投げずに全違反を集約して返す。Warn/Fail の判定はここでは行わない（呼び出し側の責務）。

using Json.Schema;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using NetYamlForge.Services;

namespace NetYamlForge.Services.Validation;

/// <summary>1 件のスキーマ違反。</summary>
/// <param name="FilePath">違反したファイルの絶対パス。</param>
/// <param name="Pointer">インスタンス内の位置（JSON Pointer）。</param>
/// <param name="Message">違反理由。</param>
/// <param name="SchemaName">適用したスキーマ種別名。</param>
public sealed record SchemaViolation(string FilePath, string Pointer, string Message, string SchemaName);

/// <summary>
/// R2-01: projects ルート配下の YAML をスキーマ検証し、全違反を集約するランナー。
/// 失敗方針（Warn/Strict）は適用せず、結果の収集のみを行う。
/// </summary>
public sealed class SchemaValidationRunner
{
    /// <summary>
    /// projects ルート配下の対象 YAML を全件検証し、違反を集約して返す。
    /// </summary>
    /// <param name="projectsRoot">プロジェクト群を含むディレクトリ（例: .../NetYamlForge/projects）。</param>
    /// <param name="opt">検証オプション（glob 等）。</param>
    public IReadOnlyList<SchemaViolation> ValidateAll(string projectsRoot, SchemaValidationOptions opt)
    {
        ArgumentNullException.ThrowIfNull(opt);
        var violations = new List<SchemaViolation>();

        if (string.IsNullOrWhiteSpace(projectsRoot) || !Directory.Exists(projectsRoot))
            return violations;

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(opt.IncludeGlobs ?? Array.Empty<string>());
        matcher.AddExcludePatterns(opt.ExcludeGlobs ?? Array.Empty<string>());

        var root = new DirectoryInfoWrapper(new DirectoryInfo(projectsRoot));
        var result = matcher.Execute(root);

        foreach (var match in result.Files)
        {
            var fullPath = Path.GetFullPath(Path.Combine(projectsRoot, match.Path));
            var kind = Classify(projectsRoot, fullPath);
            if (kind is null)
                continue; // jobs/ queries/ ai/ 等スキーマ非対象は素通り

            violations.AddRange(ValidateFile(fullPath, kind.Value));
        }

        return violations;
    }

    /// <summary>単一ファイルを指定種別で検証（読み込み/構文エラーも違反として表現）。</summary>
    public IReadOnlyList<SchemaViolation> ValidateFile(string filePath, YamlSchemaValidator.SchemaKind kind)
    {
        var schemaName = kind.ToString();

        string yaml;
        try
        {
            yaml = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            return new[] { new SchemaViolation(filePath, "/", $"ファイル読み込みエラー: {ex.Message}", schemaName) };
        }

        EvaluationResults result;
        try
        {
            result = YamlSchemaValidator.TryValidateYaml(kind, yaml);
        }
        catch (Exception ex)
        {
            // YAML 構文エラー等は 1 件の違反として表現する。
            return new[] { new SchemaViolation(filePath, "/", $"YAML 解析エラー: {ex.Message}", schemaName) };
        }

        if (result.IsValid)
            return Array.Empty<SchemaViolation>();

        return Flatten(result, filePath, schemaName);
    }

    /// <summary>ファイル配置規約から適用スキーマ種別を判定。対象外なら null。</summary>
    internal static YamlSchemaValidator.SchemaKind? Classify(string projectsRoot, string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        var relative = Path.GetRelativePath(projectsRoot, fullPath);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // ディレクトリ規約をファイル名より優先する。
        // これにより entities/ 配下の "project.yml"（= project という名のエンティティ定義）を
        // 誤ってプロジェクト マニフェスト（Project schema）扱いしない。
        if (segments.Any(s => s.Equals("entities", StringComparison.OrdinalIgnoreCase)))
            return YamlSchemaValidator.SchemaKind.Entity;

        // project.yaml / project.yml（プロジェクト直下のマニフェスト）
        if (fileName.Equals("project.yaml", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("project.yml", StringComparison.OrdinalIgnoreCase))
            return YamlSchemaValidator.SchemaKind.Project;

        // dashboard.yml / dashboard.yaml
        if (fileName.StartsWith("dashboard.", StringComparison.OrdinalIgnoreCase))
            return YamlSchemaValidator.SchemaKind.Dashboard;

        if (segments.Any(s => s.Equals("pages", StringComparison.OrdinalIgnoreCase)))
            return YamlSchemaValidator.SchemaKind.UiPage;

        return null;
    }

    private static List<SchemaViolation> Flatten(EvaluationResults result, string filePath, string schemaName)
    {
        var list = new List<SchemaViolation>();
        var details = result.Details ?? Enumerable.Empty<EvaluationResults>();
        foreach (var d in details)
        {
            if (d.IsValid || d.Errors is null)
                continue;
            foreach (var err in d.Errors)
                list.Add(new SchemaViolation(filePath, d.InstanceLocation.ToString(), $"{err.Key}: {err.Value}", schemaName));
        }

        // List 出力で Details が空でも無効なケースの保険。
        if (list.Count == 0 && !result.IsValid)
            list.Add(new SchemaViolation(filePath, result.InstanceLocation.ToString(), "スキーマ検証に失敗しました。", schemaName));

        return list;
    }
}
