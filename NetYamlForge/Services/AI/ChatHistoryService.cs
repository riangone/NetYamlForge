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
    CreatedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_aichat_user ON AIChatHistory(UserId, Id);");
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
SELECT Id, UserId, Content, Type, CreatedAt
FROM AIChatHistory
WHERE UserId = @UserId
ORDER BY Id DESC
LIMIT @Limit", new { UserId = userId, Limit = limit });
        return rows.Reverse(); // 時系列順に戻す
    }

    /// <summary>メッセージを保存します。サーバー側の記録は削除しません。</summary>
    public async Task<long> SaveMessageAsync(string userId, string content, string type)
    {
        await using var conn = new SqliteConnection(_connectionString);
        var id = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO AIChatHistory (UserId, Content, Type, CreatedAt)
VALUES (@UserId, @Content, @Type, @CreatedAt);
SELECT last_insert_rowid();",
            new
            {
                UserId = userId,
                Content = content,
                Type = type,
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
}
