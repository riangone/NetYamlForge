# AI 窓口システム設計実装案

> **バージョン**: 1.0  
> **作成日**: 2026-03-27  
> **対象プロジェクト**: NetYamlForge × 自動車ディーラー統合管理システム  
> **関連資料**: [auto-dealer-system-spec.md](./auto-dealer-system-spec.md), [auto-dealer-framework-analysis.md](./auto-dealer-framework-analysis.md)

---

## 目次

1. [システム概要](#1-システム概要)
2. [データモデル設計](#2-データモデル設計)
3. [API 設計](#3-api-設計)
4. [AI コアサービス実装](#4-ai-コアサービス実装)
5. [マルチチャネル接入層](#5-マルチチャネル接入層)
6. [エスカレーション管理フック](#6-エスカレーション管理フック)
7. [バッチジョブ設計](#7-バッチジョブ設計)
8. [ダッシュボード設計](#8-ダッシュボード設計)
9. [実装フェーズと工数](#9-実装フェーズと工数)
10. [セキュリティ・プライバシー対策](#10-セキュリティ・プライバシー対策)
11. [拡張ポイント](#11-拡張ポイント)
12. [まとめ](#12-まとめ)

---

## 1. システム概要

### 1.1 設計方針

NetYamlForge フレームワークの **YAML 駆動設計** と **フックシステム** を活用し、AI 窓口機能を段階的に実装する。

```
┌─────────────────────────────────────────────────────────────┐
│                    AI 窓口システム全体像                      │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  【マルチチャネル接入層】                                    │
│  Web チャット │ 音声通話 │ LINE │ メール │ SMS │ 店頭       │
│       ↓            ↓         ↓       ↓       ↓       ↓      │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              統一 API ゲートウェイ                      │   │
│  │         POST /api/v1/nlu, /dialog, /generate          │   │
│  └──────────────────────────────────────────────────────┘   │
│                            ↓                                  │
│  ┌──────────────────────────────────────────────────────┐   │
│  │                  AI コアエンジン                       │   │
│  │  ┌────────────┐  ┌────────────┐  ┌────────────┐      │   │
│  │  │  NLU       │  │  Dialog    │  │  Response  │      │   │
│  │  │  Engine    │  │  Manager   │  │  Generator │      │   │
│  │  └────────────┘  └────────────┘  └────────────┘      │   │
│  └──────────────────────────────────────────────────────┘   │
│                            ↓                                  │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              NetYamlForge バックエンド                 │   │
│  │  顧客管理 │ 車両在庫 │ サービス予約 │ サポートチケット │   │
│  │  (既存エンティティ + AI 専用エンティティ)                │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 1.2 NetYamlForge との統合ポイント

| 機能 | 実装方法 |
|------|---------|
| AI 対話セッション管理 | 新規エンティティ `ai_sessions.yml` |
| 対話ログ保存 | 新規エンティティ `ai_conversation_logs.yml` |
| エスカレーション処理 | 既存 `support_tickets.yml` と連携 |
| 顧客情報照会 | 既存 `customers.yml` エンティティ参照 |
| 車両在庫照会 | 既存 `vehicles.yml` エンティティ参照 |
| サービス予約 | 既存 `service_requests.yml` エンティティ作成 |
| 自動フォローアップ | 既存バッチジョブ + `followups.yml` |

### 1.3 仕様書（auto-dealer-system-spec.md）第 15 章との対応

| 仕様項目 | 実装内容 |
|---------|---------|
| 15.2 マルチチャネル AI 対応 | Web チャット・LINE・音声（拡張可能） |
| 15.3 システムアーキテクチャ | NLU・対話管理・応答生成の 3 層 |
| 15.4 AI データモデル | 4 つの新規エンティティ（ai_sessions, ai_conversation_logs, ai_recommendations, ai_escalations） |
| 15.5 API 仕様 | `/api/v1/aiwindow/nlu`, `/dialog`, `/generate`, `/inventory`, `/reservation` |
| 15.6 AI モデルのトレーニングパイプライン | Phase 5 で LLM 連携（オプション） |
| 15.7 A/B テスト仕組み | 拡張ポイントとして設計（将来） |
| 15.8 セキュリティ・プライバシー | データ暗号化・アクセス制御・監査ログ |
| 15.9 将来の拡張ポイント | 感情分析・パーソナライゼーション |

---

## 2. データモデル設計

### 2.1 新規エンティティ定義

#### `entities/ai_sessions.yml`（AI 対話セッション）

```yaml
entity: ai_sessions
displayName: AI 対話セッション
description: AI 顧客対応のセッション管理

columns:
  session_id:
    type: string
    length: 64
    required: true
    unique: true
    description: セッション ID（ユニーク）
  
  channel:
    type: string
    length: 20
    required: true
    enum: [web, voice, line, email, sms, tablet]
    description: チャネル種別
  
  customer_id:
    type: string
    length: 50
    required: false
    foreign_key:
      entity: customers
      column: customer_id
    description: 顧客 ID（紐づく場合のみ）
  
  status:
    type: string
    length: 30
    required: true
    default: active
    enum: [active, completed, escalated, abandoned]
    description: セッションステータス
  
  last_intent:
    type: string
    length: 100
    required: false
    description: 最終インテント
  
  context_data:
    type: text
    required: false
    description: コンテキスト情報（JSON）
  
  started_at:
    type: datetime
    required: true
    description: 開始日時
  
  ended_at:
    type: datetime
    required: false
    description: 終了日時
  
  escalated_to_user_id:
    type: string
    length: 50
    required: false
    foreign_key:
      entity: users
      column: user_id
    description: エスカレーション先担当者

indexes:
  idx_session_id:
    columns: [session_id]
    unique: true
  idx_customer_id:
    columns: [customer_id]
  idx_status:
    columns: [status]
  idx_started_at:
    columns: [started_at]
```

#### `entities/ai_conversation_logs.yml`（対話ログ）

```yaml
entity: ai_conversation_logs
displayName: AI 対話ログ
description: AI と顧客の対話履歴

columns:
  log_id:
    type: string
    length: 64
    required: true
    unique: true
    description: ログ ID
  
  session_id:
    type: string
    length: 64
    required: true
    foreign_key:
      entity: ai_sessions
      column: session_id
    description: セッション ID
  
  speaker:
    type: string
    length: 20
    required: true
    enum: [customer, ai]
    description: 発話者
  
  message_content:
    type: text
    required: true
    description: メッセージ内容
    encrypt: true  # 個人情報を含むため暗号化
  
  message_type:
    type: string
    length: 20
    required: false
    default: text
    enum: [text, voice_transcript, image, quick_reply]
    description: メッセージ種別
  
  intent:
    type: string
    length: 100
    required: false
    description: 判定されたインテント
  
  entities_detected:
    type: text
    required: false
    description: 検出されたエンティティ（JSON）
  
  confidence_score:
    type: decimal
    precision: 5
    scale: 4
    required: false
    description: 信頼度スコア（0.0000-1.0000）
  
  timestamp:
    type: datetime
    required: true
    description: 発話日時

indexes:
  idx_log_id:
    columns: [log_id]
    unique: true
  idx_session_id:
    columns: [session_id]
  idx_timestamp:
    columns: [timestamp]
```

#### `entities/ai_recommendations.yml`（AI 推奨記録）

```yaml
entity: ai_recommendations
displayName: AI 推奨記録
description: AI による推奨内容と顧客反応

columns:
  recommendation_id:
    type: string
    length: 64
    required: true
    unique: true
  
  session_id:
    type: string
    length: 64
    required: true
    foreign_key:
      entity: ai_sessions
      column: session_id
  
  recommendation_type:
    type: string
    length: 50
    required: true
    enum: [vehicle_proposal, service_plan, option_proposal, insurance_proposal]
    description: 推奨タイプ
  
  recommendation_data:
    type: text
    required: true
    description: 推奨内容（JSON）
    example: |
      {
        "vehicle_id": "VH-20250101-001",
        "model": "カローラクロス",
        "grade": "HYBRID WXB",
        "price": 3200000,
        "reason": "低燃費・安全装備充実"
      }
  
  customer_reaction:
    type: string
    length: 50
    required: false
    enum: [interested, not_interested, requested_detail, booked_test_drive]
    description: 顧客反応
  
  created_at:
    type: datetime
    required: true

indexes:
  idx_recommendation_id:
    columns: [recommendation_id]
    unique: true
  idx_session_id:
    columns: [session_id]
```

#### `entities/ai_escalations.yml`（エスカレーション管理）

```yaml
entity: ai_escalations
displayName: AI エスカレーション管理
description: AI から人間担当者への引き継ぎ記録

columns:
  escalation_id:
    type: string
    length: 64
    required: true
    unique: true
  
  session_id:
    type: string
    length: 64
    required: true
    foreign_key:
      entity: ai_sessions
      column: session_id
  
  ticket_id:
    type: string
    length: 64
    required: false
    foreign_key:
      entity: support_tickets
      column: ticket_id
    description: 関連サポートチケット
  
  reason:
    type: string
    length: 200
    required: true
    enum: [ai_unable, customer_request, high_value_deal, complaint, complex_inquiry]
    description: エスカレーション理由
  
  priority:
    type: string
    length: 20
    required: true
    default: medium
    enum: [low, medium, high, urgent]
  
  assigned_to_user_id:
    type: string
    length: 50
    required: false
    foreign_key:
      entity: users
      column: user_id
    description: 割り当て担当者
  
  escalated_at:
    type: datetime
    required: true
  
  resolved_at:
    type: datetime
    required: false
  
  resolution_notes:
    type: text
    required: false
    description: 解決メモ

indexes:
  idx_escalation_id:
    columns: [escalation_id]
    unique: true
  idx_session_id:
    columns: [session_id]
  idx_assigned_to_user_id:
    columns: [assigned_to_user_id]
```

### 2.2 エンティティ関係図

```
┌─────────────────┐       ┌──────────────────────┐
│   customers     │       │      vehicles        │
│  (既存顧客)      │       │   (既存車両在庫)      │
└────────┬────────┘       └──────────┬───────────┘
         │                           │
         │ 1:N                       │ 1:N
         ▼                           ▼
┌─────────────────────────────────────────────────┐
│              ai_sessions                        │
│            (AI 対話セッション)                   │
└─────────────────────┬───────────────────────────┘
                      │ 1:N
                      ▼
         ┌────────────────────────┐
         │   ai_conversation_logs │
         │      (対話ログ)         │
         └────────────────────────┘

┌─────────────────┐       ┌──────────────────────┐
│ ai_sessions     │──────▶│  ai_recommendations  │
│                 │       │    (AI 推奨)          │
└─────────────────┘       └──────────────────────┘

┌─────────────────┐       ┌──────────────────────┐
│ ai_sessions     │──────▶│   ai_escalations     │
│                 │       │   (エスカレーション)  │
└─────────────────┘       └──────────┬───────────┘
                                     │
                                     │ 1:1
                                     ▼
                            ┌──────────────────────┐
                            │  support_tickets     │
                            │  (既存サポートチケット)│
                            └──────────────────────┘
```

---

## 3. API 設計

### 3.1 コア API エンドポイント

NetYamlForge の `ApiEntityController` を拡張し、AI 専用 API を実装。

#### `Controllers/Api/AiWindowController.cs`

```csharp
namespace NetYamlForge.Controllers.Api;

[ApiController]
[Route("api/v1/[controller]")]
public class AiWindowController : ControllerBase
{
    private readonly IAiNluService _nluService;
    private readonly IAiDialogService _dialogService;
    private readonly IAiResponseGenerator _responseGenerator;
    private readonly IDynamicEntityService _entityService;

    public AiWindowController(
        IAiNluService nluService,
        IAiDialogService dialogService,
        IAiResponseGenerator responseGenerator,
        IDynamicEntityService entityService)
    {
        _nluService = nluService;
        _dialogService = dialogService;
        _responseGenerator = responseGenerator;
        _entityService = entityService;
    }

    /// <summary>
    /// NLU（自然言語理解）エンドポイント
    /// POST /api/v1/aiwindow/nlu
    /// </summary>
    [HttpPost("nlu")]
    [ProducesResponseType(typeof(NluResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ProcessNlu([FromBody] NluRequest request)
    {
        // セッション作成または取得
        var session = await GetOrCreateSessionAsync(request.SessionId, request.Channel, request.CustomerId);
        
        // NLU 処理
        var nluResult = await _nluService.AnalyzeIntentAsync(
            request.Message,
            session.SessionId,
            request.Channel);

        // 対話ログに保存
        await LogConversationAsync(session.SessionId, "customer", request.Message, nluResult);

        return Ok(new NluResponse
        {
            SessionId = session.SessionId,
            Intent = nluResult.Intent,
            Entities = nluResult.Entities,
            Confidence = nluResult.Confidence,
            NextAction = nluResult.NextAction
        });
    }

    /// <summary>
    /// 対話管理エンドポイント
    /// POST /api/v1/aiwindow/dialog
    /// </summary>
    [HttpPost("dialog")]
    [ProducesResponseType(typeof(DialogResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ManageDialog([FromBody] DialogRequest request)
    {
        var context = await BuildDialogContextAsync(request.SessionId, request.Context);
        
        var dialogResult = await _dialogService.DetermineNextActionAsync(
            request.SessionId,
            request.Intent,
            request.Entities,
            context);

        // エスカレーション判定
        if (dialogResult.ShouldEscalate)
        {
            var escalation = await CreateEscalationAsync(
                request.SessionId,
                dialogResult.EscalationReason);
            
            return Ok(new DialogResponse
            {
                SessionId = request.SessionId,
                Action = "escalate",
                EscalationReason = dialogResult.EscalationReason,
                AssignedToUserId = escalation.AssignedToUserId
            });
        }

        return Ok(dialogResult);
    }

    /// <summary>
    /// 応答生成エンドポイント
    /// POST /api/v1/aiwindow/generate
    /// </summary>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(GenerateResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GenerateResponse([FromBody] GenerateRequest request)
    {
        var response = await _responseGenerator.GenerateAsync(
            request.SessionId,
            request.TemplateName,
            request.Slots,
            request.Channel,
            request.Tone);

        // AI 応答をログに保存
        await LogConversationAsync(request.SessionId, "ai", response.ResponseText, null);

        return Ok(response);
    }

    /// <summary>
    /// 在庫照会エンドポイント
    /// GET /api/v1/aiwindow/inventory
    /// </summary>
    [HttpGet("inventory")]
    [ProducesResponseType(typeof(InventoryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInventory(
        [FromQuery] string? carModel,
        [FromQuery] int? priceMin,
        [FromQuery] int? priceMax,
        [FromQuery] bool inStockOnly = true)
    {
        var vehicles = await _entityService.QueryAsync("vehicles", filters: new Dictionary<string, object>
        {
            { "in_stock", inStockOnly }
        });

        // 価格フィルター適用
        if (priceMin.HasValue || priceMax.HasValue)
        {
            vehicles = vehicles.Where(v =>
                (!priceMin.HasValue || v.Price >= priceMin.Value) &&
                (!priceMax.HasValue || v.Price <= priceMax.Value)
            ).ToList();
        }

        // 車種フィルター適用
        if (!string.IsNullOrEmpty(carModel))
        {
            vehicles = vehicles.Where(v => v.Model.Contains(carModel)).ToList();
        }

        return Ok(new InventoryResponse
        {
            Vehicles = vehicles.Select(v => new VehicleSummary
            {
                VehicleId = v.VehicleId,
                Model = v.Model,
                Year = v.Year,
                Price = v.Price,
                InStock = v.InStock
            }).ToList(),
            TotalCount = vehicles.Count
        });
    }

    /// <summary>
    /// サービス予約エンドポイント
    /// POST /api/v1/aiwindow/reservation
    /// </summary>
    [HttpPost("reservation")]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateReservation([FromBody] ReservationRequest request)
    {
        // 既存エンティティ service_requests を使用
        var serviceRequest = new
        {
            request_id = $"SRV-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N8}",
            customer_id = request.CustomerId,
            vehicle_id = request.VehicleId,
            service_type = request.ServiceType,
            preferred_date = request.PreferredDate,
            preferred_time_slot = request.PreferredTimeSlot,
            status = "tentative",
            channel = "ai",
            session_id = request.SessionId
        };

        var created = await _entityService.CreateAsync("service_requests", serviceRequest);

        return Created($"/api/v1/aiwindow/reservation/{created.request_id}", new ReservationResponse
        {
            ReservationId = created.request_id,
            ConfirmedSlot = $"{request.PreferredDate} {request.PreferredTimeSlot}",
            Status = "tentative"
        });
    }
}
```

### 3.2 リクエスト・レスポンスモデル

#### `Models/Api/AiWindowModels.cs`

```csharp
namespace NetYamlForge.Models.Api;

// NLU リクエスト
public class NluRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Channel { get; set; } = "web";
    public string? CustomerId { get; set; }
}

// NLU レスポンス
public class NluResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public List<DetectedEntity> Entities { get; set; } = new();
    public double Confidence { get; set; }
    public string NextAction { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public class DetectedEntity
{
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public double Confidence { get; set; }
}

// 対話管理リクエスト
public class DialogRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public List<DetectedEntity> Entities { get; set; } = new();
    public DialogContext? Context { get; set; }
}

public class DialogContext
{
    public List<string> PreviousActions { get; set; } = new();
    public Dictionary<string, object>? CustomerProfile { get; set; }
    public string? VehicleOwned { get; set; }
}

// 対話管理レスポンス
public class DialogResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // respond, ask_question, escalate
    public string ResponseTemplate { get; set; } = string.Empty;
    public Dictionary<string, object> Slots { get; set; } = new();
    public string? EscalationReason { get; set; }
    public string? AssignedToUserId { get; set; }
    public string NextState { get; set; } = string.Empty;
}

// 応答生成リクエスト
public class GenerateRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public Dictionary<string, object> Slots { get; set; } = new();
    public string Channel { get; set; } = "web";
    public string Tone { get; set; } = "formal"; // formal, casual
}

// 応答生成レスポンス
public class GenerateResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string ResponseText { get; set; } = string.Empty;
    public string? ResponseAudioUrl { get; set; }
    public List<QuickReply> QuickReplies { get; set; } = new();
    public string? Error { get; set; }
}

public class QuickReply
{
    public string Label { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
}

// 在庫照会レスポンス
public class InventoryResponse
{
    public List<VehicleSummary> Vehicles { get; set; } = new();
    public int TotalCount { get; set; }
}

public class VehicleSummary
{
    public string VehicleId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Price { get; set; }
    public bool InStock { get; set; }
}

// サービス予約リクエスト
public class ReservationRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string VehicleId { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public string PreferredDate { get; set; } = string.Empty;
    public string PreferredTimeSlot { get; set; } = string.Empty;
}

// サービス予約レスポンス
public class ReservationResponse
{
    public string ReservationId { get; set; } = string.Empty;
    public string ConfirmedSlot { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
}

// エラーレスポンス
public class ErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
}
```

---

## 4. AI コアサービス実装

### 4.1 サービスインターフェース

#### `Services/AI/IAiNluService.cs`

```csharp
namespace NetYamlForge.Services.AI;

/// <summary>
/// 自然言語理解（NLU）サービス
/// </summary>
public interface IAiNluService
{
    /// <summary>
    /// インテント分析を実行
    /// </summary>
    Task<NluResult> AnalyzeIntentAsync(
        string message,
        string sessionId,
        string channel);
}

public class NluResult
{
    public string Intent { get; set; } = string.Empty;
    public List<DetectedEntity> Entities { get; set; } = new();
    public double Confidence { get; set; }
    public string NextAction { get; set; } = string.Empty;
}
```

#### `Services/AI/IAiDialogService.cs`

```csharp
namespace NetYamlForge.Services.AI;

/// <summary>
/// 対話管理サービス
/// </summary>
public interface IAiDialogService
{
    /// <summary>
    /// 次のアクションを決定
    /// </summary>
    Task<DialogResult> DetermineNextActionAsync(
        string sessionId,
        string intent,
        List<DetectedEntity> entities,
        DialogContext context);
}

public class DialogResult
{
    public string Action { get; set; } = string.Empty;
    public string ResponseTemplate { get; set; } = string.Empty;
    public Dictionary<string, object> Slots { get; set; } = new();
    public bool ShouldEscalate { get; set; }
    public string? EscalationReason { get; set; }
    public string NextState { get; set; } = string.Empty;
}
```

#### `Services/AI/IAiResponseGenerator.cs`

```csharp
namespace NetYamlForge.Services.AI;

/// <summary>
/// 応答生成サービス
/// </summary>
public interface IAiResponseGenerator
{
    /// <summary>
    /// テンプレートベースの応答を生成
    /// </summary>
    Task<GeneratedResponse> GenerateAsync(
        string sessionId,
        string templateName,
        Dictionary<string, object> slots,
        string channel,
        string tone);
}

public class GeneratedResponse
{
    public string ResponseText { get; set; } = string.Empty;
    public string? ResponseAudioUrl { get; set; }
    public List<QuickReply> QuickReplies { get; set; } = new();
}
```

### 4.2 実装例（ルールベース + LLM 連携）

#### `Services/AI/AiNluService.cs`

```csharp
namespace NetYamlForge.Services.AI;

/// <summary>
/// NLU サービス実装（ルールベース + LLM 連携ハイブリッド）
/// </summary>
public class AiNluService : IAiNluService
{
    private readonly ILogger<AiNluService> _logger;
    private readonly IAiLlmClient _llmClient;
    private readonly IntentPatternMatcher _patternMatcher;

    public AiNluService(
        ILogger<AiNluService> logger,
        IAiLlmClient llmClient,
        IntentPatternMatcher patternMatcher)
    {
        _logger = logger;
        _llmClient = llmClient;
        _patternMatcher = patternMatcher;
    }

    public async Task<NluResult> AnalyzeIntentAsync(
        string message,
        string sessionId,
        string channel)
    {
        // ステップ 1: ルールベースのパターンマッチング（高速・低コスト）
        var patternResult = _patternMatcher.Match(message);
        if (patternResult.Confidence >= 0.8)
        {
            _logger.LogInformation("Pattern match found: {Intent} (confidence: {Confidence})",
                patternResult.Intent, patternResult.Confidence);
            
            return new NluResult
            {
                Intent = patternResult.Intent,
                Entities = patternResult.Entities,
                Confidence = patternResult.Confidence,
                NextAction = GetNextActionForIntent(patternResult.Intent)
            };
        }

        // ステップ 2: LLM による分析（高精度・高コスト）
        try
        {
            var llmResult = await _llmClient.AnalyzeIntentAsync(message, sessionId);
            
            _logger.LogInformation("LLM analysis completed: {Intent} (confidence: {Confidence})",
                llmResult.Intent, llmResult.Confidence);

            return llmResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM analysis failed, falling back to default intent");
            
            // フォールバック：汎用インテント
            return new NluResult
            {
                Intent = "general_inquiry",
                Entities = new List<DetectedEntity>(),
                Confidence = 0.5,
                NextAction = "ask_for_details"
            };
        }
    }

    private string GetNextActionForIntent(string intent) => intent switch
    {
        "price_inquiry" => "ask_vehicle_model",
        "service_booking" => "ask_preferred_date",
        "test_drive_request" => "ask_customer_info",
        "complaint" => "escalate_to_human",
        _ => "ask_for_details"
    };
}
```

#### `Services/AI/IntentPatternMatcher.cs`

```csharp
namespace NetYamlForge.Services.AI;

/// <summary>
/// インテントパターンマッチャー（YAML 設定駆動）
/// </summary>
public class IntentPatternMatcher
{
    private readonly List<IntentPattern> _patterns;

    public IntentPatternMatcher(IYamlConfigProvider yamlConfig)
    {
        // config/ai_intent_patterns.yml からパターンを読み込み
        _patterns = yamlConfig.Load<List<IntentPattern>>("ai_intent_patterns");
    }

    public PatternMatchResult Match(string message)
    {
        var bestMatch = new PatternMatchResult { Confidence = 0.0 };

        foreach (var pattern in _patterns)
        {
            var confidence = CalculateMatchScore(message, pattern);
            if (confidence > bestMatch.Confidence)
            {
                bestMatch = new PatternMatchResult
                {
                    Intent = pattern.Intent,
                    Confidence = confidence,
                    Entities = ExtractEntities(message, pattern.EntityPatterns)
                };
            }
        }

        return bestMatch;
    }

    private double CalculateMatchScore(string message, IntentPattern pattern)
    {
        // キーワード一致スコア
        var keywordScore = pattern.Keywords
            .Count(k => message.Contains(k)) / (double)pattern.Keywords.Count;

        // 正規表現一致スコア
        var regexScore = pattern.RegexPatterns
            .Select(r => Regex.IsMatch(message, r) ? 1.0 : 0.0)
            .DefaultIfEmpty(0)
            .Max();

        // 加重平均
        return keywordScore * 0.6 + regexScore * 0.4;
    }

    private List<DetectedEntity> ExtractEntities(string message, List<EntityPattern> entityPatterns)
    {
        var entities = new List<DetectedEntity>();

        foreach (var entityPattern in entityPatterns)
        {
            var match = Regex.Match(message, entityPattern.Pattern);
            if (match.Success)
            {
                entities.Add(new DetectedEntity
                {
                    Type = entityPattern.EntityType,
                    Value = match.Value,
                    Confidence = 0.9
                });
            }
        }

        return entities;
    }
}

public class IntentPattern
{
    public string Intent { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = new();
    public List<string> RegexPatterns { get; set; } = new();
    public List<EntityPattern> EntityPatterns { get; set; } = new();
}

public class EntityPattern
{
    public string EntityType { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
}

public class PatternMatchResult
{
    public string Intent { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<DetectedEntity> Entities { get; set; } = new();
}
```

### 4.3 設定ファイル例

#### `config/ai_intent_patterns.yml`

```yaml
# AI インテントパターン定義
- intent: price_inquiry
  keywords:
    - 価格
    - 値段
    - いくら
    - 見積もり
    - 値引き
  regex_patterns:
    - ".*[\\d 万円]+.*"
    - ".*予算.*"
  entity_patterns:
    - entity_type: car_model
      pattern: "(カローラ | クラウン | プリウス|RAV4| ヤリス)"
    - entity_type: budget
      pattern: "(\\d+) 万円"

- intent: service_booking
  keywords:
    - 予約
    - 点検
    - 車検
    - 整備
    - オイル交換
  regex_patterns:
    - ".*[月火水木金土日] 曜.*"
    - ".*\\d+ 時.*"
  entity_patterns:
    - entity_type: service_type
      pattern: "(オイル交換 | 車検 | 点検 | 修理)"
    - entity_type: preferred_date
      pattern: "(\\d+/\\d+|\\d 月\\d+ 日)"

- intent: test_drive_request
  keywords:
    - 試乗
    - テストドライブ
    - 乗り心地
  regex_patterns: []
  entity_patterns:
    - entity_type: car_model
      pattern: "(カローラ | クラウン | プリウス|RAV4| ヤリス)"

- intent: complaint
  keywords:
    - 不満
    - 苦情
    - 困る
    - 問題
    - 故障
  regex_patterns:
    - ".*(?:ダメ | 無理 | ひどい | 最悪).*"
  entity_patterns: []

- intent: general_inquiry
  keywords: []
  regex_patterns: []
  entity_patterns: []
```

---

## 5. マルチチャネル接入層

### 5.1 Web チャット UI

`wwwroot/js/ai-chat-widget.js` が統合 UI 実装です。  
旧 `wwwroot/ai-chat/chat-widget.js` は廃止されました。

### 5.2 LINE 連携

#### `Controllers/Api/LineWebhookController.cs`

```csharp
namespace NetYamlForge.Controllers.Api;

[ApiController]
[Route("api/v1/[controller]")]
public class LineWebhookController : ControllerBase
{
    private readonly IAiWindowService _aiService;
    private readonly ILineApiClient _lineClient;

    public LineWebhookController(IAiWindowService aiService, ILineApiClient lineClient)
    {
        _aiService = aiService;
        _lineClient = lineClient;
    }

    /// <summary>
    /// LINE Webhook 受信エンドポイント
    /// POST /api/v1/linewebhook
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> HandleWebhook([FromBody] LineWebhookRequest request)
    {
        foreach (var @event in request.Events)
        {
            if (@event.Type == "message" && @event.Message.Type == "text")
            {
                // AI 処理
                var aiResponse = await _aiService.ProcessMessageAsync(
                    @event.Source.UserId,
                    @event.Message.Text,
                    "line");

                // LINE に返信
                await _lineClient.ReplyTextAsync(@event.ReplyToken, aiResponse.ResponseText);
                
                if (aiResponse.QuickReplies.Any())
                {
                    await _lineClient.ReplyQuickRepliesAsync(@event.ReplyToken, aiResponse.QuickReplies);
                }
            }
        }

        return Ok();
    }
}

public class LineWebhookRequest
{
    public List<LineEvent> Events { get; set; } = new();
}

public class LineEvent
{
    public string Type { get; set; } = string.Empty;
    public string ReplyToken { get; set; } = string.Empty;
    public LineSource Source { get; set; } = new();
    public LineMessage Message { get; set; } = new();
}

public class LineSource
{
    public string UserId { get; set; } = string.Empty;
}

public class LineMessage
{
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
```

---

## 6. エスカレーション管理フック

### 6.1 エスカレーション自動作成フック

#### `projects/auto-dealer/Hooks/AiEscalationHook.cs`

```csharp
using NetYamlForge.Hooks;

namespace AutoDealer.Hooks;

/// <summary>
/// AI エスカレーション作成時フック
/// - サポートチケット自動作成
/// - 担当者自動割り当て
/// - Slack 通知
/// </summary>
public class AiEscalationHook : IEntityHook
{
    private readonly IDynamicEntityService _entityService;
    private readonly IWebhookService _webhookService;
    private readonly ILogger<AiEscalationHook> _logger;

    public string TargetEntity => "ai_escalations";
    public int Order => 100;

    public AiEscalationHook(
        IDynamicEntityService entityService,
        IWebhookService webhookService,
        ILogger<AiEscalationHook> logger)
    {
        _entityService = entityService;
        _webhookService = webhookService;
        _logger = logger;
    }

    public async Task AfterAsync(HookContext context)
    {
        var escalation = context.Entity;
        
        _logger.LogInformation("Escalation created: {EscalationId} (reason: {Reason})",
            escalation.escalation_id, escalation.reason);

        // 1. サポートチケット自動作成
        var ticket = new
        {
            ticket_id = $"TKT-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N8}",
            inquiry_id = (string)null,
            priority = escalation.priority,
            status = "unassigned",
            description = $"AI エスカレーション：{escalation.reason}",
            session_id = escalation.session_id,
            created_at = DateTime.UtcNow
        };

        var createdTicket = await _entityService.CreateAsync("support_tickets", ticket);

        // 2. 担当者自動割り当て（優先度ベース）
        var assignedUser = await AssignOperatorAsync(escalation.priority);
        if (assignedUser != null)
        {
            await _entityService.UpdateAsync("support_tickets", ticket.ticket_id, new
            {
                assigned_to_user_id = assignedUser.user_id,
                status = "assigned"
            });

            await _entityService.UpdateAsync("ai_escalations", escalation.escalation_id, new
            {
                assigned_to_user_id = assignedUser.user_id
            });
        }

        // 3. Slack 通知
        await _webhookService.SendAsync(new WebhookRequest
        {
            Url = Environment.GetEnvironmentVariable("SLACK_WEBHOOK_URL"),
            Payload = new
            {
                channel = "#ai-escalations",
                text = $"AI エスカレーション通知",
                attachments = new[]
                {
                    new
                    {
                        color = escalation.priority == "urgent" ? "danger" : "warning",
                        fields = new[]
                        {
                            new { title = "エスカレーション ID", value = escalation.escalation_id, @short = true },
                            new { title = "理由", value = escalation.reason, @short = false },
                            new { title = "優先度", value = escalation.priority, @short = true },
                            new { title = "担当者", value = assignedUser?.name ?? "未割り当て", @short = true }
                        }
                    }
                }
            }
        });
    }

    private async Task<dynamic?> AssignOperatorAsync(string priority)
    {
        // 優先度別担当者マスタから検索
        var operators = await _entityService.QueryAsync("support_operators", new Dictionary<string, object>
        {
            { "priority_level", priority },
            { "available", true }
        });

        return operators.FirstOrDefault();
    }
}
```

---

## 7. バッチジョブ設計

### 7.1 AI 対話ログ分析バッチ

#### `jobs/ai_conversation_analysis.yml`

```yaml
job_name: ai_conversation_analysis
display_name: AI 対話ログ分析バッチ
description: 対話ログから KPI 集計・モデル改善用データセット生成

schedule:
  cron: "0 2 * * *"  # 毎日 02:00 実行

type: custom_job
handler: "AutoDealer.Jobs.AiConversationAnalysisJob, AutoDealer"

parameters:
  analysis_date: "{{yesterday}}"  # 前日分を分析
  output_dir: "/var/log/ai-analysis"

onFailure:
  webhook:
    url: "${SLACK_WEBHOOK_URL}"
    payload:
      channel: "#batch-alerts"
      text: "AI 対話分析バッチ失敗"
      attachments:
        - color: danger
          fields:
            - title: 実行日時
              value: "{{execution_time}}"
              short: true
            - title: エラー
              value: "{{error_message}}"
              short: false
```

#### `jobs/sql/ai_kpi_daily.sql`

```sql
-- AI 対話 KPI 日次集計
INSERT INTO ai_daily_kpi (
    kpi_date,
    total_sessions,
    completed_sessions,
    escalated_sessions,
    avg_confidence_score,
    avg_session_duration_sec,
    resolution_rate
)
SELECT
    DATE(@analysis_date) AS kpi_date,
    COUNT(DISTINCT session_id) AS total_sessions,
    COUNT(DISTINCT CASE WHEN status = 'completed' THEN session_id END) AS completed_sessions,
    COUNT(DISTINCT CASE WHEN status = 'escalated' THEN session_id END) AS escalated_sessions,
    AVG(confidence_score) AS avg_confidence_score,
    AVG(TIMESTAMPDIFF(SECOND, started_at, ended_at)) AS avg_session_duration_sec,
    CAST(COUNT(DISTINCT CASE WHEN status = 'completed' THEN session_id END) AS FLOAT) / 
        COUNT(DISTINCT session_id) * 100 AS resolution_rate
FROM ai_sessions
WHERE DATE(started_at) = @analysis_date;
```

### 7.2 未解決セッションエスカレーションバッチ

#### `jobs/unresolved_ai_session_alert.yml`

```yaml
job_name: unresolved_ai_session_alert
display_name: 未解決 AI セッション監視
description: 長時間放置された AI セッションを検知し管理者へ通知

schedule:
  cron: "0 */2 * * *"  # 2 時間ごと実行

type: sql_to_custom
sql_file: "jobs/sql/unresolved_sessions.sql"
handler: "AutoDealer.Jobs.UnresolvedSessionAlertJob, AutoDealer"

onFailure:
  webhook:
    url: "${SLACK_WEBHOOK_URL}"
```

#### `jobs/sql/unresolved_sessions.sql`

```sql
-- 2 時間以上放置されたアクティブセッションを抽出
SELECT
    s.session_id,
    s.channel,
    s.customer_id,
    s.started_at,
    TIMESTAMPDIFF(MINUTE, s.started_at, NOW()) AS elapsed_minutes,
    c.name AS customer_name,
    c.phone
FROM ai_sessions s
LEFT JOIN customers c ON s.customer_id = c.customer_id
WHERE s.status = 'active'
  AND s.started_at < DATE_SUB(NOW(), INTERVAL 2 HOUR)
ORDER BY elapsed_minutes DESC;
```

---

## 8. ダッシュボード設計

### 8.1 `dashboard.yml`（AI 窓口 KPI）

```yaml
cards:
  - title: "本日の AI 対話セッション数"
    sql: |
      SELECT COUNT(*) 
      FROM ai_sessions 
      WHERE DATE(started_at) = CURRENT_DATE
    format: number

  - title: "AI 解決率"
    sql: |
      SELECT 
        CAST(COUNT(CASE WHEN status = 'completed' THEN 1 END) AS FLOAT) / 
        COUNT(*) * 100
      FROM ai_sessions
      WHERE DATE(started_at) = CURRENT_DATE
    format: percent
    decimals: 1

  - title: "平均応答時間（秒）"
    sql: |
      SELECT AVG(response_time_sec)
      FROM ai_conversation_logs
      WHERE DATE(timestamp) = CURRENT_DATE
    format: number
    decimals: 2

  - title: "エスカレーション件数"
    sql: |
      SELECT COUNT(*)
      FROM ai_escalations
      WHERE DATE(escalated_at) = CURRENT_DATE
    format: number

charts:
  - title: "セッション数推移（7 日間）"
    type: line
    sql: |
      SELECT 
        DATE(started_at) AS date,
        COUNT(*) AS session_count
      FROM ai_sessions
      WHERE started_at >= DATE('now', '-7 days')
      GROUP BY DATE(started_at)
      ORDER BY date
    xColumn: date
    yColumns: [session_count]

  - title: "インテント別内訳"
    type: pie
    sql: |
      SELECT 
        COALESCE(last_intent, 'unknown') AS intent,
        COUNT(*) AS count
      FROM ai_sessions
      WHERE DATE(started_at) = CURRENT_DATE
      GROUP BY last_intent
      ORDER BY count DESC
      LIMIT 10
    labelColumn: intent
    valueColumn: count

  - title: "チャネル別セッション数"
    type: bar
    sql: |
      SELECT 
        channel,
        COUNT(*) AS count
      FROM ai_sessions
      WHERE DATE(started_at) = CURRENT_DATE
      GROUP BY channel
    xColumn: channel
    yColumns: [count]
```

---

## 9. 実装フェーズと工数

### 9.1 フェーズ分け

| フェーズ | 内容 | 工数 | 成果物 |
|---------|------|------|--------|
| **Phase 0** | 基盤整備（AI 専用エンティティ・API） | 40h | エンティティ 4 定義、API コントローラー |
| **Phase 1** | ルールベース NLU 実装 | 24h | パターンマッチャー、YAML 設定 |
| **Phase 2** | Web チャット UI 実装 | 32h | チャットウィジェット、管理画面 |
| **Phase 3** | エスカレーション管理 | 16h | フック、Slack 通知 |
| **Phase 4** | バッチジョブ・ダッシュボード | 24h | KPI 集計、可視化 |
| **Phase 5** | LLM 連携（オプション） | 40h | OpenAI/Anthropic 連携 |
| **Phase 6** | LINE 連携（オプション） | 32h | Webhook 受信、返信 |
| **合計** | | **208h** | |

### 9.2 推奨着手順序

```
Week 1-2: Phase 0（エンティティ定義・API 基盤）
  - dotnet run -- --init-project --project=auto-dealer
  - 4 エンティティ YAML 作成
  - AiWindowController 実装

Week 3: Phase 1（ルールベース NLU）
  - IntentPatternMatcher 実装
  - ai_intent_patterns.yml 定義（50+ パターン）

Week 4-5: Phase 2（Web チャット UI）
  - chat-widget.js 実装
  - 管理画面（対話ログ閲覧）

Week 6: Phase 3（エスカレーション）
  - AiEscalationHook 実装
  - Slack 通知テスト

Week 7: Phase 4（バッチ・ダッシュボード）
  - jobs/*.yml 定義
  - dashboard.yml 更新

Week 8-9: Phase 5+（オプション機能）
  - LLM 連携（精度向上）
  - LINE 連携（多渠道化）
```

---

## 10. セキュリティ・プライバシー対策

### 10.1 データ暗号化

```yaml
# entities/ai_conversation_logs.yml
columns:
  message_content:
    type: text
    required: true
    encrypt: true  # 個人情報を含むため暗号化
```

### 10.2 アクセス制御

```yaml
# config/permissions.yml
roles:
  ai_admin:
    entities:
      ai_sessions: [read, write, delete]
      ai_conversation_logs: [read, write, delete]
      ai_escalations: [read, write]
  
  ai_operator:
    entities:
      ai_sessions: [read]
      ai_conversation_logs: [read]
      ai_escalations: [read, write]  # 割り当て済みのみ
  
  general_user:
    entities:
      ai_sessions: [read_own]  # 自分のセッションのみ
```

### 10.3 監査ログ

```csharp
// 全 AI API アクセスをログ記録
public class AiAuditLogFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        // リクエスト内容を監査ログに記録
    }
}
```

---

## 11. 拡張ポイント

### 11.1 感情分析（将来）

```csharp
public interface IAiEmotionAnalyzer
{
    Task<EmotionResult> AnalyzeAsync(string text, string? audioUrl);
}

public class EmotionResult
{
    public string PrimaryEmotion { get; set; } // anger, joy, sadness, fear
    public double Intensity { get; set; } // 0.0-1.0
}
```

### 11.2 パーソナライゼーション（将来）

```csharp
// 顧客履歴に基づく推奨
public class PersonalizedRecommendation
{
    public string CustomerId { get; set; }
    public List<string> PurchaseHistory { get; set; }
    public List<string> ServiceHistory { get; set; }
    public Recommendation Generate() { }
}
```

### 11.3 A/B テスト（将来）

```csharp
public interface IAiExperimentService
{
    Task<string> GetModelVersionAsync(string sessionId);
    Task RecordOutcomeAsync(string sessionId, string outcome);
}
```

---

## 12. まとめ

### 12.1 NetYamlForge 活用ポイント

| 機能 | NetYamlForge 機能 |
|------|------------------|
| AI セッション管理 | エンティティ YAML + CRUD 自動生成 |
| 対話ログ保存 | エンティティ YAML + 監査ログ |
| エスカレーション | フックシステム + サポートチケット連携 |
| 在庫照会 | 既存 API エンドポイント流用 |
| サービス予約 | 既存エンティティ作成 API 流用 |
| KPI ダッシュボード | dashboard.yml + SQL 集計 |
| バッチ処理 | jobs/*.yml + cron スケジュール |
| Slack 通知 | Webhook フック |

### 12.2 外部連携が必要な部分

| 機能 | 外部サービス |
|------|-------------|
| LLM 連携 | OpenAI API / Anthropic API |
| LINE 連携 | LINE Messaging API |
| 音声合成 | Google TTS / Azure Speech |
| メール通知 | SendGrid / AWS SES |
| SMS 通知 | Twilio / MessageBird |

### 12.3 Next Steps

1. **Phase 0 着手**: `dotnet run -- --init-project --project=auto-dealer`
2. **エンティティ定義**: 4 つの新規エンティティ YAML 作成
3. **API 実装**: `AiWindowController.cs` スケルトン作成
4. **パターン定義**: `ai_intent_patterns.yml` で 50+ パターン登録
5. **UI 実装**: Web チャットウィジェット作成
6. **テスト**: 対話フロー・エスカレーション検証

---

## 付録 A: 用語集

| 用語 | 説明 |
|------|------|
| NLU | Natural Language Understanding（自然言語理解） |
| インテント | 顧客の発話に含まれる意図（例：価格照会、予約依頼） |
| エンティティ | 発話から抽出される固有表現（例：車種名、日付、予算） |
| エスカレーション | AI から人間担当者への引き継ぎ |
| クイックリプライ | チャット UI で表示する選択式ボタン |

## 付録 B: インテント一覧（初期 50 パターン）

| インテント | キーワード例 | 次アクション |
|-----------|-------------|-------------|
| price_inquiry | 価格、値段、いくら、見積もり | ask_vehicle_model |
| service_booking | 予約、点検、車検、整備 | ask_preferred_date |
| test_drive_request | 試乗、テストドライブ | ask_customer_info |
| complaint | 不満、苦情、困る、問題 | escalate_to_human |
| general_inquiry | その他 | ask_for_details |
| inventory_check | 在庫、ある？、納期 | ask_vehicle_model |
| trade_inquiry | 下取り、買取、査定 | ask_vehicle_info |
| insurance_inquiry | 保険、補償 | explain_insurance_options |
| finance_inquiry | ローン、金利、分割 | explain_finance_options |
| option_inquiry | オプション、装備 | list_available_options |

---

*文書終了*
