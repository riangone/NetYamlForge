using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services.AI;
using NetYamlForge.Services;

namespace NetYamlForge.Controllers;

[Route("{project}/AiAssistant")]
public class AiAssistantController : Controller
{
    private readonly IGeminiCliService _gemini;
    private readonly ProjectScope _projectScope;
    private readonly ILogger<AiAssistantController> _logger;

    public AiAssistantController(
        IGeminiCliService gemini,
        ProjectScope projectScope,
        ILogger<AiAssistantController> logger)
    {
        _gemini = gemini;
        _projectScope = projectScope;
        _logger = logger;
    }

    [HttpPost("Chat")]
    public async Task<IActionResult> Chat([FromForm] string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return BadRequest("Message cannot be empty.");
        }

        var projectName = _projectScope.Current?.Name ?? "default";
        
        // システムプロンプトを構築
        var systemPrompt = $@"あなたは NetYamlForge システム（プロジェクト: {projectName}）の高度な AI アシスタント『Hyperion』です。
ユーザーは現在、自動車販売管理システムの操作を行っています。
業務（リード管理、在庫管理、見積作成、AI自動化設定など）に関する質問に答えたり、操作のサポートを行ってください。
回答は簡潔かつ専門的に、かつ親しみやすいトーンで行ってください。
必要に応じて Markdown 形式で回答してください。";

        var fullPrompt = $"{systemPrompt}\n\nUser: {message}\nAssistant:";

        var response = await _gemini.PromptAsync(fullPrompt, projectName: projectName);

        if (string.IsNullOrEmpty(response))
        {
            response = "申し訳ありません。現在 AI との通信に問題が発生しています。しばらく時間をおいてから再度お試しください。";
        }

        // HTMX 用の部分ビューを返す
        return PartialView("_ChatMessage", new ChatMessageViewModel
        {
            Role = "assistant",
            Content = response,
            Timestamp = DateTime.Now
        });
    }
}

public class ChatMessageViewModel
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
