# NetYamlForge 项目上下文

## 项目概述

**NetYamlForge** 是一个基于 **.NET 10 MVC** 的 YAML 配置驱动的动态 CRUD 应用程序。项目采用 **多项目架构**，支持多个独立项目（每个项目拥有独立的数据库、实体定义和仪表板配置）在单一应用中同时运行。支持多语言（en-US、zh-CN、ja-JP、ko-KR）、认证授权、审计日志、HTMX 局部更新和页面定义驱动的动态视图。

### 核心技术栈

| 类别 | 技术 |
|------|------|
| 框架 | ASP.NET Core 10 MVC |
| ORM | Dapper |
| 数据库 | SQLite / SQL Server / PostgreSQL / MySQL |
| UI 框架 | DaisyUI + Bootstrap |
| 局部更新 | HTMX |
| 日志 | Serilog |
| 配置解析 | YamlDotNet |
| 认证 | Cookie Authentication |
| Schema 验证 | JsonSchema.Net |

### 项目结构

```
NetYamlForge/
├── Program.cs                 # 应用入口：DI、认证、本地化、日志配置
├── NetYamlForge.csproj   # 项目文件 (.NET 10)
├── appsettings.json           # 应用配置
├── chinook.db                 # SQLite 数据库（默认）
│
├── Controllers/
│   ├── DynamicEntityController.cs  # 动态 CRUD 核心控制器
│   ├── DashboardController.cs      # 仪表板控制器（统计卡片 + 图表）
│   ├── PageController.cs           # YAML 页面定义驱动控制器
│   ├── AccountController.cs        # 认证（登录/登出）
│   ├── UsersController.cs          # 用户管理
│   ├── HomeController.cs           # 首页（项目一览）
│   └── LocalizationController.cs   # 语言切换
│
├── Models/
│   ├── EntityMetadata.cs      # YAML 元数据模型定义
│   ├── DashboardConfig.cs     # 仪表板配置（统计卡片 + 图表）
│   ├── PageDefinition.cs      # 页面定义模型
│   ├── ProjectConfig.cs       # 项目配置模型
│   └── Auth/
│       ├── AppUser.cs         # 用户模型
│       └── LoginViewModel.cs  # 登录视图模型
│
├── Services/
│   ├── DynamicCrudRepository.cs   # 动态 SQL 生成与执行
│   ├── EntityMetadataProvider.cs  # YAML 元数据加载
│   ├── DashboardConfigProvider.cs # 仪表板配置加载
│   ├── PageMetadataProvider.cs    # 页面定义加载
│   ├── ProjectManager.cs          # 多项目管理（Singleton）
│   ├── ProjectScope.cs            # 请求级项目作用域（Scoped）
│   ├── ProjectAware*.cs           # 项目感知代理（Entity/Dashboard）
│   ├── YamlSchemaValidator.cs     # YAML 架构验证
│   ├── ValueConverter.cs          # 类型转换
│   ├── Dialect/                   # SQL 方言（SQLite/SQL Server/PostgreSQL/MySQL）
│   │   ├── ISqlDialect.cs
│   │   ├── SqliteDialect.cs
│   │   ├── SqlServerDialect.cs
│   │   ├── PostgreSqlDialect.cs
│   │   └── MySqlDialect.cs
│   ├── Hooks/                     # 实体钩子（前处理/后处理）
│   │   ├── IEntityHook.cs
│   │   ├── EntityHookRegistry.cs
│   │   └── CommonHooks.cs         # 通用钩子（验证/转换/审计）
│   ├── ProjectHookRegistry.cs     # 项目固有钩子注册表
│   ├── ProjectHookLoader.cs       # 项目固有钩子加载器
│   └── Auth/
│       ├── UserAuthService.cs     # 用户认证服务
│       └── AuditLogService.cs     # 审计日志服务
│
├── Middleware/
│   ├── ProjectMiddleware.cs   # 项目路由中间件（解析 {project} 参数）
│   └── RequestTraceMiddleware.cs  # 请求追踪中间件（X-Trace-Id）
│
├── Data/
│   └── DbInitializer.cs       # 数据库初始化（Auth 表创建、项目 DB 初始化）
│
├── Views/
│   ├── DynamicEntity/         # 动态实体视图（Index/List/Form/Filter/Definition/Picker）
│   ├── Dashboard/             # 仪表板视图（统计卡片 + Chart.js 图表）
│   ├── Page/                  # YAML 页面视图
│   ├── Account/               # 认证视图
│   ├── Users/                 # 用户管理视图
│   ├── Home/                  # 首页（项目一览）
│   └── Shared/                # 共享布局与部分视图
│
├── Schemas/
│   ├── project-schema.json    # project.yaml JSON 架构（嵌入资源）
│   ├── entity-schema.json     # entities/*.yml JSON 架构
│   ├── dashboard-schema.json  # dashboard.yml JSON 架构
│   └── ui-page-schema.json    # pages/*.yaml JSON 架构
│
├── projects/                  # 多项目目录（每个子目录是一个独立项目）
│   ├── chinook/               # Chinook 音乐商店示例项目
│   │   ├── project.yaml       # 项目配置
│   │   ├── layout.yml         # 布局与导航配置
│   │   ├── dashboard.yml      # 仪表板配置
│   │   ├── entities/          # 实体定义（SQLite）
│   │   ├── entities-sqlserver/# SQL Server 差分实体
│   │   ├── pages/             # 页面定义 YAML
│   │   ├── views/             # 项目专用视图
│   │   ├── Hooks/             # 项目固有钩子
│   │   └── database/          # 数据库文件
│   ├── todo/                  # TODO 项目管理项目
│   ├── blog/                  # 博客项目
│   ├── northwind-sqlite3/     # Northwind 示例项目
│   ├── b2b-order-ops/         # B2B 受发注管理项目
│   └── ...                    # 更多项目
│
├── Resources/                 # 多语言 RESX 资源
├── Localization/              # 本地化资源类
└── docs/                      # 文档目录
```

## 构建与运行

### 前置条件

- .NET 10 SDK
- SQLite（内置，无需单独安装）
- （可选）SQL Server / PostgreSQL / MySQL

### 构建命令

```bash
dotnet restore
dotnet build
```

### 运行命令

```bash
dotnet run
```

应用默认在 `http://localhost:5239` 启动（具体端口见控制台输出）。

**多项目路由格式：**
```
/{project}/{controller=Dashboard}/{action=Index}/{id?}
```

示例：
- `/` - 首页（项目一览）
- `/chinook/Dashboard/Index` - Chinook 项目仪表板
- `/todo/Dashboard/Index` - TODO 项目仪表板
- `/blog/Dashboard/Index` - 博客项目仪表板
- `/chinook/DynamicEntity/Index?entity=customer` - Chinook 客户管理
- `/todo/DynamicEntity/Index?entity=task` - TODO 任务管理
- `/b2b-order-ops/Page/OrderWorkbench` - B2B 订单工作台

### 默认管理员账户

首次运行时会自动创建默认管理员：

| 字段 | 值 |
|------|-----|
| 用户名 | `admin` |
| 密码 | `Admin@123` |

**注意：** 首次登录后请立即修改密码。

### 测试命令

```bash
dotnet test
```

## 核心功能

### 1. 多项目架构

项目支持多个独立项目同时运行，每个项目拥有：
- 独立的数据库（SQLite / SQL Server / PostgreSQL / MySQL）
- 独立的实体定义（`entities/*.yml`）
- 独立的仪表板配置（`dashboard.yml`）
- 独立的页面定义（`pages/*.yaml`）
- 可选的项目专用视图和钩子

**项目配置文件（`project.yaml`）：**
```yaml
name: todo
displayName: TODO Project Manager
version: "1.0.0"
database:
  type: sqlite          # sqlite | sqlserver | postgresql | mysql
  path: database/todo.db
features:
  multiLanguage: true
  userAuthentication: true
layout:
  header:
    title: My Project
  navigation:
    showDashboard: true
    entities:
      - task
      - project
```

**核心组件：**
| 组件 | 作用 |
|------|------|
| `ProjectManager` | Singleton，启动时扫描 `projects/` 目录，加载并验证所有 `project.yaml` |
| `ProjectScope` | Scoped，保存当前请求的项目上下文 |
| `ProjectMiddleware` | 解析 URL 中的 `{project}` 参数，初始化 `ProjectScope` |
| `ProjectAwareEntityMetadataProvider` | 项目感知的实体元数据提供者代理 |
| `ProjectAwareDashboardConfigProvider` | 项目感知的仪表板配置提供者代理 |
| `ProjectHookLoader` | 加载项目固有钩子（`Hooks/` 目录） |
| `ProjectHookRegistry` | 项目固有钩子注册表 |

### 2. YAML 驱动的动态 CRUD

实体配置位于 `projects/{name}/entities/*.yml`，定义：

- **表结构**：表名、主键、关联（JOIN）
- **列定义**：类型、标签、可搜索/可排序标志、表达式
- **表单定义**：输入类型、验证规则、外键关联
- **筛选器**：下拉、多选、范围、日期范围
- **分页设置**：页大小、模式（numbered/keyset）
- **多语言**：`displayNameI18n`、`labelI18n`
- **布局**：表单/筛选器的列数和字段顺序
- **确认对话框**：新建/更新/删除确认消息
- **钩子**：前后处理钩子配置

示例配置片段：

```yaml
imports: []
entities:
  customer:
    table: Customer
    key: CustomerId
    displayName: Customer
    displayNameI18n:
      ja-JP: 顧客
    softDelete: false
    paging:
      pageSize: 10
      mode: numbered
    joins:
      - type: left
        table: Employee
        alias: e
        on: Customer.SupportRepId = e.EmployeeId
    columns:
      CustomerId:
        type: int
        identity: true
        label: ID
        sortable: true
      FirstName:
        type: string
        required: true
        label: First Name
        searchable: true
      SupportRepName:
        type: string
        label: Support Rep
        expression: e.LastName || ', ' || e.FirstName
        sortable: true
    forms:
      FirstName:
        type: string
        required: true
        editable: true
      SupportRepId:
        type: int
        foreignKey:
          entity: employee
          displayColumns: [LastName, FirstName, Title]
          picker: true
    filters:
      Country:
        type: multi-select
        options: [USA, Canada, Brazil]
    layout:
      forms:
        columns: 2
        order: [FirstName, LastName, Email, Country]
      filters:
        columns: 3
    confirmation:
      create: 新しい顧客を登録してよろしいですか？
      update: 顧客情報を更新してよろしいですか？
    hooks:
      beforeCreate: [validate_email, validate_required, trim]
      afterCreate: [audit_log, customer_welcome]
```

### 3. 多数据库支持

项目支持多种数据库，通过 `project.yaml` 切换：

```yaml
database:
  type: sqlite          # sqlite | sqlserver | postgresql | mysql
  path: database/app.db
  # 或
  connectionString: "Server=localhost;Database=App;Trusted_Connection=True;"
```

**SQL 方言抽象（`ISqlDialect`）：**
| 方言 | 分页语法 | 字符串连接 |
|------|---------|-----------|
| SQLite | `LIMIT/OFFSET` | `||` |
| SQL Server | `OFFSET/FETCH NEXT` | `+` |
| PostgreSQL | `LIMIT/OFFSET` | `||` |
| MySQL | `LIMIT/OFFSET` | `CONCAT()` |

### 4. 仪表板与图表

**统计卡片（`dashboard.yml`）：**
- 支持 `count`/`sum`/`avg` 聚合
- 可配置多语言标签、图标、颜色
- 支持 WHERE 过滤条件
- 支持点击跳转到实体列表

**Chart.js 图表：**
- 支持 `bar`/`line`/`doughnut`/`pie` 类型
- 支持 GROUP BY 表达式和 FK JOIN 获取标签
- 可配置排序、限制数量、自定义颜色

示例配置：
```yaml
stats:
  - label: Total Revenue
    entity: invoice
    aggregate: sum
    column: Total
    icon: "💰"
    color: badge-success
    link: /chinook/DynamicEntity/Index?entity=invoice

charts:
  - title: Monthly Revenue
    type: line
    entity: invoice
    valueAggregate: sum
    valueColumn: Total
    groupExpression: "strftime('%Y-%m', InvoiceDate)"
```

### 5. 页面定义驱动（Page 功能）

通过 YAML 定义动态生成页面视图：

```yaml
page:
  title: Blog List
  layout: list    # list/form/dashboard
  entity: post
  columns: [Title, Author, PublishDate, Status]
  filters: [Status, DateRange]
```

**核心组件：**
- `PageController`：解析 YAML 并渲染视图
- `PageMetadataProvider`：加载 `pages/*.yaml`
- `ProjectViewLocationExpander`：项目专用视图路径展开

### 6. 认证与授权

- **Cookie 认证**：滑动过期 8 小时
- **角色策略**：`AdminOnly` 策略限制管理员访问
- **用户管理**：用户列表、创建、编辑、激活/禁用
- **默认管理员**：用户名 `admin`，密码 `Admin@123`（首次登录后请立即修改）

### 7. 审计日志

所有 CRUD 操作和用户管理操作均记录到 `AuditLog` 表：

```sql
CREATE TABLE AuditLog (
    Id INTEGER PRIMARY KEY,
    UserName TEXT,
    Action TEXT,
    Entity TEXT,
    Detail TEXT,
    CreatedAt TEXT
);
```

### 8. 多语言支持

支持四种语言切换：

| 代码 | 语言 |
|------|------|
| `en-US` | 英语 |
| `zh-CN` | 简体中文 |
| `ja-JP` | 日语 |
| `ko-KR` | 韩语 |

语言切换通过 `LocalizationController` 实现，UI 文本使用 RESX 资源文件和 YAML 多语言配置管理。

### 9. 日志系统

Serilog 配置：

- **控制台输出**：开发调试
- **文件输出**：`logs/app-YYYYMMDD.log`，保留 14 天
- **请求日志**：所有 HTTP 请求自动记录（含 `X-Trace-Id` 和 `Project`）
- **慢查询日志**：超过阈值的 SQL 自动记录（默认 1000ms）

### 10. 实体钩子（Hooks）与确认对话框

**钩子类型：**
- `beforeCreate` / `beforeUpdate` / `beforeDelete`：DB 写入前执行（验证、数据转换）
- `afterCreate` / `afterUpdate` / `afterDelete`：DB 写入后执行（日志记录、通知）

**通用钩子（内置）：**
| 钩子名 | 功能 |
|--------|------|
| `validate_required` | 必填验证 |
| `validate_email` | 邮箱格式验证 |
| `validate_phone` | 电话格式验证 |
| `validate_url` | URL 格式验证 |
| `validate_regex` | 正则表达式验证 |
| `validate_range` | 范围验证 |
| `validate_unique` | 唯一性验证 |
| `trim` | 去除空格 |
| `uppercase` / `lowercase` / `titlecase` | 大小写转换 |
| `default` | 默认值设置 |
| `now` | 当前时间戳 |
| `current_user` | 当前用户 |
| `audit_log` | 审计日志记录 |
| `webhook` | Webhook 通知 |
| `soft_delete` | 软删除 |

**确认对话框：**
- `confirmation.create`：新建确认消息
- `confirmation.update`：更新确认消息
- `confirmation.delete`：删除确认消息

示例配置：
```yaml
entities:
  customer:
    confirmation:
      create: "新しい顧客を登録してよろしいですか？"
      update: "顧客情報を更新してよろしいですか？"
    hooks:
      beforeCreate:
        - "validate_email"
        - "validate_required:FirstName,LastName"
        - "trim:FirstName,LastName,Email"
      afterCreate:
        - "audit_log"
        - "customer_welcome"  # 项目固有钩子
```

**项目固有钩子：**
每个项目可在 `Hooks/` 目录下定义自己的钩子实现：

```csharp
// projects/chinook/Hooks/SampleHooks.cs
public class ChinookCustomerWelcomeHook : IEntityHook
{
    public string Name => "chinook_customer_welcome";
    public Task<bool> BeforeAsync(EntityHookContext ctx) => Task.FromResult(true);
    public async Task AfterAsync(EntityHookContext ctx)
    {
        // 发送欢迎邮件等
        _logger.LogInformation("[Chinook] 顧客 {Name} さん、ようこそ！", ctx.Values["FirstName"]);
    }
}
```

### 11. 复合主键支持

支持复合主键（多列主键）定义：

```yaml
entities:
  orderdetail:
    table: OrderDetail
    keys: ["OrderId", "ProductId"]  # 复合主键
    displayName: Order Detail
```

URL 格式：
- 单一主键：`/chinook/DynamicEntity/EditPage?entity=customer&id=123`
- 复合主键：`/chinook/DynamicEntity/EditPage?entity=orderdetail&id={"OrderId":1001,"ProductId":5}`

### 12. 外键增强功能

**多列显示（`displayColumns`）：**
```yaml
foreignKey:
  entity: employee
  displayColumns: [LastName, FirstName, Title]  # 多列标签显示
```

**自定义查询（`query`）：**
```yaml
foreignKey:
  entity: employee
  displayColumns: [LastName, FirstName, Title]
  query: |
    SELECT EmployeeId AS Id, LastName, FirstName, Title
    FROM Employee
    WHERE IsActive = 1
```

**Picker 模式：**
```yaml
foreignKey:
  entity: employee
  picker: true       # 使用模态框选择
  multiPicker: true  # 支持多选
```

### 13. 实体定义管理页面

管理员可通过 Web 界面查看实体定义：

- **单个实体定义**：`/{project}/DynamicEntity/Definition?entity={name}`
- **全部实体一览**：`/{project}/DynamicEntity/AllDefinitions`

显示标签页：
- Columns：列定义一览
- Forms：表单定义一览
- Filters：筛选器定义一览
- Joins：JOIN 定义一览
- Links：实体间链接定义
- Settings：分页、布局、确认、钩子设置

## 开发约定

### 代码风格

- **可空引用类型**：启用（`<Nullable>enable</Nullable>`）
- **隐式 using**：启用（`<ImplicitUsings>enable</ImplicitUsings>`）
- **注释语言**：核心文件使用日语注释（`ファイル概要`）
- **日志规范**：关键操作记录 `ILogger`，包含实体名、ID、SQL
- **警告抑制**：CS8602/CS8620/CS8625 通过 `<NoWarn>` 抑制

### 多项目开发规范

1. **新增项目**：在 `projects/` 目录下创建新项目文件夹
2. **project.yaml**：必须包含 `name`、`displayName`、`database` 配置
3. **实体定义**：放在 `entities/` 目录，支持分文件或合并文件
4. **数据库**：放在 `database/` 目录，支持 `.db` 或 `.sqlite` 扩展名
5. **项目固有钩子**：放在 `Hooks/` 目录，实现 `IEntityHook` 接口

### 事务处理

CRUD 操作与审计日志写入在同一事务中执行：

```csharp
await using var tx = conn.BeginTransaction();
try
{
    await _repo.InsertAsync(entity, values, tx);
    await _audit.LogAsync(user, "Create", entity, detail, tx);
    await tx.CommitAsync();
}
catch
{
    await tx.RollbackAsync();
    throw;
}
```

### SQL 安全

动态 SQL 生成时进行严格验证：

- **标识符验证**：`^[A-Za-z_][A-Za-z0-9_]*$`
- **表达式验证**：白名单字符集，拒绝 `;`、`--`、`/* */`
- **JOIN 类型限制**：仅允许 `LEFT`、`INNER`、`RIGHT`
- **多项目隔离**：每个项目使用独立数据库，防止跨项目数据访问

### YAML Schema 验证

启动时对所有 YAML 配置进行 JSON Schema 验证：

| 配置文件 | Schema 文件 |
|---------|-----------|
| `project.yaml` | `project-schema.json` |
| `entities/*.yml` | `entity-schema.json` |
| `dashboard.yml` | `dashboard-schema.json` |
| `pages/*.yaml` | `ui-page-schema.json` |

### 提交规范

每次 push 前必须更新 `docs/CHANGELOG.md`，记录：

1. 变更内容（Added/Fixed/Changed/Removed）
2. 影响范围
3. 验证结果（至少 1 个验证点）

## 关键接口与类

### 多项目核心组件

```csharp
// ProjectManager (Singleton)
IReadOnlyCollection<ProjectInfo> GetAll();    // 获取所有已加载项目
bool TryGet(string name, out ProjectInfo? info);  // 按名称获取项目

// ProjectScope (Scoped)
bool IsSet { get; }              // 是否已设置项目上下文
ProjectInfo Current { get; }     // 当前项目信息
void Set(ProjectInfo project);   // 设置当前项目
void Clear();                    // 清除项目上下文
```

### IDynamicCrudRepository

```csharp
// 一覧取得：通常分页 / count 省略 / keyset 光标方式支持
Task<IEnumerable<dynamic>> GetAllAsync(
    string entity,
    string? search,
    string? sort,
    string? dir,
    Dictionary<string, string?>? filters = null,
    int page = 1,
    int? pageSize = null,
    string? cursor = null,
    bool keyset = false,
    bool fetchOneExtra = false);

Task<dynamic?> GetByIdAsync(string entity, object id);
Task<dynamic?> GetByIdAsync(string entity, IDictionary<string, object?> keyValues);  // 复合主键

Task<int> InsertAsync(string entity, IDictionary<string, object?> values, IDbTransaction? tx = null);
Task<int> UpdateAsync(string entity, object id, IDictionary<string, object?> values, IDbTransaction? tx = null);
Task<int> UpdateAsync(string entity, IDictionary<string, object?> keyValues, IDictionary<string, object?> values, IDbTransaction? tx = null);  // 复合主键
Task<int> DeleteAsync(string entity, object id, IDbTransaction? tx = null);
Task<int> DeleteAsync(string entity, IDictionary<string, object?> keyValues, IDbTransaction? tx = null);  // 复合主键

Task<IEnumerable<dynamic>> GetAllForEntityAsync(...);  // Picker 用
Task<int> CountAsync(string entity, string? search, Dictionary<string, string?>? filters = null);
```

### IEntityMetadataProvider

```csharp
EntityDefinition Get(string entityName);
IReadOnlyDictionary<string, EntityDefinition> GetAll();
```

### IEntityHook

```csharp
string Name { get; }
Task<bool> BeforeAsync(EntityHookContext ctx);   // 前处理（返回 false 可中断）
Task AfterAsync(EntityHookContext ctx);          // 后处理
```

### 认证服务

```csharp
// IUserAuthService
Task<AppUser?> GetUserByIdAsync(int id, IDbTransaction? tx = null);
Task<IEnumerable<AppUser>> GetAllUsersAsync();
Task<int> CreateUserAsync(AppUser user, string password, IDbTransaction? tx = null);
Task<int> UpdateUserAsync(AppUser user, IDbTransaction? tx = null);

// IAuditLogService
Task LogAsync(string userName, string action, string? entity, string? detail, IDbTransaction? tx = null);
```

## CLI 命令

### 项目初始化

```bash
# 创建新项目
dotnet run -- --init-project \
  --project=demo-ops \
  --display-name="Demo Ops" \
  --db-type=sqlite \
  --db-path=database/demo-ops.db
```

### 实体脚手架

```bash
# 从数据库自动生成实体 YAML
dotnet run -- --scaffold-entities \
  --project=demo-ops \
  --output-dir=entities \
  --with-label-keys
```

### YAML 升级

```bash
# 升级实体 YAML 到最新格式
dotnet run -- --upgrade-entity-yaml \
  --project=demo-ops
```

## 常见问题

### 项目未被识别

确认 `projects/{name}/project.yaml` 文件存在且格式正确：

```yaml
name: myproject
displayName: My Project
database:
  type: sqlite
  path: database/myproject.db
```

### 数据库文件不存在

首次运行时 `DbInitializer` 会自动：
1. 扫描 `projects/` 目录下所有 `project.yaml`
2. 检查每个项目的数据库文件
3. 对于 SQLite 项目，如果数据库不存在则创建

### 实体配置未生效

检查 `projects/{name}/entities/` 目录下的 YAML 文件：

1. 文件扩展名必须为 `.yml`
2. 根节点必须是 `entities:`
3. 表达式中的单引号需用双引号包裹

### 认证失败

确认 `AppUser` 表已创建且存在有效用户：

```bash
sqlite3 projects/chinook/database/chinook.db "SELECT * FROM AppUser;"
```

### 钩子未执行

1. 确认钩子名称在 YAML 中正确配置
2. 确认钩子实现类已注册到 DI（通用钩子在 `Program.cs`，项目固有钩子自动加载）
3. 检查日志输出确认钩子执行状态

## 相关文档

| 文档 | 说明 |
|------|------|
| `docs/README-ja.md` | 文档导航（日语） |
| `docs/quickstart-ja.md` | 5 分钟快速入门 |
| `docs/framework-overview-tutorial-ja.md` | 框架概述与详细教程 |
| `docs/operations-checklist-ja.md` | 运维检查清单 |
| `docs/dashboard.md` | 仪表板配置说明 |
| `docs/project-hooks-guide.md` | 项目固有钩子指南 |
| `docs/COMMON_HOOKS.md` | 通用钩子参考 |
| `docs/confirmation-and-hooks.md` | 确认对话框与钩子机制 |
| `docs/sqlserver-setup.md` | SQL Server 环境设置指南 |
| `docs/composite-key-example.md` | 复合主键配置示例 |
| `docs/foreignkey-displaycolumns-query-ja.md` | 外键多列显示与自定义查询 |
| `docs/CHANGELOG.md` | 变更历史 |
