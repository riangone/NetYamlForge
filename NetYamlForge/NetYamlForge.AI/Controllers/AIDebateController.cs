using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models.Debate;
using NetYamlForge.AI.Services;

namespace NetYamlForge.AI.Controllers;

/// <summary>
/// AI ディベートコントローラー (UI + API)
/// </summary>
[Authorize]
public class AIDebateController : Controller
{
    private readonly AIDebateDbService _db;
    private readonly AIDebateOrchestrator _orchestrator;
    private readonly CLIServiceFactory _cliFactory;
    private readonly ILogger<AIDebateController> _logger;

    public AIDebateController(
        AIDebateDbService db,
        AIDebateOrchestrator orchestrator,
        CLIServiceFactory cliFactory,
        ILogger<AIDebateController> logger)
    {
        _db = db;
        _orchestrator = orchestrator;
        _cliFactory = cliFactory;
        _logger = logger;
    }

    // GET /AIDebate
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var topics = await _db.GetTopicsAsync();
        ViewBag.Topics = topics;
        ViewBag.AvailableTools = await _cliFactory.GetAvailableToolsAsync();
        return View();
    }

    // GET /AIDebate/Topic/{id}
    [HttpGet("AIDebate/Topic/{id:int}")]
    public async Task<IActionResult> Topic(int id)
    {
        var topic = await _db.GetTopicAsync(id);
        if (topic == null) return NotFound();
        var messages = await _db.GetMessagesAsync(id);
        ViewBag.Topic = topic;
        ViewBag.Messages = messages;
        ViewBag.IsRunning = _orchestrator.IsRunning(id);
        return View();
    }

    // POST /AIDebate/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] CreateTopicRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
        {
            TempData["Error"] = "タイトルは必須です";
            return RedirectToAction(nameof(Index));
        }
        var id = await _db.CreateTopicAsync(req);
        return RedirectToAction(nameof(Topic), new { id });
    }

    // POST /api/debate/topics/{id}/start
    [HttpPost("/api/debate/topics/{id:int}/start")]
    public async Task<IActionResult> StartDebate(int id)
    {
        var ok = await _orchestrator.StartDebateAsync(id);
        return ok ? Ok(new { started = true }) : BadRequest(new { error = "開始できませんでした" });
    }

    // POST /api/debate/topics/{id}/cancel
    [HttpPost("/api/debate/topics/{id:int}/cancel")]
    public async Task<IActionResult> CancelDebate(int id)
    {
        await _orchestrator.CancelDebateAsync(id);
        return Ok(new { cancelled = true });
    }

    // GET /api/debate/topics/{id}/messages
    [HttpGet("/api/debate/topics/{id:int}/messages")]
    public async Task<IActionResult> GetMessages(int id)
    {
        var messages = await _db.GetMessagesAsync(id);
        return Ok(messages);
    }

    // GET /api/debate/topics/{id}
    [HttpGet("/api/debate/topics/{id:int}")]
    public async Task<IActionResult> GetTopic(int id)
    {
        var topic = await _db.GetTopicAsync(id);
        if (topic == null) return NotFound();
        return Ok(topic);
    }

    // GET /api/debate/topics
    [HttpGet("/api/debate/topics")]
    public async Task<IActionResult> GetTopics()
    {
        var topics = await _db.GetTopicsAsync();
        return Ok(topics);
    }

    // GET /api/debate/tools
    [HttpGet("/api/debate/tools")]
    public async Task<IActionResult> GetTools()
    {
        var tools = await _cliFactory.GetAvailableToolsAsync();
        return Ok(tools.Select(kv => new { name = kv.Key, info = kv.Value }));
    }
}
