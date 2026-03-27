-- 在庫切れ・発注点以下の商品を抽出する SQL（参考ジョブ）
SELECT
    p.product_id,
    p.product_name,
    p.category,
    s.quantity,
    s.reorder_point,
    s.reorder_quantity,
    w.warehouse_name
FROM products p
INNER JOIN stock s ON p.product_id = s.product_id
INNER JOIN warehouses w ON s.warehouse_id = w.warehouse_id
WHERE s.quantity <= s.reorder_point
ORDER BY s.quantity ASC, p.product_name ASC;
