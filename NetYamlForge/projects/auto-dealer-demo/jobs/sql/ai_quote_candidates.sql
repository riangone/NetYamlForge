-- AI 見積候補抽出
-- スコア80以上で見積未作成のリードを抽出します
SELECT
    l.lead_id,
    c.name AS customer_name,
    c.mobile AS contact_phone,
    c.email,
    l.vehicle_interest,
    l.budget,
    l.lead_score,
    l.status,
    l.assigned_to_user_id,
    l.last_contact_at,
    l.created_at,
    v.vehicle_id,
    v.maker,
    v.brand,
    v.model,
    v.grade,
    v.year,
    v.price AS vehicle_price,
    v.status AS vehicle_status
FROM sales_leads l
INNER JOIN customers c ON l.customer_id = c.customer_id
LEFT JOIN vehicles v ON (
    v.vehicle_id = l.vehicle_interest
    OR v.model LIKE '%' || l.vehicle_interest || '%'
    OR l.vehicle_interest LIKE '%' || v.model || '%'
)
WHERE l.lead_score >= 80
  AND l.status NOT IN ('won', 'lost')
  AND NOT EXISTS (
      SELECT 1 FROM ai_quotes q
      WHERE q.lead_id = l.lead_id
        AND q.status NOT IN ('rejected', 'expired')
  )
ORDER BY l.lead_score DESC,
         l.created_at ASC;
