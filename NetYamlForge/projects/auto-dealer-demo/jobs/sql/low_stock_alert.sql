-- 車両在庫不足アラート：販売可能台数が少ない車種を抽出する SQL
SELECT
    vehicle_type AS 車両タイプ,
    fuel_type AS 燃料種別,
    COUNT(*) AS 在庫台数,
    MIN(price) AS 最低価格,
    MAX(price) AS 最高価格,
    ROUND(AVG(price), 0) AS 平均価格
FROM vehicles
WHERE status = 'available'
  AND customer_id IS NULL
GROUP BY vehicle_type, fuel_type
HAVING COUNT(*) <= 2
ORDER BY COUNT(*) ASC, vehicle_type ASC;
