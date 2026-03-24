-- 報価単明細（見積番号＋品目を1行に展開）
SELECT
    q.QuoteNo,
    cu.Name          AS CustomerName,
    q.IssueDate,
    q.ExpiryDate,
    q.Currency,
    q.Status,
    qi.LineNo,
    qi.PartNo,
    qi.Description,
    qi.Unit,
    qi.Quantity,
    qi.UnitPrice,
    qi.Amount,
    q.Total          AS QuoteTotal
FROM Quotation q
JOIN Customer        cu ON cu.Id = q.CustomerId
JOIN QuotationItem   qi ON qi.QuotationId = q.Id
ORDER BY q.IssueDate DESC, q.Id, qi.LineNo
