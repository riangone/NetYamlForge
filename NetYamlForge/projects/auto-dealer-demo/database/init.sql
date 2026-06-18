-- auto-dealer-demo プロジェクト固有のテーブル定義
-- 自動車ディーラー管理システムのエンティティテーブルを作成します

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
    login_username VARCHAR(50),
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

-- 車両在庫マスタ（ディーラー在庫 + 顧客所有車両を統合管理）
CREATE TABLE IF NOT EXISTS vehicles (
    vehicle_id VARCHAR(50) NOT NULL PRIMARY KEY,
    customer_id VARCHAR(50),               -- 顧客所有車両の場合は顧客 ID（在庫車は NULL）
    vin VARCHAR(17) UNIQUE,
    maker VARCHAR(50) NOT NULL,
    brand VARCHAR(50) NOT NULL,
    model VARCHAR(50) NOT NULL,
    grade VARCHAR(50),
    year INTEGER NOT NULL,
    color VARCHAR(30),
    mileage INTEGER DEFAULT 0,
    transmission VARCHAR(20),              -- AT / MT / CVT / DCT
    fuel_type VARCHAR(20),                 -- gasoline / diesel / hybrid / ev / phev
    engine_capacity INTEGER,               -- 排気量（cc）
    vehicle_type VARCHAR(30) NOT NULL DEFAULT 'sedan',  -- sedan/wagon/suv/minivan/truck/sports/kei
    price DECIMAL(12,2) NOT NULL DEFAULT 0,
    cost DECIMAL(12,2),
    status VARCHAR(20) NOT NULL DEFAULT 'available',    -- available/reserved/sold/maintenance/display
    arrival_date DATE,
    inspection_date DATE,
    image_url VARCHAR(500),
    features TEXT,
    notes TEXT,
    purchase_date DATE,                    -- 顧客購入日（顧客所有車両用）
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

-- 販売リード
CREATE TABLE IF NOT EXISTS sales_leads (
    lead_id VARCHAR(50) NOT NULL PRIMARY KEY,
    customer_id VARCHAR(50),
    vehicle_interest VARCHAR(100),
    budget DECIMAL(12,2),
    lead_score INTEGER NOT NULL DEFAULT 50,
    status VARCHAR(20) NOT NULL DEFAULT 'new',
    lead_source VARCHAR(30) DEFAULT 'web',
    assigned_to_user_id VARCHAR(50),
    assigned_sales VARCHAR(50),
    last_contact_at DATETIME,
    created_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (customer_id) REFERENCES customers(customer_id)
);

CREATE INDEX IF NOT EXISTS idx_leads_customer ON sales_leads(customer_id);
CREATE INDEX IF NOT EXISTS idx_leads_status ON sales_leads(status);
CREATE INDEX IF NOT EXISTS idx_leads_score ON sales_leads(lead_score);

CREATE INDEX IF NOT EXISTS idx_customers_name ON customers(name);

CREATE INDEX IF NOT EXISTS idx_vehicles_customer ON vehicles(customer_id);

CREATE INDEX IF NOT EXISTS idx_appointments_customer ON service_appointments(customer_id);
CREATE INDEX IF NOT EXISTS idx_appointments_date ON service_appointments(preferred_date);

CREATE INDEX IF NOT EXISTS idx_requests_customer ON service_requests(customer_id);
CREATE INDEX IF NOT EXISTS idx_requests_status ON service_requests(status);

-- リードアクティビティログ（セールス担当者の対応履歴）
CREATE TABLE IF NOT EXISTS lead_activities (
    activity_id VARCHAR(64) NOT NULL PRIMARY KEY,
    lead_id VARCHAR(50) NOT NULL,
    activity_type VARCHAR(30) NOT NULL,  -- call / email / visit / proposal_sent / test_drive
    notes TEXT,
    outcome VARCHAR(20),                 -- positive / neutral / negative / no_answer
    next_action TEXT,
    next_action_date DATETIME,
    created_by VARCHAR(50),
    created_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (lead_id) REFERENCES sales_leads(lead_id)
);

CREATE INDEX IF NOT EXISTS idx_activities_lead ON lead_activities(lead_id);
CREATE INDEX IF NOT EXISTS idx_activities_created ON lead_activities(created_at);

-- 従業員マスタ
CREATE TABLE IF NOT EXISTS employees (
    employee_id VARCHAR(50) NOT NULL PRIMARY KEY,
    user_name VARCHAR(50) NOT NULL UNIQUE,
    employee_number VARCHAR(20) NOT NULL UNIQUE,
    name VARCHAR(100) NOT NULL,
    name_kana VARCHAR(100),
    gender VARCHAR(10),
    birth_date DATE,
    phone VARCHAR(20),
    mobile VARCHAR(20),
    email VARCHAR(100) NOT NULL UNIQUE,
    postal_code VARCHAR(10),
    address VARCHAR(200),
    department VARCHAR(50),
    position VARCHAR(50),
    role VARCHAR(50) NOT NULL,
    supervisor_id VARCHAR(50),
    hire_date DATE NOT NULL,
    employment_type VARCHAR(30) NOT NULL,
    salary DECIMAL(12,2),
    hourly_rate DECIMAL(10,2),
    commission_rate DECIMAL(5,2) DEFAULT 0,
    status VARCHAR(20) NOT NULL DEFAULT 'active',
    termination_date DATE,
    termination_reason TEXT,
    notes TEXT,
    created_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (supervisor_id) REFERENCES employees(employee_id)
);

CREATE INDEX IF NOT EXISTS idx_employees_employee_number ON employees(employee_number);
CREATE INDEX IF NOT EXISTS idx_employees_name ON employees(name);
CREATE INDEX IF NOT EXISTS idx_employees_department ON employees(department);
CREATE INDEX IF NOT EXISTS idx_employees_status ON employees(status);

-- ============================================================
-- Phase 4 追加テーブル
-- ============================================================

-- 拠点マスタ
CREATE TABLE IF NOT EXISTS branches (
    branch_id   VARCHAR(50)  NOT NULL PRIMARY KEY,
    branch_name VARCHAR(100) NOT NULL,
    branch_type VARCHAR(20)  NOT NULL DEFAULT 'sub',
    address     VARCHAR(200),
    phone       VARCHAR(20),
    email       VARCHAR(100),
    manager_id  VARCHAR(50),
    is_active   INTEGER      NOT NULL DEFAULT 1,
    sort_order  INTEGER      NOT NULL DEFAULT 0,
    created_at  DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at  DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (manager_id) REFERENCES employees(employee_id)
);

-- 営業クォータ
CREATE TABLE IF NOT EXISTS sales_quotas (
    quota_id        VARCHAR(50)    NOT NULL PRIMARY KEY,
    employee_id     VARCHAR(50)    NOT NULL,
    branch_id       VARCHAR(50),
    year            INTEGER        NOT NULL,
    month           INTEGER        NOT NULL,
    quota_amount    DECIMAL(12,2)  NOT NULL DEFAULT 0,
    quota_units     INTEGER        NOT NULL DEFAULT 0,
    achieved_amount DECIMAL(12,2)  NOT NULL DEFAULT 0,
    achieved_units  INTEGER        NOT NULL DEFAULT 0,
    created_at      DATETIME       NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at      DATETIME       NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (employee_id) REFERENCES employees(employee_id),
    FOREIGN KEY (branch_id)   REFERENCES branches(branch_id)
);

-- 車両画像
CREATE TABLE IF NOT EXISTS vehicle_images (
    image_id    VARCHAR(50)  NOT NULL PRIMARY KEY,
    vehicle_id  VARCHAR(50)  NOT NULL,
    image_url   VARCHAR(500) NOT NULL,
    caption     VARCHAR(200),
    image_type  VARCHAR(20)  NOT NULL DEFAULT 'exterior',
    sort_order  INTEGER      NOT NULL DEFAULT 0,
    is_primary  INTEGER      NOT NULL DEFAULT 0,
    uploaded_by VARCHAR(50),
    created_at  DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (vehicle_id) REFERENCES vehicles(vehicle_id)
);

-- 支払いプラン
CREATE TABLE IF NOT EXISTS payment_plans (
    plan_id          VARCHAR(50)   NOT NULL PRIMARY KEY,
    lead_id          VARCHAR(50),
    customer_id      VARCHAR(50)   NOT NULL,
    vehicle_id       VARCHAR(50)   NOT NULL,
    plan_type        VARCHAR(20)   NOT NULL DEFAULT 'loan',
    total_amount     DECIMAL(12,2) NOT NULL DEFAULT 0,
    down_payment     DECIMAL(12,2),
    loan_amount      DECIMAL(12,2),
    interest_rate    DECIMAL(5,2),
    term_months      INTEGER,
    monthly_payment  DECIMAL(10,2),
    status           VARCHAR(20)   NOT NULL DEFAULT 'draft',
    contract_date    DATE,
    created_at       DATETIME      NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at       DATETIME      NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (lead_id)     REFERENCES sales_leads(lead_id),
    FOREIGN KEY (customer_id) REFERENCES customers(customer_id),
    FOREIGN KEY (vehicle_id)  REFERENCES vehicles(vehicle_id)
);

-- 試乗
CREATE TABLE IF NOT EXISTS test_drives (
    test_drive_id   VARCHAR(64)  NOT NULL PRIMARY KEY,
    lead_id         VARCHAR(50),
    customer_id     VARCHAR(50)  NOT NULL,
    vehicle_id      VARCHAR(50)  NOT NULL,
    assigned_staff_id VARCHAR(50),
    branch_id       VARCHAR(50),
    scheduled_at    DATETIME     NOT NULL,
    actual_start_at DATETIME,
    actual_end_at   DATETIME,
    status          VARCHAR(20)  NOT NULL DEFAULT 'scheduled',
    feedback_score  INTEGER,
    feedback_notes  TEXT,
    created_at      DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at      DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (lead_id)          REFERENCES sales_leads(lead_id),
    FOREIGN KEY (customer_id)      REFERENCES customers(customer_id),
    FOREIGN KEY (vehicle_id)       REFERENCES vehicles(vehicle_id),
    FOREIGN KEY (assigned_staff_id) REFERENCES employees(employee_id),
    FOREIGN KEY (branch_id)        REFERENCES branches(branch_id)
);

-- AI 会話
CREATE TABLE IF NOT EXISTS ai_conversations (
    conversation_id   VARCHAR(64)  NOT NULL PRIMARY KEY,
    customer_id       VARCHAR(50),
    channel           VARCHAR(20)  NOT NULL DEFAULT 'web',
    status            VARCHAR(20)  NOT NULL DEFAULT 'active',
    last_intent       VARCHAR(100),
    last_confidence   REAL,
    sentiment_score   REAL,
    started_at        DATETIME,
    created_at        DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at        DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (customer_id) REFERENCES customers(customer_id)
);

-- AI 引継ぎ
CREATE TABLE IF NOT EXISTS ai_handovers (
    handover_id       VARCHAR(64)  NOT NULL PRIMARY KEY,
    conversation_id   VARCHAR(64)  NOT NULL,
    reason            VARCHAR(100) NOT NULL,
    priority          VARCHAR(20)  NOT NULL DEFAULT 'medium',
    target_department VARCHAR(50),
    status            VARCHAR(20)  NOT NULL DEFAULT 'pending',
    handover_notes    TEXT,
    escalated_at      DATETIME,
    created_at        DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (conversation_id) REFERENCES ai_conversations(conversation_id)
);

-- AI 決定
CREATE TABLE IF NOT EXISTS ai_decisions (
    decision_id      VARCHAR(64)  NOT NULL PRIMARY KEY,
    decision_type    VARCHAR(50)  NOT NULL,
    entity_type      VARCHAR(50),
    entity_id        VARCHAR(64),
    ai_reasoning     TEXT,
    confidence_score REAL,
    status           VARCHAR(20)  NOT NULL DEFAULT 'pending',
    requires_human   INTEGER      NOT NULL DEFAULT 0,
    created_at       DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime'))
);

-- AI 見積
CREATE TABLE IF NOT EXISTS ai_quotes (
    quote_id        VARCHAR(64)   NOT NULL PRIMARY KEY,
    lead_id         VARCHAR(50),
    customer_id     VARCHAR(50),
    vehicle_id      VARCHAR(50),
    base_price      DECIMAL(12,2) NOT NULL DEFAULT 0,
    discount_amount DECIMAL(12,2) NOT NULL DEFAULT 0,
    final_price     DECIMAL(12,2) NOT NULL DEFAULT 0,
    ai_reasoning    TEXT,
    status          VARCHAR(20)   NOT NULL DEFAULT 'draft',
    valid_until     DATETIME,
    created_at      DATETIME      NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (lead_id)     REFERENCES sales_leads(lead_id),
    FOREIGN KEY (customer_id) REFERENCES customers(customer_id),
    FOREIGN KEY (vehicle_id)  REFERENCES vehicles(vehicle_id)
);

-- AI アクションログ
CREATE TABLE IF NOT EXISTS ai_action_log (
    log_id      VARCHAR(64) NOT NULL PRIMARY KEY,
    action_type VARCHAR(50) NOT NULL,
    entity_type VARCHAR(50),
    entity_id   VARCHAR(64),
    created_at  DATETIME    NOT NULL DEFAULT (datetime('now', 'localtime'))
);

-- AI 通信
CREATE TABLE IF NOT EXISTS ai_communications (
    comm_id          VARCHAR(64)  NOT NULL PRIMARY KEY,
    lead_id          VARCHAR(50),
    customer_id      VARCHAR(50),
    comm_channel     VARCHAR(20)  NOT NULL DEFAULT 'email',
    subject          VARCHAR(200),
    body_text        TEXT,
    ai_personalized  INTEGER      NOT NULL DEFAULT 0,
    ai_confidence    REAL,
    send_status      VARCHAR(20)  NOT NULL DEFAULT 'pending',
    sent_at          DATETIME,
    created_at       DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at       DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (lead_id)     REFERENCES sales_leads(lead_id),
    FOREIGN KEY (customer_id) REFERENCES customers(customer_id)
);

-- リード育成タスク
CREATE TABLE IF NOT EXISTS lead_nurturing_tasks (
    task_id            VARCHAR(64)  NOT NULL PRIMARY KEY,
    lead_id            VARCHAR(50),
    customer_id        VARCHAR(50),
    task_type          VARCHAR(50)  NOT NULL,
    trigger_reason     VARCHAR(200),
    priority_score     INTEGER      NOT NULL DEFAULT 50,
    status             VARCHAR(20)  NOT NULL DEFAULT 'pending',
    ai_recommendation  TEXT,
    due_date           DATETIME,
    created_at         DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at         DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (lead_id)     REFERENCES sales_leads(lead_id),
    FOREIGN KEY (customer_id) REFERENCES customers(customer_id)
);

-- 外部ユーザー（提携会社）
CREATE TABLE IF NOT EXISTS third_party_users (
    third_party_id  VARCHAR(50)  NOT NULL PRIMARY KEY,
    app_user_id     INTEGER      NOT NULL,
    company_name    VARCHAR(100) NOT NULL,
    service_type    VARCHAR(50)  NOT NULL,
    contact_person  VARCHAR(100),
    contact_email   VARCHAR(100),
    status          VARCHAR(20)  NOT NULL DEFAULT 'active',
    rating          INTEGER,
    created_at      DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at      DATETIME     NOT NULL DEFAULT (datetime('now', 'localtime'))
);
