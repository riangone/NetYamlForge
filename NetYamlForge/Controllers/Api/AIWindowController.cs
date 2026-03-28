using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Controllers.Api;

/// <summary>
/// AI 窗口 API コントローラー
/// </summary>
[ApiController]
[Route("api/ai")]
[Produces("application/json")]
public class AIWindowController : ControllerBase
{
    private readonly IConversationManager _conversationManager;
    private readonly IIntentClassifier _intentClassifier;
    private readonly IResponseGenerator _responseGenerator;
    private readonly IHandoverManager _handoverManager;
    private readonly ISentimentAnalyzer _sentimentAnalyzer;
    private readonly ICustomerDataService _customerDataService;
    private readonly IAppointmentService _appointmentService;
    private readonly ILogger<AIWindowController> _logger;

    public AIWindowController(
        IConversationManager conversationManager,
        IIntentClassifier intentClassifier,
        IResponseGenerator responseGenerator,
        IHandoverManager handoverManager,
        ISentimentAnalyzer sentimentAnalyzer,
        ICustomerDataService customerDataService,
        IAppointmentService appointmentService,
        ILogger<AIWindowController> logger)
    {
        _conversationManager = conversationManager;
        _intentClassifier = intentClassifier;
        _responseGenerator = responseGenerator;
        _handoverManager = handoverManager;
        _sentimentAnalyzer = sentimentAnalyzer;
        _customerDataService = customerDataService;
        _appointmentService = appointmentService;
        _logger = logger;
    }

    /// <summary>
    /// 対話セッションを開始
    /// </summary>
    [HttpPost("conversations")]
    public async Task<ActionResult<StartConversationResponse>> StartConversation([FromBody] StartConversationRequest request)
    {
        try
        {
            var conversation = await _conversationManager.StartConversationAsync(request);

            var welcomeMessage = _responseGenerator.GenerateWelcomeMessage(request.Channel);

            return Ok(new StartConversationResponse
            {
                ConversationId = conversation.ConversationId,
                WelcomeMessage = welcomeMessage,
                AiModel = "System",
                SentAt = DateTime.UtcNow,
                SessionTimeoutMinutes = 30
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
    public async Task<ActionResult<Conversation>> GetConversation(string id)
    {
        var conversation = await _conversationManager.GetConversationAsync(id);
        if (conversation == null)
            return NotFound();

        return Ok(conversation);
    }

    /// <summary>
    /// 対話セッションを終了
    /// </summary>
    [HttpPost("conversations/{id}/close")]
    public async Task<ActionResult> CloseConversation(string id)
    {
        var success = await _conversationManager.CloseConversationAsync(id);
        if (!success)
            return NotFound();

        return Ok(new { message = "対話セッションを終了しました" });
    }

    /// <summary>
    /// メッセージを送信して応答を取得
    /// </summary>
    [HttpPost("conversations/{id}/messages")]
    public async Task<ActionResult<SendMessageResponse>> SendMessage(string id, [FromBody] SendMessageRequest request)
    {
        try
        {
            // 対話セッションの存在確認
            var conversation = await _conversationManager.GetConversationAsync(id);
            if (conversation == null)
                return NotFound("対話セッションが見つかりません");

            // 感情分析
            var sentiment = await _sentimentAnalyzer.AnalyzeAsync(request.Content);

            // 意図分類
            var context = new ConversationContext
            {
                ConversationId = id,
                CurrentIntent = conversation.LastIntent
            };
            var intentResult = await _intentClassifier.ClassifyAsync(request.Content, context);
            intentResult.SentimentScore = sentiment.Score;

            // エスカレーション評価
            var handoverEval = await _handoverManager.EvaluateHandoverNeedAsync(intentResult, context);

            // 応答生成
            var response = await _responseGenerator.GenerateResponseAsync(intentResult, context);

            // エスカレーションが必要か
            if (handoverEval.NeedsHandover && !conversation.Status.Equals("escalated", StringComparison.OrdinalIgnoreCase))
            {
                var handoverResult = await _handoverManager.CreateHandoverAsync(new HandoverRequest
                {
                    ConversationId = id,
                    Reason = handoverEval.Reason ?? "ai_unable",
                    Priority = handoverEval.Priority,
                    TargetDepartment = handoverEval.TargetDepartment,
                    HandoverNotes = $"インテント：{intentResult.Intent}, 置信度：{intentResult.Confidence:P0}, 感情：{sentiment.Label}"
                }, null);

                if (handoverResult.Success)
                {
                    response.Message = _responseGenerator.GenerateHandoverMessage(handoverEval.Reason ?? "ai_unable");
                    response.QuickReplies = new List<QuickReplyButton>();
                }
            }

            return Ok(new SendMessageResponse
            {
                ConversationId = id,
                ResponseText = response.Message,
                Intent = intentResult.Intent,
                Confidence = intentResult.Confidence,
                Entities = intentResult.Entities,
                QuickReplies = response.QuickReplies,
                AiModel = response.AiModel,
                SentAt = DateTime.UtcNow,
                ProcessingTimeMs = response.ProcessingTimeMs,
                SuggestHandover = handoverEval.NeedsHandover
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
    public async Task<ActionResult<VerifyCustomerResponse>> VerifyCustomer([FromBody] VerifyCustomerRequest request)
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
                request.VerificationCode);

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
    public async Task<ActionResult<CustomerInfo>> GetCustomer(string id)
    {
        try
        {
            var customer = await _customerDataService.GetCustomerByIdAsync(id);
            
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
    public async Task<ActionResult<object>> GetCustomerHistory(string id)
    {
        try
        {
            var history = await _customerDataService.GetCustomerServiceHistoryAsync(id);
            
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
        [FromQuery] int days = 7)
    {
        try
        {
            var slots = await _appointmentService.GetAvailableSlotsAsync(
                startDate ?? DateTime.Today,
                days);

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
        [FromBody] CreateAppointmentRequest request)
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
            });

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
        [FromBody] AppointmentUpdateRequest request)
    {
        try
        {
            var result = await _appointmentService.UpdateAppointmentAsync(id, request);

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
    public async Task<ActionResult> CancelAppointment(string id)
    {
        try
        {
            var success = await _appointmentService.CancelAppointmentAsync(id);

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
    public async Task<ActionResult<AppointmentInfo>> GetAppointment(string id)
    {
        try
        {
            var appointment = await _appointmentService.GetAppointmentAsync(id);

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
    public async Task<ActionResult<List<HandoverInfo>>> GetHandoverQueue(string? department = null)
    {
        try
        {
            var queue = await _handoverManager.GetPendingQueueAsync(department);
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
    public async Task<ActionResult> ResolveHandover(string id, [FromBody] ResolveHandoverRequest request)
    {
        try
        {
            var success = await _handoverManager.ResolveHandoverAsync(id, request.ResolutionNotes);
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
}
