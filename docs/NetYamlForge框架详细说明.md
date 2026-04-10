# NetYamlForge 框架详细说明文档

> **版本**: 1.0  
> **创建日期**: 2026-04-09  
> **基于**: .NET 10.0 ASP.NET Core MVC  
> **定位**: YAML 驱动的 CRUD 应用程序框架

---

## 目录

- [一、框架概述](#一框架概述)
- [二、整体架构](#二整体架构)
- [三、启动流程](#三启动流程)
- [四、中间件管道](#四中间件管道)
- [五、YAML 实体引擎](#五yaml-实体引擎)
- [六、钩子系统](#六钩子系统)
- [七、多租户项目管理](#七多租户项目管理)
- [八、多数据库方言](#八多数据库方言)
- [九、SQL 安全保障](#九sql-安全保障)
- [十、JSON Schema 验证](#十json-schema-验证)
- [十一、热重载机制](#十一热重载机制)
- [十二、认证与授权](#十二认证与授权)
- [十三、数据库初始化](#十三数据库初始化)
- [十四、SignalR 实时通信](#十四signalr-实时通信)
- [十五、AI 集成架构](#十五ai-集成架构)
- [十六、CLI 脚手架工具](#十六cli-脚手架工具)
- [十七、批处理作业系统](#十七批处理作业系统)
- [十八、控制器层](#十八控制器层)
- [十九、模型层](#十九模型层)
- [二十、前端与视图](#二十前端与视图)
- [二十一、自定义分析器](#二十一自定义分析器)
- [二十二、测试体系](#二十二测试体系)
- [二十三、配置参考](#二十三配置参考)
- [二十四、扩展指南](#二十四扩展指南)

---

## 一、框架概述

### 1.1 核心理念

NetYamlForge 是一个**从零开始设计**的 YAML 驱动应用程序框架。与传统 ORM 不同，它**不使用任何 ORM**，而是通过：

1. **YAML 声明式定义**实体、页面、仪表板
2. **动态 SQL 生成**（基于 YAML 元数据 + 数据库方言）
3. **钩子钩入机制**（IEntityHook，支持动态编译）
4. **多租户项目隔离**（`projects/<name>/` 目录结构）

实现完整的 CRUD 功能，同时保持极致的灵活性和可控性。

### 1.2 设计原则

| 原则 | 说明 |
|------|------|
| **YAML First** | 所有配置通过 YAML 声明，无需写代码即可完成基本 CRUD |
| **No ORM** | 不使用 Entity Framework 等 ORM，直接使用 Dapper + 手写 SQL 生成 |
| **SQL Safety** | 通过 `SqlSafetyGuard` + Roslyn 分析器双重保障 SQL 安全 |
| **多租户隔离** | 每个项目有独立的数据库/实体定义/钩子代码/页面配置 |
| **热重载** | 开发模式下 YAML 修改自动重载，无需重启应用 |
| **钩子扩展** | 内置 20+ 钩子 + 支持 Roslyn 动态编译项目级 C# 钩子 |
| **AI Ready** | 内置 AI CLI 集成、意图识别、槽位填充、人工接管等能力 |

### 1.3 技术栈

| 类别 | 技术/库 | 版本 |
|------|---------|------|
| **运行时** | .NET SDK | 10.0 |
| **Web 框架** | ASP.NET Core MVC | 内置 |
| **ORM** | Dapper | 2.1.66 |
| **YAML** | YamlDotNet | 16.3.0 |
| **JSON Schema** | JsonSchema.Net | 8.0.0 |
| **日志** | Serilog + Serilog.AspNetCore | 9.0.0 |
| **PDF** | PDFsharp | 7.0.0-preview-1 |
| **实时通信** | SignalR | 内置 |
| **动态编译** | Microsoft.CodeAnalysis (Roslyn) | 内置 |
| **测试** | xUnit + Moq | 最新 |
| **国际化** | Microsoft.AspNetCore.Localization | 内置 |
| **AI CLI** | 7 种 CLI 工具适配器 | 自定义 |

---

## 二、整体架构

### 2.1 分层架构

```
┌──────────────────────────────────────────────────────────────────┐
│                        表现层 (Presentation)                       │
│  ┌────────────┐  ┌────────────┐  ┌──────────┐  ┌─────────────┐  │
│  │ Razor Views│  │  REST API  │  │ SignalR  │  │  PDF Report │  │
│  │ (wwwroot/) │  │ Controllers│  │   Hubs   │  │   Service   │  │
│  └────────────┘  └────────────┘  └──────────┘  └─────────────┘  │
└──────────────────────────────┬───────────────────────────────────┘
                               │
┌──────────────────────────────▼───────────────────────────────────┐
│                        业务层 (Business)                           │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────────────┐  │
│  │ DynamicEntity│  │ HookExecution│  │ PageDataQuery /       │  │
│  │   Services   │  │   Service    │  │ RowMutationService    │  │
│  └──────┬───────┘  └──────┬───────┘  └──────────┬────────────┘  │
│         │                 │                      │                │
│  ┌──────▼───────┐  ┌──────▼───────┐  ┌───────────▼───────────┐  │
│  │  SqlDialect  │  │  HookRegistry│  │   IDbConnection       │  │
│  │  (4方言)     │  │  + Project   │  │   (Dapper Factory)    │  │
│  └──────────────┘  └──────────────┘  └───────────────────────┘  │
└──────────────────────────────┬───────────────────────────────────┘
                               │
┌──────────────────────────────▼───────────────────────────────────┐
│                        数据层 (Data)                               │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────────────┐  │
│  │  SQLite      │  │  PostgreSQL  │  │  MySQL / SQL Server   │  │
│  │  (默认)      │  │              │  │                       │  │
│  └──────────────┘  └──────────────┘  └───────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────┐
│                        横切关注点                                  │
│  ┌────────────┐  ┌────────────┐  ┌──────────┐  ┌─────────────┐  │
│  │ ProjectMgr │  │  HotReload │  │  Schema  │  │ SQL Safety  │  │
│  │ (多租户)   │  │  (监视器)  │  │ Validator│  │  Guard      │  │
│  └────────────┘  └────────────┘  └──────────┘  └─────────────┘  │
│  ┌────────────┐  ┌────────────┐  ┌──────────┐  ┌─────────────┐  │
│  │ AI Services│  │  BatchJob  │  │  AuthZ   │  │  Analyzer   │  │
│  │ (CLI+API)  │  │  Scheduler │  │  (RBAC)  │  │ (Roslyn)    │  │
│  └────────────┘  └────────────┘  └──────────┘  └─────────────┘  │
└──────────────────────────────────────────────────────────────────┘
```

### 2.2 项目结构

```
NetYamlForge/
├── NetYamlForge/                         # 主应用程序
│   ├── Controllers/                      # MVC 控制器 (12 个)
│   │   ├── Api/                          # API 控制器
│   │   ├── DynamicEntityController.cs    # 动态实体 CRUD
│   │   ├── DashboardController.cs        # 仪表板
│   │   ├── PageController.cs             # 自定义页面
│   │   └── AIController.cs               # AI CLI 集成
│   ├── Services/                         # 业务服务 (80+ 文件)
│   │   ├── AI/                           # AI 服务 (42 文件)
│   │   ├── BatchJob/                     # 批处理作业
│   │   ├── Dialect/                      # 多数据库方言 (4 实现)
│   │   ├── DynamicEntity/                # 实体 CRUD (12 文件)
│   │   ├── Hooks/                        # 钩子系统
│   │   ├── HotReload/                    # 热重载
│   │   └── Project/                      # 多租户管理
│   ├── Models/                           # 数据模型 (30+ 文件)
│   │   ├── AI/                           # AI DTO (19 文件)
│   │   └── Auth/                         # 认证模型 (6 文件)
│   ├── Views/                            # Razor 视图
│   ├── Hubs/                             # SignalR Hub (4 个)
│   ├── Middleware/                       # 自定义中间件
│   ├── Data/                             # 数据库初始化
│   ├── Schemas/                          # JSON Schema (4 个)
│   ├── Extensions/                       # 服务注册扩展
│   ├── skills/                           # AI Prompt 模板
│   │   ├── auto-dealer/                  # 汽车销售 Prompt
│   │   └── jpiere/                       # 会计业务 Prompt
│   ├── projects/                         # 多租户项目配置
│   │   └── <name>/
│   │       ├── project.yaml              # 项目主配置
│   │       ├── entities/*.yml            # 实体定义
│   │       ├── pages/*.yaml              # 页面配置
│   │       ├── config/                   # 布局/日历配置
│   │       ├── hooks/                    # C# 钩子代码
│   │       ├── batch-jobs/               # 批处理作业
│   │       └── database/                 # 数据库文件/种子
│   └── wwwroot/                          # 静态资源
│
├── NetYamlForge.Tests/                   # xUnit 测试
├── NetYamlForge.Analyzers/               # Roslyn 分析器
├── docs/                                 # 技术文档
├── docker/                               # Docker 配置
└── scripts/                              # 辅助脚本
```

---

## 三、启动流程

### 3.1 三阶段启动

```
阶段 1: CLI 命令处理 (同步阻塞)
├── --init-project        → 创建新项目目录结构
├── --scaffold-entities   → 从数据库反向生成实体 YAML
├── --scaffold-hook       → 生成钩子代码模板
├── --upgrade-entity-yaml → 升级实体 YAML 到最新格式
├── --scaffold-batch-job  → 生成批处理作业模板
└── 任何 CLI 命令执行后 → Environment.Exit() 退出

阶段 2: WebHost 构建
├── Serilog 日志初始化
├── 本地化注册 (en-US / zh-CN / ja-JP / ko-KR)
├── Cookie 认证 + 授权策略
├── AddNetYamlForge()     → 核心服务注册（6 个分组）
├── AI CLI 服务注册
├── SignalR Hub 注册
└── AI 相关服务注册

阶段 3: 中间件管道 + 数据库初始化
├── SystemDatabaseInitializer.InitializeAsync()   → 系统数据库
├── DbInitializer.InitializeAsync()               → 项目数据库
├── SystemDatabaseInitializer.SyncProjectsAsync()  → 项目元数据同步
└── 中间件管道构建
```

### 3.2 服务注册分组

`AddNetYamlForge()` 是一个**门面方法**，内部调用 6 个分组注册：

| 分组方法 | 注册内容 | 生命周期 |
|---------|---------|---------|
| `AddMultiProjectInfrastructure()` | ProjectManager (Singleton), ProjectScope (Scoped), ProjectAwareEntityMetadataProvider | Singleton + Scoped |
| `AddDatabaseServices()` | IDbConnection (工厂), ISqlDialect (工厂) | Scoped |
| `AddDynamicCrudCore()` | 所有 CRUD/验证/页面/批处理服务 | Scoped + Singleton |
| `AddProjectHooks()` | 项目钩子注册表/加载器 | Singleton |
| `AddEntityHooks()` | 20+ 内置 IEntityHook 实现 | Singleton |
| `AddYamlHotReload()` | YAML 文件监视器 + 缓存管理器 | Singleton + IHostedService |

### 3.3 关键服务注册示例

```csharp
// 多项目基础设施
services.AddSingleton<ProjectManager>();
services.AddScoped<ProjectScope>();

// 数据库连接工厂
services.AddScoped<IDbConnection>(sp => {
    var scope = sp.GetRequiredService<ProjectScope>();
    var conn = CreateConnection(scope.Current);
    conn.Open();
    return conn;
});

// SQL 方言工厂
services.AddScoped<ISqlDialect>(sp => {
    var scope = sp.GetRequiredService<ProjectScope>();
    return scope.Current.DatabaseType.ToLowerInvariant() switch {
        "sqlserver"  => new SqlServerDialect(),
        "postgresql" => new PostgreSqlDialect(),
        "mysql"      => new MySqlDialect(),
        _            => new SqliteDialect()
    };
});

// 钩子执行服务
services.AddScoped<HookExecutionService>();
services.AddSingleton<IEntityHookRegistry, EntityHookRegistry>();
```

---

## 四、中间件管道

### 4.1 中间件执行顺序

```
RequestTraceMiddleware (自定义请求跟踪)
    ↓
SerilogRequestLogging (HTTP 请求日志)
    ↓
RequestLocalization (I18N 本地化)
    ↓
StaticFiles (静态文件服务)
    ↓
Routing (端点路由)
    ↓
ProjectMiddleware (多租户项目解析) ← 关键：UseRouting 后, UseAuth 前
    ↓
Authentication (Cookie 认证)
    ↓
Authorization (授权策略)
    ↓
ProjectScopeMiddleware (项目范围验证)
    ↓
Endpoint (控制器/Hub)
```

### 4.2 ProjectMiddleware（核心中间件）

**职责**: 从请求 URL 中解析项目名称，设置 `ProjectScope`。

**解析逻辑**:
1. 从路由 `{project}` 段提取项目名（如 `/auto-dealer-demo/DynamicEntity/Index`）
2. 或从 `ReturnUrl` 查询参数提取
3. 调用 `ProjectManager.TryGet(name)` 获取项目信息
4. 设置 `ProjectScope.Current = projectInfo`
5. 设置 `LocalizationProjectContext.CurrentProjectName` 用于 I18N

**重要性**: 所有后续服务都依赖 `ProjectScope.Current` 来确定当前操作的项目上下文。

### 4.3 自定义中间件列表

| 中间件 | 文件 | 职责 |
|--------|------|------|
| `ProjectMiddleware` | `Middleware/ProjectMiddleware.cs` | 从 URL 解析项目并设置上下文 |
| `RequestTraceMiddleware` | `Middleware/` | 请求跟踪（用于调试/日志） |

---

## 五、YAML 实体引擎

### 5.1 实体 YAML 示例

```yaml
# projects/auto-dealer-demo/entities/vehicles.yml
entities:
  vehicles:
    table: vehicles
    key: vehicle_id
    displayName: 車両在庫
    isPublic: true

    columns:
      - name: vehicle_id
        type: string
        length: 20
        isPrimaryKey: true
        displayName: 車両 ID

      - name: maker
        type: string
        length: 50
        displayName: メーカー

      - name: model
        type: string
        length: 50
        displayName: モデル

      - name: price
        type: decimal
        precision: 12
        scale: 2
        displayName: 価格（税込）

      - name: fuel_type
        type: string
        length: 20
        displayName: 燃料タイプ
        options:
          - value: gasoline
            label: ガソリン
          - value: hybrid
            label: ハイブリッド
          - value: ev
            label: 電気

      - name: status
        type: string
        length: 20
        displayName: 状態
        options:
          - value: available
            label: 販売中
          - value: reserved
            label: 商談中
          - value: sold
            label: 売約済

    forms:
      fields:
        - maker
        - model
        - price
        - fuel_type
        - status

    hooks:
      beforeCreate:
        - now:created_at
      beforeUpdate:
        - now:updated_at

    paging:
      mode: numbered
      pageSize: 20
      enableCount: true
```

### 5.2 实体元数据模型

```csharp
public class EntityMetadata
{
    public string Table { get; set; }          // 数据库表名
    public string DisplayName { get; set; }    // 显示名称
    public string PrimaryKey { get; set; }     // 单主键
    public string[] CompositeKeys { get; set; }// 复合主键
    public Dictionary<string, ColumnDefinition> Columns { get; set; }
    public List<JoinDefinition> Joins { get; set; }
    public List<string> BeforeCreateHooks { get; set; }
    public List<string> BeforeUpdateHooks { get; set; }
    public List<string> AfterCreateHooks { get; set; }
    public List<string> AfterUpdateHooks { get; set; }
    public PagingConfig Paging { get; set; }
    public bool IsPublic { get; set; }
    public bool SoftDelete { get; set; }
}

public class ColumnDefinition
{
    public string Name { get; set; }
    public string Type { get; set; }           // string/int/long/decimal/datetime/text/email/...
    public int? Length { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsNullable { get; set; }
    public bool IsReadOnly { get; set; }
    public List<OptionDefinition> Options { get; set; }  // 枚举选项
    public string DisplayName { get; set; }
    public string ForeignKey { get; set; }     // 外键引用
}
```

### 5.3 数据流转

```
YAML 文件 (entities/*.yml)
    ↓
[1] YamlSchemaValidator.ValidateEntityYaml()
    ↓
[2] EntityMetadataProvider 解析 + 缓存
    ├── 读取 YAML 文件
    ├── 反序列化为 EntityMetadata
    ├── 验证必需字段
    └── 缓存到内存
    ↓
[3] IEntityMetadataProvider.Get(entityName) → EntityMetadata
    ↓
[4] DynamicCrudRepository 使用元数据生成 SQL
    ├── BuildSelectClause(columns)
    ├── BuildFromClause(table + JOINs)
    ├── BuildWhereClause(filters)
    ├── BuildOrderByClause(sort)
    └── 所有标识符经过 SqlSafetyGuard 验证
    ↓
[5] Dapper.QueryAsync() / ExecuteAsync()
    ↓
[6] HookExecutionService 执行 Before/After 钩子
    ↓
[7] HTTP 响应 → Razor 视图 / JSON API
```

### 5.4 动态 CRUD 服务链

| 服务 | 职责 |
|------|------|
| `DynamicCrudRepository` | 核心 SQL 生成 + CRUD 执行 (828 行) |
| `DynamicEntityCommandService` | CRUD 命令编排 (Insert/Update/Delete) |
| `DynamicEntityListQueryService` | 列表查询构建（分页/排序/过滤） |
| `DynamicEntityListResponseService` | 响应格式化 |
| `DynamicEntityFormValidationService` | 表单验证 |
| `DynamicEntityFormViewModelFactory` | 表单视图模型构建 |
| `DynamicEntityForeignKeyDataService` | 外键关联数据获取 |
| `DynamicEntityNavigationService` | 实体间导航链接 |
| `DynamicEntityKeyResolverService` | 主键解析 (单键/复合键) |
| `DynamicEntityConfigDiffService` | 配置差异分析 |
| `DynamicEntityConfigDiagnosticsService` | 配置诊断 |
| `DynamicEntityListHttpResponseService` | HTTP 响应映射 |

---

## 六、钩子系统

### 6.1 核心接口

```csharp
public interface IEntityHook
{
    /// <summary>
    /// 钩子名称（在 YAML 中引用）
    /// </summary>
    string Name { get; }

    /// <summary>
    /// DB 写入前执行。返回 HookResult.Abort() 可取消操作。
    /// </summary>
    Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx);

    /// <summary>
    /// DB 写入后执行（同一事务内）。抛出异常则回滚。
    /// </summary>
    Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx);
}
```

### 6.2 钩子注册表架构

```
IEntityHookRegistry (框架级)
└── EntityHookRegistry
    └── Dictionary<string, IEntityHook>   // 20+ 内置钩子

IProjectHookRegistry (项目级)
└── ProjectHookRegistry
    └── Dictionary<string, Dictionary<string, IEntityHook>>
        // projectName → (hookName → IEntityHook)

HookExecutionService (执行引擎)
├── ResolveWithSource()  → 两级查找: 项目钩 → 框架钩
├── RunBeforeAsync()     → 顺序执行, 遇到 Abort 即停
└── RunAfterAsync()      → 顺序执行, 异常即回滚
```

### 6.3 内置钩子列表（20+ 个）

| 分类 | 钩子名 | 配置参数 | 说明 |
|------|--------|---------|------|
| **验证** | `validate_email` | 列名列表 | 邮箱格式验证 |
| | `validate_phone` | 列名列表 | 电话号码验证 |
| | `validate_url` | 列名列表 | URL 格式验证 |
| | `validate_regex` | 列名:正则表达式 | 正则表达式验证 |
| | `validate_range` | 列名:最小:最大 | 数值范围验证 |
| | `validate_unique` | 列名列表 | 唯一性验证 |
| | `validate_required` | 列名列表 | 非空验证 |
| **数据转换** | `trim` | 列名列表 | 去除首尾空格 |
| | `uppercase` | 列名列表 | 转大写 |
| | `lowercase` | 列名列表 | 转小写 |
| | `titlecase` | 列名列表 | 标题格式 |
| | `default` | 列名:默认值 | 设置默认值 |
| | `now` | 列名 | 设置当前时间 |
| | `current_user` | 列名 | 设置当前用户 |
| **审计/通知** | `audit_log` | - | 审计日志记录 |
| | `webhook` | URL | 发送 Webhook |
| **关联操作** | `update_count` | 表:列:条件 | 更新计数 |
| | `update_related` | 配置 | 更新关联数据 |
| **软删除** | `soft_delete` | - | 软删除标记 |
| **示例** | `customer_email_domain` | - | 邮箱域名检查 |
| | `customer_name_normalize` | - | 姓名规范化 |
| | `console_log_after` | 消息 | 操作后控制台日志 |

### 6.4 钩子配置语法

```yaml
entities:
  customers:
    hooks:
      beforeCreate:
        - validate_email:Email,Phone        # 冒号后为配置参数
        - trim:FirstName,LastName
        - now:CreatedAt
        - current_user:CreatedBy
      beforeUpdate:
        - now:UpdatedAt
        - current_user:UpdatedBy
      afterCreate:
        - webhook:https://example.com/hook  # Webhook 通知
```

配置参数通过 `EntityHookContext.Data["__hookConfig"]` 传递给钩子。

### 6.5 动态钩子加载（Roslyn 编译）

```
projects/<name>/hooks/*.cs
    ↓
ProjectHookLoader.CompileHooksAsync()
    ├── 收集所有 .cs 文件
    ├── 添加引用 (mscorlib, Dapper, ILogger, IOptions 等)
    ├── Roslyn CSharpCompilation.Create()
    ├── 编译为 MemoryStream
    └── Assembly.Load(ms.ToArray())
    ↓
反射查找 IEntityHook 实现类
    ↓
ActivatorUtilities.CreateInstance() → DI 解析依赖
    ↓
ProjectHookRegistry.Register(projectName, hookName, hookInstance)
```

**安全注意**: 动态编译仅在应用启动时执行一次，不在热重载时重新编译。

---

## 七、多租户项目管理

### 7.1 核心类关系

```
ProjectManager (Singleton)
├── 构造函数: 扫描 projects/*/project.yaml
├── Dictionary<string, ProjectInfo>  // 项目名 → 项目信息
├── TryGet(name) → ProjectInfo?
└── GetAll() → IReadOnlyDictionary<string, ProjectInfo>

ProjectInfo (数据模型)
├── Name, DisplayName, Description, Version
├── ProjectDir (绝对路径)
├── DatabaseType (sqlite/postgresql/mysql/sqlserver)
├── ConnectionString
├── IEntityMetadataProvider       // 实体元数据提供者
├── IDashboardConfigProvider      // 仪表板配置
├── IPageMetadataProvider         // 页面元数据
├── ProjectLayoutConfig           // 布局配置
├── CalendarConfig                // 日历配置
└── AiConfig                      // AI 配置

ProjectScope (Scoped)
├── 由 ProjectMiddleware 设置
├── Current: ProjectInfo
└── IsSet: bool
```

### 7.2 项目加载流程

```
启动时:
  ProjectManager 构造函数
    ↓
  扫描 projects/*/project.yaml
    ↓
  对每个项目:
    ├── YamlSchemaValidator.ValidateProjectYaml()
    ├── 反序列化 ProjectConfig
    ├── 加载 config/layout.yml (如果存在)
    ├── 环境变量覆盖 (NYFORGE_{NAME}_DB_TYPE)
    ├── 构建连接字符串
    ├── 创建 EntityMetadataProvider
    ├── 创建 DashboardConfigProvider
    ├── 创建 PageMetadataProvider
    └── EntityDbSchemaConsistencyValidator.ValidateOrThrow()
    ↓
  加载项目钩子: ProjectHookLoader.LoadProjectHooksAsync()
  加载项目业务逻辑: LoadProjectBusinessLogicAsync()
  加载项目动作处理器: LoadProjectActionHandlersAsync()
```

### 7.3 项目配置示例

```yaml
# projects/auto-dealer-demo/project.yaml
name: auto-dealer-demo
displayName: "自動車ディーラー AI 窓口システム"
description: "AI 技術を活用した 24 時間 365 日のスマートカスタマーサポート"
version: "1.1.0"

database:
  type: sqlite
  path: database/auto-dealer-demo.db

features:
  multiLanguage: true
  userAuthentication: true
  dashboard: true
  pages: true
  api: true

roles:
  - customer
  - operator
  - sales_rep
  - sales_manager
  - service_staff
  - ai_admin
  - executive

landingPageByRole:
  customer: CustomerDashboard
  operator: OperatorConsole
  sales_rep: SalesRepDashboard
  sales_manager: LeadKanban
  service_staff: Appointments
  ai_admin: AIDashboard
  executive: ExecDashboard

navigation:
  groups:
    - name: 顧客
      role: customer
      pages: [MyPage, Appointments, PublicVehicles]
    - name: AI 窓口
      role: ai_admin
      pages: [AIDashboard, OperatorConsole, AIReports]
    - name: 販売管理
      role: sales_manager
      pages: [LeadKanban, SalesLeads, SalesRepDashboard, VehicleInventory]

settings:
  culture: ja-JP
  timeZone: Asia/Tokyo
```

### 7.4 项目目录结构

```
projects/auto-dealer-demo/
├── project.yaml                  # 项目主配置
├── entities/                     # 实体定义 (14 个)
│   ├── vehicles.yml
│   ├── customers.yml
│   ├── sales_leads.yml
│   ├── employees.yml
│   ├── service_appointments.yml
│   ├── service_requests.yml
│   ├── ai_conversations.yml
│   ├── ai_messages.yml
│   ├── ai_knowledge.yml
│   ├── ai_handovers.yml
│   ├── ai_feedback.yml
│   ├── lead_activities.yml
│   ├── lead_nurturing_tasks.yml
│   └── third_party_users.yml
├── pages/                        # 自定义页面 (15+)
│   ├── CustomerDashboard.yaml
│   ├── OperatorConsole.yaml
│   ├── SalesRepDashboard.yaml
│   ├── LeadKanban.yaml
│   ├── VehicleInventory.yaml
│   └── ...
├── config/                       # 布局/日历配置
│   ├── layout.yml
│   └── calendar.yml
├── hooks/                        # C# 钩子代码
│   └── sales_leads/
│       ├── SetLeadTimestampsHook.cs
│       ├── CalculateLeadScoreHook.cs
│       └── UpdateLeadUpdatedAtHook.cs
├── batch-jobs/                   # 批处理作业
├── database/                     # 数据库/种子数据
│   ├── auto-dealer-demo.db
│   └── init.sql
└── ai-config.yaml                # AI 专用配置
```

---

## 八、多数据库方言

### 8.1 接口定义

```csharp
public interface ISqlDialect
{
    /// <summary>
    /// 编号分页 (ROW_NUMBER / LIMIT OFFSET)
    /// </summary>
    void AppendNumberedPagination(StringBuilder sql, string orderByClause,
        int pageNumber, int pageSize);

    /// <summary>
    /// 键集分页 (WHERE id > @cursor)
    /// </summary>
    void AppendKeysetPagination(StringBuilder sql, string orderByColumn,
        object cursorValue, string direction);

    /// <summary>
    /// 字符串连接运算符
    /// </summary>
    string ConcatOperator { get; }
}
```

### 8.2 四种方言实现

| 方言类 | 数据库 | 编号分页策略 | 字符串连接 | 特色 |
|--------|--------|------------|-----------|------|
| `SqliteDialect` | SQLite 3 | `LIMIT @offset, @pageSize` | `\|\|` | 默认数据库 |
| `SqlServerDialect` | SQL Server | `ROW_NUMBER() OVER(ORDER BY ...)` | `+` | 企业级 |
| `PostgreSqlDialect` | PostgreSQL | `LIMIT @pageSize OFFSET @offset` | `\|\|` | 开源推荐 |
| `MySqlDialect` | MySQL/MariaDB | `LIMIT @offset, @pageSize` | `CONCAT()` | 广泛使用 |

### 8.3 方言解析工厂

在 `ServiceCollectionExtensions.AddDatabaseServices()` 中注册：

```csharp
services.AddScoped<ISqlDialect>(sp => {
    var scope = sp.GetRequiredService<ProjectScope>();
    var dbType = scope.Current.DatabaseType.ToLowerInvariant();
    return dbType switch {
        "sqlserver"   => new SqlServerDialect(),
        "postgresql"  => new PostgreSqlDialect(),
        "mysql"       => new MySqlDialect(),
        _             => new SqliteDialect()
    };
});
```

### 8.4 方言在 SQL 生成中的使用

```csharp
// DynamicCrudRepository.cs 示例
var sql = new StringBuilder();
sql.Append($"SELECT {selectColumns} FROM {tableName}");
sql.Append($" WHERE {whereClause}");
sql.Append($" ORDER BY {orderByClause}");

// 使用方言生成分页 SQL
_dialect.AppendNumberedPagination(sql, orderByClause, pageNumber, pageSize);

// 使用方言的字符串连接符
sql.Append($" SELECT FirstName {_dialect.ConcatOperator} ' ' {_dialect.ConcatOperator} LastName AS FullName");
```

---

## 九、SQL 安全保障

### 9.1 SqlSafetyGuard

**文件**: `Services/SqlSafetyGuard.cs`

```csharp
public static class SqlSafetyGuard
{
    // 标识符正则: 仅允许 [A-Za-z_][A-Za-z0-9_]*
    public static readonly Regex IdentifierRegex =
        new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    // 表达式正则: 仅允许安全的字符子集
    public static readonly Regex ExpressionRegex =
        new(@"^[A-Za-z0-9_.\s,()+*/%<>=!'|-]+$", RegexOptions.Compiled);

    /// <summary>
    /// 验证 SQL 标识符（表名、列名）
    /// </summary>
    public static void EnsureIdentifier(string? value, string context)
    {
        if (string.IsNullOrWhiteSpace(value) || !IdentifierRegex.IsMatch(value))
            throw new InvalidOperationException($"Invalid identifier in {context}: {value}");
        if (IsUnsafeToken(value))
            throw new InvalidOperationException($"Unsafe token in {context}: {value}");
    }

    /// <summary>
    /// 验证 SQL 表达式
    /// </summary>
    public static void EnsureExpression(string? value, string context)
    {
        if (string.IsNullOrWhiteSpace(value) || !ExpressionRegex.IsMatch(value))
            throw new InvalidOperationException($"Unsafe expression in {context}: {value}");
    }

    /// <summary>
    /// 检测危险标记
    /// </summary>
    public static bool IsUnsafeToken(string? value) =>
        value is not null && (
            value.Contains(";") ||
            value.Contains("--") ||
            value.Contains("/*") ||
            value.Contains("*/")
        );
}
```

### 9.2 安全使用模式

```csharp
// ✅ 正确：标识符经过验证
SqlSafetyGuard.EnsureIdentifier(tableName, "entity.table");
SqlSafetyGuard.EnsureIdentifier(columnName, "order-by column");
sql.Append($"SELECT {columns} FROM {tableName}");

// ✅ 正确：值使用参数化
param.Add("@Id", id);
sql.Append(" WHERE Id = @Id");

// ❌ 错误：直接拼接（会被 DCS001 分析器捕获）
var sql = $"SELECT * FROM {userInput}";  // DCS001 Error!

// ❌ 错误：直接实例化连接（会被 DCS003 分析器捕获）
using var conn = new SqliteConnection(connectionString);  // DCS003 Error!
```

### 9.3 四层安全保障

| 层级 | 机制 | 检测时机 | 效果 |
|------|------|---------|------|
| **1. 标识符验证** | `SqlSafetyGuard.EnsureIdentifier()` | 运行时 | 抛异常阻止 |
| **2. 表达式验证** | `SqlSafetyGuard.EnsureExpression()` | 运行时 | 抛异常阻止 |
| **3. 参数化查询** | Dapper 参数绑定 | 运行时 | 防止值注入 |
| **4. Roslyn 分析器** | DCS001-DCS004 | 编译时 | 构建失败 |

---

## 十、JSON Schema 验证

### 10.1 Schema 文件列表

**目录**: `Schemas/`

| Schema 文件 | 验证对象 | 关键约束 |
|------------|---------|---------|
| `project-schema.json` | `project.yaml` | name, displayName, database.type, features |
| `entity-schema.json` | `entities/*.yml` | entities 非空, table 必填, key/keys 二选一 |
| `ui-page-schema.json` | `pages/*.yaml` | 页面组件结构 |
| `dashboard-schema.json` | `dashboard.yml` | 统计卡片 + 图表配置 |

### 10.2 验证流程

```
YAML 文件
    ↓
[1] YamlDotNet 反序列化为 object 图
    ↓
[2] ConvertYamlValue() 类型转换 (string → bool/int/decimal)
    ↓
[3] JsonSerializer.Serialize() 转为 JSON
    ↓
[4] JsonSchema.Net.Evaluate() 验证
    ↓
[5] 无效时抛出 InvalidOperationException (含详细错误信息)
```

### 10.3 Entity Schema 关键约束

```json
{
  "required": ["table"],
  "properties": {
    "table": { "type": "string", "minLength": 1 },
    "key": { "type": "string" },
    "keys": { "type": "array", "items": { "type": "string" } },
    "displayName": { "type": "string" },
    "columns": {
      "type": "object",
      "additionalProperties": {
        "required": ["type"],
        "properties": {
          "type": {
            "enum": ["string", "int", "long", "decimal", "datetime", "text", "email", "date", "boolean"]
          }
        }
      }
    }
  },
  "oneOf": [
    { "required": ["key"] },
    { "required": ["keys"] }
  ]
}
```

**约束要点**:
- `entities` 对象至少 1 个属性
- 每个实体必须包含 `table`（与数据库精确匹配）
- `key`（单主键）或 `keys`（复合主键）必须存在其一
- `displayName` / `displayNameKey` / `displayNameI18n` 必须存在其一

### 10.4 Schema 加载机制

Schema 文件作为**嵌入资源**（EmbeddedResource）打包到程序集中，运行时懒加载：

```csharp
var assembly = Assembly.GetExecutingAssembly();
using var stream = assembly.GetManifestResourceStream("NetYamlForge.Schemas.entity-schema.json");
var schemaText = new StreamReader(stream).ReadToEnd();
var schema = JsonSchema.FromText(schemaText);
// 静态缓存，避免重复加载
```

---

## 十一、热重载机制

### 11.1 核心组件

**目录**: `Services/HotReload/`

| 组件 | 职责 |
|------|------|
| `YamlHotReloadService` | IHostedService，生命周期管理 |
| `YamlFileWatcher` | FileSystemWatcher 封装，防抖处理 |
| `ProjectYamlCacheManager` | YAML 缓存失效 + 重载 |
| `HotReloadOptions` | 配置（Enabled/OnlyInDevelopment/DebounceMs） |

### 11.2 工作流程

```
启动时 (StartAsync):
  读取 appsettings.json HotReload 配置
  如果 Enabled && (OnlyInDevelopment == false || IsDevelopment):
    对每个 projects/*/ 目录启动 FileSystemWatcher
    监视 *.yml, *.yaml, *.cs 文件

文件变更时 (OnFileChanged):
  FileSystemWatcher 触发 → 500ms 防抖
    ↓
  解析变更文件路径 → 确定项目名和类型
    ↓
  路由到对应缓存重载:
    ├── /entities/     → ReloadAsync(project_entities, path)
    ├── dashboard.yml  → ReloadAsync(project_dashboard, path)
    ├── /pages/        → ReloadAsync(project_pages, path)
    ├── /config/       → ReloadProjectAsync(project)
    └── project.yaml   → ReloadProjectAsync(project)
```

### 11.3 配置选项

```json
{
  "HotReload": {
    "Enabled": true,
    "OnlyInDevelopment": true,
    "DebounceMs": 500
  }
}
```

| 选项 | 说明 | 默认值 |
|------|------|--------|
| `Enabled` | 是否启用热重载 | `true` |
| `OnlyInDevelopment` | 仅在 Development 环境下启用 | `true` |
| `DebounceMs` | 防抖延迟（毫秒），避免频繁重载 | `500` |

### 11.4 缓存失效策略

```
┌─────────────────────┐
│  ProjectYamlCacheManager │
├─────────────────────┤
│  Entities Cache    │ → 变更时移除指定实体
│  Dashboard Cache   │ → 变更时移除仪表板配置
│  Pages Cache       │ → 变更时移除页面定义
│  Layout Cache      │ → 变更时移除布局配置
│  Calendar Cache    │ → 变更时移除日历配置
└─────────────────────┘
```

---

## 十二、认证与授权

### 12.1 认证配置

```csharp
// Program.cs
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
```

### 12.2 系统数据库表

**系统数据库**: `system.db`（项目根目录）

| 表名 | 说明 |
|------|------|
| `app_user` | 多租户用户表（含 `user_type`, `default_project_name`） |
| `app_user_project_role` | 用户-项目-角色关联（UNIQUE(user_id, project_name)） |
| `projects` | 项目元数据 |
| `AIChatHistory` | AI 聊天历史（含 `ChatContext` 多上下文区分） |
| `AICommandLog` | AI 命令日志 |

### 12.3 默认管理员

首次启动时自动创建：

| 用户名 | 密码 | 角色 |
|--------|------|------|
| `admin` | `Admin123!` | Admin |

**安全建议**: 生产环境务必修改默认密码。

### 12.4 授权策略

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireRole(UserRoles.Admin))
    .AddPolicy("ProjectAccess", policy =>
        policy.RequireAssertion(ctx => HasProjectAccess(ctx)));
```

---

## 十三、数据库初始化

### 13.1 三层初始化架构

```
启动时
    │
    ├── [1] SystemDatabaseInitializer.InitializeAsync()
    │     └── 初始化 system.db（用户/角色/项目元数据）
    │
    ├── [2] DbInitializer.InitializeAsync()
    │     └── 遍历所有项目，初始化各项目数据库
    │
    └── [3] SystemDatabaseInitializer.SyncProjectsAsync()
          └── 同步项目元数据到 system.db
```

### 13.2 系统级初始化

`SystemDatabaseInitializer` 创建：

1. **app_user** 表 — 多租户用户表
2. **app_user_project_role** 表 — 用户-项目-角色关联
3. **projects** 表 — 项目元数据
4. **AIChatHistory** 表 — AI 聊天历史
5. **AICommandLog** 表 — AI 命令日志
6. 默认管理员：`admin / Admin123!`
7. WAL 模式启用（提高并发）

### 13.3 项目级初始化协调器

`DbInitializer` 根据数据库类型分发：

| 数据库类型 | 初始化器 |
|-----------|---------|
| **SQL Server** | `SqlServerAuthSchemaInitializer` + `DefaultAdminSeeder` |
| **PostgreSQL** | `PostgreSqlAuthSchemaInitializer` + `DefaultAdminSeeder` |
| **MySQL** | `MySqlAuthSchemaInitializer` + `DefaultAdminSeeder` |
| **SQLite** (默认) | `SqliteAuthSchemaInitializer` + `DefaultAdminSeeder` + `RbacSeeder` |

### 13.4 项目特定初始化

`ProjectSpecificInitializer` 按项目名分发：

| 项目 | 初始化内容 |
|------|-----------|
| `auto-dealer-demo` | 运行 init.sql + 种子用户 + 全面测试用户 |
| `contact-manager` | 运行 init.sql |
| `todo-app` | 运行 init_seed.sql |
| `task-management` | 运行 init_seed.sql + 添加列 + 创建表 |
| `attendance-ops` | 添加 ApprovedAt 列 |
| `framework` / `biz-docs` / `inventory` / `ui-showcase` | 项目特定测试用户 |

---

## 十四、SignalR 实时通信

### 14.1 Hub 列表

| Hub | 路径 | 职责 |
|-----|------|------|
| `AIChatHub` | `/aiChatHub` | AI 聊天实时通信 |
| `AIProgressHub` | `/aiProgressHub` | AI 任务进度推送 |
| `AIDebateHub` | `/aiDebateHub` | AI 辩论实时通信 |
| `NaturalLanguageQueryHub` | `/nlQueryHub` | 自然语言查询实时执行 |

### 14.2 AIChatHub

**注入的服务**:
- `IConversationManager` — 对话管理
- `IDirectAIProcessor` — 直接 AI 处理
- `IHandoverManager` — 人工接管管理

**核心方法**:

| 方法 | 参数 | 说明 |
|------|------|------|
| `Connect` | `channelId` | 建立连接 |
| `SendMessage` | `conversationId, content` | 消息处理主流程 |
| `QuickReplySelected` | `conversationId, actionValue` | 快速回复选择 |
| `TypingStart` | - | 输入开始指示 |
| `TypingStop` | - | 输入结束指示 |

### 14.3 NaturalLanguageQueryHub

**注入的服务**:
- `QueryParserService` — AI 自然语言解析
- `QueryExecutionService` — 查询执行
- `QueryResultFormatter` — 结果格式化（Markdown）

**核心方法**:

| 方法 | 参数 | 说明 |
|------|------|------|
| `Connect` | `project` | 连接 |
| `SendQuery` | `query, project, entity` | 完整流程：解析 → 执行 → 格式化 → 返回 |
| `CancelQuery` | - | 取消查询（TODO） |
| `GetHistory` | `count` | 历史查询（TODO） |

### 14.4 注册方式

```csharp
// Program.cs
app.MapHub<AIChatHub>("/aiChatHub");
app.MapHub<AIProgressHub>("/aiProgressHub");
app.MapHub<AIDebateHub>("/aiDebateHub");
app.MapHub<NaturalLanguageQueryHub>("/nlQueryHub");
```

---

## 十五、AI 集成架构

### 15.1 双模式 AI 调用

```
                    ┌─────────────────┐
                    │  AIController   │
                    └────────┬────────┘
                             │
              ┌──────────────┼──────────────┐
              │              │              │
    ┌─────────▼─────────┐   │   ┌──────────▼──────────┐
    │   CLI 模式         │   │   │   API 直连模式       │
    │   (开发/本地)      │   │   │   (生产/远程)        │
    └─────────┬─────────┘   │   └──────────┬──────────┘
              │              │              │
    ┌─────────▼─────────┐   │   ┌──────────▼──────────┐
    │ CLIServiceFactory │   │   │  ILlmProvider       │
    │                   │   │   │  (OllamaProvider)   │
    │ ├── QwenCode      │   │   └──────────┬──────────┘
    │ ├── Claude        │   │              │
    │ ├── Codex         │   │   ┌──────────▼──────────┐
    │ ├── Gemini        │   │   │ CliFirstLlmProvider │
    │ ├── Ollama        │   │   │ (CLI → API 回退)    │
    │ ├── LM Studio     │   │   └─────────────────────┘
    │ ├── Copilot       │   │
    │ └── Mock          │   │
    └───────────────────┘   │
```

### 15.2 支持的 AI CLI 工具

| 工具 | 类型 | 环境变量 | 路径配置 |
|------|------|---------|---------|
| **Qwen Code** | 云端 | `DASHSCOPE_API_KEY`, `DASHSCOPE_BASE_URL` | `AICli:QwenCode:Path` |
| **Claude Code** | 云端 | `ANTHROPIC_API_KEY` | `AICli:Claude:Path` |
| **OpenAI Codex** | 云端 | `OPENAI_API_KEY`, `OPENAI_BASE_URL` | `AICli:Codex:Path` |
| **Google Gemini** | 云端 | `GOOGLE_API_KEY` | `AICli:Gemini:Path` |
| **Ollama** | 本地 | - | `AICli:Ollama:Path` |
| **LM Studio** | 本地 | - | `AICli:LmStudio:Path` |
| **Copilot** | 云端 | - | `AICli:Copilot:Path` |
| **Mock** | 测试 | - | N/A |

### 15.3 AI 服务组件

**目录**: `Services/AI/`（42 个文件）

| 组件 | 职责 |
|------|------|
| `AutoDealerChatService` | 汽车销售专用聊天服务（1546 行） |
| `BaseChatService` | 聊天服务基类 |
| `ConversationManager` | 会话状态管理 |
| `HybridIntentClassifier` | 混合意图分类器（规则 + LLM） |
| `SlotFillingManager` | 槽位填充管理器 |
| `SentimentAnalyzer` | 情感分析 |
| `HandoverManager` | 人工接管管理 |
| `ChatHistoryService` | 聊天历史持久化 |
| `QueryParserService` | 自然语言查询解析 |
| `QueryExecutionService` | 查询执行 |
| `QueryResultFormatter` | 查询结果格式化 |
| `CustomerDataService` | 客户数据服务 |
| `AppointmentService` | 预约服务 |
| `KnowledgeBaseService` | 知识库服务 |
| `TaskQueueService` | 异步任务队列 |
| `ProgressTracker` | 任务进度跟踪器 |
| `AIDebateService` | AI 辩论系统 |
| `AIReportPdfService` | AI 报告 PDF 生成 |
| `AISettingsService` | AI 设置服务 |

### 15.4 AI 配置示例

```json
{
  "AICli": {
    "DefaultTool": "qwen",
    "TaskTimeoutSeconds": 3600,
    "MaxConcurrentTasks": 2,
    "DefaultWorkingDirectory": "/home/ubuntu/ws/NetYamlForge",
    "DefaultAllowedTools": ["Read", "Write", "Edit", "Bash", "Git"],
    "QwenCode": { "Path": "/path/to/qwen" },
    "Claude": { "Path": "/path/to/claude" }
  },
  "AiWindow": {
    "CliFirst": true,
    "CliTimeoutSeconds": 3600,
    "ChatResponseTimeoutSeconds": 3600,
    "ProviderPriority": ["qwen", "claude", "gemini", "ollama"],
    "DefaultProvider": "qwen",
    "DealerName": "AI 窓口ディーラー",
    "BusinessHours": "月〜土 9:00〜18:00"
  }
}
```

---

## 十六、CLI 脚手架工具

### 16.1 可用命令

| 命令 | 说明 | 示例 |
|------|------|------|
| `--init-project` | 初始化新项目 | `--init-project --project=myapp --display-name="My App" --db-type=sqlite` |
| `--scaffold-entities` | 从数据库生成实体 YAML | `--scaffold-entities --project=myapp [--no-overwrite]` |
| `--scaffold-hook` | 生成钩子代码模板 | `--scaffold-hook --name=ValidateEmail --project=myapp [--with-tests]` |
| `--upgrade-entity-yaml` | 升级实体 YAML 到最新格式 | `--upgrade-entity-yaml --project=myapp` |
| `--scaffold-batch-job` | 生成批处理作业模板 | `--scaffold-batch-job --name=cleanup --project=myapp` |

### 16.2 实体脚手架（Scaffold Entities）

**流程**:
1. 连接项目数据库
2. 读取所有用户表（排除系统表）
3. 对每个表：
   - 读取列元数据（名称、类型、长度、是否可空）
   - 检测主键
   - 生成 YAML 实体定义
4. 写入 `projects/<name>/entities/` 目录

**选项**:
- `--no-overwrite`: 跳过已存在的文件
- `--json`: JSON 输出模式（CI 集成）

### 16.3 钩子脚手架（Scaffold Hook）

**生成的文件**:
```csharp
// projects/<name>/hooks/<entity>/<HookName>Hook.cs
public class <HookName>Hook : IEntityHook
{
    public string Name => "<hook_name>";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 实现钩子逻辑
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 实现钩子逻辑
        return Task.CompletedTask;
    }
}
```

**选项**:
- `--with-tests`: 同时生成 xUnit 测试文件

### 16.4 项目初始化（Init Project）

**创建的目录结构**:
```
projects/<name>/
├── project.yaml          # 项目主配置
├── entities/             # 实体定义目录
├── pages/                # 页面配置目录
├── config/               # 布局/日历配置目录
├── hooks/                # 钩子代码目录
├── batch-jobs/           # 批处理作业目录
└── database/             # 数据库目录
```

---

## 十七、批处理作业系统

### 17.1 架构

```
IBatchJob (接口)
├── JobName (作业名称)
├── ExecuteAsync(context) (执行方法)
└── BatchJobContext (执行上下文)

BatchJobRunner (调度器)
├── 加载 YAML 作业定义
├── 解析调度计划 (cron 表达式)
├── 创建作业实例
├── 执行作业
└── 记录执行结果

BatchJobController (管理控制器)
├── 列出所有作业
├── 手动触发执行
├── 查看执行历史
└── 启用/禁用作业
```

### 17.2 作业定义 YAML

```yaml
# projects/<name>/batch-jobs/cleanup.yaml
name: cleanup
displayName: 数据清理作业
description: 清理 30 天前的过期数据
enabled: true
schedule: "0 2 * * *"  # 每天凌晨 2 点
timeout: 3600          # 超时时间（秒）
params:
  retentionDays: 30
```

---

## 十八、控制器层

### 18.1 控制器列表

| 控制器 | 路由 | 职责 |
|--------|------|------|
| `HomeController` | `/` | 首页、关于页 |
| `DynamicEntityController` | `/{project}/DynamicEntity/*` | 动态实体 CRUD（列表/表单/详情） |
| `DashboardController` | `/{project}/Dashboard/*` | 仪表板统计/图表 |
| `PageController` | `/{project}/Page/*` | 自定义 YAML 页面 |
| `AccountController` | `/Account/*` | 登录/注册/登出 |
| `BatchJobController` | `/{project}/BatchJob/*` | 批处理作业管理 |
| `AIController` | `/{project}/api/AI/*` | AI CLI 集成 API |
| `ApiEntityController` | `/{project}/api/Entity/*` | 实体 REST API |

### 18.2 API 控制器

**目录**: `Controllers/Api/`

| 控制器 | 端点 | 说明 |
|--------|------|------|
| `ApiEntityController` | `GET/POST/PUT/DELETE /{project}/api/Entity/{entity}` | 实体 CRUD API |
| `AIController` | `POST /{project}/api/AI/chat` | AI 聊天请求 |
| | `GET /{project}/api/AI/tasks` | 任务列表 |
| | `GET /{project}/api/AI/history` | 聊天历史 |
| | `GET /{project}/api/AI/health` | 健康检查 |

---

## 十九、模型层

### 19.1 核心模型

| 模型 | 说明 |
|------|------|
| `EntityMetadata` | 实体元数据定义 |
| `ProjectConfig` | 项目配置 |
| `PageDefinition` | 页面定义 |
| `DashboardConfig` | 仪表板配置 |
| `PaginationModel` | 分页模型 |
| `HooksDefinitionBase` | 钩子定义基类 |

### 19.2 AI 模型（19 个 DTO）

| 模型 | 说明 |
|------|------|
| `AIChatRequest` | AI 聊天请求 |
| `AIChatResponse` | AI 聊天响应 |
| `AITask` | AI 任务实体 |
| `ChatMessage` | 聊天消息（支持多上下文） |
| `Conversation` | 对话会话（支持多通道） |
| `IntentResult` | 意图识别结果 |
| `HandoverRequest` | AI 人工接管请求 |
| `NaturalLanguageQueryRequest` | 自然语言查询请求 |
| `NaturalLanguageQueryResponse` | 自然语言查询响应 |
| `DebateSession` | AI 辩论会话 |
| `DebateMessage` | AI 辩论消息 |
| `ProgressUpdate` | 进度更新（流式） |
| `AiSkill` | AI 技能/提示词模板 |
| `CliToolInfo` | CLI 工具信息 |
| `CommandLog` | 命令日志 |
| `TaskStatus` | 任务状态枚举 |

### 19.3 认证模型（6 个）

| 模型 | 说明 |
|------|------|
| `AppUser` | 应用用户实体 |
| `LoginViewModel` | 登录视图模型 |
| `RegisterViewModel` | 注册视图模型 |
| `CustomerRegisterViewModel` | 客户注册视图模型 |
| `UserEditViewModel` | 用户编辑视图模型 |
| `ProjectViewModels` | 项目视图模型 |

---

## 二十、前端与视图

### 20.1 视图结构

```
Views/
├── Shared/
│   ├── _Layout.cshtml         # 主布局
│   ├── _LoginLayout.cshtml    # 登录页布局
│   ├── _ValidationScriptsPartial.cshtml
│   └── Error.cshtml
├── Home/
│   ├── Index.cshtml
│   └── About.cshtml
├── Account/
│   ├── Login.cshtml
│   ├── Register.cshtml
│   └── AccessDenied.cshtml
├── DynamicEntity/
│   ├── Index.cshtml           # 实体列表
│   ├── FormPage.cshtml        # 创建/编辑表单
│   └── DetailPage.cshtml      # 详情页面
├── Dashboard/
│   └── Index.cshtml
├── Page/
│   └── (动态页面)
└── AI/
    └── (AI 窗口页面)
```

### 20.2 静态资源

```
wwwroot/
├── css/
│   ├── site.css               # 全局样式
│   └── (主题 CSS)
├── js/
│   ├── site.js                # 全局脚本
│   ├── dynamic-entity.js      # 实体页面交互
│   ├── ai-window.js           # AI 窗口
│   └── signalr-client.js      # SignalR 客户端
├── lib/
│   ├── bootstrap/             # Bootstrap CSS/JS
│   ├── jquery/                # jQuery
│   ├── signalr/               # SignalR JS 客户端
│   └── (其他第三方库)
└── (图片、图标等)
```

### 20.3 国际化

框架支持 **4 种语言**：

| 语言 | 代码 | 说明 |
|------|------|------|
| 英语 | `en-US` | 默认 |
| 中文 | `zh-CN` | 简体中文 |
| 日语 | `ja-JP` | 日本语 |
| 韩语 | `ko-KR` | 韩语 |

**本地化资源目录**: `Resources/`

---

## 二十一、自定义分析器

### 21.1 项目结构

**项目**: `NetYamlForge.Analyzers/`

基于 **Roslyn**，针对 .NET Standard 2.0 编译。

### 21.2 分析规则

| ID | 类别 | 严重性 | 检测内容 |
|----|------|--------|---------|
| **DCS001** | Security | **Error** | SQL 字符串插值（防止 SQL 注入） |
| **DCS002** | Reliability | **Error** | 异步方法阻塞调用（`.Result`/`.Wait()`） |
| **DCS003** | Architecture | **Error** | 直接实例化数据库连接类 |
| **DCS004** | Maintainability | **Warning** | 硬编码角色名直接比较 |

### 21.3 DCS001 智能检测逻辑

```csharp
// 不仅检测插值字符串，还检测：
// 1. 是否赋值给 SQL 变量名（sql, query, commandText, sqlText, rawSql, sqlQuery）
// 2. 是否传递给 SQL 类方法的第一参数（QueryAsync, ExecuteAsync, QueryFirstAsync 等）

// ❌ 触发 DCS001
var sql = $"SELECT * FROM {tableName}";
var query = $"DELETE FROM {table} WHERE {condition}";

// ✅ 安全（不使用插值）
var sql = "SELECT * FROM " + SqlSafetyGuard.SanitizeIdentifier(tableName);
```

### 21.4 DCS003 例外

内存测试连接被允许：

```csharp
// ✅ 允许（内存测试）
using var conn = new SqliteConnection("Data Source=:memory:");

// ❌ 禁止（真实数据库连接）
using var conn = new SqliteConnection("Data Source=app.db");
```

`DbInitializer` 中通过 `#pragma warning disable DCS003` 抑制（因为 DI 未就绪）。

---

## 二十二、测试体系

### 22.1 测试项目

**项目**: `NetYamlForge.Tests/`

**测试框架**: xUnit + Moq

### 22.2 主要测试文件

| 测试文件 | 测试对象 |
|---------|---------|
| `DynamicEntityControllerTests.cs` | 控制器集成测试 |
| `EntityCrudExecutionServiceTests.cs` | 钩子执行/事务 |
| `YamlSchemaValidationTests.cs` | YAML Schema 验证 |
| `SqlGenerationSnapshotTests.cs` | SQL 生成回归测试 |
| `YamlConfigStartupValidatorTests.cs` | 启动时类型验证 |
| `ListStateUrlBuilderTests.cs` | URL 状态构建器 |

### 22.3 测试命令

```bash
# 运行所有测试
dotnet test

# 运行特定测试类
dotnet test --filter "FullyQualifiedName~EntityCrudExecutionServiceTests"

# 运行包含特定关键词的测试
dotnet test --filter "FullyQualifiedName~Hook"

# 生成代码覆盖率报告
dotnet test --collect:"XPlat Code Coverage"

# 并行运行测试
dotnet test --configuration Release -- --parallel-threads 4
```

---

## 二十三、配置参考

### 23.1 appsettings.json 完整结构

```json
{
  "DatabaseProvider": "sqlite",
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=chinook.db"
  },
  "AICli": {
    "DefaultTool": "qwen",
    "TaskTimeoutSeconds": 3600,
    "MaxConcurrentTasks": 2,
    "DefaultWorkingDirectory": "/home/ubuntu/ws/NetYamlForge",
    "DefaultAllowedTools": ["Read", "Write", "Edit", "Bash", "Git"],
    "QwenCode": { "Path": "/path/to/qwen" },
    "Claude": { "Path": "/path/to/claude" },
    "Codex": { "Path": "/path/to/codex" },
    "Gemini": { "Path": "/path/to/gemini" },
    "Copilot": { "Path": "/path/to/copilot" }
  },
  "AiWindow": {
    "CliFirst": true,
    "CliTimeoutSeconds": 3600,
    "ChatResponseTimeoutSeconds": 3600,
    "ProviderPriority": ["qwen", "claude", "gemini", "ollama"],
    "DefaultProvider": "qwen",
    "MaxResponseChars": 0,
    "FallbackToTemplate": false,
    "DealerName": "AI 窓口ディーラー",
    "BusinessHours": "月〜土 9:00〜18:00（日曜・祝日定休）"
  },
  "HotReload": {
    "Enabled": true,
    "OnlyInDevelopment": true,
    "DebounceMs": 500
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  },
  "AllowedHosts": "*"
}
```

### 23.2 环境变量

| 变量 | 说明 | 示例 |
|------|------|------|
| `ASPNETCORE_ENVIRONMENT` | 运行环境 | `Production` / `Development` |
| `NYFORGE_<PROJECT>_DB_TYPE` | 项目数据库类型 | `NYFORGE_AUTO_DEALER_DEMO_DB_TYPE=sqlite` |
| `NYFORGE_<PROJECT>_CONNECTION_STRING` | 项目连接字符串 | `NYFORGE_AUTO_DEALER_DEMO_CONNECTION_STRING=...` |

---

## 二十四、扩展指南

### 24.1 如何添加新实体

1. 在 `projects/<name>/entities/` 目录下创建新的 YAML 文件
2. 定义实体结构（表名、列、钩子、分页）
3. 重启应用或等待热重载
4. 框架自动：
   - 验证 YAML Schema
   - 解析元数据
   - 生成 CRUD 页面和 API

### 24.2 如何添加自定义页面

1. 在 `projects/<name>/pages/` 目录下创建 YAML 文件
2. 定义页面布局、组件、数据源
3. 在 `project.yaml` 的 `navigation` 中添加导航入口
4. 热重载自动生效

### 24.3 如何添加项目级钩子

1. 在 `projects/<name>/hooks/<entity>/` 目录下创建 C# 文件
2. 实现 `IEntityHook` 接口
3. 在实体 YAML 的 `hooks` 段中引用
4. 应用启动时自动编译并注册

### 24.4 如何集成新的 AI 工具

1. 在 `Services/AI/Providers/` 目录下创建新的 CLI 服务类
2. 继承 `BaseCLIService` 并重写 `CommandPath` 和 `GetEnvironmentVariables()`
3. 在 `CLIServiceFactory` 中注册
4. 在 `appsettings.json` 的 `AICli` 段中添加配置

### 24.5 如何添加新的数据库方言

1. 在 `Services/Dialect/` 目录下实现 `ISqlDialect` 接口
2. 在 `ServiceCollectionExtensions.AddDatabaseServices()` 的 switch 表达式中添加新分支
3. 测试分页和字符串连接功能

### 24.6 如何添加自定义分析规则

1. 在 `NetYamlForge.Analyzers/` 项目中创建新的 `DiagnosticAnalyzer` 类
2. 定义 `DiagnosticDescriptor`（ID、类别、严重性、消息）
3. 注册语法节点分析
4. 在主项目中引用分析器项目

---

## 附录 A：关键文件路径索引

| 组件 | 路径 |
|------|------|
| **入口点** | `NetYamlForge/Program.cs` |
| **服务注册** | `NetYamlForge/Extensions/ServiceCollectionExtensions.cs` |
| **项目管理器** | `NetYamlForge/Services/Project/ProjectManager.cs` |
| **项目中间件** | `NetYamlForge/Middleware/ProjectMiddleware.cs` |
| **钩子加载器** | `NetYamlForge/Services/Project/ProjectHookLoader.cs` |
| **钩子接口** | `NetYamlForge/Services/Hooks/IEntityHook.cs` |
| **钩子执行** | `NetYamlForge/Services/HookExecutionService.cs` |
| **SQL 安全** | `NetYamlForge/Services/SqlSafetyGuard.cs` |
| **CRUD 仓库** | `NetYamlForge/Services/DynamicEntity/DynamicCrudRepository.cs` |
| **方言接口** | `NetYamlForge/Services/Dialect/ISqlDialect.cs` |
| **热重载** | `NetYamlForge/Services/HotReload/YamlHotReloadService.cs` |
| **Schema 验证** | `NetYamlForge/Services/YamlSchemaValidator.cs` |
| **系统初始化** | `NetYamlForge/Data/Schemas/SystemDatabaseInitializer.cs` |
| **项目初始化** | `NetYamlForge/Data/DbInitializer.cs` |
| **AI 聊天服务** | `NetYamlForge/Services/AI/AutoDealerChatService.cs` |
| **AI 控制器** | `NetYamlForge/Controllers/AIController.cs` |
| **实体控制器** | `NetYamlForge/Controllers/DynamicEntityController.cs` |
| **分析器** | `NetYamlForge.Analyzers/ForbiddenPatternAnalyzer.cs` |

## 附录 B：实体 YAML 字段完整参考

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| `table` | string | ✅ | 数据库表名 |
| `key` | string | ⚠️ | 单主键（与 keys 二选一） |
| `keys` | string[] | ⚠️ | 复合主键（与 key 二选一） |
| `displayName` | string | ⚠️ | 显示名称（与 displayNameKey/displayNameI18n 三选一） |
| `displayNameKey` | string | ⚠️ | 本地化键 |
| `displayNameI18n` | object | ⚠️ | 多语言显示名称 |
| `isPublic` | boolean | | 是否公开访问 |
| `softDelete` | boolean | | 是否启用软删除 |
| `columns` | object | | 列定义 |
| `columns[].type` | enum | ✅ | 列类型 |
| `columns[].length` | int | | 字符串长度 |
| `columns[].precision` | int | | decimal 精度 |
| `columns[].scale` | int | | decimal 小数位 |
| `columns[].isPrimaryKey` | boolean | | 是否主键 |
| `columns[].isNullable` | boolean | | 是否可空 |
| `columns[].isReadOnly` | boolean | | 是否只读 |
| `columns[].options` | array | | 枚举选项 |
| `columns[].foreignKey` | string | | 外键引用 |
| `forms` | object | | 表单配置 |
| `hooks` | object | | 钩子配置 |
| `paging` | object | | 分页配置 |
| `paging.mode` | enum | | `numbered` / `keyset` |
| `paging.pageSize` | int | | 每页条数 |
| `paging.enableCount` | boolean | | 是否显示总数 |
| `joins` | array | | JOIN 配置 |

## 附录 C：列类型枚举

| 类型 | 数据库映射 (SQLite) | 说明 |
|------|-------------------|------|
| `string` | TEXT | 字符串 |
| `int` | INTEGER | 整数 |
| `long` | INTEGER | 长整数 |
| `decimal` | REAL | 小数 |
| `datetime` | TEXT | 日期时间 |
| `date` | TEXT | 日期 |
| `text` | TEXT | 长文本 |
| `email` | TEXT | 邮箱 |
| `boolean` | INTEGER | 布尔值 |

---

*文档版本: 1.0 | 创建: 2026-04-09 | 基于: NetYamlForge 项目现状*
