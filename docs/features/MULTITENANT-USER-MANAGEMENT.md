# 汽车销售多租户用户管理方案

## 概述

本文档描述了 NetYamlForge 框架中汽车销售场景（auto-dealer-demo）的多租户用户管理实现方案。

---

## 核心设计

### 用户类型分类

| 用户类型 | 说明 | 典型角色 |
|---------|------|---------|
| **employee** | 销售公司员工 | admin, sales_manager, sales_rep |
| **customer** | 购车客户 | customer |
| **third_party** | 第三方合作伙伴 | vendor, logistics, finance |

### 数据模型

```
全局层 (framework 项目)
├── app_user (全局用户账户)
│   ├── id
│   ├── user_name
│   ├── password_hash
│   ├── user_type (employee/customer/third_party)
│   └── default_project_name
│
├── app_user_project_role (项目角色关联)
│   ├── user_id
│   ├── project_name
│   ├── role_name
│   └── permission_scope (JSON)
│
业务层 (auto-dealer-demo 项目)
├── customers (客户档案)
│   ├── customer_id
│   ├── app_user_id (FK -> app_user)
│   └── 业务字段...
│
├── employees (员工信息)
│   ├── employee_id
│   ├── app_user_id (FK -> app_user)
│   └── 业务字段...
│
└── third_party_users (第三方用户)
    ├── third_party_id
    ├── app_user_id (FK -> app_user)
    └── 业务字段...
```

---

## 角色权限矩阵

### 销售公司员工

| 角色 | 权限范围 | 典型操作 |
|------|---------|---------|
| `admin` | 全系统 | 系统设置、用户管理、所有业务操作 |
| `sales_manager` | 销售团队 | 团队管理、报价审批、业绩查看、客户分配 |
| `sales_rep` | 个人客户 | 客户管理、报价创建、跟进记录 |

### 客户

| 角色 | 权限范围 | 典型操作 |
|------|---------|---------|
| `customer` | 仅自己的数据 | 查看订单、预约服务、提交咨询、查看报价 |

### 第三方用户

| 角色 | 权限范围 | 典型操作 |
|------|---------|---------|
| `vendor` | 供应商模块 | 查看采购订单、更新供货状态、报价提交 |
| `logistics` | 物流模块 | 更新配送状态、查看配送清单、签收确认 |
| `finance` | 金融模块 | 贷款审批、保险办理、付款确认 |

---

## 已创建的文件

### Entity YAML

| 文件 | 项目 | 说明 |
|------|------|------|
| `framework/entities/app_user.yml` | framework | 全局用户表 |
| `framework/entities/app_user_project_role.yml` | framework | 项目角色关联表 |
| `auto-dealer-demo/entities/customers.yml` | auto-dealer-demo | 客户档案（已添加 app_user_id 关联） |
| `auto-dealer-demo/entities/employees.yml` | auto-dealer-demo | 员工信息（已添加 app_user_id 关联） |
| `auto-dealer-demo/entities/third_party_users.yml` | auto-dealer-demo | 第三方用户（新建） |

### C# 服务层

| 文件 | 说明 |
|------|------|
| `Services/Tenant/ITenantUserService.cs` | 多租户用户服务接口 |
| `Services/Tenant/TenantUserService.cs` | 多租户用户服务实现 |
| `Services/Tenant/ProjectScopeMiddleware.cs` | 项目范围验证中间件 |
| `Controllers/TenantAccountController.cs` | 多租户认证控制器 |

---

## 认证流程

### 登录流程

```
1. 用户访问 /Account/Login
2. 输入用户名和密码
3. TenantUserService.ValidateCredentialsAsync 验证
4. 获取用户可访问的项目列表
5. 创建 Claims (包含用户 ID、项目列表、默认项目)
6. 生成 Cookie
7. 重定向到默认项目 Dashboard
```

### 项目访问验证

```
1. 用户访问 /{project}/Customers
2. ProjectScopeMiddleware 提取 project 名称
3. 验证用户是否有该项目访问权限
4. 无权限 -> 重定向到 /Account/AccessDenied
5. 有权限 -> 继续处理请求
```

### 项目选择器

```
1. 用户访问 /Account/SelectProject
2. 显示用户可访问的所有项目
3. 用户选择项目
4. 重定向到 /{project}/Dashboard
```

---

## 使用示例

### 1. 创建销售员工

```csharp
// 管理员创建销售员工
var request = new CreateUserRequest
{
    UserName = "zhangsan",
    Password = "SecurePass123",
    DisplayName = "张三",
    Email = "zhangsan@dealer.com",
    Phone = "13800138000",
    UserType = "employee",
    DefaultProjectName = "auto-dealer-demo",
    ProjectRole = "sales_rep",
    CreatedByUserId = 1 // 管理员 ID
};

var userId = await _tenantUsers.CreateUserWithProjectRoleAsync(request);
```

### 2. 为客户创建账户

```csharp
// 销售为客户创建账户
var request = new CreateUserRequest
{
    UserName = "customer_li",
    Password = "TempPass123",
    DisplayName = "李先生",
    Email = "li@example.com",
    Phone = "13900139000",
    UserType = "customer",
    DefaultProjectName = "auto-dealer-demo",
    ProjectRole = "customer",
    CreatedByUserId = 2 // 销售 ID
};

var userId = await _tenantUsers.CreateUserWithProjectRoleAsync(request);

// 关联到客户档案
await _db.ExecuteAsync(@"
    UPDATE customers SET app_user_id = @UserId WHERE customer_id = @CustomerId
", new { UserId = userId, CustomerId = "CUST-2026-001" });
```

### 3. 为第三方用户分配权限

```csharp
// 为物流公司创建账户
var request = new CreateUserRequest
{
    UserName = "logistics_wuliu",
    Password = "TempPass123",
    DisplayName = "XX 物流公司",
    Email = "contact@wuliu.com",
    Phone = "400-888-8888",
    UserType = "third_party",
    DefaultProjectName = "auto-dealer-demo",
    ProjectRole = "logistics",
    CreatedByUserId = 1
};

var userId = await _tenantUsers.CreateUserWithProjectRoleAsync(request);

// 关联到第三方用户档案
await _db.ExecuteAsync(@"
    INSERT INTO third_party_users (third_party_id, app_user_id, company_name, service_type)
    VALUES (@Id, @UserId, @Company, @Service)
", new { Id = "TP-001", UserId = userId, Company = "XX 物流公司", Service = "logistics" });
```

### 4. 查询用户可访问的项目

```csharp
var projects = await _tenantUsers.GetAccessibleProjectsAsync(userId);
// 返回：["auto-dealer-demo", "inventory", "service-center"]
```

### 5. 检查项目访问权限

```csharp
var hasAccess = await _tenantUsers.HasProjectAccessAsync(userId, "auto-dealer-demo");
// 返回：true 或 false
```

---

## 配置步骤

### 1. 注册服务 (Program.cs)

```csharp
// 添加多租户用户服务
builder.Services.AddScoped<ITenantUserService, TenantUserService>();

// 添加 Cookie 认证
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
    });

builder.Services.AddAuthorization();
```

### 2. 添加中间件 (Program.cs)

```csharp
// 在 UseAuthentication 和 UseAuthorization 之间添加
app.UseAuthentication();
app.UseAuthorization();
app.UseProjectScope(); // 项目范围验证
```

### 3. 数据库迁移

```bash
# 使用 CLI 工具生成数据库
dotnet run -- --scaffold-entities --project=framework --no-overwrite
dotnet run -- --scaffold-entities --project=auto-dealer-demo --no-overwrite
```

### 4. 初始化默认管理员

```sql
-- 创建默认管理员账户
INSERT INTO app_user (user_name, password_hash, display_name, email, user_type, is_active, created_at, updated_at)
VALUES ('admin', '<哈希密码>', '系统管理员', 'admin@dealer.com', 'employee', 1, datetime('now'), datetime('now'));

-- 分配项目角色
INSERT INTO app_user_project_role (user_id, project_name, role_name, assigned_by, created_at)
VALUES (1, 'auto-dealer-demo', 'admin', 1, datetime('now'));
```

---

## 视图文件（待创建）

### Views/TenantAccount/

| 文件 | 说明 |
|------|------|
| `Login.cshtml` | 登录页面 |
| `Logout.cshtml` | 登出确认 |
| `SelectProject.cshtml` | 项目选择器 |
| `AccessDenied.cshtml` | 访问拒绝页面 |
| `Register.cshtml` | 注册页面 |

### 共享组件

| 文件 | 说明 |
|------|------|
| `Views/Shared/_ProjectSelector.cshtml` | 项目选择下拉框 |
| `Views/Shared/_UserMenu.cshtml` | 用户菜单 |

---

## 安全考虑

### 密码存储

- ✅ 使用 BCrypt 或 PBKDF2 进行密码哈希
- ❌ 不要使用明文或简单哈希（如 MD5、SHA1）

### SQL 注入防护

- ✅ 使用 Dapper 参数化查询
- ❌ 禁止字符串插值拼接 SQL

### 权限验证

- ✅ 每次请求验证项目访问权限
- ✅ 在服务层再次验证权限（纵深防御）
- ❌ 不要仅依赖前端隐藏

### 会话管理

- ✅ 设置合理的 Cookie 过期时间
- ✅ 实现登出功能
- ✅ 支持强制下线（禁用用户）

---

## 测试计划

### 单元测试

```csharp
// TenantUserServiceTests.cs
- ValidateCredentialsAsync_ValidCredentials_ReturnsUser
- ValidateCredentialsAsync_InvalidPassword_ReturnsNull
- GetProjectRolesAsync_UserHasRoles_ReturnsRoles
- HasProjectAccessAsync_UserHasAccess_ReturnsTrue
- CreateUserWithProjectRoleAsync_ValidRequest_CreatesUser
```

### 集成测试

```csharp
// TenantAccountControllerTests.cs
- Login_ValidCredentials_RedirectsToProject
- Login_InvalidCredentials_ShowsError
- Logout_RedirectsToLogin
- SelectProject_ShowsUserProjects
- AccessDenied_ShowsMessage
```

---

## 后续扩展

### 1. 角色权限细化

```yaml
# 在 app_user_project_role 中添加 permission_scope
permission_scope: |
  {
    "modules": ["customers", "orders", "quotes"],
    "actions": ["read", "write"],
    "constraints": {
      "customers": "assigned_to = @UserId"
    }
  }
```

### 2. 项目模板

- 预定义项目角色模板
- 批量分配角色

### 3. 审计日志

- 记录用户登录历史
- 记录项目访问日志
- 记录权限变更

### 4. 多因素认证 (MFA)

- 短信验证码
- 邮箱验证
- TOTP (Google Authenticator)

---

## 故障排查

### 问题：用户登录后无法访问项目

**检查清单**:
1. 确认 `app_user_project_role` 表中有对应记录
2. 检查 `project_name` 拼写是否一致
3. 确认中间件 `UseProjectScope()` 已注册
4. 查看日志中的权限验证信息

### 问题：创建用户时报错"用户名已存在"

**解决方案**:
- 检查 `app_user.user_name` 的唯一约束
- 使用不同的用户名或先删除重复记录

### 问题：跨项目引用不生效

**解决方案**:
- 确认 YAML 中 `reference.crossProject: true` 已设置
- 检查 `app_user` 实体在 framework 项目中已正确加载

---

*文档创建日期：2026 年 4 月 2 日*
