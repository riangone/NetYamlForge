-- 高価格製品用 SQL
SELECT 
    Id,
    ProductName,
    Category,
    Price,
    Stock,
    Rating,
    ReleaseDate,
    IsActive
FROM ExportDemo
WHERE Price >= 50000
ORDER BY Price DESC;
