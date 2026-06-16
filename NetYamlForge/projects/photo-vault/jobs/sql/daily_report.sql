-- 每日标注进度报告
SELECT
    date(annotation_at) AS 日期,
    COUNT(*) AS 已标注数量,
    ROUND(AVG(confidence_score), 3) AS 平均置信度,
    COUNT(DISTINCT scene_type) AS 场景种类数,
    SUM(CASE WHEN person_count > 0 THEN 1 ELSE 0 END) AS 含人照片数,
    SUM(CASE WHEN ocr_text IS NOT NULL AND ocr_text != '' THEN 1 ELSE 0 END) AS 含 OCR 照片数,
    annotation_model AS 使用模型
FROM photos
WHERE annotation_status = 'done'
  AND date(annotation_at) = date('now', '-1 day')
GROUP BY date(annotation_at), annotation_model
ORDER BY 日期 DESC;
