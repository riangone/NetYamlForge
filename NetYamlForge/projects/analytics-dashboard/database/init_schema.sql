CREATE TABLE IF NOT EXISTS kpi_metric (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    metric_name TEXT NOT NULL,
    current_value TEXT NOT NULL,
    target_value TEXT,
    growth_rate TEXT,
    status TEXT DEFAULT 'healthy',
    icon TEXT,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS business_channel (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    channel_name TEXT NOT NULL,
    category TEXT NOT NULL,
    revenue REAL NOT NULL,
    conversions INTEGER NOT NULL,
    roi_multiple REAL NOT NULL,
    summary TEXT,
    created_at TEXT NOT NULL
);

INSERT INTO kpi_metric (id, metric_name, current_value, target_value, growth_rate, status, icon, updated_at)
VALUES
(1, '季度 GMV 累计', '¥1,280,000', '¥1,200,000', '+18.4%', 'healthy', '💰', '2026-10-25 10:00:00'),
(2, '月度活跃租户', '142 家', '120 家', '+26.8%', 'healthy', '🏢', '2026-10-25 10:00:00'),
(3, '付费转化率', '12.8%', '10.0%', '+3.2%', 'healthy', '🎯', '2026-10-25 10:00:00'),
(4, 'API 99分位响应', '42ms', '<50ms', '-8ms', 'healthy', '⚡', '2026-10-25 10:00:00');

INSERT INTO business_channel (id, channel_name, category, revenue, conversions, roi_multiple, summary, created_at)
VALUES
(1, '企业专有云与微服务解决方案', '核心业务', 620000.0, 38, 4.2, '大中型客户定制化高可用私有部署与技术咨询服务', '2026-10-24 10:00:00'),
(2, '多智能体自举演进套件 (Agent Studio)', '创新引擎', 380000.0, 52, 5.8, '研发提效与全自动自举交付企业级订阅', '2026-10-24 10:00:00'),
(3, '低代码声明式组件市场', '生态应用', 280000.0, 96, 3.5, 'UI积木与行业模板包销售', '2026-10-24 10:00:00');
