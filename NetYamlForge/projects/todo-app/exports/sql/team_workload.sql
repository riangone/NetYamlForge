-- 担当者別の作業負荷サマリ
SELECT
    t.AssignedTo,
    COUNT(*)                                            AS TotalTasks,
    SUM(CASE WHEN t.Status = 'done' THEN 1 ELSE 0 END) AS DoneTasks,
    SUM(CASE WHEN t.Status IN ('in_progress','review') THEN 1 ELSE 0 END) AS ActiveTasks,
    SUM(CASE WHEN t.DueDate < date('now')
             AND t.Status NOT IN ('done','cancelled') THEN 1 ELSE 0 END) AS OverdueTasks,
    ROUND(SUM(t.EstimatedHours), 1)                    AS TotalEstimatedHours,
    ROUND(SUM(t.ActualHours), 1)                       AS TotalActualHours
FROM Task t
WHERE t.AssignedTo IS NOT NULL AND t.AssignedTo != ''
GROUP BY t.AssignedTo
ORDER BY ActiveTasks DESC, TotalTasks DESC
