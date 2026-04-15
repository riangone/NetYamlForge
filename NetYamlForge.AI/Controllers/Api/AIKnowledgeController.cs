using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Services;

namespace NetYamlForge.AI.Controllers.Api;

/// <summary>
/// ナレッジベース管理 API
/// </summary>
[ApiController]
[Route("api/ai/knowledge")]
[Produces("application/json")]
public class AIKnowledgeController : ControllerBase
{
    private readonly IKnowledgeBaseService _knowledgeService;
    private readonly ILogger<AIKnowledgeController> _logger;

    public AIKnowledgeController(
        IKnowledgeBaseService knowledgeService,
        ILogger<AIKnowledgeController> logger)
    {
        _knowledgeService = knowledgeService;
        _logger = logger;
    }

    /// <summary>
    /// ナレッジ検索
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<KnowledgeResult>> Search(
        [FromQuery] string intent,
        [FromQuery] string? keyword = null)
    {
        try
        {
            KnowledgeResult? result;

            if (!string.IsNullOrEmpty(keyword))
            {
                var results = await _knowledgeService.SearchByKeywordAsync(keyword);
                result = results.FirstOrDefault();
            }
            else
            {
                result = await _knowledgeService.SearchAsync(intent, new Dictionary<string, string>());
            }

            if (result == null)
                return NotFound();

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ナレッジ検索に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// ナレッジ一覧取得
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<KnowledgeItem>>> GetKnowledge(
        [FromQuery] string? category = null,
        [FromQuery] string? intent = null,
        [FromQuery] string? language = null,
        [FromQuery] bool? isActive = null)
    {
        try
        {
            // TODO: フィルター処理を実装
            var items = new List<KnowledgeItem>();
            
            if (!string.IsNullOrEmpty(category))
            {
                items = await _knowledgeService.GetByCategoryAsync(category);
            }

            return Ok(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ナレッジ一覧取得に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// ナレッジ詳細取得
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<KnowledgeItem>> GetKnowledge(string id)
    {
        try
        {
            var item = await _knowledgeService.GetByIdAsync(id);

            if (item == null)
                return NotFound();

            return Ok(item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ナレッジ取得に失敗：{KnowledgeId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// ナレッジ作成
    /// </summary>
    [HttpPost]
    public async Task<ActionResult> CreateKnowledge([FromBody] KnowledgeCreateRequest request)
    {
        try
        {
            var knowledgeId = await _knowledgeService.CreateAsync(request);

            return CreatedAtAction(nameof(GetKnowledge), new { id = knowledgeId }, knowledgeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ナレッジ作成に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// ナレッジ更新
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateKnowledge(
        string id,
        [FromBody] KnowledgeUpdateRequest request)
    {
        try
        {
            var success = await _knowledgeService.UpdateAsync(id, request);

            if (!success)
                return NotFound();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ナレッジ更新に失敗：{KnowledgeId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// ナレッジ削除
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteKnowledge(string id)
    {
        try
        {
            var success = await _knowledgeService.DeleteAsync(id);

            if (!success)
                return NotFound();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ナレッジ削除に失敗：{KnowledgeId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// フィードバック送信
    /// </summary>
    [HttpPost("{id}/feedback")]
    public async Task<ActionResult> SendFeedback(
        string id,
        [FromBody] FeedbackRequest request)
    {
        try
        {
            var success = await _knowledgeService.RecordFeedbackAsync(id, request.IsHelpful);

            if (!success)
                return NotFound();

            return Ok(new { message = "フィードバックを送信しました" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "フィードバック送信に失敗：{KnowledgeId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }
}

/// <summary>
/// フィードバックリクエスト
/// </summary>
public class FeedbackRequest
{
    public bool IsHelpful { get; set; }
}
