-- AI 全面主導化 マイグレーション
-- lead_nurturing_tasks テーブル（既存の entity YAML に対応）
CREATE TABLE IF NOT EXISTS lead_nurturing_tasks (
    task_id VARCHAR(50) NOT NULL PRIMARY KEY,
    lead_id VARCHAR(50) NOT NULL,
    customer_id VARCHAR(50) NOT NULL,
    task_type VARCHAR(30) NOT NULL,
    trigger_reason VARCHAR(300),
    priority_score INTEGER NOT NULL DEFAULT 50,
    status VARCHAR(20) NOT NULL DEFAULT 'pending',
    assigned_to VARCHAR(50),
    ai_recommendation TEXT,
    ai_reasoning TEXT,
    due_date DATETIME,
    completed_at DATETIME,
    completed_by VARCHAR(50),
    result_notes TEXT,
    created_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (lead_id) REFERENCES sales_leads(lead_id),
    FOREIGN KEY (customer_id) REFERENCES customers(customer_id)
);

CREATE INDEX IF NOT EXISTS idx_nurturing_lead ON lead_nurturing_tasks(lead_id);
CREATE INDEX IF NOT EXISTS idx_nurturing_status ON lead_nurturing_tasks(status);
CREATE INDEX IF NOT EXISTS idx_nurturing_priority ON lead_nurturing_tasks(priority_score);
CREATE INDEX IF NOT EXISTS idx_nurturing_due ON lead_nurturing_tasks(due_date);

-- sales_leads に AI 属性カラム追加
ALTER TABLE sales_leads ADD COLUMN ai_touch_count INTEGER DEFAULT 0;
ALTER TABLE sales_leads ADD COLUMN ai_conversion_path TEXT;
ALTER TABLE sales_leads ADD COLUMN ai_first_touch_conversation_id VARCHAR(64);
ALTER TABLE sales_leads ADD COLUMN ai_last_touch_conversation_id VARCHAR(64);

CREATE INDEX IF NOT EXISTS idx_leads_ai_touch ON sales_leads(ai_touch_count);

-- AI 決定テーブル
CREATE TABLE IF NOT EXISTS ai_decisions (
    decision_id VARCHAR(50) NOT NULL PRIMARY KEY,
    decision_type VARCHAR(50) NOT NULL,
    entity_type VARCHAR(50) NOT NULL,
    entity_id VARCHAR(50) NOT NULL,
    ai_reasoning TEXT,
    confidence_score DECIMAL(5,2) NOT NULL DEFAULT 0.00,
    status VARCHAR(20) NOT NULL DEFAULT 'pending',
    requires_human BOOLEAN NOT NULL DEFAULT 0,
    executed_at DATETIME,
    created_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime'))
);

CREATE INDEX IF NOT EXISTS idx_ai_decisions_type ON ai_decisions(decision_type);
CREATE INDEX IF NOT EXISTS idx_ai_decisions_status ON ai_decisions(status);
CREATE INDEX IF NOT EXISTS idx_ai_decisions_entity ON ai_decisions(entity_type, entity_id);

-- AI 見積テーブル
CREATE TABLE IF NOT EXISTS ai_quotes (
    quote_id VARCHAR(50) NOT NULL PRIMARY KEY,
    lead_id VARCHAR(50) NOT NULL,
    customer_id VARCHAR(50) NOT NULL,
    vehicle_id VARCHAR(50) NOT NULL,
    base_price DECIMAL(12,2) NOT NULL,
    discount_amount DECIMAL(12,2) DEFAULT 0,
    final_price DECIMAL(12,2) NOT NULL,
    ai_reasoning TEXT,
    status VARCHAR(20) NOT NULL DEFAULT 'draft',
    valid_until DATETIME,
    created_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (lead_id) REFERENCES sales_leads(lead_id),
    FOREIGN KEY (customer_id) REFERENCES customers(customer_id),
    FOREIGN KEY (vehicle_id) REFERENCES vehicles(vehicle_id)
);

CREATE INDEX IF NOT EXISTS idx_ai_quotes_lead ON ai_quotes(lead_id);
CREATE INDEX IF NOT EXISTS idx_ai_quotes_status ON ai_quotes(status);

-- AI アクションログテーブル
CREATE TABLE IF NOT EXISTS ai_action_log (
    log_id VARCHAR(64) NOT NULL PRIMARY KEY,
    action_type VARCHAR(50) NOT NULL,
    entity_type VARCHAR(50) NOT NULL,
    entity_id VARCHAR(50) NOT NULL,
    ai_model VARCHAR(50),
    prompt_summary TEXT,
    result_summary TEXT,
    execution_ms INTEGER,
    created_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime'))
);

CREATE INDEX IF NOT EXISTS idx_ai_action_log_type ON ai_action_log(action_type);
CREATE INDEX IF NOT EXISTS idx_ai_action_log_entity ON ai_action_log(entity_type, entity_id);
CREATE INDEX IF NOT EXISTS idx_ai_action_log_created ON ai_action_log(created_at);
