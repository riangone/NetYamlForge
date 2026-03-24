-- 進行中・レビュー中タスクを優先度順に一覧
SELECT
    t.Title,
    t.Priority,
    t.Status,
    t.AssignedTo,
    t.DueDate,
    t.EstimatedHours,
    t.ActualHours,
    p.Name   AS ProjectName,
    c.Name   AS CategoryName
FROM Task t
LEFT JOIN Project  p ON p.Id = t.ProjectId
LEFT JOIN Category c ON c.Id = t.CategoryId
WHERE t.Status IN ('in_progress', 'review')
ORDER BY
    CASE t.Priority
        WHEN 'urgent' THEN 1
        WHEN 'high'   THEN 2
        WHEN 'medium' THEN 3
        ELSE 4
    END,
    t.DueDate ASC
