namespace NetYamlForge.Models.AI;

/// <summary>
/// 対話開始リクエスト
/// </summary>
public class StartConversationRequest
{
    /// <summary>
    /// チャネル種別
    /// </summary>
    public string Channel { get; set; } = "web";

    /// <summary>
    /// 顧客 ID（任意、後から紐付け可能）
    /// </summary>
    public string? CustomerId { get; set; }

    /// <summary>
    /// 初期メッセージ（任意）
    /// </summary>
    public string? InitialMessage { get; set; }

    /// <summary>
    /// メタデータ（LINE ユーザー ID など）
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// 対話開始レスポンス
/// </summary>
public class StartConversationResponse
{
    /// <summary>
    /// 対話 ID
    /// </summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// 初期応答メッセージ
    /// </summary>
    public string? WelcomeMessage { get; set; }

    /// <summary>
    /// セッション有効期限（分）
    /// </summary>
    public int SessionTimeoutMinutes { get; set; } = 30;
}

/// <summary>
/// メッセージ送信リクエスト
/// </summary>
public class SendMessageRequest
{
    /// <summary>
    /// 顧客メッセージ
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// メッセージタイプ
    /// </summary>
    public string MessageType { get; set; } = "text";

    /// <summary>
    /// 添付ファイル URL（画像など）
    /// </summary>
    public string? AttachmentUrl { get; set; }
}

/// <summary>
/// メッセージ送信レスポンス
/// </summary>
public class SendMessageResponse
{
    /// <summary>
    /// 対話 ID
    /// </summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// AI 応答メッセージ
    /// </summary>
    public string ResponseText { get; set; } = string.Empty;

    /// <summary>
    /// 判定されたインテント
    /// </summary>
    public string? Intent { get; set; }

    /// <summary>
    /// 置信度
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 抽出されたエンティティ
    /// </summary>
    public Dictionary<string, string>? Entities { get; set; }

    /// <summary>
    /// クイック返信ボタン
    /// </summary>
    public List<QuickReplyButton>? QuickReplies { get; set; }

    /// <summary>
    /// エスカレーション推奨フラグ
    /// </summary>
    public bool SuggestHandover { get; set; }
}

/// <summary>
/// 顧客認証リクエスト
/// </summary>
public class VerifyCustomerRequest
{
    /// <summary>
    /// 電話番号またはメールアドレス
    /// </summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    /// 認証コード（SMS/メールで送信）
    /// </summary>
    public string VerificationCode { get; set; } = string.Empty;
}

/// <summary>
/// 顧客認証レスポンス
/// </summary>
public class VerifyCustomerResponse
{
    /// <summary>
    /// 認証成功フラグ
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 顧客 ID（認証成功時のみ）
    /// </summary>
    public string? CustomerId { get; set; }

    /// <summary>
    /// 顧客名（認証成功時のみ）
    /// </summary>
    public string? CustomerName { get; set; }

    /// <summary>
    /// 顧客ランク
    /// </summary>
    public string? TierLevel { get; set; }

    /// <summary>
    /// エラーメッセージ（認証失敗時のみ）
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 予約作成リクエスト
/// </summary>
public class CreateAppointmentRequest
{
    /// <summary>
    /// 顧客 ID
    /// </summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// サービス種別
    /// </summary>
    public string ServiceType { get; set; } = string.Empty;

    /// <summary>
    /// 車両 ID（任意）
    /// </summary>
    public string? VehicleId { get; set; }

    /// <summary>
    /// 希望日時
    /// </summary>
    public DateTime PreferredDateTime { get; set; }

    /// <summary>
    /// 詳細情報
    /// </summary>
    public Dictionary<string, string>? Details { get; set; }
}
