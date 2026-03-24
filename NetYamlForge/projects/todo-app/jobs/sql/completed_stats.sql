-- 前日に完了したタスクをプロジェクト別に集計
SELECT
    COALESCE(p.Name, '(未割当)')                           AS ProjectName,
    COUNT(*)                                               AS CompletedCount,
    ROUND(AVG(t.ActualHours), 1)                           AS AvgActualHours,
    MIN(t.CompletedAt)                                     AS FirstCompletedAt,
    MAX(t.CompletedAt)                                     AS LastCompletedAt
FROM Task t
LEFT JOIN Project p ON t.ProjectId = p.Id
WHERE t.Status = 'done'
  AND date(t.CompletedAt) = date('now', '-1 day')
GROUP BY t.ProjectId, p.Name
ORDER BY CompletedCount DESC
