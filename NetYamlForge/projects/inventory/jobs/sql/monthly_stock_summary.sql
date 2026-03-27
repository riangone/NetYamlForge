-- 月次入出庫サマリー
-- 前月（実行月 - 1 ヶ月）の入出庫を商品・種別・倉庫ごとに集計します。
SELECT
    STRFTIME('%Y-%m', sm.MovedAt)               AS year_month,
    p.Code                                      AS product_code,
    p.Name                                      AS product_name,
    COALESCE(cat.Name, '（未設定）')            AS category_name,
    w.Name                                      AS warehouse_name,
    sm.MovementType                             AS movement_type,
    COUNT(*)                                    AS movement_count,
    SUM(sm.Quantity)                            AS total_quantity,
    SUM(sm.Quantity * COALESCE(sm.UnitPrice, 0))AS total_amount
FROM StockMovement sm
INNER JOIN Product  p   ON sm.ProductId   = p.Id
INNER JOIN Warehouse w  ON sm.WarehouseId = w.Id
LEFT  JOIN Category cat ON p.CategoryId   = cat.Id
WHERE sm.MovedAt >= DATE('now', 'start of month', '-1 month')
  AND sm.MovedAt <  DATE('now', 'start of month')
GROUP BY
    STRFTIME('%Y-%m', sm.MovedAt),
    p.Code,
    p.Name,
    cat.Name,
    w.Name,
    sm.MovementType
ORDER BY
    year_month ASC,
    p.Code     ASC,
    w.Name     ASC,
    sm.MovementType ASC;
