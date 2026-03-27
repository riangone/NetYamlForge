SELECT
    t.Id,
    t.Title,
    t.AssignedTo,
    t.DueDate,
    t.Priority,
    t.Status
FROM Task t
WHERE
    t.Status NOT IN ('completed')
    AND date(t.DueDate) = date('now', '+1 day', 'localtime')
ORDER BY t.Priority DESC, t.Title
