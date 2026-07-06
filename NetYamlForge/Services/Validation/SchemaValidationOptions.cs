// ファイル概要: 起動時 / CLI での JSON Schema 検証の動作を制御するオプション。
// appsettings.json の "Forge:SchemaValidation" セクションにバインドします（R2-01）。

namespace NetYamlForge.Services.Validation;

/// <summary>スキーマ検証の失敗方針。</summary>
public enum SchemaValidationMode
{
    /// <summary>検証を行わない。</summary>
    Off,

    /// <summary>違反を警告ログに出すが起動は継続する（既定・現状互換）。</summary>
    Warn,

    /// <summary>違反をエラーとして扱う。FailFastOnStartup=true なら起動を中止する。</summary>
    Strict
}

/// <summary>
/// R2-01: プロジェクト YAML を JSON Schema で検証する際の設定。
/// 既定は現状互換の <see cref="SchemaValidationMode.Warn"/>。
/// </summary>
public sealed class SchemaValidationOptions
{
    /// <summary>appsettings.json のバインド先セクション名。</summary>
    public const string SectionName = "Forge:SchemaValidation";

    /// <summary>失敗方針（Off / Warn / Strict）。既定: Warn。</summary>
    public SchemaValidationMode Mode { get; set; } = SchemaValidationMode.Warn;

    /// <summary>Strict のとき、違反があれば起動を中止するか。既定: false（本番グレーアウト用）。</summary>
    public bool FailFastOnStartup { get; set; }

    /// <summary>
    /// 検証対象の glob（projects ルート相対）。既定は全 YAML。
    /// 注: ルートは「プロジェクト群を含むディレクトリ」（例: NetYamlForge/projects）。
    /// </summary>
    public string[] IncludeGlobs { get; set; } = { "**/*.yml", "**/*.yaml" };

    /// <summary>除外 glob。</summary>
    public string[] ExcludeGlobs { get; set; } = { "**/_disabled/**" };
}
