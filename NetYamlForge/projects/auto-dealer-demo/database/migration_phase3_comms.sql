-- Phase 3: コミュニケーション自動化 マイグレーション
-- ai_communications テーブル（送信済み/予定の自動メッセージ記録）
CREATE TABLE IF NOT EXISTS ai_communications (
    comm_id         VARCHAR(60)  NOT NULL PRIMARY KEY,
    lead_id         VARCHAR(50)  NOT NULL,
    customer_id     VARCHAR(50)  NOT NULL,
    nurturing_task_id VARCHAR(50),              -- 元となった育成タスク
    comm_channel    VARCHAR(20)  NOT NULL,      -- email / sms / line / phone_memo
    subject         VARCHAR(200),               -- メール件名
    body_text       TEXT         NOT NULL,      -- 送信本文（AIが生成）
    ai_personalized INTEGER      NOT NULL DEFAULT 1,  -- AI パーソナライズ済みか
    ai_model        VARCHAR(50)  DEFAULT 'gemini',
    ai_confidence   DECIMAL(5,2) DEFAULT 0,
    send_status     VARCHAR(20)  NOT NULL DEFAULT 'pending',
                                               -- pending / sent / failed / cancelled
    sent_at         DATETIME,
    error_message   TEXT,
    -- 反応トラッキング
    response_received INTEGER DEFAULT 0,       -- 1=返信あり
    response_at     DATETIME,
    response_sentiment VARCHAR(20),            -- positive / neutral / negative
    response_summary TEXT,
    -- メタ
    requires_human  INTEGER      NOT NULL DEFAULT 0,
    approved_by     VARCHAR(100),
    approved_at     DATETIME,
    created_at      DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at      DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (lead_id) REFERENCES sales_leads(lead_id),
    FOREIGN KEY (customer_id) REFERENCES customers(customer_id)
);

CREATE INDEX IF NOT EXISTS idx_ai_comms_lead      ON ai_communications(lead_id);
CREATE INDEX IF NOT EXISTS idx_ai_comms_status    ON ai_communications(send_status);
CREATE INDEX IF NOT EXISTS idx_ai_comms_channel   ON ai_communications(comm_channel);
CREATE INDEX IF NOT EXISTS idx_ai_comms_created   ON ai_communications(created_at);
CREATE INDEX IF NOT EXISTS idx_ai_comms_response  ON ai_communications(response_received);

-- lead_nurturing_tasks に送信追跡カラム追加
ALTER TABLE lead_nurturing_tasks ADD COLUMN comm_sent_at DATETIME;
ALTER TABLE lead_nurturing_tasks ADD COLUMN comm_id VARCHAR(60);

-- ai_quotes に顧客への送信状態追加
ALTER TABLE ai_quotes ADD COLUMN quote_sent_at DATETIME;
ALTER TABLE ai_quotes ADD COLUMN quote_comm_id VARCHAR(60);
ALTER TABLE ai_quotes ADD COLUMN discount_rate DECIMAL(5,2) DEFAULT 0;
ALTER TABLE ai_quotes ADD COLUMN ai_confidence DECIMAL(5,2) DEFAULT 0;
ALTER TABLE ai_quotes ADD COLUMN trade_in_value DECIMAL(12,2) DEFAULT 0;
ALTER TABLE ai_quotes ADD COLUMN down_payment DECIMAL(12,2) DEFAULT 0;
ALTER TABLE ai_quotes ADD COLUMN monthly_payment DECIMAL(12,2) DEFAULT 0;
ALTER TABLE ai_quotes ADD COLUMN loan_months INTEGER DEFAULT 60;
ALTER TABLE ai_quotes ADD COLUMN accessories TEXT;
ALTER TABLE ai_quotes ADD COLUMN notes TEXT;
