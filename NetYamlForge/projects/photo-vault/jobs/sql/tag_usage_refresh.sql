-- 统计各标签的使用次数
SELECT
    t.tag_id,
    t.name,
    COUNT(pt.photo_id) AS usage_count
FROM tags t
LEFT JOIN photo_tags pt ON t.tag_id = pt.tag_id
GROUP BY t.tag_id, t.name
ORDER BY usage_count DESC;
