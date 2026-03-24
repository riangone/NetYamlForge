-- 見積書明細（見積書＋品目を1行に展開）
SELECT
    e.EstimateNo,
    cu.Name              AS CustomerName,
    e.Title,
    e.IssueDate,
    e.ExpiryDate,
    e.Status,
    ei.LineNo,
    ei.ItemCode,
    ei.ItemName,
    ei.Spec,
    ei.Unit,
    ei.Quantity,
    ei.UnitPrice,
    ei.Amount,
    ei.TaxRate,
    e.Total              AS EstimateTotal,
    e.Subtotal10,
    e.TaxAmount10,
    e.Subtotal8,
    e.TaxAmount8,
    e.ValidityNote,
    e.PreparedBy
FROM JpEstimate e
JOIN Customer       cu ON cu.Id = e.CustomerId
JOIN JpEstimateItem ei ON ei.EstimateId = e.Id
ORDER BY e.IssueDate DESC, e.Id, ei.LineNo
