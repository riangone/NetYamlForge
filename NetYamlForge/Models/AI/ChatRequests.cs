namespace NetYamlForge.Models.AI;

// 共通チャット API リクエスト DTO
public record ChatStartSessionRequest(string? Channel, string? GuestSessionId);
public record ChatSendMessageRequest(string Message);
public record ChatFeedbackRequest(int Rating, string? Comment);
public record ChatOperatorReplyRequest(string Message);
public record ChatAcceptHandoverRequest(string HandoverId);
public record ChatResolveRequest(string? ResolutionNotes);
