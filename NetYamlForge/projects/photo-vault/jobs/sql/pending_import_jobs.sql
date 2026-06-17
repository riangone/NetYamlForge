-- 查询待处理的目录扫描导入任务（每次取 1 个，按优先顺序）
SELECT
    j.job_id,
    j.job_type,
    j.source_path,
    j.recursive,
    j.file_filter,
    j.ai_provider,
    j.album_id,
    j.created_by,
    j.created_at
FROM import_jobs j
WHERE j.status = 'queued'
  AND j.job_type = 'directory'
ORDER BY j.created_at ASC
LIMIT 1;
