-- 当日の AI 対話統計を収集する SQL
SELECT
    date('now') AS stat_date,
    COUNT(*) AS total_conversations,
    SUM(CASE WHEN status = 'completed' THEN 1 ELSE 0 END) AS completed_count,
    SUM(CASE WHEN status = 'escalated' THEN 1 ELSE 0 END) AS escalated_count,
    SUM(CASE WHEN status = 'abandoned' THEN 1 ELSE 0 END) AS abandoned_count,
    ROUND(AVG(last_confidence) * 100, 2) AS avg_confidence,
    ROUND(AVG(sentiment_score), 4) AS avg_sentiment,
    COUNT(DISTINCT customer_id) AS unique_customers
FROM ai_conversations
WHERE date(started_at) = date('now');
