-- 未収金一覧（issued / overdue）
SELECT
    inv.InvoiceNo,
    cu.Name                                                    AS CustomerName,
    inv.IssueDate,
    inv.DueDate,
    inv.Currency,
    ROUND(inv.Total, 2)                                        AS Total,
    ROUND(inv.PaidAmount, 2)                                   AS PaidAmount,
    ROUND(inv.Total - inv.PaidAmount, 2)                       AS Balance,
    CASE
        WHEN inv.DueDate < date('now')
        THEN CAST(julianday('now') - julianday(inv.DueDate) AS INTEGER)
        ELSE 0
    END                                                        AS OverdueDays,
    inv.BankName,
    inv.BankAccount,
    inv.PaymentTerms
FROM Invoice inv
JOIN Customer cu ON cu.Id = inv.CustomerId
WHERE inv.Status IN ('issued', 'overdue')
ORDER BY OverdueDays DESC, inv.DueDate ASC
