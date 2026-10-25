CREATE TABLE IF NOT EXISTS approval_request (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    category TEXT NOT NULL,
    applicant TEXT NOT NULL,
    department TEXT NOT NULL,
    amount REAL DEFAULT 0.0,
    urgency TEXT DEFAULT 'normal',
    status TEXT DEFAULT 'pending',
    reason TEXT,
    approver TEXT,
    comment TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS approval_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    request_id INTEGER NOT NULL,
    operator TEXT NOT NULL,
    action TEXT NOT NULL,
    comment TEXT,
    created_at TEXT NOT NULL
);

-- 预置典型演示数据
INSERT INTO approval_request (id, title, category, applicant, department, amount, urgency, status, reason, approver, comment, created_at, updated_at)
VALUES 
(1, 'Q3 架构师大会差旅与门票报销', '费用报销', 'Tanaka', '研发架构部', 3800.0, 'urgent', 'pending', '参加全球分布式系统峰会与技术选型闭门会，含往返高铁与酒店', NULL, NULL, '2026-10-24 09:30:00', '2026-10-24 09:30:00'),
(2, 'GPU 推理集群节点扩容采购申请', '设备采购', 'Chen Wei', 'AI 工程院', 45000.0, 'high', 'pending', '支撑多智能体自举推演并发需求，申请增加 2 台高性能推理服务器', NULL, NULL, '2026-10-24 14:15:00', '2026-10-24 14:15:00'),
(3, '带薪年假申请 (4天)', '请假申请', 'Alice Smith', '云基础设施组', 4.0, 'normal', 'approved', '处理家庭个人紧急事务并休整年假', 'Manager Bob', '已安排团队成员接替 oncall，同意批准', '2026-10-22 11:00:00', '2026-10-22 15:20:00'),
(4, '安全合规扫描工具企业版采购', '软件采购', 'Kenji', '安全运维部', 12000.0, 'normal', 'rejected', '采购第三方静态扫描商业授权', 'Director Zhang', '目前开源自研网闸已覆盖相关需求，暂缓采购', '2026-10-21 16:40:00', '2026-10-22 09:10:00');

INSERT INTO approval_log (request_id, operator, action, comment, created_at)
VALUES
(3, 'Alice Smith', 'submit', '提交请假申请', '2026-10-22 11:00:00'),
(3, 'Manager Bob', 'approve', '已安排团队成员接替 oncall，同意批准', '2026-10-22 15:20:00'),
(4, 'Kenji', 'submit', '提交采购申请', '2026-10-21 16:40:00'),
(4, 'Director Zhang', 'reject', '目前开源自研网闸已覆盖相关需求，暂缓采购', '2026-10-22 09:10:00');
