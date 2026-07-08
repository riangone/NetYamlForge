// ファイル概要：バッチジョブの定義モデルです。YAML から読み込まれ、スケジューリングと実行設定を保持します。

using System.Text.Json.Serialization;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// バッチジョブの定義モデル
/// </summary>
public class BatchJobDefinition
{
    /// <summary>
    /// ジョブ ID（YAML ファイル名または明示的な ID）
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 表示名
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// スケジュール設定
    /// </summary>
    public JobSchedule Schedule { get; set; } = new();

    /// <summary>
    /// ジョブタイプ
    /// </summary>
    public string Type { get; set; } = "sql_to_csv";

    /// <summary>
    /// ジョブ設定
    /// </summary>
    public JobSettings Settings { get; set; } = new();

    /// <summary>
    /// 実行前フック
    /// </summary>
    public List<string>? BeforeRun { get; set; }

    /// <summary>
    /// 実行後フック
    /// </summary>
    public List<string>? AfterRun { get; set; }

    /// <summary>
    /// 失敗時の設定
    /// </summary>
    public JobFailurePolicy? OnFailure { get; set; }

    /// <summary>
    /// ジョブ挙動設定
    /// </summary>
    public JobBehavior Behavior { get; set; } = new();

    /// <summary>
    /// ジョブが有効か（デフォルト：true）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 説明
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 使用する AI プロバイダー（photo_annotator / ai_annotator ジョブで使用）
    /// </summary>
    public string? AiProvider { get; set; }

    /// <summary>
    /// 一度に処理するバッチサイズ
    /// </summary>
    public int BatchSize { get; set; } = 5;

    /// <summary>
    /// ai_annotator ジョブの設定（テーブル名・フィールドマッピング・プロンプトなど）
    /// </summary>
    public AnnotationJobConfig? AnnotationConfig { get; set; }

    /// <summary>
    /// ai_embedding_generator ジョブの設定
    /// </summary>
    public EmbeddingJobConfig? EmbeddingConfig { get; set; }

    /// <summary>
    /// biz_card_parser ジョブの設定
    /// </summary>
    public BizCardParseConfig? BizCardParseConfig { get; set; }
}

/// <summary>
/// Generic AI annotation job configuration.
/// Allows any project to run image/document annotation via YAML jobs.
/// </summary>
public class AnnotationJobConfig
{
    /// <summary>Entity table containing items to annotate.</summary>
    public string SourceTable { get; set; } = "photos";
    /// <summary>Primary key column of SourceTable.</summary>
    public string PrimaryKey { get; set; } = "photo_id";
    /// <summary>Column holding the file path to pass to the vision model.</summary>
    public string FilePathField { get; set; } = "file_path";
    /// <summary>Column tracking annotation status (pending/processing/done/failed).</summary>
    public string StatusField { get; set; } = "annotation_status";
    /// <summary>Queue table for deferred/batch annotation.</summary>
    public string QueueTable { get; set; } = "processing_queue";
    /// <summary>Table where embedding vectors are stored.</summary>
    public string EmbeddingTable { get; set; } = "photo_embeddings";
    /// <summary>Custom vision/annotation prompt. Null = use built-in default photo-analysis prompt.</summary>
    public string? AnnotationPrompt { get; set; }
    /// <summary>
    /// Mapping from annotation JSON field → target DB column.
    /// Null value means skip that field. Omit to use built-in photo schema defaults.
    /// </summary>
    public Dictionary<string, string?>? ResultFields { get; set; }
    /// <summary>Whether to generate embeddings inline after annotation.</summary>
    public bool AutoEmbed { get; set; } = true;
    /// <summary>Text fields to build embedding input from (field name → optional label prefix).</summary>
    public List<EmbeddingTextField>? EmbedTextFields { get; set; }
}

/// <summary>
/// Generic AI embedding generator job configuration.
/// </summary>
public class EmbeddingJobConfig
{
    /// <summary>Entity table to read annotated rows from.</summary>
    public string SourceTable { get; set; } = "photos";
    /// <summary>Primary key column of SourceTable.</summary>
    public string PrimaryKey { get; set; } = "photo_id";
    /// <summary>Status column that gates which rows get embedded.</summary>
    public string StatusField { get; set; } = "annotation_status";
    /// <summary>Only embed rows where StatusField equals this value.</summary>
    public string StatusValue { get; set; } = "done";
    /// <summary>Table where vectors are stored (must have photo_id, embedding, created_at columns).</summary>
    public string EmbeddingTable { get; set; } = "photo_embeddings";
    /// <summary>
    /// Source text columns and their optional label prefixes.
    /// Defaults to caption_short + caption_long + scene_type/subjects/activities (photo schema).
    /// </summary>
    public List<EmbeddingTextField>? TextFields { get; set; }
    /// <summary>If true, update StatusField to DoneStatusValue after embedding is written.</summary>
    public bool UpdateStatusAfterEmbed { get; set; } = false;
    /// <summary>Value to set StatusField to after embedding (e.g. "done"). Only used when UpdateStatusAfterEmbed=true.</summary>
    public string DoneStatusValue { get; set; } = "done";
}

/// <summary>
/// Biz-card import parse job configuration.
/// </summary>
public class BizCardParseConfig
{
    /// <summary>Table that queues uploaded card images (default: import_jobs).</summary>
    public string ImportTable { get; set; } = "import_jobs";
    /// <summary>Destination entity table for parsed contact info (default: business_cards).</summary>
    public string CardsTable { get; set; } = "business_cards";
    /// <summary>Table where embedding vectors are stored (default: card_embeddings).</summary>
    public string EmbeddingTable { get; set; } = "card_embeddings";
    /// <summary>Whether to generate embedding inline after parsing (default: true).</summary>
    public bool AutoEmbed { get; set; } = true;
}

/// <summary>A source column that contributes text to the embedding input.</summary>
public class EmbeddingTextField
{
    /// <summary>Column name in the source table.</summary>
    public string Field { get; set; } = "";
    /// <summary>Optional Chinese-label prefix (e.g. "场景" → "场景: outdoor").</summary>
    public string? Prefix { get; set; }
}

/// <summary>
/// ジョブスケジュール設定
/// </summary>
public class JobSchedule
{
    /// <summary>
    /// Cron 式（分 時 日 月 曜）
    /// </summary>
    public string? Cron { get; set; }

    /// <summary>
    /// タイムゾーン（デフォルト：UTC）
    /// </summary>
    public string Timezone { get; set; } = "UTC";

    /// <summary>
    /// 実行間隔（秒）- Cron を使わない場合
    /// </summary>
    public int? IntervalSeconds { get; set; }

    /// <summary>
    /// ジョブの有効期限（UTC）。指定時刻に達すると自動的に enabled:false へ切り替わり、
    /// スケジューラから除外される（YAML 本体は書き換えず .job-states.json オーバーライドで無効化）。
    /// null の場合は無期限。
    /// </summary>
    public DateTime? EndDate { get; set; }
}

/// <summary>
/// ジョブ設定
/// </summary>
public class JobSettings
{
    /// <summary>
    /// SQL ファイルパス（プロジェクトルートからの相対パス）
    /// </summary>
    public string? SqlFile { get; set; }

    /// <summary>
    /// 直接 SQL
    /// </summary>
    public string? SqlQuery { get; set; }

    /// <summary>
    /// 出力ファイルパス（プロジェクトルートからの相対パス）
    /// </summary>
    public string? OutputFile { get; set; }

    /// <summary>
    /// CSV ヘッダーを含めるか
    /// </summary>
    public bool IncludeHeader { get; set; } = true;

    /// <summary>
    /// CSV 区切り文字
    /// </summary>
    public char Delimiter { get; set; } = ',';

    /// <summary>
    /// 出力形式（csv, json, xml）
    /// </summary>
    public string OutputFormat { get; set; } = "csv";

    /// <summary>
    /// メール通知先（カンマ区切り）
    /// </summary>
    public string? NotifyEmails { get; set; }

    /// <summary>
    /// 受信メールを保存するテーブル名
    /// </summary>
    public string? TargetTable { get; set; }

    /// <summary>
    /// 受信後に既読にするか
    /// </summary>
    public bool AutoMarkRead { get; set; } = true;

    /// <summary>
    /// バッチサイズ（一度に取得するメール数）
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// カスタムパラメータ
    /// </summary>
    public Dictionary<string, string>? Params { get; set; }
}

/// <summary>
/// 失敗時のポリシー
/// </summary>
public class JobFailurePolicy
{
    /// <summary>
    /// リトライ回数
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// リトライ間隔（秒）
    /// </summary>
    public int RetryInterval { get; set; } = 60;

    /// <summary>
    /// 失敗時に通知するユーザー
    /// </summary>
    public List<string>? Notify { get; set; }

    /// <summary>
    /// 失敗時にログを記録するか
    /// </summary>
    public bool LogError { get; set; } = true;
}

/// <summary>
/// ジョブ実行結果
/// </summary>
public class BatchJobResult
{
    /// <summary>
    /// ジョブ ID
    /// </summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>
    /// 成功したか
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 步骤间传递的输入-输出上下文管道（Pipe Context）
    /// </summary>
    public BatchJobPipeContext PipeContext { get; set; } = new();

    /// <summary>
    /// 処理開始時刻
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// 処理終了時刻
    /// </summary>
    public DateTime EndedAt { get; set; }

    /// <summary>
    /// 処理時間（ミリ秒）
    /// </summary>
    public long DurationMs => (EndedAt - StartedAt).Milliseconds;

    /// <summary>
    /// 影響を受けた行数
    /// </summary>
    public int RowsAffected { get; set; }

    /// <summary>
    /// 出力ファイルパス
    /// </summary>
    public string? OutputFile { get; set; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// エラー詳細（スタックトレース）
    /// </summary>
    public string? ErrorDetail { get; set; }

    /// <summary>
    /// リトライ回数
    /// </summary>
    public int RetryCount { get; set; }
}

/// <summary>
/// ジョブ実行履歴
/// </summary>
public class BatchJobHistory
{
    /// <summary>
    /// ジョブ ID
    /// </summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>
    /// 実行時刻
    /// </summary>
    public DateTime ExecutedAt { get; set; }

    /// <summary>
    /// 結果
    /// </summary>
    public BatchJobResult Result { get; set; } = new();
}

/// <summary>
/// ジョブ挙動設定
/// </summary>
public class JobBehavior
{
    public string UpdateStatusOnStart { get; set; } = "scanning";
    public string UpdateStatusOnDone { get; set; } = "done";
    public string UpdateStatusOnError { get; set; } = "failed";
    public bool WriteExif { get; set; } = true;
    public bool DeduplicateByPath { get; set; } = true;
    public bool AutoEnqueueAnnotation { get; set; } = true;
}
