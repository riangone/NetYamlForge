-- 前日に完了したタスクをプロジェクト別に集計
SELECT
    project_name,
    COUNT(*)                         AS completed_count,
    AVG(
        CAST((julianday(updated_at) - julianday(created_at)) AS REAL)
    )                                AS avg_days_to_complete,
    MIN(updated_at)                  AS first_completed_at,
    MAX(updated_at)                  AS last_completed_at
FROM tasks
WHERE status = 'done'
  AND date(updated_at) = date('now', '-1 day')
GROUP BY project_name
ORDER BY completed_count DESC
