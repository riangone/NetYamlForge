-- 非アクティブ会話エクスポート用 SQL
-- 一定期間更新されていない会話を抽出
SELECT
    c.Id,
    c.Title,
    c.Status,
    m.DisplayName AS ModelName,
    c.MessageCount,
    c.TotalTokens,
    c.Language,
    c.Theme,
    c.CreatedAt,
    c.UpdatedAt,
    CAST(julianday('now') - julianday(c.UpdatedAt) AS INTEGER) AS DaysSinceUpdate
FROM conversations c
LEFT JOIN ai_models m ON c.ModelId = m.Id
WHERE c.Status IN ('paused', 'archived')
  OR c.UpdatedAt < date('now', '-30 days')
ORDER BY c.UpdatedAt DESC;
