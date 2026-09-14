-- Stale Ticket Reminder - サンプルクエリ
-- このファイルはバッチジョブスキャフォールダーによって生成されました

-- 例：日次統計データの集計
SELECT 
    DATE('now') AS stat_date,
    COUNT(*) AS total_count,
    SUM(amount) AS total_amount
FROM orders
WHERE order_date >= DATE('now', '-1 day')
  AND order_date < DATE('now')
GROUP BY DATE('now');

-- 出力結果は CSV ファイルに書き出されます
