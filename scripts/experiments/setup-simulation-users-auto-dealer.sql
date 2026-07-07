-- setup-simulation-users-auto-dealer.sql
-- 为 auto-dealer-demo (汽车销售) 子项目的 AI User Simulation 实验配置 Persona 账号
-- 复用 task-management 实验中验证过的思路：固定 api_token + 修正 project_role，
-- 使 ApiTokenAuthenticationHandler / ApiEntityAccessGuard 能按真实业务角色鉴权。
--
-- Persona 映射（均为 auto-dealer-demo 现有 demo 账号，密码 Demo@123）：
--   mgr  -> yamada     (部长 / sales_manager)  写权限 + AI 决定审批权限
--   rep  -> suzuki     (营业 / sales_rep)      线索/试乘写权限，禁止审批 AI 决定
--   op   -> takahashi  (操作员 / operator)     仅服务预约/工单相关
--   cust -> sato        (顾客 / customer)       仅自身数据只读，禁止越权访问他人客户数据

-- 1. 固定 API 令牌
UPDATE app_user SET api_token = 'token_ad_mgr_user',  updated_at = datetime('now') WHERE user_name = 'yamada';
UPDATE app_user SET api_token = 'token_ad_rep_user',  updated_at = datetime('now') WHERE user_name = 'suzuki';
UPDATE app_user SET api_token = 'token_ad_op_user',   updated_at = datetime('now') WHERE user_name = 'takahashi';
UPDATE app_user SET api_token = 'token_ad_cust_user', updated_at = datetime('now') WHERE user_name = 'sato';

-- 2. 修正 app_user_project_role.role_name
-- 发现：yamada/suzuki/takahashi 在 employees 表里的 role 分别是
-- sales_manager/sales_rep/operator，但 app_user_project_role 里三人此前全部是
-- 通用的 'user'，与 project.yaml 菜单/权限声明的角色名 (sales_manager/sales_rep/
-- service_staff/operator/executive/ai_admin) 不一致。若 ApiEntityAccessGuard 是按
-- 这张表的 role_name 做细粒度校验，之前这三个账号实际上是以 'user' 角色在访问
-- API，而不是各自的业务角色 —— 这本身就是一个值得记录的配置漂移(ROLE_SOURCE_MISMATCH)。
-- 这里将其修正为与 employees.role 一致，作为模拟实验的正确前提。
UPDATE app_user_project_role
SET role_name = 'sales_manager'
WHERE user_id = (SELECT id FROM app_user WHERE user_name = 'yamada')
  AND project_name = 'auto-dealer-demo';

UPDATE app_user_project_role
SET role_name = 'sales_rep'
WHERE user_id = (SELECT id FROM app_user WHERE user_name = 'suzuki')
  AND project_name = 'auto-dealer-demo';

UPDATE app_user_project_role
SET role_name = 'operator'
WHERE user_id = (SELECT id FROM app_user WHERE user_name = 'takahashi')
  AND project_name = 'auto-dealer-demo';

-- sato 已经是 'customer'（与 customer1 一致），此处仅作幂等保障
UPDATE app_user_project_role
SET role_name = 'customer'
WHERE user_id = (SELECT id FROM app_user WHERE user_name = 'sato')
  AND project_name = 'auto-dealer-demo';

-- 3. 验证
SELECT u.id, u.user_name, u.api_token, pr.role_name
FROM app_user u
LEFT JOIN app_user_project_role pr ON u.id = pr.user_id AND pr.project_name = 'auto-dealer-demo'
WHERE u.user_name IN ('yamada', 'suzuki', 'takahashi', 'sato');
