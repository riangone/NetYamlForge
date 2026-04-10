-- =====================================================
-- auto-dealer-demo 数据库迁移脚本
-- 添加 components_json 列到 ai_messages 表
-- 用于存储 AI 返回的富 UI 组件数据
-- =====================================================

-- 检查列是否已存在，如果不存在则添加
-- SQLite 不支持 IF NOT EXISTS for ALTER TABLE，所以使用 try/catch 方式

-- 方法 1: 直接添加（如果列已存在会报错，可以忽略）
ALTER TABLE ai_messages ADD COLUMN components_json TEXT;

-- 如果需要查看迁移结果：
-- SELECT column_name, data_type FROM information_schema.columns WHERE table_name = 'ai_messages' AND column_name = 'components_json';
