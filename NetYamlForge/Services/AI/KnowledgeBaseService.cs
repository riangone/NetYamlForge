using System.Data;
using Dapper;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.BatchJob;

namespace NetYamlForge.Services.AI;

/// <summary>
/// ナレッジベースサービス
/// </summary>
public interface IKnowledgeBaseService
{
    /// <summary>
    /// インテントとエンティティに基づいてナレッジを検索
    /// </summary>
    Task<KnowledgeResult?> SearchAsync(string intent, Dictionary<string, string> entities, string? projectId = null);

    /// <summary>
    /// キーワードでナレッジを検索
    /// </summary>
    Task<List<KnowledgeResult>> SearchByKeywordAsync(string keyword, string? projectId = null);

    /// <summary>
    /// ナレッジ ID で取得
    /// </summary>
    Task<KnowledgeItem?> GetByIdAsync(string knowledgeId, string? projectId = null);

    /// <summary>
    /// ナレッジを作成
    /// </summary>
    Task<string> CreateAsync(KnowledgeCreateRequest request, string? projectId = null);

    /// <summary>
    /// ナレッジを更新
    /// </summary>
    Task<bool> UpdateAsync(string knowledgeId, KnowledgeUpdateRequest request, string? projectId = null);

    /// <summary>
    /// ナレッジを削除
    /// </summary>
    Task<bool> DeleteAsync(string knowledgeId, string? projectId = null);

    /// <summary>
    /// フィードバックを記録
    /// </summary>
    Task<bool> RecordFeedbackAsync(string knowledgeId, bool isHelpful, string? projectId = null);

    /// <summary>
    /// 使用回数をインクリメント
    /// </summary>
    Task IncrementUsageAsync(string knowledgeId, string? projectId = null);

    /// <summary>
    /// カテゴリ別ナレッジ一覧
    /// </summary>
    Task<List<KnowledgeItem>> GetByCategoryAsync(string category, string? projectId = null);

    /// <summary>
    /// 未回答質問を記録
    /// </summary>
    Task RecordUnansweredQuestionAsync(string question, string intent, string? projectId = null);
}

/// <summary>
/// ナレッジ検索結果
/// </summary>
public class KnowledgeResult
{
    public string KnowledgeId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<QuickReplyButton> QuickReplies { get; set; } = new();
    public double MatchScore { get; set; }
    public string Source { get; set; } = "knowledge";
}

/// <summary>
/// ナレッジアイテム
/// </summary>
public class KnowledgeItem
{
    public string KnowledgeId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string? AnswerHtml { get; set; }
    public string? Keywords { get; set; }
    public string Channel { get; set; } = "all";
    public string Language { get; set; } = "ja";
    public int Priority { get; set; }
    public bool IsActive { get; set; }
    public int UsageCount { get; set; }
    public int HelpfulCount { get; set; }
    public int NotHelpfulCount { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// ナレッジ作成リクエスト
/// </summary>
public class KnowledgeCreateRequest
{
    public string Category { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string? AnswerHtml { get; set; }
    public string? Keywords { get; set; }
    public string Channel { get; set; } = "all";
    public string Language { get; set; } = "ja";
    public int Priority { get; set; }
}

/// <summary>
/// ナレッジ更新リクエスト
/// </summary>
public class KnowledgeUpdateRequest
{
    public string? Category { get; set; }
    public string? Intent { get; set; }
    public string? Question { get; set; }
    public string? Answer { get; set; }
    public string? AnswerHtml { get; set; }
    public string? Keywords { get; set; }
    public string? Channel { get; set; }
    public string? Language { get; set; }
    public int? Priority { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>
/// 未回答質問
/// </summary>
public class UnansweredQuestion
{
    public string Question { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTime LastAskedAt { get; set; }
}

/// <summary>
/// ナレッジベースサービス実装
/// </summary>
public class KnowledgeBaseService : IKnowledgeBaseService
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly ILogger<KnowledgeBaseService> _logger;
    private const string DefaultProjectId = "auto-dealer-demo";

    public KnowledgeBaseService(
        IDbConnectionFactory dbConnectionFactory,
        ILogger<KnowledgeBaseService> logger)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<KnowledgeResult?> SearchAsync(string intent, Dictionary<string, string> entities, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            // インテントと言語で検索（有効なナレッジのみ）
            var sql = @"
                SELECT
                    knowledge_id,
                    question,
                    answer,
                    answer_html,
                    priority,
                    usage_count
                FROM ai_knowledge
                WHERE is_active = 1
                AND language = @Language
                AND (
                    intent = @Intent
                    OR instr(',' || intent || ',', ',' || @Intent || ',') > 0
                )
                ORDER BY priority DESC, usage_count DESC
                LIMIT 1";

            var result = await db.QueryFirstOrDefaultAsync(sql, new
            {
                Intent = intent,
                Language = "ja" // TODO: 多言語対応
            });

            if (result == null)
                return null;

            return new KnowledgeResult
            {
                KnowledgeId = result.knowledge_id,
                Message = result.answer,
                MatchScore = 1.0,
                Source = "knowledge"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ナレッジ検索に失敗：{Intent}", intent);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<List<KnowledgeResult>> SearchByKeywordAsync(string keyword, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;
        var results = new List<KnowledgeResult>();

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            var sql = @"
                SELECT
                    knowledge_id,
                    question,
                    answer,
                    answer_html,
                    priority
                FROM ai_knowledge
                WHERE is_active = 1
                AND language = @Language
                AND (
                    question LIKE @Keyword
                    OR answer LIKE @Keyword
                    OR keywords LIKE @Keyword
                )
                ORDER BY priority DESC, usage_count DESC
                LIMIT 10";

            var items = await db.QueryAsync(sql, new
            {
                Keyword = $"%{keyword}%",
                Language = "ja"
            });

            foreach (var item in items)
            {
                results.Add(new KnowledgeResult
                {
                    KnowledgeId = item.knowledge_id,
                    Message = item.answer,
                    MatchScore = 0.8,
                    Source = "knowledge"
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "キーワード検索に失敗：{Keyword}", keyword);
            return results;
        }
    }

    /// <inheritdoc />
    public async Task<KnowledgeItem?> GetByIdAsync(string knowledgeId, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            var sql = "SELECT * FROM ai_knowledge WHERE knowledge_id = @KnowledgeId";

            var result = await db.QueryFirstOrDefaultAsync(sql, new { KnowledgeId = knowledgeId });

            if (result == null)
                return null;

            return MapToKnowledgeItem(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ナレッジ取得に失敗：{KnowledgeId}", knowledgeId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string> CreateAsync(KnowledgeCreateRequest request, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;
        var knowledgeId = GenerateKnowledgeId();

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            var sql = @"
                INSERT INTO ai_knowledge (
                    knowledge_id, category, intent, question, answer, answer_html,
                    keywords, channel, language, priority, is_active,
                    created_at, updated_at
                ) VALUES (
                    @KnowledgeId, @Category, @Intent, @Question, @Answer, @AnswerHtml,
                    @Keywords, @Channel, @Language, @Priority, 1,
                    datetime('now'), datetime('now')
                )";

            await db.ExecuteAsync(sql, new
            {
                KnowledgeId = knowledgeId,
                request.Category,
                request.Intent,
                request.Question,
                request.Answer,
                request.AnswerHtml,
                request.Keywords,
                request.Channel,
                request.Language,
                request.Priority
            });

            _logger.LogInformation("ナレッジ作成：{KnowledgeId}", knowledgeId);
            return knowledgeId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ナレッジ作成に失敗");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(string knowledgeId, KnowledgeUpdateRequest request, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            var updateFields = new List<string>();
            var parameters = new Dictionary<string, object?>
            {
                ["KnowledgeId"] = knowledgeId
            };

            if (request.Category != null)
            {
                updateFields.Add("category = @Category");
                parameters["Category"] = request.Category;
            }

            if (request.Intent != null)
            {
                updateFields.Add("intent = @Intent");
                parameters["Intent"] = request.Intent;
            }

            if (request.Question != null)
            {
                updateFields.Add("question = @Question");
                parameters["Question"] = request.Question;
            }

            if (request.Answer != null)
            {
                updateFields.Add("answer = @Answer");
                parameters["Answer"] = request.Answer;
            }

            if (request.AnswerHtml != null)
            {
                updateFields.Add("answer_html = @AnswerHtml");
                parameters["AnswerHtml"] = request.AnswerHtml;
            }

            if (request.Keywords != null)
            {
                updateFields.Add("keywords = @Keywords");
                parameters["Keywords"] = request.Keywords;
            }

            if (request.Channel != null)
            {
                updateFields.Add("channel = @Channel");
                parameters["Channel"] = request.Channel;
            }

            if (request.Language != null)
            {
                updateFields.Add("language = @Language");
                parameters["Language"] = request.Language;
            }

            if (request.Priority.HasValue)
            {
                updateFields.Add("priority = @Priority");
                parameters["Priority"] = request.Priority;
            }

            if (request.IsActive.HasValue)
            {
                updateFields.Add("is_active = @IsActive");
                parameters["IsActive"] = request.IsActive;
            }

            if (updateFields.Count == 0)
                return false;

            updateFields.Add("updated_at = datetime('now')");

            var updateSql = $@"
                UPDATE ai_knowledge
                SET {string.Join(", ", updateFields)}
                WHERE knowledge_id = @KnowledgeId";

            var rows = await db.ExecuteAsync(updateSql, parameters);

            if (rows > 0)
            {
                _logger.LogInformation("ナレッジ更新：{KnowledgeId}", knowledgeId);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ナレッジ更新に失敗：{KnowledgeId}", knowledgeId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string knowledgeId, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            var sql = "DELETE FROM ai_knowledge WHERE knowledge_id = @KnowledgeId";
            var rows = await db.ExecuteAsync(sql, new { KnowledgeId = knowledgeId });

            if (rows > 0)
            {
                _logger.LogInformation("ナレッジ削除：{KnowledgeId}", knowledgeId);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ナレッジ削除に失敗：{KnowledgeId}", knowledgeId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RecordFeedbackAsync(string knowledgeId, bool isHelpful, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            // パラメータ化クエリを使用（文字列連結なし）
            string sql;
            
            if (isHelpful)
            {
                sql = @"
                    UPDATE ai_knowledge
                    SET helpful_count = helpful_count + 1,
                        updated_at = datetime('now')
                    WHERE knowledge_id = @KnowledgeId";
            }
            else
            {
                sql = @"
                    UPDATE ai_knowledge
                    SET not_helpful_count = not_helpful_count + 1,
                        updated_at = datetime('now')
                    WHERE knowledge_id = @KnowledgeId";
            }

            var rows = await db.ExecuteAsync(sql, new { KnowledgeId = knowledgeId });
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "フィードバック記録に失敗：{KnowledgeId}", knowledgeId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task IncrementUsageAsync(string knowledgeId, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            var sql = @"
                UPDATE ai_knowledge
                SET 
                    usage_count = usage_count + 1,
                    last_used_at = datetime('now'),
                    updated_at = datetime('now')
                WHERE knowledge_id = @KnowledgeId";

            await db.ExecuteAsync(sql, new { KnowledgeId = knowledgeId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "使用回数インクリメントに失敗：{KnowledgeId}", knowledgeId);
        }
    }

    /// <inheritdoc />
    public async Task<List<KnowledgeItem>> GetByCategoryAsync(string category, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;
        var items = new List<KnowledgeItem>();

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            var sql = @"
                SELECT * FROM ai_knowledge
                WHERE category = @Category
                ORDER BY priority DESC, usage_count DESC";

            var results = await db.QueryAsync(sql, new { Category = category });

            foreach (var result in results)
            {
                items.Add(MapToKnowledgeItem(result));
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "カテゴリ別ナレッジ取得に失敗：{Category}", category);
            return items;
        }
    }

    /// <inheritdoc />
    public async Task RecordUnansweredQuestionAsync(string question, string intent, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            // 未回答質問テーブルが存在する場合に実装
            // TODO: ai_unanswered_questions テーブル作成
            _logger.LogInformation("未回答質問記録：{Question}, Intent: {Intent}", question, intent);
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "未回答質問記録に失敗");
        }
    }

    private static string GenerateKnowledgeId()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
        var random = new Random().Next(1000, 9999);
        return $"KNOW-{timestamp}-{random}";
    }

    private static KnowledgeItem MapToKnowledgeItem(dynamic result)
    {
        return new KnowledgeItem
        {
            KnowledgeId = result.knowledge_id,
            Category = result.category,
            Intent = result.intent,
            Question = result.question,
            Answer = result.answer,
            AnswerHtml = result.answer_html,
            Keywords = result.keywords,
            Channel = result.channel,
            Language = result.language,
            Priority = result.priority,
            IsActive = result.is_active,
            UsageCount = result.usage_count,
            HelpfulCount = result.helpful_count,
            NotHelpfulCount = result.not_helpful_count,
            LastUsedAt = result.last_used_at,
            CreatedAt = result.created_at,
            UpdatedAt = result.updated_at
        };
    }
}
