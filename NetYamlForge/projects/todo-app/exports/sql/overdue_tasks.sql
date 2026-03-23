-- 期限切れ未完了タスク一覧
-- 期限日が過去かつステータスが done/cancelled でないタスクを返す
SELECT
    t.Id,
    t.Title,
    t.Status,
    t.Priority,
    t.DueDate,
    t.AssignedTo,
    p.Name  AS ProjectName,
    CAST(julianday('now') - julianday(t.DueDate) AS INTEGER) AS OverdueDays
FROM tasks t
LEFT JOIN projects p ON p.Id = t.ProjectId
WHERE t.DueDate < date('now')
  AND t.Status NOT IN ('done', 'cancelled')
ORDER BY t.DueDate ASC
