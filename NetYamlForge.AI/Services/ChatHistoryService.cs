// DCS003 抑制理由：ChatHistory はプロジェクトごとに異なる DB を使用するため
// ProjectScope とは独立した接続を直接生成します。
#pragma warning disable DCS003

using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models;

namespace NetYamlForge.AI.Services;

/// <summary>
/// AI チャット履歴を SQLite に保存するサービス。
/// 全局 AI は system.db、子プロジェクトは projects/&lt;name&gt;/chat.db を使用。
/// 各プロジェクトの聊天记录は完全に隔離されます。
/// </summary>
public class ChatHistoryService
{
    private readonly string _globalConnectionString;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ChatHistoryService> _logger;
    private const int MaxMessagesPerUser = 200;

    public ChatHistoryService(IConfiguration config, IWebHostEnvironment env, ILogger<ChatHistoryService> logger)
    {
        var globalDbPath = config["ChatHistory:DbPath"] ?? "system.db";
        _globalConnectionString = $"Data Source={globalDbPath}";
        _env = env;
        _logger = logger;
        
        // 全局数据库初始化
        InitializeSchema(_globalConnectionString, "framework");
    }

    /// <summary>
    /// プロジェクト別のデータベース接続文字列を取得
    /// </summary>
    /// <param name="projectName">プロジェクト名。null の場合は全局 DB</param>
    /// <returns>SQLite 接続文字列</returns>
    private string GetConnectionString(string? projectName)
    {
        if (string.IsNullOrEmpty(projectName))
        {
            return _globalConnectionString;
        }

        // プロジェクトディレクトリ内の chat.db を使用
        var projectChatDbPath = Path.Combine(_env.ContentRootPath, "projects", projectName, "chat.db");
        
        // ディレクトリが存在することを確認
        var projectDir = Path.GetDirectoryName(projectChatDbPath);
        if (!string.IsNullOrEmpty(projectDir) && !Directory.Exists(projectDir))
        {
            Directory.CreateDirectory(projectDir);
        }

        var projectConnectionString = $"Data Source={projectChatDbPath}";
        
        // プロジェクト DB のスキーマを初期化
        InitializeSchema(projectConnectionString, projectName);
        
        return projectConnectionString;
    }

    private void InitializeSchema(string connectionString, string contextName)
    {
        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();
            // テーブル作成（新規 DB 向け）とベースインデックス
            conn.Execute(@"
CREATE TABLE IF NOT EXISTS AIChatHistory (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId      TEXT NOT NULL,
    Content     TEXT NOT NULL,
    Type        TEXT NOT NULL,
    Provider    TEXT,
    ChatContext TEXT NOT NULL DEFAULT 'framework',
    CreatedAt   TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_aichat_user ON AIChatHistory(UserId, Id);");

            // マイグレーション：カラムが存在しない場合は追加（既存 DB 向け）
            var columns = conn.Query<dynamic>("PRAGMA table_info(AIChatHistory)")
                .ToDictionary(c => ((string)c.name).ToLowerInvariant(), c => c);

            if (!columns.ContainsKey("provider"))
            {
                conn.Execute("ALTER TABLE AIChatHistory ADD COLUMN Provider TEXT");
                _logger.LogInformation("Migrated AIChatHistory ({Context}): added Provider column", contextName);
            }
            if (!columns.ContainsKey("chatcontext"))
            {
                conn.Execute("ALTER TABLE AIChatHistory ADD COLUMN ChatContext TEXT NOT NULL DEFAULT 'framework'");
                _logger.LogInformation("Migrated AIChatHistory ({Context}): added ChatContext column", contextName);
            }

            // ChatContext カラムが確実に存在してからコンテキスト用インデックスを作成
            conn.Execute("CREATE INDEX IF NOT EXISTS idx_aichat_context ON AIChatHistory(UserId, ChatContext, Id);");

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
    ErrorText  TEXT,
    DurationMs  INTEGER,
    CreatedAt   TEXT NOT NULL,
    CompletedAt TEXT
);
CREATE INDEX IF NOT EXISTS idx_aicommand_user ON AICommandLog(UserId, Id);
CREATE INDEX IF NOT EXISTS idx_aicommand_task ON AICommandLog(TaskId);");

            _logger.LogDebug("Initialized AIChatHistory schema for context: {Context}", contextName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize AIChatHistory schema for context: {Context}", contextName);
        }
    }

    /// <summary>ユーザーのチャット履歴を時系列順で取得します。</summary>
    /// <param name="projectName">プロジェクト名。null の場合は全局 DB</param>
    /// <param name="chatContext">絞り込むコンテキスト。null の場合は全件取得。</param>
    public async Task<IEnumerable<ChatMessage>> GetHistoryAsync(string userId, string? projectName = null, int limit = 100, string? chatContext = null)
    {
        var connString = GetConnectionString(projectName);
        await using var conn = new SqliteConnection(connString);
        
        // デフォルトのチャットコンテキストを設定
        var defaultContext = string.IsNullOrEmpty(projectName) ? "framework" : projectName;
        
        var sql = chatContext == null
            ? @"SELECT Id, UserId, Content, Type, CreatedAt, Provider, ChatContext
                FROM AIChatHistory WHERE UserId = @UserId ORDER BY Id DESC LIMIT @Limit"
            : @"SELECT Id, UserId, Content, Type, CreatedAt, Provider, ChatContext
                FROM AIChatHistory WHERE UserId = @UserId AND ChatContext = @ChatContext ORDER BY Id DESC LIMIT @Limit";
        
        var rows = await conn.QueryAsync<ChatMessage>(sql,
            new { UserId = userId, Limit = limit, ChatContext = chatContext ?? defaultContext });
        return rows.Reverse(); // 時系列順に戻す
    }

    /// <summary>メッセージを保存します。</summary>
    /// <param name="projectName">プロジェクト名。null の場合は全局 DB</param>
    /// <param name="chatContext">チャットのコンテキスト識別子（例：framework / dealer-staff / dealer-customer）。</param>
    public async Task<long> SaveMessageAsync(string userId, string content, string type, string? provider = null, string chatContext = "framework", string? projectName = null)
    {
        var connString = GetConnectionString(projectName);
        await using var conn = new SqliteConnection(connString);
        
        // デフォルトのチャットコンテキストを設定
        var defaultContext = string.IsNullOrEmpty(projectName) ? "framework" : projectName;
        var actualContext = string.IsNullOrEmpty(chatContext) ? defaultContext : chatContext;
        
        var id = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO AIChatHistory (UserId, Content, Type, Provider, ChatContext, CreatedAt)
VALUES (@UserId, @Content, @Type, @Provider, @ChatContext, @CreatedAt);
SELECT last_insert_rowid();",
            new
            {
                UserId = userId,
                Content = content,
                Type = type,
                Provider = provider,
                ChatContext = actualContext,
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

    // [已禁用] 禁止清空聊天记录 - 2026-04-03
    // /// <summary>ユーザーの履歴を削除します。chatContext を指定すると特定コンテキストのみ削除します。</summary>
    // /// <param name="projectName">プロジェクト名。null の場合は全局 DB</param>
    // public async Task ClearHistoryAsync(string userId, string? chatContext = null, string? projectName = null)
    // {
    //     var connString = GetConnectionString(projectName);
    //     await using var conn = new SqliteConnection(connString);
    //
    //     // デフォルトのチャットコンテキストを設定
    //     var defaultContext = string.IsNullOrEmpty(projectName) ? "framework" : projectName;
    //
    //     var sql = chatContext == null
    //         ? "DELETE FROM AIChatHistory WHERE UserId = @UserId"
    //         : "DELETE FROM AIChatHistory WHERE UserId = @UserId AND ChatContext = @ChatContext";
    //
    //     await conn.ExecuteAsync(sql, new { UserId = userId, ChatContext = chatContext ?? defaultContext });
    // }

    // ──────────────────────────────────────────────
    // AICommandLog（指令実行ログ）
    // ──────────────────────────────────────────────

    /// <summary>コマンド実行ログを新規作成します（タスク開始時に呼び出す）。</summary>
    /// <param name="projectName">プロジェクト名。null の場合は全局 DB</param>
    public async Task<long> CreateCommandLogAsync(
        string userId,
        string taskId,
        string cliTool,
        string inputText,
        string? projectName,
        string? sessionId)
    {
        var connString = GetConnectionString(projectName);
        await using var conn = new SqliteConnection(connString);
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
        await using var conn = new SqliteConnection(_globalConnectionString);
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
    /// <param name="projectName">プロジェクト名。null の場合は全局 DB</param>
    public async Task<IEnumerable<CommandLog>> GetCommandLogsAsync(string userId, string? projectName = null, int limit = 50)
    {
        var connString = GetConnectionString(projectName);
        await using var conn = new SqliteConnection(connString);
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
