-- AI 对话表新增 FSM 状态字段
-- 迁移脚本: 005_add_fsm_state.sql

-- 新增当前状态字段
ALTER TABLE ai_conversations
ADD COLUMN current_state TEXT DEFAULT 'init'
CHECK(current_state IN ('init', 'collect_vehicle', 'collect_date', 'collect_time',
                         'collect_name', 'collect_phone', 'confirming', 'booked',
                         'cancelled', 'escalate'));

-- 新增已收集槽位字段(JSON 格式)
ALTER TABLE ai_conversations
ADD COLUMN collected_slots TEXT;
-- 示例: {"vehicle":"RAV4","date":"2026-04-15","time":"10:00","name":"张三","phone":"138****5678"}

-- 新增连续低置信度计数
ALTER TABLE ai_conversations
ADD COLUMN low_confidence_count INTEGER DEFAULT 0;

-- 新增人工坐席标记
ALTER TABLE ai_conversations
ADD COLUMN escalated_to TEXT;
-- 示例: "agent_wang" 或 "queue_default"

-- 创建索引加速状态查询
CREATE INDEX IF NOT EXISTS idx_ai_conversations_state
ON ai_conversations(current_state, updated_at);

-- 创建索引加速人工队列查询
CREATE INDEX IF NOT EXISTS idx_ai_conversations_escalated
ON ai_conversations(escalated_to, current_state)
WHERE current_state = 'escalate';
