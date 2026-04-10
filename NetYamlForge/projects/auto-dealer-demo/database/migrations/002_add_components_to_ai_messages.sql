-- 添加 components_json 列到 ai_messages 表
-- 用于存储 AI 返回的富 UI 组件数据

ALTER TABLE ai_messages ADD COLUMN components_json TEXT;
