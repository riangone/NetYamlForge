-- 発注点以下在庫アラートレポート
-- Stock.Quantity が Product.ReorderPoint 以下の商品を倉庫別に出力します。
-- ReorderPoint が NULL または 0 の商品はスキップします。
SELECT
    p.Code                                      AS product_code,
    p.Name                                      AS product_name,
    COALESCE(cat.Name, '（未設定）')            AS category_name,
    COALESCE(sup.Name, '（未設定）')            AS supplier_name,
    p.Unit                                      AS unit,
    p.ReorderPoint                              AS reorder_point,
    s.Quantity                                  AS current_stock,
    p.ReorderPoint - s.Quantity                 AS shortage,
    w.Name                                      AS warehouse_name,
    s.UpdatedAt                                 AS stock_updated_at
FROM Stock s
INNER JOIN Product  p   ON s.ProductId   = p.Id
INNER JOIN Warehouse w  ON s.WarehouseId = w.Id
LEFT  JOIN Category cat ON p.CategoryId  = cat.Id
LEFT  JOIN Supplier sup ON p.SupplierId  = sup.Id
WHERE p.ReorderPoint IS NOT NULL
  AND p.ReorderPoint > 0
  AND s.Quantity <= p.ReorderPoint
ORDER BY shortage DESC, p.Code ASC;
