-- 取出优先级最高的待处理照片（每批最多 10 张）
SELECT
    q.queue_id,
    q.photo_id,
    q.file_path,
    q.priority,
    q.retry_count,
    p.file_format,
    p.width,
    p.height
FROM processing_queue q
JOIN photos p ON q.photo_id = p.photo_id
WHERE q.status = 'queued'
  AND q.retry_count <= 3
ORDER BY q.priority DESC, q.queued_at ASC
LIMIT 10;
