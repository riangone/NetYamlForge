# NetYamlForge - QWEN.md

## 项目概述

**NetYamlForge** 是一个基于 YAML 驱动的 ASP.NET Core MVC 低代码开发框架。它通过 YAML 配置文件自动生成 CRUD 界面、业务逻辑和数据访问层，无需编写传统 ORM 代码。

### 核心技术栈

| 类别 | 技术 |
|------|------|
| **框架** | ASP.NET Core MVC (.NET 10.0) |
| **数据访问** | Dapper (轻量级 SQL 映射) |
| **数据库支持** | SQLite, SQL Server, MySQL, PostgreSQL |
| **配置格式** | YAML (YamlDotNet) |
| **日志** | Serilog |
| **测试框架** | xUnit |
| **代码分析** | Roslyn 自定义 Analyzers |

### 架构特点

- **YAML 驱动设计**: 实体定义、页面布局、业务钩子全部通过 YAML 配置
- **多租户项目结构**: 每个项目独立配置，支持多数据库类型
- **自动 SQL 生成**: 根据 YAML 定义自动生成 CRUD SQL，支持方言适配
- **钩子系统**: 支持 before/after 钩子链，用于业务逻辑扩展
- **批处理作业**: 支持 Cron 调度的定时任务（SQL 执行、CSV 输出）
- **国际化 (i18n)**: 支持日语、英语、中文、韩语

---

## 项目结构

```
NetYamlForge/
├── NetYamlForge/              # 主应用程序
│   ├── Controllers/           # MVC 控制器
│   │   ├── DynamicEntityController.cs  # 动态 CRUD 核心
│   │   ├── DashboardController.cs
│   │   ├── PageController.cs
│   │   └── AccountController.cs
│   ├── Services/              # 业务服务层
│   │   ├── DynamicEntity/     # 动态实体服务
│   │   ├── BatchJob/          # 批处理作业服务
│   │   ├── Hooks/             # 钩子执行服务
│   │   ├── Dialect/           # SQL 方言适配
│   │   ├── Cli/               # CLI 脚手架工具
│   │   └── Auth/              # 认证授权服务
│   ├── Models/                # 数据模型
│   ├── Views/                 # Razor 视图
│   ├── Schemas/               # JSON Schema 验证
│   ├── projects/              # 多租户项目配置
│   │   └── <project-name>/
│   │       ├── project.yaml   # 项目定义
│   │       ├── entities/*.yml # 实体定义
│   │       ├── pages/*.yaml   # 页面定义
│   │       ├── jobs/*.yml     # 批处理作业定义
│   │       └── config/*.yml   # 配置文件
│   └── Program.cs             # 应用入口
│
├── NetYamlForge.Tests/        # xUnit 测试
├── NetYamlForge.Analyzers/    # Roslyn 代码分析器
└── docs/                      # 文档
```

---

## 构建与运行命令

### 基础命令

```bash
# 构建解决方案
dotnet build

# 构建 Release 版本
dotnet build -c Release

# 运行 Web 应用
dotnet run --project NetYamlForge

# 运行所有测试
dotnet test

# 运行单个测试
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"

# 代码覆盖率
dotnet test --collect:"XPlat Code Coverage"
```

### CLI 脚手架命令

```bash
# 初始化新项目
dotnet run -- --init-project \
  --project=<name> \
  --display-name="显示名称" \
  --db-type=sqlite

# 从数据库生成实体 YAML
dotnet run -- --scaffold-entities \
  --project=<name> \
  [--no-overwrite]

# 生成钩子模板
dotnet run -- --scaffold-hook \
  --name=<HookName> \
  --project=<name> \
  [--with-tests]

# 生成批处理作业模板
dotnet run -- --scaffold-batch-job \
  --project=<name> \
  --name=<job_name>

# YAML 现代化升级
dotnet run -- --upgrade-entity-yaml \
  --project=<name>
```

### 脚本命令

```bash
# 启动应用（后台运行）
./start.sh

# 停止应用
./stop.sh

# 重启应用
./restart.sh
```

---

## 开发规范

### 代码风格

- **缩进**: 4 空格
- **命名**: 
  - 类/方法：`PascalCase`
  - 局部变量/参数：`camelCase`
- **文件命名**: 与类型名称匹配 (如 `ProjectManager.cs`)
- **YAML 键名**: `camelCase`

### 安全规范

- **禁止 SQL 字符串插值**: 使用 `SqlSafetyGuard` 和方言类
- **禁止提交密钥**: 使用 `appsettings.Development.json` 或环境变量
- **默认凭据**: 首次运行时存在默认管理员账户，生产环境需修改

### 测试实践

- 测试文件命名：`*Tests.cs`
- 新功能需添加对应的 Controller/Service 测试
- 使用快照测试验证 SQL 生成

---

## 核心组件说明

### 1. 控制器

| 控制器 | 职责 |
|--------|------|
| `DynamicEntityController` | 动态 CRUD 操作（列表、创建、编辑、删除） |
| `DashboardController` | 仪表板页面 |
| `PageController` | 自定义 YAML 页面 |
| `AccountController` | 用户认证（登录/登出） |
| `UsersController` | 用户管理 |

### 2. 服务层

| 服务 | 职责 |
|------|------|
| `DynamicEntityCommandService` | CRUD 命令执行 |
| `DynamicEntityListQueryService` | 列表查询构建 |
| `EntityCrudExecutionService` | 钩子编排与事务管理 |
| `HookExecutionService` | 钩子执行引擎 |
| `BatchJobHostedService` | 批处理作业调度 |
| `BatchJobExecutor` | 作业执行（SQL→CSV 等） |
| `FormValueValidationService` | 表单验证与类型转换 |
| `SqlSafetyGuard` | SQL 注入防护 |

### 3. 钩子系统

钩子阶段：
- `beforeCreate` / `afterCreate`
- `beforeUpdate` / `afterUpdate`
- `beforeDelete` / `afterDelete`
- `beforeRun` / `afterRun` (批处理作业)

钩子可返回：
- `Continue`: 继续执行
- `Cancel`: 中止操作
- `Abort`: 返回错误

### 4. 批处理作业

支持的作业类型：
- `sql_to_csv`: SQL 查询结果输出为 CSV
- `sql_command`: 执行 SQL 命令
- `stored_procedure`: 执行存储过程

Cron 表达式示例：
- `0 2 * * *` - 每天 2:00
- `0 */6 * * *` - 每 6 小时
- `30 9 * * 1-5` - 工作日 9:30

### 5. 数据库方言

支持多种数据库的 SQL 语法适配：
- `SqliteDialect`
- `SqlServerDialect`
- `MySqlDialect`
- `PostgresDialect`

---

## YAML 配置示例

### 项目定义 (project.yaml)

```yaml
name: todo-app
displayName: "Todo App"
description: "タスク・プロジェクト管理の全機能デモ"
version: "1.0.0"

database:
  type: sqlite
  path: database/todo-app.db

features:
  multiLanguage: true
  userAuthentication: true
  dashboard: true
  pages: true

layout:
  dashboardTheme: workspace
  navigation:
    entities:
      - task
      - project
      - category
```

### 实体定义 (entities/task.yml)

```yaml
entities:
  task:
    table: Task
    key: Id
    displayName: Task
    
    forms:
      Title:
        type: string
        required: true
        label: タイトル
        editable: true
      Status:
        type: string
        required: true
        label: ステータス
        options:
          - pending
          - in_progress
          - review
          - done
          - cancelled
      Priority:
        type: string
        label: 優先度
        options:
          - low
          - medium
          - high
          - urgent
```

### 批处理作业 (jobs/nightly_stats.yml)

```yaml
jobs:
  nightly_stats:
    displayName: 夜间统计作业
    enabled: true
    
    schedule:
      cron: "0 2 * * *"  # 每天 2:00
      timezone: "Asia/Tokyo"
    
    type: sql_to_csv
    
    settings:
      sqlFile: jobs/sql/nightly_stats.sql
      outputFile: jobs/output/stats_{date:yyyyMMdd}.csv
      includeHeader: true
      delimiter: ","
    
    onFailure:
      retryCount: 3
      retryInterval: 300
```

---

## 测试架构

### 主要测试文件

| 文件 | 测试对象 |
|------|---------|
| `DynamicEntityControllerTests.cs` | 控制器响应分岐 |
| `EntityCrudExecutionServiceTests.cs` | 钩子执行与事务 |
| `YamlSchemaValidationTests.cs` | YAML 格式验证 |
| `SqlGenerationSnapshotTests.cs` | SQL 生成回归 |
| `BatchJobExecutorTests.cs` | 批处理作业执行 |

### 测试模式

```csharp
// 典型测试结构
public class MyServiceTests
{
    [Fact]
    public void Method_ExpectedBehavior()
    {
        // Arrange - 准备 Fake 依赖
        // Act - 执行操作
        // Assert - 验证结果
    }
}
```

---

## Git 提交规范

使用 Conventional Commits:

```
feat: 新功能
fix: 修复 bug
docs: 文档更新
refactor: 重构
test: 测试相关
chore: 构建/工具
```

---

## 常见问题

### Q: 启动时 "No entity yaml found" 错误

**解决**: 运行脚手架生成 YAML
```bash
dotnet run -- --scaffold-entities --project=<项目名>
```

### Q: 钩子不执行

**检查点**:
1. YAML 钩子键名是否为 camelCase
2. 钩子名拼写是否正确
3. `Program.cs` 中是否注册了 DI

### Q: 批处理作业不执行

**检查点**:
1. 作业是否 `enabled: true`
2. Cron 表达式是否正确
3. 时区配置是否匹配

---

## 示例项目

项目包含两个示例项目：

1. **todo-app**: 任务管理示例
   - 实体：task, project, category, comment
   - 功能：CRUD、看板、分析仪表板

2. **ui-showcase**: UI 组件展示
   - 展示所有 UI 组件样式

---

## 输出语言规范

根据项目配置，响应应使用**中文**，但以下内容保持原样：
- 代码块、CLI 命令、文件路径
- 日志、堆栈跟踪、JSON 键
- 标识符和工具原始输出
