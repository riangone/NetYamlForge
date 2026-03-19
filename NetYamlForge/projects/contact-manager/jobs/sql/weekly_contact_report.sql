-- 每周联系人统计报告
-- 生成上周的联系人统计数据

SELECT 
    '上周新增联系人' as metric,
    COUNT(*) as value
FROM contact
WHERE createdAt >= date('now', '-7 days')

UNION ALL

SELECT 
    '上周更新联系人' as metric,
    COUNT(*) as value
FROM contact
WHERE updatedAt >= date('now', '-7 days')
  AND updatedAt != createdAt

UNION ALL

SELECT 
    '上周交互总数' as metric,
    COUNT(*) as value
FROM interaction
WHERE scheduledAt >= date('now', '-7 days')

UNION ALL

SELECT 
    '按状态统计 - 活跃' as metric,
    COUNT(*) as value
FROM contact
WHERE status = 'active'

UNION ALL

SELECT 
    '按状态统计 - 潜在' as metric,
    COUNT(*) as value
FROM contact
WHERE status = 'lead'

UNION ALL

SELECT 
    '按优先级统计 - 高' as metric,
    COUNT(*) as value
FROM contact
WHERE priority = 'high'

UNION ALL

SELECT 
    '按类型统计 - 电话' as metric,
    COUNT(*) as value
FROM interaction
WHERE type = 'call'
  AND scheduledAt >= date('now', '-7 days')

UNION ALL

SELECT 
    '按类型统计 - 邮件' as metric,
    COUNT(*) as value
FROM interaction
WHERE type = 'email'
  AND scheduledAt >= date('now', '-7 days')

UNION ALL

SELECT 
    '按类型统计 - 会议' as metric,
    COUNT(*) as value
FROM interaction
WHERE type = 'meeting'
  AND scheduledAt >= date('now', '-7 days')
