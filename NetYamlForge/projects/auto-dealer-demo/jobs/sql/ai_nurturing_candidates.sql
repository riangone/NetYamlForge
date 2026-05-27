-- AI 育成タスク候補抽出
-- スコア30-79で最終接触から3日以上経過したリードを抽出します
SELECT
    l.lead_id,
    c.name AS customer_name,
    c.mobile AS contact_phone,
    c.email,
    c.preferred_contact,
    l.vehicle_interest,
    l.budget,
    l.lead_score,
    l.status,
    l.assigned_to_user_id,
    l.last_contact_at,
    l.created_at,
    CASE
        WHEN l.last_contact_at IS NULL THEN CAST(JULIANDAY('now') - JULIANDAY(l.created_at) AS INTEGER)
        ELSE CAST(JULIANDAY('now') - JULIANDAY(l.last_contact_at) AS INTEGER)
    END AS days_since_last_contact,
    c.tier_level
FROM sales_leads l
INNER JOIN customers c ON l.customer_id = c.customer_id
WHERE l.lead_score BETWEEN 30 AND 79
  AND l.status NOT IN ('won', 'lost')
  AND (
      l.last_contact_at IS NULL
      OR CAST(JULIANDAY('now') - JULIANDAY(l.last_contact_at) AS INTEGER) >= 3
  )
ORDER BY l.lead_score DESC,
         days_since_last_contact DESC;
