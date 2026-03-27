-- auto-dealer-demo プロジェクト固有のテーブル定義
-- AI 窓口システムのエンティティテーブルを作成します

-- AI 対話セッション
CREATE TABLE IF NOT EXISTS ai_conversations (
    conversation_id VARCHAR(64) NOT NULL PRIMARY KEY,
    customer_id VARCHAR(50),
    channel VARCHAR(20) NOT NULL,
    status VARCHAR(30) NOT NULL DEFAULT 'active',
    last_intent VARCHAR(100),
    last_confidence DECIMAL(10,4),
    sentiment_score DECIMAL(10,4),
    context_data TEXT,
    assigned_to_user_id VARCHAR(50),
    started_at DATETIME NOT NULL,
    ended_at DATETIME,
    created_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime'))
);

-- AI メッセージ
CREATE TABLE IF NOT EXISTS ai_messages (
    message_id VARCHAR(64) NOT NULL PRIMARY KEY,
    conversation_id VARCHAR(64) NOT NULL,
    sender VARCHAR(20) NOT NULL,
    message_type VARCHAR(20) NOT NULL DEFAULT 'text',
    content TEXT NOT NULL,
    intent VARCHAR(100),
    entities_json TEXT,
    confidence_score DECIMAL(10,4),
    sentiment_score DECIMAL(10,4),
    metadata_json TEXT,
    timestamp DATETIME NOT NULL,
    FOREIGN KEY (conversation_id) REFERENCES ai_conversations(conversation_id)
);

-- AI 引継ぎ
CREATE TABLE IF NOT EXISTS ai_handovers (
    handover_id VARCHAR(64) NOT NULL PRIMARY KEY,
    conversation_id VARCHAR(64) NOT NULL,
    ticket_id VARCHAR(64),
    reason VARCHAR(50) NOT NULL,
    priority VARCHAR(20) NOT NULL DEFAULT 'medium',
    target_department VARCHAR(50),
    assigned_to_user_id VARCHAR(50),
    status VARCHAR(30) NOT NULL DEFAULT 'pending',
    handover_notes TEXT,
    resolution_notes TEXT,
    escalated_at DATETIME NOT NULL,
    assigned_at DATETIME,
    resolved_at DATETIME,
    created_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (conversation_id) REFERENCES ai_conversations(conversation_id)
);

-- AI フィードバック
CREATE TABLE IF NOT EXISTS ai_feedback (
    feedback_id VARCHAR(64) NOT NULL PRIMARY KEY,
    conversation_id VARCHAR(64) NOT NULL,
    message_id VARCHAR(64),
    rating INTEGER NOT NULL,
    feedback_text TEXT,
    category VARCHAR(50),
    created_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (conversation_id) REFERENCES ai_conversations(conversation_id),
    FOREIGN KEY (message_id) REFERENCES ai_messages(message_id)
);

-- AI 知識ベース
CREATE TABLE IF NOT EXISTS ai_knowledge (
    knowledge_id VARCHAR(64) NOT NULL PRIMARY KEY,
    category VARCHAR(50) NOT NULL,
    intent VARCHAR(50) NOT NULL,
    question VARCHAR(500) NOT NULL,
    answer TEXT NOT NULL,
    answer_html TEXT,
    keywords TEXT,
    channel VARCHAR(50) DEFAULT 'all',
    language VARCHAR(10) NOT NULL DEFAULT 'ja',
    priority INTEGER DEFAULT 0,
    is_active BOOLEAN NOT NULL DEFAULT 1,
    usage_count INTEGER DEFAULT 0,
    helpful_count INTEGER DEFAULT 0,
    not_helpful_count INTEGER DEFAULT 0,
    last_used_at DATETIME,
    created_by VARCHAR(50),
    updated_by VARCHAR(50),
    created_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime'))
);

-- 顧客
CREATE TABLE IF NOT EXISTS customers (
    customer_id VARCHAR(50) NOT NULL PRIMARY KEY,
    customer_type VARCHAR(20) NOT NULL DEFAULT 'individual',
    name VARCHAR(100) NOT NULL,
    name_kana VARCHAR(100),
    gender VARCHAR(10),
    birth_date DATE,
    phone VARCHAR(20) NOT NULL,
    mobile VARCHAR(20),
    email VARCHAR(100),
    postal_code VARCHAR(10),
    address VARCHAR(200),
    tier_level VARCHAR(20) NOT NULL DEFAULT 'regular',
    purchase_count INTEGER DEFAULT 0,
    total_purchase_amount DECIMAL(12,2) DEFAULT 0,
    last_visit_date DATE,
    preferred_contact VARCHAR(20) DEFAULT 'phone',
    line_user_id VARCHAR(100),
    notes TEXT,
    created_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime'))
);

-- 車両
CREATE TABLE IF NOT EXISTS vehicles (
    vehicle_id VARCHAR(50) NOT NULL PRIMARY KEY,
    customer_id VARCHAR(50),
    vin VARCHAR(17),
    maker VARCHAR(50) NOT NULL,
    brand VARCHAR(50) NOT NULL,
    model VARCHAR(50) NOT NULL,
    grade VARCHAR(50),
    year INTEGER NOT NULL,
    color VARCHAR(30),
    mileage INTEGER,
    purchase_date DATE,
    created_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (customer_id) REFERENCES customers(customer_id)
);

-- サービス予約
CREATE TABLE IF NOT EXISTS service_appointments (
    appointment_id VARCHAR(64) NOT NULL PRIMARY KEY,
    customer_id VARCHAR(50) NOT NULL,
    vehicle_id VARCHAR(50),
    appointment_type VARCHAR(30) NOT NULL,
    service_menu VARCHAR(100),
    preferred_date DATETIME NOT NULL,
    end_date DATETIME,
    duration_minutes INTEGER DEFAULT 60,
    status VARCHAR(30) NOT NULL DEFAULT 'pending',
    assigned_staff_id VARCHAR(50),
    service_bay VARCHAR(20),
    customer_request TEXT,
    service_notes TEXT,
    estimated_cost DECIMAL(10,2),
    actual_cost DECIMAL(10,2),
    reminder_sent BOOLEAN DEFAULT 0,
    reminder_sent_at DATETIME,
    confirmed_at DATETIME,
    completed_at DATETIME,
    cancelled_at DATETIME,
    cancel_reason VARCHAR(200),
    created_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (customer_id) REFERENCES customers(customer_id),
    FOREIGN KEY (vehicle_id) REFERENCES vehicles(vehicle_id)
);

-- サービスリクエスト
CREATE TABLE IF NOT EXISTS service_requests (
    request_id VARCHAR(64) NOT NULL PRIMARY KEY,
    appointment_id VARCHAR(64),
    customer_id VARCHAR(50),
    vehicle_id VARCHAR(50),
    request_type VARCHAR(30) NOT NULL,
    subject VARCHAR(200) NOT NULL,
    description TEXT NOT NULL,
    priority VARCHAR(20) NOT NULL DEFAULT 'normal',
    status VARCHAR(30) NOT NULL DEFAULT 'open',
    assigned_to VARCHAR(50),
    source VARCHAR(30),
    related_appointment_id VARCHAR(64),
    estimated_resolution_date DATE,
    resolution_date DATE,
    resolution_notes TEXT,
    customer_satisfaction INTEGER,
    follow_up_required BOOLEAN DEFAULT 0,
    follow_up_date DATE,
    follow_up_notes TEXT,
    created_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (appointment_id) REFERENCES service_appointments(appointment_id),
    FOREIGN KEY (customer_id) REFERENCES customers(customer_id),
    FOREIGN KEY (vehicle_id) REFERENCES vehicles(vehicle_id)
);

-- インデックス作成
CREATE INDEX IF NOT EXISTS idx_conversations_customer ON ai_conversations(customer_id);
CREATE INDEX IF NOT EXISTS idx_conversations_status ON ai_conversations(status);
CREATE INDEX IF NOT EXISTS idx_conversations_channel ON ai_conversations(channel);
CREATE INDEX IF NOT EXISTS idx_conversations_started ON ai_conversations(started_at);

CREATE INDEX IF NOT EXISTS idx_messages_conversation ON ai_messages(conversation_id);
CREATE INDEX IF NOT EXISTS idx_messages_timestamp ON ai_messages(timestamp);

CREATE INDEX IF NOT EXISTS idx_handovers_conversation ON ai_handovers(conversation_id);
CREATE INDEX IF NOT EXISTS idx_handovers_status ON ai_handovers(status);

CREATE INDEX IF NOT EXISTS idx_feedback_conversation ON ai_feedback(conversation_id);

CREATE INDEX IF NOT EXISTS idx_knowledge_status ON ai_knowledge(status);
CREATE INDEX IF NOT EXISTS idx_knowledge_category ON ai_knowledge(category);

CREATE INDEX IF NOT EXISTS idx_customers_name ON customers(name);

CREATE INDEX IF NOT EXISTS idx_vehicles_customer ON vehicles(customer_id);

CREATE INDEX IF NOT EXISTS idx_appointments_customer ON service_appointments(customer_id);
CREATE INDEX IF NOT EXISTS idx_appointments_date ON service_appointments(preferred_date);

CREATE INDEX IF NOT EXISTS idx_requests_customer ON service_requests(customer_id);
CREATE INDEX IF NOT EXISTS idx_requests_status ON service_requests(status);
