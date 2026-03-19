-- 夜间统计作业 - 商品販売統計
-- 前日の販売データを集計して CSV 出力します

-- 日別商品販売統計
SELECT 
    DATE('now') AS stat_date,
    p.ProductId AS product_id,
    p.ProductName AS product_name,
    p.CategoryId AS category_id,
    COUNT(od.OrderDetailId) AS order_count,
    SUM(od.Quantity) AS total_quantity,
    SUM(od.UnitPrice * od.Quantity * (1 - od.Discount)) AS total_amount,
    AVG(od.UnitPrice) AS avg_price,
    AVG(od.Discount) AS avg_discount
FROM Products p
INNER JOIN `Order Details` od ON od.ProductId = p.ProductId
INNER JOIN Orders o ON o.OrderId = od.OrderId
WHERE o.OrderDate >= DATE('now', '-1 day')
  AND o.OrderDate < DATE('now')
GROUP BY p.ProductId, p.ProductName, p.CategoryId
ORDER BY total_amount DESC;
