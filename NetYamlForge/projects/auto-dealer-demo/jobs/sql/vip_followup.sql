-- フォローアップが必要な VIP 顧客を抽出する SQL
SELECT
    c.customer_id,
    c.name,
    c.phone,
    c.email,
    c.tier_level,
    MAX(sa.preferred_date) AS last_appointment_date,
    COUNT(sa.appointment_id) AS total_appointments
FROM customers c
LEFT JOIN service_appointments sa ON c.customer_id = sa.customer_id
WHERE c.tier_level IN ('vip', 'platinum')
GROUP BY c.customer_id, c.name, c.phone, c.email, c.tier_level
HAVING 
    (last_appointment_date IS NULL) 
    OR (date(last_appointment_date) < date('now', '-30 days'))
ORDER BY 
    CASE c.tier_level 
        WHEN 'platinum' THEN 1 
        WHEN 'vip' THEN 2 
        ELSE 3 
    END,
    last_appointment_date ASC;
