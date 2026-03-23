-- 期限切れタスクレポート
-- 実行日時点で期限を過ぎた未完了タスクを出力します。
SELECT
    t.Id                                    AS task_id,
    t.Title                                 AS title,
    t.Status                                AS status,
    t.Priority                              AS priority,
    t.DueDate                               AS due_date,
    CAST(JULIANDAY('now') - JULIANDAY(t.DueDate) AS INTEGER)
                                            AS overdue_days,
    t.AssignedTo                            AS assigned_to,
    COALESCE(p.Name, '（未設定）')           AS project_name,
    COALESCE(c.Name, '（未設定）')           AS category_name
FROM Task t
LEFT JOIN Project  p ON t.ProjectId  = p.Id
LEFT JOIN Category c ON t.CategoryId = c.Id
WHERE t.Status NOT IN ('done', 'cancelled')
  AND t.DueDate IS NOT NULL
  AND t.DueDate < DATE('now')
ORDER BY overdue_days DESC, t.Priority DESC, t.Id ASC;
