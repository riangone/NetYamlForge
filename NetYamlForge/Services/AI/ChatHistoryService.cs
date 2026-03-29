// DCS003 抑制理由: ChatHistory は全プロジェクト共通の system.db を使うため
// ProjectScope とは独立した接続を直接生成します。
#pragma warning disable DCS003

using Dapper;
using Microsoft.Data.Sqlite;
using NetYamlForge.Models.AI;

namespace NetYamlForge.Services.AI;

/// <summary>
/// AI チャット履歴をサーバー側 SQLite (system.db) に保存するサービス。
/// Singleton で登録し、起動時にテーブルを初期化します。
/// </summary>
public class ChatHistoryService
{
    private readonly string _connectionString;
    private readonly ILogger<ChatHistoryService> _logger;
    private const int MaxMessagesPerUser = 200;

    public ChatHistoryService(IConfiguration config, ILogger<ChatHistoryService> logger)
    {
        var dbPath = config["ChatHistory:DbPath"] ?? "system.db";
        _connectionString = $"Data Source={dbPath}";
        _logger = logger;
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            conn.Execute(@"
CREATE TABLE IF NOT EXISTS AIChatHistory (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId    TEXT NOT NULL,
    Content   TEXT NOT NULL,
    Type      TEXT NOT NULL,
    Provider  TEXT,
    CreatedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_aichat_user ON AIChatHistory(UserId, Id);");

            // マイグレーション: Provider カラムが存在しない場合は追加
            var hasProvider = conn.Query<dynamic>("PRAGMA table_info(AIChatHistory)")
                .Any(c => ((string)c.name).Equals("Provider", StringComparison.OrdinalIgnoreCase));
            if (!hasProvider)
            {
                conn.Execute("ALTER TABLE AIChatHistory ADD COLUMN Provider TEXT");
                _logger.LogInformation("Migrated AIChatHistory: added Provider column");
            }

            conn.Execute(@"

CREATE TABLE IF NOT EXISTS AICommandLog (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId      TEXT NOT NULL,
    TaskId      TEXT NOT NULL UNIQUE,
    CliTool     TEXT NOT NULL,
    InputText   TEXT NOT NULL,
    ProjectName TEXT,
    SessionId   TEXT,
    Status      TEXT NOT NULL DEFAULT 'Pending',
    ResultText  TEXT,
    ErrorText   TEXT,
    DurationMs  INTEGER,
    CreatedAt   TEXT NOT NULL,
    CompletedAt TEXT
);
CREATE INDEX IF NOT EXISTS idx_aicommand_user ON AICommandLog(UserId, Id);
CREATE INDEX IF NOT EXISTS idx_aicommand_task ON AICommandLog(TaskId);");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize AIChatHistory schema");
        }
    }

    /// <summary>ユーザーのチャット履歴を時系列順で取得します。</summary>
    public async Task<IEnumerable<ChatMessage>> GetHistoryAsync(string userId, int limit = 100)
    {
        await using var conn = new SqliteConnection(_connectionString);
        var rows = await conn.QueryAsync<ChatMessage>(@"
SELECT Id, UserId, Content, Type, CreatedAt, Provider
FROM AIChatHistory
WHERE UserId = @UserId
ORDER BY Id DESC
LIMIT @Limit", new { UserId = userId, Limit = limit });
        return rows.Reverse(); // 時系列順に戻す
    }

    /// <summary>メッセージを保存します。サーバー側の記録は削除しません。</summary>
    public async Task<long> SaveMessageAsync(string userId, string content, string type, string? provider = null)
    {
        await using var conn = new SqliteConnection(_connectionString);
        var id = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO AIChatHistory (UserId, Content, Type, Provider, CreatedAt)
VALUES (@UserId, @Content, @Type, @Provider, @CreatedAt);
SELECT last_insert_rowid();",
            new
            {
                UserId = userId,
                Content = content,
                Type = type,
                Provider = provider,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            });

        // 自動削除は無効化（記録は永続保存）
        // await conn.ExecuteAsync(@"
        // DELETE FROM AIChatHistory
        // WHERE UserId = @UserId AND Id NOT IN (
        //     SELECT Id FROM AIChatHistory WHERE UserId = @UserId ORDER BY Id DESC LIMIT @Max
        // )", new { UserId = userId, Max = MaxMessagesPerUser });

        return id;
    }

    /// <summary>ユーザーの全履歴を削除します。</summary>
    public async Task ClearHistoryAsync(string userId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "DELETE FROM AIChatHistory WHERE UserId = @UserId",
            new { UserId = userId });
    }

    // ──────────────────────────────────────────────
    // AICommandLog（指令実行ログ）
    // ──────────────────────────────────────────────

    /// <summary>コマンド実行ログを新規作成します（タスク開始時に呼び出す）。</summary>
    public async Task<long> CreateCommandLogAsync(
        string userId,
        string taskId,
        string cliTool,
        string inputText,
        string? projectName,
        string? sessionId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        var id = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO AICommandLog (UserId, TaskId, CliTool, InputText, ProjectName, SessionId, Status, CreatedAt)
VALUES (@UserId, @TaskId, @CliTool, @InputText, @ProjectName, @SessionId, 'Pending', @CreatedAt);
SELECT last_insert_rowid();",
            new
            {
                UserId = userId,
                TaskId = taskId,
                CliTool = cliTool,
                InputText = inputText,
                ProjectName = projectName,
                SessionId = sessionId,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            });
        return id;
    }

    /// <summary>コマンド実行ログを完了・失敗・キャンセル状態に更新します。</summary>
    public async Task UpdateCommandLogAsync(
        string taskId,
        string status,
        string? resultText,
        string? errorText,
        long durationMs)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(@"
UPDATE AICommandLog
SET Status      = @Status,
    ResultText  = @ResultText,
    ErrorText   = @ErrorText,
    DurationMs  = @DurationMs,
    CompletedAt = @CompletedAt
WHERE TaskId = @TaskId",
            new
            {
                TaskId = taskId,
                Status = status,
                ResultText = resultText,
                ErrorText = errorText,
                DurationMs = durationMs,
                CompletedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            });
    }

    /// <summary>ユーザーのコマンド実行ログ一覧を新しい順で取得します。</summary>
    public async Task<IEnumerable<CommandLog>> GetCommandLogsAsync(string userId, int limit = 50)
    {
        await using var conn = new SqliteConnection(_connectionString);
        return await conn.QueryAsync<CommandLog>(@"
SELECT Id, UserId, TaskId, CliTool, InputText, ProjectName, SessionId,
       Status, ResultText, ErrorText, DurationMs, CreatedAt, CompletedAt
FROM AICommandLog
WHERE UserId = @UserId
ORDER BY Id DESC
LIMIT @Limit",
            new { UserId = userId, Limit = limit });
    }
}
