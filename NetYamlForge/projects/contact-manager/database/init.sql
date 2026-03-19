-- Contact Manager 初始化脚本

CREATE TABLE IF NOT EXISTS company (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    industry TEXT, website TEXT, phone TEXT, email TEXT,
    employeeCount INTEGER, rating TEXT,
    createdAt TEXT DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS contact (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    firstName TEXT NOT NULL, lastName TEXT NOT NULL,
    companyId INTEGER, title TEXT,
    email TEXT NOT NULL, phone TEXT,
    status TEXT DEFAULT 'active', priority TEXT DEFAULT 'medium',
    createdAt TEXT DEFAULT (datetime('now')),
    FOREIGN KEY (companyId) REFERENCES company(id)
);

CREATE TABLE IF NOT EXISTS interaction (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    contactId INTEGER NOT NULL, companyId INTEGER,
    type TEXT NOT NULL, subject TEXT NOT NULL,
    scheduledAt TEXT, status TEXT DEFAULT 'planned', priority TEXT DEFAULT 'medium',
    createdAt TEXT DEFAULT (datetime('now')),
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
('咨询集团', '咨询', 'https://consulting.example.com', 'hello@consulting.example.com', 100, 'A'),
('制造工厂', '制造', 'https://factory.example.com', 'sales@factory.example.com', 1000, 'B'),
('设计公司', '设计', 'https://design.example.com', 'info@design.example.com', 50, 'C');

INSERT INTO contact (firstName, lastName, companyId, title, email, phone, status, priority) VALUES
('伟', '张', 1, '技术总监', 'zhang.wei@techcorp.example.com', '13800138001', 'active', 'high'),
('娜', '李', 1, '产品经理', 'li.na@techcorp.example.com', '13800138002', 'active', 'medium'),
('强', '王', 2, '销售总监', 'wang.qiang@tradeco.example.com', '13800138003', 'active', 'high'),
('芳', '刘', 2, '市场经理', 'liu.fang@tradeco.example.com', '13800138004', 'active', 'medium'),
('敏', '陈', 3, '首席顾问', 'chen.min@consulting.example.com', '13800138005', 'active', 'high'),
('杰', '杨', 4, '生产经理', 'yang.jie@factory.example.com', '13800138006', 'active', 'low'),
('丽', '赵', 5, '创意总监', 'zhao.li@design.example.com', '13800138007', 'lead', 'medium'),
('磊', '孙', 1, '工程师', 'sun.lei@techcorp.example.com', '13800138008', 'active', 'low'),
('秀英', '周', 3, '顾问', 'zhou.xiuying@consulting.example.com', '13800138009', 'inactive', 'low'),
('勇', '吴', 4, '质检员', 'wu.yong@factory.example.com', '13800138010', 'active', 'medium');

INSERT INTO interaction (contactId, companyId, type, subject, scheduledAt, status, priority) VALUES
(1, 1, 'meeting', '项目启动会议', datetime('now', '+1 day', '10:00:00'), 'planned', 'high'),
(2, 1, 'call', '产品功能确认', datetime('now', '+2 days', '14:00:00'), 'planned', 'medium'),
(3, 2, 'email', '报价单发送', datetime('now', '-3 days'), 'completed', 'high'),
(4, 2, 'meeting', '市场推广讨论', datetime('now', '-1 week'), 'completed', 'medium'),
(5, 3, 'call', '咨询项目跟进', datetime('now', '+3 days', '11:00:00'), 'planned', 'high'),
(6, 4, 'email', '生产进度汇报', datetime('now', '-2 days'), 'completed', 'low'),
(7, 5, 'meeting', '设计方案评审', datetime('now', '+5 days', '15:00:00'), 'planned', 'medium'),
(1, 1, 'note', '初次接触记录', datetime('now', '-1 month'), 'completed', 'medium'),
(3, 2, 'task', '合同准备', datetime('now', '-5 days'), 'completed', 'high'),
(5, 3, 'note', '项目总结', datetime('now', '-2 weeks'), 'completed', 'medium');
