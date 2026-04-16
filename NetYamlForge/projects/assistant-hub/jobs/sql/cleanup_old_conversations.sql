-- 90日以上前のアーカイブ済み会話とメッセージの一覧を取得
-- バッチジョブの出力ファイルとして CSV に記録されます

SELECT
    c.id,
    c.title,
    c.model_name,
    (SELECT COUNT(*) FROM messages WHERE conversation_id = c.id) as message_count,
    (SELECT SUM(tokens_used) FROM messages WHERE conversation_id = c.id) as total_tokens,
    c.updated_at,
    CAST(julianday('now') - julianday(c.updated_at) AS INTEGER) as days_ago
FROM conversations c
WHERE c.is_archived = 1
  AND c.updated_at < datetime('now', '-90 days')
ORDER BY c.updated_at ASC;
