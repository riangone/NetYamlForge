-- 每周联系人统计报告
SELECT 
    '公司总数' as metric, COUNT(*) as value FROM company
UNION ALL
SELECT 
    '联系人总数' as metric, COUNT(*) as value FROM contact
UNION ALL
SELECT 
    '活跃联系人' as metric, COUNT(*) as value FROM contact WHERE status = 'active'
UNION ALL
SELECT 
    '本周新增联系人' as metric, COUNT(*) as value FROM contact WHERE createdAt >= datetime('now', '-7 days')
UNION ALL
SELECT 
    '交互总数' as metric, COUNT(*) as value FROM interaction
UNION ALL
SELECT 
    '已完成交互' as metric, COUNT(*) as value FROM interaction WHERE status = 'completed'
