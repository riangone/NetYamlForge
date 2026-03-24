-- 納品書明細（納品書＋品目を1行に展開）
SELECT
    d.DeliveryNo,
    cu.Name              AS CustomerName,
    d.Title,
    d.DeliveryDate,
    d.DeliveryPlace,
    d.Status,
    di.LineNo,
    di.ItemCode,
    di.ItemName,
    di.Spec,
    di.Unit,
    di.Quantity,
    di.UnitPrice,
    di.Amount,
    di.TaxRate,
    d.Total              AS DeliveryTotal,
    d.Subtotal10,
    d.TaxAmount10,
    d.Subtotal8,
    d.TaxAmount8,
    d.InspectionPeriodDays,
    d.ReceiptConfirmedDate,
    d.PreparedBy
FROM JpDelivery d
JOIN Customer       cu ON cu.Id = d.CustomerId
JOIN JpDeliveryItem di ON di.DeliveryId = d.Id
ORDER BY d.DeliveryDate DESC, d.Id, di.LineNo
