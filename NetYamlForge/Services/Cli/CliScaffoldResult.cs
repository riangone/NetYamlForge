// ファイル概要: CLI スキャフォールドコマンドの実行結果を保持する共用レコード群。
// --json フラグ指定時にこのデータが JSON としてstdoutに出力され、CI ツールが解析できる。

using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetYamlForge.Services.Cli;

/// <summary>
/// CLI コマンド実行結果の共用データモデル。
/// --json フラグ時に JSON シリアライズされ stdout に出力されます。
/// </summary>
public sealed class CliScaffoldResult
{
    /// <summary>コマンド名 (scaffold-entities / scaffold-hook / init-project / upgrade-entity-yaml)</summary>
    [JsonPropertyName("command")]
    public string Command { get; init; } = "";

    /// <summary>成功なら true</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    /// <summary>プロセス終了コード (0=成功, 1=失敗)</summary>
    [JsonPropertyName("exitCode")]
    public int ExitCode { get; set; }

    /// <summary>対象プロジェクト名 (複数の場合はカンマ区切りまたは配列)</summary>
    [JsonPropertyName("project")]
    public string? Project { get; set; }

    /// <summary>生成したファイルの相対パス一覧</summary>
    [JsonPropertyName("generatedFiles")]
    public List<string> GeneratedFiles { get; } = new();

    /// <summary>スキップしたファイルの相対パス一覧（既存ファイルなど）</summary>
    [JsonPropertyName("skippedFiles")]
    public List<string> SkippedFiles { get; } = new();

    /// <summary>生成ファイル数の合計</summary>
    [JsonPropertyName("generatedCount")]
    public int GeneratedCount => GeneratedFiles.Count;

    /// <summary>スキップ数の合計</summary>
    [JsonPropertyName("skippedCount")]
    public int SkippedCount => SkippedFiles.Count;

    /// <summary>CI での次ステップやコマンド例のリスト</summary>
    [JsonPropertyName("nextSteps")]
    public List<string> NextSteps { get; } = new();

    /// <summary>エラーメッセージ一覧（Success=false の場合に設定）</summary>
    [JsonPropertyName("errors")]
    public List<string> Errors { get; } = new();

    /// <summary>その他メッセージ（info/warn）</summary>
    [JsonPropertyName("messages")]
    public List<string> Messages { get; } = new();

    // ── ファクトリ/ヘルパー ─────────────────────────────────────────────────

    public static CliScaffoldResult Failure(string command, string error) => new()
    {
        Command  = command,
        Success  = false,
        ExitCode = 1,
        Errors   = { error }
    };

    /// <summary>結果を JSON として stdout に書き出す</summary>
    public void WriteJson()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        Console.WriteLine(json);
    }
}
