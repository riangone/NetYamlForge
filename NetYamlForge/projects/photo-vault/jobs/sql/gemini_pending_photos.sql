-- Gemini API 待处理照片队列
-- 取出 provider=gemini 且 status=queued 的任务，按优先级排序，限制批次大小
SELECT
    q.queue_id,
    q.photo_id,
    q.file_path,
    q.priority,
    q.retry_count,
    p.file_name,
    p.file_size_bytes,
    p.width,
    p.height,
    p.taken_at
FROM processing_queue q
JOIN photos p ON p.photo_id = q.photo_id
WHERE q.status = 'queued'
  AND (q.provider = 'gemini' OR q.provider IS NULL AND 'gemini' = (
      SELECT value FROM app_settings WHERE key = 'default_annotation_provider' LIMIT 1
  ))
  AND q.retry_count <= 3
  AND p.deleted_at IS NULL
ORDER BY q.priority DESC, q.queued_at ASC
LIMIT 5
