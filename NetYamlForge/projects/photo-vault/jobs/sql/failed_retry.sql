-- 查询需要重试的失败任务
SELECT queue_id, photo_id, file_path, retry_count
FROM processing_queue
WHERE status = 'failed'
  AND retry_count <= 3;
