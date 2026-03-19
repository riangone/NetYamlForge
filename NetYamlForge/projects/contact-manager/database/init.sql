-- Contact Manager 初始化脚本
-- 创建数据库表和初始数据

-- 公司表
CREATE TABLE IF NOT EXISTS company (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    industry TEXT,
    website TEXT,
    phone TEXT,
    email TEXT,
    address TEXT,
    employeeCount INTEGER,
    annualRevenue REAL,
    rating TEXT,
    notes TEXT,
    createdAt TEXT DEFAULT (datetime('now')),
    updatedAt TEXT DEFAULT (datetime('now'))
);

-- 联系人表
CREATE TABLE IF NOT EXISTS contact (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    firstName TEXT NOT NULL,
    lastName TEXT NOT NULL,
    fullName TEXT,
    companyId INTEGER,
    title TEXT,
    department TEXT,
    email TEXT NOT NULL,
    phone TEXT,
    linkedin TEXT,
    birthday TEXT,
    tags TEXT,
    status TEXT DEFAULT 'active',
    priority TEXT DEFAULT 'medium',
    notes TEXT,
    createdAt TEXT DEFAULT (datetime('now')),
    updatedAt TEXT DEFAULT (datetime('now')),
    FOREIGN KEY (companyId) REFERENCES company(id)
);

-- 交互记录表
CREATE TABLE IF NOT EXISTS interaction (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    contactId INTEGER NOT NULL,
    companyId INTEGER,
    type TEXT NOT NULL,
    subject TEXT NOT NULL,
    description TEXT,
    scheduledAt TEXT,
    completedAt TEXT,
    status TEXT DEFAULT 'planned',
    priority TEXT DEFAULT 'medium',
    outcome TEXT,
    followUpAt TEXT,
    assignedTo TEXT,
    createdAt TEXT DEFAULT (datetime('now')),
    updatedAt TEXT DEFAULT (datetime('now')),
    FOREIGN KEY (contactId) REFERENCES contact(id),
    FOREIGN KEY (companyId) REFERENCES company(id)
);

-- 创建索引
CREATE INDEX IF NOT EXISTS idx_contact_company ON contact(companyId);
CREATE INDEX IF NOT EXISTS idx_contact_status ON contact(status);
CREATE INDEX IF NOT EXISTS idx_contact_email ON contact(email);
CREATE INDEX IF NOT EXISTS idx_interaction_contact ON interaction(contactId);
CREATE INDEX IF NOT EXISTS idx_interaction_company ON interaction(companyId);
CREATE INDEX IF NOT EXISTS idx_interaction_scheduled ON interaction(scheduledAt);
CREATE INDEX IF NOT EXISTS idx_interaction_status ON interaction(status);

-- 插入示例公司数据
INSERT INTO company (name, industry, website, phone, email, address, employeeCount, annualRevenue, rating) VALUES
('科技有限公司', '科技', 'https://techcorp.example.com', '010-12345678', 'info@techcorp.example.com', '北京市朝阳区科技园 1 号', 500, 50000000.00, 'A'),
('贸易公司', '贸易', 'https://tradeco.example.com', '021-87654321', 'contact@tradeco.example.com', '上海市浦东新区贸易大厦', 200, 30000000.00, 'B'),
('咨询集团', '咨询', 'https://consulting.example.com', '0755-11112222', 'hello@consulting.example.com', '深圳市南山区咨询中心', 100, 20000000.00, 'A'),
('制造工厂', '制造', 'https://factory.example.com', '020-33334444', 'sales@factory.example.com', '广州市工业区制造路 88 号', 1000, 100000000.00, 'B'),
('设计公司', '设计', 'https://designstudio.example.com', '028-55556666', 'info@designstudio.example.com', '成都市创意园区设计楼', 50, 8000000.00, 'C');

-- 插入示例联系人数据
INSERT INTO contact (firstName, lastName, fullName, companyId, title, department, email, phone, linkedin, status, priority) VALUES
('伟', '张', '张伟', 1, '技术总监', '技术部', 'zhang.wei@techcorp.example.com', '13800138001', 'https://linkedin.com/in/zhangwei', 'active', 'high'),
('娜', '李', '李娜', 1, '产品经理', '产品部', 'li.na@techcorp.example.com', '13800138002', 'https://linkedin.com/in/lina', 'active', 'medium'),
('强', '王', '王强', 2, '销售总监', '销售部', 'wang.qiang@tradeco.example.com', '13800138003', 'https://linkedin.com/in/wangqiang', 'active', 'high'),
('芳', '刘', '刘芳', 2, '市场经理', '市场部', 'liu.fang@tradeco.example.com', '13800138004', null, 'active', 'medium'),
('敏', '陈', '陈敏', 3, '首席顾问', '咨询部', 'chen.min@consulting.example.com', '13800138005', 'https://linkedin.com/in/chenmin', 'active', 'high'),
('杰', '杨', '杨杰', 4, '生产经理', '生产部', 'yang.jie@factory.example.com', '13800138006', null, 'active', 'low'),
('丽', '赵', '赵丽', 5, '创意总监', '设计部', 'zhao.li@designstudio.example.com', '13800138007', 'https://linkedin.com/in/zhaoli', 'lead', 'medium'),
('磊', '孙', '孙磊', 1, '工程师', '技术部', 'sun.lei@techcorp.example.com', '13800138008', null, 'active', 'low'),
('秀英', '周', '周秀英', 3, '顾问', '咨询部', 'zhou.xiuying@consulting.example.com', '13800138009', null, 'inactive', 'low'),
('勇', '吴', '吴勇', 4, '质检员', '质检部', 'wu.yong@factory.example.com', '13800138010', null, 'active', 'medium');

-- 插入示例交互记录
INSERT INTO interaction (contactId, companyId, type, subject, description, scheduledAt, status, priority) VALUES
(1, 1, 'meeting', '项目启动会议', '讨论新项目的技术方案和需求', datetime('now', '+1 day', '10:00:00'), 'planned', 'high'),
(2, 1, 'call', '产品功能确认', '确认下一版本的产品功能优先级', datetime('now', '+2 days', '14:00:00'), 'planned', 'medium'),
(3, 2, 'email', '报价单发送', '发送最新的产品报价单', datetime('now', '-3 days'), 'completed', 'high'),
(4, 2, 'meeting', '市场推广讨论', '讨论下一季度的市场推广计划', datetime('now', '-1 week'), 'completed', 'medium'),
(5, 3, 'call', '咨询项目跟进', '跟进正在进行的管理咨询项目', datetime('now', '+3 days', '11:00:00'), 'planned', 'high'),
(6, 4, 'email', '生产进度汇报', '发送本月生产进度报告', datetime('now', '-2 days'), 'completed', 'low'),
(7, 5, 'meeting', '设计方案评审', '评审新的品牌设计方案', datetime('now', '+5 days', '15:00:00'), 'planned', 'medium'),
(1, 1, 'note', '初次接触记录', '客户对技术方案很感兴趣，需要进一步沟通', datetime('now', '-1 month'), 'completed', 'medium'),
(3, 2, 'task', '合同准备', '准备销售合同草案', datetime('now', '-5 days'), 'completed', 'high'),
(5, 3, 'note', '项目总结', '第一阶段咨询项目顺利完成', datetime('now', '-2 weeks'), 'completed', 'medium');

-- 更新交互记录的结果
UPDATE interaction SET outcome = '会议顺利进行，确定了技术方案框架' WHERE id = 1;
UPDATE interaction SET outcome = '已确认功能优先级列表' WHERE id = 2;
UPDATE interaction SET outcome = '报价单已发送，等待客户回复' WHERE id = 3;
