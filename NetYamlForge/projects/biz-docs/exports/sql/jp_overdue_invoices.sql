-- 未回収請求書（issued / overdue）
SELECT
    inv.InvoiceNo,
    cu.Name                                                      AS CustomerName,
    inv.Title,
    inv.IssueDate,
    inv.DueDate,
    ROUND(inv.Total, 0)                                          AS Total,
    CASE
        WHEN inv.DueDate < date('now')
        THEN CAST(julianday('now') - julianday(inv.DueDate) AS INTEGER)
        ELSE 0
    END                                                          AS OverdueDays,
    inv.BankName,
    inv.BranchName,
    inv.AccountNo,
    inv.AccountHolder,
    inv.PreparedBy
FROM JpInvoice inv
JOIN Customer cu ON cu.Id = inv.CustomerId
WHERE inv.Status IN ('issued', 'overdue')
ORDER BY OverdueDays DESC, inv.DueDate ASC
