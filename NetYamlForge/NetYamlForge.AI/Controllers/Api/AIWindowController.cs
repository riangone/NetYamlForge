using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models;
using NetYamlForge.AI.Services;

namespace NetYamlForge.AI.Controllers.Api;

/// <summary>
/// AI 窗口 API コントローラー
/// </summary>
[ApiController]
[Route("api/aiwindow")]  // api/ai は AIController ({project?}/api/AI) と競合するため aiwindow に変更
[Route("{project}/api/aiwindow")]
[Produces("application/json")]
public class AIWindowController : ControllerBase
{
    private readonly IConversationManager _conversationManager;
    private readonly IDirectAIProcessor _aiProcessor;
    private readonly IHandoverManager _handoverManager;
    private readonly ICustomerDataService _customerDataService;
    private readonly IAppointmentService _appointmentService;
    private readonly ILogger<AIWindowController> _logger;

    public AIWindowController(
        IConversationManager conversationManager,
        IDirectAIProcessor aiProcessor,
        IHandoverManager handoverManager,
        ICustomerDataService customerDataService,
        IAppointmentService appointmentService,
        ILogger<AIWindowController> logger)
    {
        _conversationManager = conversationManager;
        _aiProcessor = aiProcessor;
        _handoverManager = handoverManager;
        _customerDataService = customerDataService;
        _appointmentService = appointmentService;
        _logger = logger;
    }

    /// <summary>
    /// 対話セッションを開始
    /// </summary>
    [HttpPost("conversations")]
    public async Task<ActionResult<StartConversationResponse>> StartConversation(
        [FromBody] StartConversationRequest request,
        [FromRoute] string? project)
    {
        try
        {
            var conversation = await _conversationManager.StartConversationAsync(request, project);

            var welcomeMessage = GenerateWelcomeMessage(request.Channel);

            return Ok(new StartConversationResponse
            {
                ConversationId = conversation.ConversationId,
                WelcomeMessage = welcomeMessage,
                AiModel = "System",
                SentAt = DateTime.UtcNow,
                SessionTimeoutMinutes = 60
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "対話セッションの開始に失敗");
            return StatusCode(500, new { error = "対話セッションの開始に失敗しました" });
        }
    }

    /// <summary>
    /// 対話セッションを取得
    /// </summary>
    [HttpGet("conversations/{id}")]
    public async Task<ActionResult<Conversation>> GetConversation(string id, [FromRoute] string? project)
    {
        var conversation = await _conversationManager.GetConversationAsync(id, project);
        if (conversation == null)
            return NotFound();

        return Ok(conversation);
    }

    /// <summary>
    /// 対話セッションを終了
    /// </summary>
    [HttpPost("conversations/{id}/close")]
    public async Task<ActionResult> CloseConversation(string id, [FromRoute] string? project)
    {
        var success = await _conversationManager.CloseConversationAsync(id, project);
        if (!success)
            return NotFound();

        return Ok(new { message = "対話セッションを終了しました" });
    }

    /// <summary>
    /// メッセージを送信して応答を取得
    /// </summary>
    [HttpPost("conversations/{id}/messages")]
    public async Task<ActionResult<SendMessageResponse>> SendMessage(
        string id,
        [FromBody] SendMessageRequest request,
        [FromRoute] string? project)
    {
        try
        {
            // 対話セッションの存在確認
            var conversation = await _conversationManager.GetConversationAsync(id, project);
            if (conversation == null)
                return NotFound("対話セッションが見つかりません");

            // 直接 AI 処理
            var context = new ConversationContext
            {
                ConversationId = id,
                CurrentIntent = conversation.LastIntent
            };
            var aiResult = await _aiProcessor.ProcessAsync(request.Content, context, project);

            // エスカレーションが必要か
            if (aiResult.NeedsHandover && !conversation.Status.Equals("escalated", StringComparison.OrdinalIgnoreCase))
            {
                var handoverResult = await _handoverManager.CreateHandoverAsync(new HandoverRequest
                {
                    ConversationId = id,
                    Reason = aiResult.HandoverReason ?? "ai_unable",
                    Priority = aiResult.Priority,
                    TargetDepartment = aiResult.TargetDepartment,
                    HandoverNotes = $"感情：{aiResult.SentimentLabel} ({aiResult.SentimentScore:F2}), エンティティ：{string.Join(", ", aiResult.Entities.Select(e => $"{e.Key}={e.Value}"))}"
                }, project);

                if (handoverResult.Success)
                {
                    aiResult.Message = _handoverManager.GetHandoverMessage(aiResult.HandoverReason ?? "ai_unable");
                    aiResult.QuickReplies = new List<QuickReplyButton>();
                }
            }

            return Ok(new SendMessageResponse
            {
                ConversationId = id,
                ResponseText = aiResult.Message,
                Intent = aiResult.Method,
                Confidence = aiResult.NeedsHandover ? 0.0 : 1.0,
                Entities = aiResult.Entities,
                QuickReplies = aiResult.QuickReplies,
                AiModel = aiResult.AiModel,
                SentAt = DateTime.UtcNow,
                ProcessingTimeMs = 0,
                SuggestHandover = aiResult.NeedsHandover,
                SentimentScore = aiResult.SentimentScore
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メッセージ処理に失敗");
            return StatusCode(500, new { error = "メッセージ処理に失敗しました" });
        }
    }

    /// <summary>
    /// 顧客認証
    /// </summary>
    [HttpPost("customers/verify")]
    public async Task<ActionResult<VerifyCustomerResponse>> VerifyCustomer(
        [FromBody] VerifyCustomerRequest request,
        [FromRoute] string? project)
    {
        try
        {
            if (string.IsNullOrEmpty(request.Identifier))
            {
                return BadRequest(new VerifyCustomerResponse
                {
                    Success = false,
                    ErrorMessage = "電話番号またはメールアドレスを入力してください"
                });
            }

            // 顧客認証実行
            var result = await _customerDataService.VerifyCustomerAsync(
                request.Identifier,
                request.VerificationCode,
                project);

            if (!result.Success)
            {
                return Ok(result);
            }

            // 認証成功時は対話に顧客を紐付け
            if (!string.IsNullOrEmpty(result.CustomerId))
            {
                // 既存の対話があれば紐付け（実装による）
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "顧客認証に失敗：{Identifier}", request.Identifier);
            return StatusCode(500, new { 
                success = false,
                error = "認証処理中にエラーが発生しました" 
            });
        }
    }

    /// <summary>
    /// 顧客情報取得
    /// </summary>
    [HttpGet("customers/{id}")]
    public async Task<ActionResult<CustomerInfo>> GetCustomer(string id, [FromRoute] string? project)
    {
        try
        {
            var customer = await _customerDataService.GetCustomerByIdAsync(id, project);
            
            if (customer == null)
                return NotFound();

            return Ok(customer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "顧客情報取得に失敗：{CustomerId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// 顧客サービス履歴取得
    /// </summary>
    [HttpGet("customers/{id}/history")]
    public async Task<ActionResult<object>> GetCustomerHistory(string id, [FromRoute] string? project)
    {
        try
        {
            var history = await _customerDataService.GetCustomerServiceHistoryAsync(id, project);
            
            if (history == null)
                return NotFound();

            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "顧客履歴取得に失敗：{CustomerId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// 空き枠検索
    /// </summary>
    [HttpGet("appointments/available")]
    public async Task<ActionResult<List<TimeSlot>>> GetAvailableSlots(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] int days = 7,
        [FromRoute] string? project = null)
    {
        try
        {
            var slots = await _appointmentService.GetAvailableSlotsAsync(
                startDate ?? DateTime.Today,
                days,
                project);

            return Ok(slots);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "空き枠検索に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// 予約作成
    /// </summary>
    [HttpPost("appointments")]
    public async Task<ActionResult<AppointmentResult>> CreateAppointment(
        [FromBody] CreateAppointmentRequest request,
        [FromRoute] string? project)
    {
        try
        {
            var result = await _appointmentService.CreateAppointmentAsync(new AppointmentRequest
            {
                CustomerId = request.CustomerId,
                ServiceType = request.ServiceType,
                VehicleId = request.VehicleId,
                PreferredDateTime = request.PreferredDateTime,
                Details = request.Details
            }, project);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予約作成に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// 予約変更
    /// </summary>
    [HttpPut("appointments/{id}")]
    public async Task<ActionResult<AppointmentResult>> UpdateAppointment(
        string id,
        [FromBody] AppointmentUpdateRequest request,
        [FromRoute] string? project)
    {
        try
        {
            var result = await _appointmentService.UpdateAppointmentAsync(id, request, project);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予約変更に失敗：{AppointmentId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// 予約キャンセル
    /// </summary>
    [HttpDelete("appointments/{id}")]
    public async Task<ActionResult> CancelAppointment(string id, [FromRoute] string? project)
    {
        try
        {
            var success = await _appointmentService.CancelAppointmentAsync(id, project);

            if (!success)
            {
                return NotFound("予約が見つかりません");
            }

            return Ok(new { message = "予約をキャンセルしました" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予約キャンセルに失敗：{AppointmentId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// 予約詳細取得
    /// </summary>
    [HttpGet("appointments/{id}")]
    public async Task<ActionResult<AppointmentInfo>> GetAppointment(string id, [FromRoute] string? project)
    {
        try
        {
            var appointment = await _appointmentService.GetAppointmentAsync(id, project);

            if (appointment == null)
                return NotFound();

            return Ok(appointment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予約取得に失敗：{AppointmentId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// エスカレーションキューを取得（オペレーター用）
    /// </summary>
    [HttpGet("handovers/queue")]
    public async Task<ActionResult<List<HandoverInfo>>> GetHandoverQueue(
        string? department = null,
        [FromRoute] string? project = null)
    {
        try
        {
            var queue = await _handoverManager.GetPendingQueueAsync(department, project);
            return Ok(queue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エスカレーションキューの取得に失敗");
            return StatusCode(500, new { error = "エスカレーションキューの取得に失敗しました" });
        }
    }

    /// <summary>
    /// エスカレーションを解決（オペレーター用）
    /// </summary>
    [HttpPut("handovers/{id}/resolve")]
    public async Task<ActionResult> ResolveHandover(
        string id,
        [FromBody] ResolveHandoverRequest request,
        [FromRoute] string? project)
    {
        try
        {
            var success = await _handoverManager.ResolveHandoverAsync(id, request.ResolutionNotes, project);
            if (!success)
                return NotFound();

            return Ok(new { message = "エスカレーションを解決済みにマークしました" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エスカレーションの解決に失敗：{HandoverId}", id);
            return StatusCode(500, new { error = "エスカレーションの解決に失敗しました" });
        }
    }

    /// <summary>
    /// 健康チェック
    /// </summary>
    [HttpGet("health")]
    public ActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// 歓迎メッセージを生成
    /// </summary>
    private static string GenerateWelcomeMessage(string channel)
    {
        return channel switch
        {
            "line" => "こんにちは！自動車ディーラー AI アシスタントです。どのようなご用件でしょうか？",
            "web" => "ようこそ！自動車ディーラー AI アシスタントです。お気軽にお問い合わせください。",
            "email" => "お問い合わせいただき、ありがとうございます。自動車ディーラー AI アシスタントが対応させていただきます。",
            _ => "こんにちは！自動車ディーラー AI アシスタントです。どのようなご用件でしょうか？"
        };
    }
}
