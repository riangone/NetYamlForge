-- 週次レポート用 SQL
SELECT 
    fd.Id,
    fd.Title,
    fd.Category,
    fd.Status,
    fd.ViewCount,
    fd.Rating,
    fd.PublishedDate,
    fc.TextField AS Author
FROM FilterDemo fd
LEFT JOIN FormComponent fc ON fd.Author = fc.TextField
WHERE fd.PublishedDate >= date('now', '-7 days')
ORDER BY fd.ViewCount DESC;
