-- AI リード再スコアリング
-- 7日以上未更新で、再スコアリングが必要なリードを抽出します
SELECT
    l.lead_id,
    c.name AS customer_name,
    l.vehicle_interest,
    l.lead_score AS current_score,
    l.status,
    l.assigned_to_user_id,
    l.last_contact_at,
    l.created_at,
    l.updated_at,
    CAST(JULIANDAY('now') - JULIANDAY(COALESCE(l.updated_at, l.created_at)) AS INTEGER) AS days_since_update,
    COALESCE(la.activity_count, 0) AS activity_count
FROM sales_leads l
INNER JOIN customers c ON l.customer_id = c.customer_id
LEFT JOIN (
    SELECT lead_id, COUNT(*) AS activity_count
    FROM lead_activities
    WHERE created_at >= date('now', '-30 days')
    GROUP BY lead_id
) la ON l.lead_id = la.lead_id
WHERE l.status NOT IN ('won', 'lost')
  AND (
      l.updated_at IS NULL
      OR CAST(JULIANDAY('now') - JULIANDAY(l.updated_at) AS INTEGER) >= 7
  )
ORDER BY l.lead_score DESC,
         days_since_update DESC;
