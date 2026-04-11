namespace NetYamlForge.AI.Models;

// 共通チャット API リクエスト DTO
public record ChatStartSessionRequest(string? Channel, string? GuestSessionId);

/// <summary>チャットメッセージ送信リクエスト</summary>
/// <param name="Message">メッセージ本文</param>
/// <param name="Provider">使用する AI プロバイダー（null の場合はデフォルト設定を使用）</param>
public record ChatSendMessageRequest(string Message, string? Provider = null);
public record ChatFeedbackRequest(int Rating, string? Comment);
public record ChatOperatorReplyRequest(string Message);
public record ChatAcceptHandoverRequest(string HandoverId);
public record ChatResolveRequest(string? ResolutionNotes);
