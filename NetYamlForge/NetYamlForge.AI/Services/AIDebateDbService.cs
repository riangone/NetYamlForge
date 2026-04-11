using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models.Debate;

namespace NetYamlForge.AI.Services;

/// <summary>
/// AI ディベート専用の SQLite データアクセスサービス
/// </summary>
public class AIDebateDbService
{
    private readonly string _connectionString;
    private readonly ILogger<AIDebateDbService> _logger;

    public AIDebateDbService(IWebHostEnvironment env, ILogger<AIDebateDbService> logger)
    {
        var dir = Path.Combine(env.ContentRootPath, "projects", "ai-debate", "database");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "ai-debate.db");
        _connectionString = $"Data Source={dbPath}";
        _logger = logger;
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        // DCS003 警告: このサービスはプロジェクトスコープ外のバックグラウンドサービスのため、直接接続を使用します
#pragma warning disable DCS003
        using var conn = new SqliteConnection(_connectionString);
#pragma warning restore DCS003
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS debate_topics (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    description TEXT,
    left_ai TEXT NOT NULL DEFAULT 'claude',
    right_ai TEXT NOT NULL DEFAULT 'qwen',
    left_role TEXT,
    right_role TEXT,
    status TEXT NOT NULL DEFAULT 'pending',
    left_count INTEGER NOT NULL DEFAULT 0,
    right_count INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
);
CREATE TABLE IF NOT EXISTS debate_messages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    topic_id INTEGER NOT NULL,
    ai_side TEXT NOT NULL,
    ai_provider TEXT NOT NULL,
    content TEXT NOT NULL,
    message_index INTEGER NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (topic_id) REFERENCES debate_topics(id)
);";
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        // DCS003: バックグラウンドサービスのため直接接続を使用
#pragma warning disable DCS003
        var conn = new SqliteConnection(_connectionString);
#pragma warning restore DCS003
        conn.Open();
        return conn;
    }

    public async Task<int> CreateTopicAsync(CreateTopicRequest req)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO debate_topics (title, description, left_ai, right_ai, left_role, right_role)
VALUES (@title, @desc, @leftAi, @rightAi, @leftRole, @rightRole);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@title", req.Title);
        cmd.Parameters.AddWithValue("@desc", (object?)req.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@leftAi", req.LeftAi);
        cmd.Parameters.AddWithValue("@rightAi", req.RightAi);
        cmd.Parameters.AddWithValue("@leftRole", (object?)req.LeftRole ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rightRole", (object?)req.RightRole ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<List<DebateTopic>> GetTopicsAsync()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM debate_topics ORDER BY created_at DESC LIMIT 50";
        var list = new List<DebateTopic>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(MapTopic(reader));
        return list;
    }

    public async Task<DebateTopic?> GetTopicAsync(int id)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM debate_topics WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapTopic(reader) : null;
    }

    public async Task UpdateTopicStatusAsync(int id, string status, int leftCount, int rightCount)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE debate_topics SET status = @status, left_count = @lc, right_count = @rc WHERE id = @id";
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@lc", leftCount);
        cmd.Parameters.AddWithValue("@rc", rightCount);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<DebateMessage> SaveMessageAsync(int topicId, string aiSide, string aiProvider, string content, int messageIndex)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO debate_messages (topic_id, ai_side, ai_provider, content, message_index)
VALUES (@topicId, @side, @provider, @content, @idx);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@topicId", topicId);
        cmd.Parameters.AddWithValue("@side", aiSide);
        cmd.Parameters.AddWithValue("@provider", aiProvider);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@idx", messageIndex);
        var newId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return new DebateMessage
        {
            Id = newId,
            TopicId = topicId,
            AiSide = aiSide,
            AiProvider = aiProvider,
            Content = content,
            MessageIndex = messageIndex,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    public async Task<List<DebateMessage>> GetMessagesAsync(int topicId)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM debate_messages WHERE topic_id = @id ORDER BY id ASC";
        cmd.Parameters.AddWithValue("@id", topicId);
        var list = new List<DebateMessage>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(MapMessage(reader));
        return list;
    }

    private static DebateTopic MapTopic(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("id")),
        Title = r.GetString(r.GetOrdinal("title")),
        Description = r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
        LeftAi = r.GetString(r.GetOrdinal("left_ai")),
        RightAi = r.GetString(r.GetOrdinal("right_ai")),
        LeftRole = r.IsDBNull(r.GetOrdinal("left_role")) ? null : r.GetString(r.GetOrdinal("left_role")),
        RightRole = r.IsDBNull(r.GetOrdinal("right_role")) ? null : r.GetString(r.GetOrdinal("right_role")),
        Status = r.GetString(r.GetOrdinal("status")),
        LeftCount = r.GetInt32(r.GetOrdinal("left_count")),
        RightCount = r.GetInt32(r.GetOrdinal("right_count")),
        CreatedAt = r.GetString(r.GetOrdinal("created_at"))
    };

    private static DebateMessage MapMessage(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("id")),
        TopicId = r.GetInt32(r.GetOrdinal("topic_id")),
        AiSide = r.GetString(r.GetOrdinal("ai_side")),
        AiProvider = r.GetString(r.GetOrdinal("ai_provider")),
        Content = r.GetString(r.GetOrdinal("content")),
        MessageIndex = r.GetInt32(r.GetOrdinal("message_index")),
        CreatedAt = r.GetString(r.GetOrdinal("created_at"))
    };
}
