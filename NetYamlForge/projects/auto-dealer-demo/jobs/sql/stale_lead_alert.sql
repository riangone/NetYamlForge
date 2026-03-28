-- 停滞リードアラート
-- 7 日以上フォローアップが行われていないリードを抽出します。
-- スコアが高いほど優先度が高い。
SELECT
    l.lead_id,
    c.name AS customer_name,
    c.mobile AS contact,
    c.preferred_contact,
    l.vehicle_interest,
    l.lead_score,
    l.status,
    l.assigned_to_user_id,
    CASE
        WHEN l.last_contact_at IS NULL THEN '未接触'
        ELSE CAST(CAST((JULIANDAY('now') - JULIANDAY(l.last_contact_at)) AS INTEGER) AS TEXT) || '日間未対応'
    END AS stale_duration,
    l.created_at AS lead_created_at
FROM sales_leads l
INNER JOIN customers c ON l.customer_id = c.customer_id
WHERE l.status NOT IN ('won', 'lost')
  AND (
      l.last_contact_at IS NULL
      OR CAST((JULIANDAY('now') - JULIANDAY(l.last_contact_at)) AS INTEGER) >= 7
  )
ORDER BY l.lead_score DESC,
         COALESCE(JULIANDAY(l.last_contact_at), JULIANDAY(l.created_at)) ASC;
