-- 90日以内に期限切れとなる有効な契約
SELECT
    c.ContractNo,
    cu.Name                                                           AS CustomerName,
    c.Title,
    c.ContractType,
    c.StartDate,
    c.EndDate,
    CAST(julianday(c.EndDate) - julianday('now') AS INTEGER)          AS DaysUntilExpiry,
    CASE WHEN c.AutoRenew = 1 THEN 'あり' ELSE 'なし' END            AS AutoRenew,
    CASE WHEN c.IsElectronic = 1 THEN '電子' ELSE '紙' END           AS ContractForm,
    c.JurisdictionCourt,
    c.Status,
    c.OurSignatory,
    c.TheirSignatory,
    c.ContractAmount
FROM JpContract c
JOIN Customer cu ON cu.Id = c.CustomerId
WHERE c.Status = 'active'
  AND c.EndDate IS NOT NULL
  AND c.EndDate BETWEEN date('now') AND date('now', '+90 days')
ORDER BY DaysUntilExpiry ASC
