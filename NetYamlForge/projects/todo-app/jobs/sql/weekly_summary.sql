-- 週次タスクサマリー
-- プロジェクト × ステータス別のタスク数・完了率を集計します。
SELECT
    COALESCE(p.Name, '（プロジェクトなし）')  AS project_name,
    p.Status                                AS project_status,
    COUNT(t.Id)                             AS total_tasks,
    SUM(CASE WHEN t.Status = 'pending'     THEN 1 ELSE 0 END) AS pending,
    SUM(CASE WHEN t.Status = 'in_progress' THEN 1 ELSE 0 END) AS in_progress,
    SUM(CASE WHEN t.Status = 'review'      THEN 1 ELSE 0 END) AS review,
    SUM(CASE WHEN t.Status = 'done'        THEN 1 ELSE 0 END) AS done,
    SUM(CASE WHEN t.Status = 'cancelled'   THEN 1 ELSE 0 END) AS cancelled,
    ROUND(
        SUM(CASE WHEN t.Status = 'done' THEN 1 ELSE 0 END) * 100.0 / COUNT(t.Id),
        1
    )                                       AS completion_rate_pct,
    SUM(COALESCE(t.EstimatedHours, 0))      AS total_estimated_hours,
    SUM(COALESCE(t.ActualHours, 0))         AS total_actual_hours
FROM Project p
LEFT JOIN Task t ON t.ProjectId = p.Id
GROUP BY p.Id, p.Name, p.Status
HAVING COUNT(t.Id) > 0
ORDER BY completion_rate_pct DESC, project_name ASC;
