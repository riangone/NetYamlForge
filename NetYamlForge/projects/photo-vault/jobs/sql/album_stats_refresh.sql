-- 刷新各相册的 photo_count 和 annotated_count
SELECT
    a.album_id,
    a.name,
    COUNT(p.photo_id) AS photo_count,
    SUM(CASE WHEN p.annotation_status = 'done' THEN 1 ELSE 0 END) AS annotated_count
FROM albums a
LEFT JOIN photos p ON a.album_id = p.album_id AND p.deleted_at IS NULL
GROUP BY a.album_id, a.name;
