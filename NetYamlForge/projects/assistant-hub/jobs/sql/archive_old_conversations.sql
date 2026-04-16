-- 古い会話のアーカイブ SQL
-- 30日以上前に更新された非アクティブな会話をアーカイブ対象として出力
SELECT
    Id,
    Title,
    Status,
    MessageCount,
    TotalTokens,
    UpdatedAt,
    CAST(julianday('now') - julianday(UpdatedAt) AS INTEGER) AS DaysSinceUpdate
FROM conversations
WHERE Status != 'active'
  AND UpdatedAt < date('now', '-30 days')
ORDER BY UpdatedAt ASC;
