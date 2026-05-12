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
