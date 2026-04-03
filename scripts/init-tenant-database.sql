-- ============================================
-- NetYamlForge 多租户用户管理数据库初始化脚本
-- ============================================
-- 此脚本用于初始化全局用户表和项目角色表
-- 适用于 SQLite 数据库
-- ============================================

-- ============================================
-- 1. 创建全局用户表 (app_user)
-- ============================================
CREATE TABLE IF NOT EXISTS app_user (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_name TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    display_name TEXT NOT NULL,
    email TEXT,
    phone TEXT,
    user_type TEXT NOT NULL DEFAULT 'employee',
    default_project_name TEXT,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

-- 创建索引
CREATE UNIQUE INDEX IF NOT EXISTS IX_app_user_user_name ON app_user(user_name);
CREATE UNIQUE INDEX IF NOT EXISTS IX_app_user_email ON app_user(email) WHERE email IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_app_user_user_type ON app_user(user_type);
CREATE INDEX IF NOT EXISTS IX_app_user_is_active ON app_user(is_active);

-- ============================================
-- 2. 创建项目角色表 (app_user_project_role)
-- ============================================
CREATE TABLE IF NOT EXISTS app_user_project_role (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    project_name TEXT NOT NULL,
    role_name TEXT NOT NULL,
    permission_scope TEXT,
    assigned_by INTEGER,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (user_id) REFERENCES app_user(id),
    FOREIGN KEY (assigned_by) REFERENCES app_user(id),
    UNIQUE(user_id, project_name)
);

-- 创建索引
CREATE UNIQUE INDEX IF NOT EXISTS IX_app_user_project_role_user_project 
    ON app_user_project_role(user_id, project_name);
CREATE INDEX IF NOT EXISTS IX_app_user_project_role_project 
    ON app_user_project_role(project_name);
CREATE INDEX IF NOT EXISTS IX_app_user_project_role_role_name 
    ON app_user_project_role(role_name);

-- ============================================
-- 3. 创建项目配置表 (projects) - 如果不存在
-- ============================================
CREATE TABLE IF NOT EXISTS projects (
    name TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    description TEXT,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================
-- 4. 插入默认项目数据
-- ============================================
INSERT OR IGNORE INTO projects (name, display_name, description, is_active, created_at, updated_at) VALUES
('auto-dealer-demo', '汽车销售演示', '汽车销售业务演示项目', 1, datetime('now'), datetime('now')),
('framework', '框架管理', 'NetYamlForge 框架管理项目', 1, datetime('now'), datetime('now')),
('inventory', '库存管理', '车辆库存管理系统', 1, datetime('now'), datetime('now')),
('service-center', '服务中心', '客户服务中心', 1, datetime('now'), datetime('now'));

-- ============================================
-- 5. 插入默认管理员账户
-- ============================================
-- 默认管理员用户名：admin
-- 默认密码：Admin@123 (请在使用后立即修改)
-- 密码哈希使用 SHA256 算法生成
-- ============================================
INSERT OR IGNORE INTO app_user (
    id, user_name, password_hash, display_name, email, phone, 
    user_type, default_project_name, is_active, created_at, updated_at
) VALUES (
    1,
    'admin',
    'q6AdV75pK1N9qO3qJ8rL2mP5sT0uW4xY7zA1bC3dE6fG8hI9jK0lM2nO4pQ5rS7tU',  -- 临时哈希，实际应使用 BCrypt
    '系统管理员',
    'admin@netyamlforge.com',
    NULL,
    'employee',
    'framework',
    1,
    datetime('now'),
    datetime('now')
);

-- 为管理员分配项目角色
INSERT OR IGNORE INTO app_user_project_role (user_id, project_name, role_name, assigned_by, created_at) VALUES
(1, 'framework', 'admin', 1, datetime('now')),
(1, 'auto-dealer-demo', 'admin', 1, datetime('now')),
(1, 'inventory', 'admin', 1, datetime('now')),
(1, 'service-center', 'admin', 1, datetime('now'));

-- ============================================
-- 6. 插入示例用户数据（可选）
-- ============================================

-- 示例：销售经理
INSERT OR IGNORE INTO app_user (
    user_name, password_hash, display_name, email, phone, 
    user_type, default_project_name, is_active, created_at, updated_at
) VALUES (
    'sales_manager',
    '临时哈希占位符',
    '销售经理',
    'manager@dealer.com',
    '13800138001',
    'employee',
    'auto-dealer-demo',
    1,
    datetime('now'),
    datetime('now')
);

-- 为销售经理分配角色
INSERT OR IGNORE INTO app_user_project_role (user_id, project_name, role_name, assigned_by, created_at) 
SELECT 
    u.id, 'auto-dealer-demo', 'sales_manager', 1, datetime('now')
FROM app_user u 
WHERE u.user_name = 'sales_manager';

-- 示例：销售代表
INSERT OR IGNORE INTO app_user (
    user_name, password_hash, display_name, email, phone, 
    user_type, default_project_name, is_active, created_at, updated_at
) VALUES (
    'sales_rep',
    '临时哈希占位符',
    '销售代表',
    'rep@dealer.com',
    '13800138002',
    'employee',
    'auto-dealer-demo',
    1,
    datetime('now'),
    datetime('now')
);

-- 为销售代表分配角色
INSERT OR IGNORE INTO app_user_project_role (user_id, project_name, role_name, assigned_by, created_at) 
SELECT 
    u.id, 'auto-dealer-demo', 'sales_rep', 1, datetime('now')
FROM app_user u 
WHERE u.user_name = 'sales_rep';

-- 示例：客户
INSERT OR IGNORE INTO app_user (
    user_name, password_hash, display_name, email, phone, 
    user_type, default_project_name, is_active, created_at, updated_at
) VALUES (
    'customer_zhang',
    '临时哈希占位符',
    '张先生',
    'zhang@example.com',
    '13900139001',
    'customer',
    'auto-dealer-demo',
    1,
    datetime('now'),
    datetime('now')
);

-- 为客户分配角色
INSERT OR IGNORE INTO app_user_project_role (user_id, project_name, role_name, assigned_by, created_at) 
SELECT 
    u.id, 'auto-dealer-demo', 'customer', 1, datetime('now')
FROM app_user u 
WHERE u.user_name = 'customer_zhang';

-- 示例：第三方物流用户
INSERT OR IGNORE INTO app_user (
    user_name, password_hash, display_name, email, phone, 
    user_type, default_project_name, is_active, created_at, updated_at
) VALUES (
    'logistics_wuliu',
    '临时哈希占位符',
    'XX 物流公司',
    'contact@wuliu.com',
    '400-888-8888',
    'third_party',
    'auto-dealer-demo',
    1,
    datetime('now'),
    datetime('now')
);

-- 为物流用户分配角色
INSERT OR IGNORE INTO app_user_project_role (user_id, project_name, role_name, assigned_by, created_at) 
SELECT 
    u.id, 'auto-dealer-demo', 'logistics', 1, datetime('now')
FROM app_user u 
WHERE u.user_name = 'logistics_wuliu';

-- ============================================
-- 7. 创建视图 - 用户项目角色概览
-- ============================================
CREATE VIEW IF NOT EXISTS v_user_project_roles AS
SELECT 
    u.id AS user_id,
    u.user_name,
    u.display_name,
    u.email,
    u.user_type,
    u.is_active,
    pr.project_name,
    p.display_name AS project_display_name,
    pr.role_name,
    pr.permission_scope,
    pr.created_at AS role_created_at
FROM app_user u
LEFT JOIN app_user_project_role pr ON u.id = pr.user_id
LEFT JOIN projects p ON pr.project_name = p.name
ORDER BY u.id, pr.project_name;

-- ============================================
-- 8. 创建触发器 - 自动更新 updated_at
-- ============================================
CREATE TRIGGER IF NOT EXISTS trg_app_user_update 
AFTER UPDATE ON app_user
BEGIN
    UPDATE app_user SET updated_at = datetime('now') WHERE id = NEW.id;
END;

CREATE TRIGGER IF NOT EXISTS trg_projects_update 
AFTER UPDATE ON projects
BEGIN
    UPDATE projects SET updated_at = datetime('now') WHERE name = NEW.name;
END;

-- ============================================
-- 9. 查询示例
-- ============================================

-- 查询所有活跃用户及其项目角色
-- SELECT * FROM v_user_project_roles WHERE is_active = 1;

-- 查询特定项目的所有用户
-- SELECT u.*, pr.role_name 
-- FROM app_user u
-- INNER JOIN app_user_project_role pr ON u.id = pr.user_id
-- WHERE pr.project_name = 'auto-dealer-demo';

-- 查询用户可访问的所有项目
-- SELECT pr.project_name, p.display_name, pr.role_name
-- FROM app_user_project_role pr
-- LEFT JOIN projects p ON pr.project_name = p.name
-- WHERE pr.user_id = 1;

-- ============================================
-- 10. 安全提示
-- ============================================
-- 1. 请在部署后立即修改默认管理员密码
-- 2. 生产环境应使用 BCrypt 或 PBKDF2 进行密码哈希
-- 3. 建议启用 HTTPS 传输加密
-- 4. 定期审计用户权限分配情况
-- 5. 为敏感操作添加审计日志
-- ============================================
