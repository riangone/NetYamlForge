-- 翌日の予約一覧を取得する SQL
SELECT
    sa.appointment_id,
    sa.customer_id,
    c.name AS customer_name,
    c.phone,
    c.email,
    c.preferred_contact,
    sa.appointment_type,
    sa.preferred_date,
    sa.service_menu,
    sa.status
FROM service_appointments sa
INNER JOIN customers c ON sa.customer_id = c.customer_id
WHERE date(sa.preferred_date) = date('now', '+1 day')
  AND sa.status IN ('pending', 'confirmed')
ORDER BY sa.preferred_date ASC;
