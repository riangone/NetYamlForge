-- 日次エクスポート用 SQL
SELECT 
    Id,
    TextField,
    EmailField,
    NumberField,
    DateField,
    BoolToggle,
    CreatedAt,
    UpdatedAt
FROM FormComponent
ORDER BY CreatedAt DESC;
