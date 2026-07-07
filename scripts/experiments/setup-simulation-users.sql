-- setup-simulation-users.sql
-- 为 AI User Simulation 实验配置 Persona 账号的 API 令牌与项目角色
-- 目标用户：taskmgr_manager (PM), taskmgr_worker1 (Dev), taskmgr_viewer (Observer)

-- 1. 更新 API 令牌
UPDATE app_user 
SET api_token = 'token_pm_user', 
    updated_at = datetime('now') 
WHERE user_name = 'taskmgr_manager';

UPDATE app_user 
SET api_token = 'token_dev_user', 
    updated_at = datetime('now') 
WHERE user_name = 'taskmgr_worker1';

UPDATE app_user 
SET api_token = 'token_obs_user', 
    updated_at = datetime('now') 
WHERE user_name = 'taskmgr_viewer';

-- 2. 更新项目角色，以便 ApiEntityAccessGuard 能进行细粒度权限判定
-- PM (taskmgr_manager) -> manager (有写权限)
UPDATE app_user_project_role 
SET role_name = 'manager' 
WHERE user_id = (SELECT id FROM app_user WHERE user_name = 'taskmgr_manager')
  AND project_name = 'task-management';

-- Dev (taskmgr_worker1) -> worker (有写权限)
UPDATE app_user_project_role 
SET role_name = 'worker' 
WHERE user_id = (SELECT id FROM app_user WHERE user_name = 'taskmgr_worker1')
  AND project_name = 'task-management';

-- Observer (taskmgr_viewer) -> viewer (只读权限)
UPDATE app_user_project_role 
SET role_name = 'viewer' 
WHERE user_id = (SELECT id FROM app_user WHERE user_name = 'taskmgr_viewer')
  AND project_name = 'task-management';

-- 验证更新结果
SELECT u.id, u.user_name, u.api_token, pr.role_name 
FROM app_user u
LEFT JOIN app_user_project_role pr ON u.id = pr.user_id AND pr.project_name = 'task-management'
WHERE u.user_name LIKE 'taskmgr%';

