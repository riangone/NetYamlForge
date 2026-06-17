-- Antigravity CLI 待处理照片队列
-- 取出 provider=antigravity 且 status=queued 的任务，按优先级排序
SELECT
    q.queue_id,
    q.photo_id,
    q.file_path,
    q.priority,
    q.retry_count,
    p.file_name,
    p.file_size,
    p.width,
    p.height,
    p.taken_at
FROM processing_queue q
JOIN photos p ON p.photo_id = q.photo_id
WHERE q.status = 'queued'
  AND q.provider = 'antigravity'
  AND q.retry_count <= 3
  AND p.deleted_at IS NULL
ORDER BY q.priority DESC, q.queued_at ASC
LIMIT 10
