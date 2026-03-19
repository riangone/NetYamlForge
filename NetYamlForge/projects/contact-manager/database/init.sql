-- Contact Manager 初始化脚本

CREATE TABLE IF NOT EXISTS company (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    industry TEXT, website TEXT, phone TEXT, email TEXT, address TEXT,
    employeeCount INTEGER, rating TEXT, notes TEXT,
    createdAt TEXT DEFAULT (datetime('now')),
    updatedAt TEXT DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS contact (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    firstName TEXT NOT NULL, lastName TEXT NOT NULL, fullName TEXT,
    companyId INTEGER, title TEXT, department TEXT,
    email TEXT NOT NULL, phone TEXT, linkedin TEXT,
    status TEXT DEFAULT 'active', priority TEXT DEFAULT 'medium', notes TEXT,
    createdAt TEXT DEFAULT (datetime('now')),
    updatedAt TEXT DEFAULT (datetime('now')),
    FOREIGN KEY (companyId) REFERENCES company(id)
);

CREATE TABLE IF NOT EXISTS interaction (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    contactId INTEGER NOT NULL, companyId INTEGER,
    type TEXT NOT NULL, subject TEXT NOT NULL, description TEXT,
    scheduledAt TEXT, completedAt TEXT,
    status TEXT DEFAULT 'planned', priority TEXT DEFAULT 'medium', outcome TEXT,
    createdAt TEXT DEFAULT (datetime('now')),
    updatedAt TEXT DEFAULT (datetime('now')),
    FOREIGN KEY (contactId) REFERENCES contact(id),
    FOREIGN KEY (companyId) REFERENCES company(id)
);

CREATE INDEX IF NOT EXISTS idx_contact_company ON contact(companyId);
CREATE INDEX IF NOT EXISTS idx_contact_status ON contact(status);
CREATE INDEX IF NOT EXISTS idx_interaction_contact ON interaction(contactId);
CREATE INDEX IF NOT EXISTS idx_interaction_status ON interaction(status);

-- 示例数据
INSERT INTO company (name, industry, website, email, employeeCount, rating) VALUES
('科技有限公司', '科技', 'https://techcorp.example.com', 'info@techcorp.example.com', 500, 'A'),
('贸易公司', '贸易', 'https://tradeco.example.com', 'contact@tradeco.example.com', 200, 'B'),
('咨询集团', '咨询', 'https://consulting.example.com', 'hello@consulting.example.com', 100, 'A');

INSERT INTO contact (firstName, lastName, fullName, companyId, title, email, status, priority) VALUES
('伟', '张', '张伟', 1, '技术总监', 'zhang.wei@techcorp.example.com', 'active', 'high'),
('娜', '李', '李娜', 1, '产品经理', 'li.na@techcorp.example.com', 'active', 'medium'),
('强', '王', '王强', 2, '销售总监', 'wang.qiang@tradeco.example.com', 'active', 'high'),
('芳', '刘', '刘芳', 2, '市场经理', 'liu.fang@tradeco.example.com', 'active', 'medium'),
('敏', '陈', '陈敏', 3, '首席顾问', 'chen.min@consulting.example.com', 'active', 'high');

INSERT INTO interaction (contactId, companyId, type, subject, scheduledAt, status, priority) VALUES
(1, 1, 'meeting', '项目启动会议', datetime('now', '+1 day', '10:00:00'), 'planned', 'high'),
(2, 1, 'call', '产品功能确认', datetime('now', '+2 days', '14:00:00'), 'planned', 'medium'),
(3, 2, 'email', '报价单发送', datetime('now', '-3 days'), 'completed', 'high');
