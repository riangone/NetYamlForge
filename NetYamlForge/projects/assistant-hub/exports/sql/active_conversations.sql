-- アクティブな会話（アーカイブされていない）を取得
SELECT
    c.id,
    c.title,
    p.name as provider_name,
    c.model_name,
    (SELECT COUNT(*) FROM messages WHERE conversation_id = c.id) as message_count,
    (SELECT SUM(tokens_used) FROM messages WHERE conversation_id = c.id) as total_tokens,
    c.last_activity
FROM conversations c
LEFT JOIN ai_providers p ON c.ai_provider_id = p.id
WHERE c.is_archived = 0
ORDER BY c.last_activity DESC;
