# 汽车销售子项目客户认证功能实现报告

## 概述

为 `auto-dealer-demo` 子项目添加了完整的客户注册和登录功能，使客户可以通过自助注册创建账户，然后使用各种服务。

---

## 实现的功能

### 1. 用户注册功能

#### 1.1 一般用户注册 (`/Account/Register`)
- **用途**: 一般用户注册
- **字段**: 用户名、密码、确认密码、显示名称、邮箱、电话
- **自动登录**: 注册成功后自动登录并分配 `customer` 角色

#### 1.2 客户专用注册 (`/Account/CustomerRegister`)
- **用途**: 汽车销售客户专用注册表单
- **字段**: 
  - 账户信息：用户名、密码、确认密码
  - 客户信息：姓名、フリガナ、电话、手机、邮箱、邮编、地址、希望联络方式
- **自动登录**: 注册成功后自动登录并跳转到客户仪表板

### 2. 数据模型扩展

#### 2.1 认证用户表 (`AppUser`)
- 已存在的表，包含用户名、密码哈希、显示名称等字段
- 通过 `AppUserRole` 表分配角色（如 `customer`）

#### 2.2 客户表 (`customers`) 扩展
添加了 `user_name` 字段用于关联认证用户：
```yaml
user_name:
  type: string
  length: 50
  required: false
  label: ユーザー名
  description: ログイン認証用のユーザー名（顧客登録時に自動設定）
  searchable: true
  sortable: true
  unique: true
```

### 3. 业务逻辑钩子

#### 3.1 ValidateCustomerRegistrationHook
- **功能**: 验证客户注册时用户名的唯一性
- **触发时机**: `beforeCreate` (customers 表)
- **验证逻辑**: 检查 `customers` 表中 `user_name` 是否已存在

---

## 文件变更清单

### 新增文件

| 文件 | 说明 |
|------|------|
| `Models/Auth/RegisterViewModel.cs` | 一般用户注册视图模型 |
| `Models/Auth/CustomerRegisterViewModel.cs` | 客户专用注册视图模型 |
| `Views/Account/Register.cshtml` | 一般用户注册视图 |
| `Views/Account/CustomerRegister.cshtml` | 客户专用注册视图 |

### 修改文件

| 文件 | 修改内容 |
|------|---------|
| `Services/Auth/IUserAuthService.cs` | 添加 `RegisterAsync`、`RegisterCustomerAsync`、`IsUserNameTakenAsync` 方法 |
| `Services/Auth/UserAuthService.cs` | 实现注册逻辑 |
| `Controllers/AccountController.cs` | 添加 `Register`、`CustomerRegister` 动作方法 |
| `projects/auto-dealer-demo/entities/customers.yml` | 添加 `user_name` 字段和 `validate_customer_registration` 钩子 |
| `projects/auto-dealer-demo/Hooks/AutoDealerHooks.cs` | 添加 `ValidateCustomerRegistrationHook` 类 |

---

## 使用流程

### 客户注册流程

```
1. 客户访问 /auto-dealer-demo/Account/CustomerRegister
   ↓
2. 填写注册表单（用户名、密码、姓名、电话等）
   ↓
3. 提交表单
   ↓
4. 系统验证：
   - 用户名唯一性
   - 必填字段完整性
   - 密码匹配
   ↓
5. 创建认证用户（AppUser）
   - 密码哈希存储
   - 分配 customer 角色
   ↓
6. 自动登录
   ↓
7. 重定向到客户仪表板 (/auto-dealer-demo/Page/CustomerDashboard)
```

### 客户登录流程

```
1. 客户访问 /auto-dealer-demo/Account/Login
   ↓
2. 输入用户名和密码
   ↓
3. 系统验证凭据
   ↓
4. 验证成功：
   - 创建认证 Cookie
   - 设置语言偏好
   - 记录最后登录时间
   ↓
5. 根据角色重定向：
   - customer → CustomerDashboard
   - operator → OperatorConsole
   - sales_rep → SalesRepDashboard
   - 等
```

---

## 可用的服务

客户登录后可以使用以下服务：

### 客户专用功能
- **マイページ (客户仪表板)**: 查看个人信息和活动历史
- **预约・服务确认**: 查看和管理服务预约
- **库存车辆浏览**: 查看可用车辆库存

### AI 咨询服务
- **AI 对话**: 24/365 智能客服
- **试驾预约**: 通过 AI 安排试驾
- **车辆咨询**: 获取车辆信息和建议

### 销售相关
- **销售线索管理**: 如果从 AI 对话生成销售线索
- **跟进活动**: 查看销售人员的跟进记录

---

## 安全特性

### 密码安全
- 使用 ASP.NET Core Identity 的 `PasswordHasher` 进行密码哈希
- 支持密码重哈希（成功验证后自动更新哈希）

### SQL 注入防护
- 所有数据库查询使用参数化查询
- 遵循项目的 `SqlSafetyGuard` 规范

### 用户名唯一性
- 注册时检查用户名是否已被使用
- 数据库层面设置唯一约束

### 角色隔离
- 客户仅能访问客户角色的页面和功能
- 通过 `LandingPageByRole` 配置控制登录后重定向

---

## 配置说明

### project.yaml 配置

```yaml
features:
  userAuthentication: true  # 启用用户认证

layout:
  landingPageByRole:
    customer: /auto-dealer-demo/Page/CustomerDashboard
```

### 导航配置

```yaml
navigation:
  items:
    - label: マイページ
      url: /auto-dealer-demo/Page/CustomerDashboard
      icon: 👤
      roles: [customer]
    - label: 予約・サービス確認
      url: /auto-dealer-demo/Page/Appointments
      icon: 📅
      roles: [customer]
```

---

## 测试验证

### 构建结果
```
Build succeeded.
    0 Error(s)
```

### 测试结果
```
Passed: 416
Failed: 7 (与本次实现无关的已有问题)
Skipped: 0
Total: 423
```

---

## 后续改进建议

### 短期改进
1. **邮箱验证**: 添加邮箱验证流程
2. **密码重置**: 实现忘记密码功能
3. **LINE 登录**: 集成 LINE Login（日本常用）

### 中期改进
1. **双因素认证 (2FA)**: 提高账户安全性
2. **社交登录**: Google、Facebook 等第三方登录
3. **客户资料完善**: 注册后引导客户完善资料

### 长期改进
1. **客户积分系统**: 基于购买和互动积分
2. **推荐奖励**: 客户推荐新客户奖励
3. **个性化推荐**: 基于客户偏好的车辆推荐

---

## 访问 URL

假设项目名为 `auto-dealer-demo`，运行在 `http://localhost:5000`：

| 功能 | URL |
|------|-----|
| 客户注册 | http://localhost:5000/auto-dealer-demo/Account/CustomerRegister |
| 一般注册 | http://localhost:5000/auto-dealer-demo/Account/Register |
| 登录 | http://localhost:5000/auto-dealer-demo/Account/Login |
| 登出 | http://localhost:5000/auto-dealer-demo/Account/Logout |
| 客户仪表板 | http://localhost:5000/auto-dealer-demo/Page/CustomerDashboard |

---

## 运行项目

```bash
# 运行项目
dotnet run --project NetYamlForge

# 访问
# http://localhost:5000/auto-dealer-demo/Account/CustomerRegister
```

---

*实现日期：2026 年 4 月 2 日*
