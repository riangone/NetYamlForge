# NetYamlForge — Qwen Code 上下文指南

## 项目概述

**NetYamlForge** 是一个基于 YAML 驱动的 ASP.NET Core MVC 应用程序框架，能够从 YAML 配置自动生成 CRUD 界面、业务逻辑钩子和数据管理功能。

### 核心技术栈

| 类别 | 技术 |
|------|------|
| **框架** | ASP.NET Core MVC (.NET 10.0) |
| **数据库** | SQLite (默认), PostgreSQL, MySQL, SQL Server |
| **ORM** | Dapper (轻量级 SQL 映射) |
| **YAML 处理** | YamlDotNet |
| **JSON Schema** | JsonSchema.Net |
| **日志** | Serilog |
| **PDF 生成** | PDFsharp |
| **实时通信** | SignalR |
| **测试框架** | xUnit + Moq |
| **自定义分析器** | Roslyn Analyzers (NetYamlForge.Analyzers) |

### 架构特点

- **YAML 驱动设计**: 所有实体定义、页面配置、钩子逻辑均通过 YAML 文件声明
- **多租户支持**: 通过 `projects/<name>/` 目录隔离不同项目配置
- **动态 SQL 生成**: 基于 YAML 定义自动生成安全的 SQL 语句（使用 `SqlSafetyGuard`）
- **钩子系统**: 支持在 CRUD 操作前后执行业务逻辑
- **热重载**: 开发模式下支持 YAML 配置热更新
- **AI 集成**: 内置 AI CLI 工具支持（Qwen Code、Claude、Gemini、Ollama、LM Studio 等）

---

## 项目结构

```
NetYamlForge/
├── NetYamlForge/                 # 主应用程序
│   ├── Controllers/              # MVC 控制器
│   │   ├── Api/                  # API 端点
│   │   ├── DynamicEntityController.cs   # 动态实体 CRUD
│   │   ├── DashboardController.cs       # 仪表板
│   │   └── PageController.cs            # 自定义页面
│   ├── Services/                 # 业务服务层
│   │   ├── DynamicEntity/        # 实体解析服务
│   │   ├── Hooks/                # 钩子执行引擎
│   │   ├── Dialect/              # 多数据库方言
│   │   ├── AI/                   # AI CLI 集成
│   │   └── Cli/                  # CLI 脚手架工具
│   ├── Models/                   # 数据模型
│   ├── Views/                    # Razor 视图
│   ├── Schemas/                  # JSON Schema 验证
│   ├── projects/                 # 多租户项目配置
│   │   └── <name>/
│   │       ├── project.yaml      # 项目主配置
│   │       ├── entities/*.yml    # 实体定义
│   │       ├── pages/*.yaml      # 页面配置
│   │       └── hooks/            # 业务钩子代码
│   └── wwwroot/                  # 静态资源
│
├── NetYamlForge.Tests/           # xUnit 测试项目
├── NetYamlForge.Analyzers/       # Roslyn 代码分析器
├── docs/                         # 技术文档
├── docker/                       # Docker 配置
└── scripts/                      # 辅助脚本
```

---

## 构建与运行命令

### 基础命令

```bash
# 构建解决方案
dotnet build

# 构建 Release 版本
dotnet build -c Release

# 运行开发服务器
dotnet run --project NetYamlForge

# 运行所有测试 (约 380 个)
dotnet test

# 运行单个测试
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"

# 生成代码覆盖率报告
dotnet test --collect:"XPlat Code Coverage"
```

### CLI 脚手架工具

```bash
# 初始化新项目
dotnet run -- --init-project --project=<name> --display-name="显示名称" --db-type=sqlite

# 从数据库生成实体 YAML
dotnet run -- --scaffold-entities --project=<name> [--no-overwrite]

# 生成钩子代码（可选包含测试）
dotnet run -- --scaffold-hook --name=<HookName> --project=<name> [--with-tests]

# 升级实体 YAML 到最新格式
dotnet run -- --upgrade-entity-yaml --project=<name>

# 生成批处理作业模板
dotnet run -- --scaffold-batch-job --name=<job_name> --project=<name>

# JSON 输出模式（CI 集成）
dotnet run -- --scaffold-entities --project=<name> --json
```

### Docker 部署

```bash
# 默认 SQLite 模式
cp .env.example .env
docker compose up -d

# PostgreSQL 模式
docker compose --profile postgres up -d

# MySQL 模式
docker compose --profile mysql up -d

# SQL Server 模式
docker compose --profile sqlserver up -d
```

---

## 开发规范

### 代码风格

- **缩进**: 4 空格
- **命名**:
  - 类型/方法: `PascalCase`
  - 局部变量/参数: `camelCase`
  - 文件名: 通常与类型名匹配（如 `ProjectManager.cs`）
- **YAML 键名**: `pages/*.yaml` 使用 `camelCase` 与实体约定保持一致

### SQL 安全

- **禁止** 直接字符串插值拼接 SQL
- **必须** 使用现有的 `SqlSafetyGuard` 和数据库方言处理
- 分析器会在构建时检测违规代码（DCS001-DCS004）

### 测试实践

- 测试文件命名: `*Tests.cs`
- 快照测试用于 SQL 生成回帰测试
- 添加新功能时需包含：
  - 控制器/服务测试
  - YAML Schema 影响验证

### Git 提交规范

使用 Conventional Commits:

```
feat: 添加新功能
fix: 修复 bug
docs: 文档更新
refactor: 代码重构（无功能变化）
test: 添加/修改测试
chore: 构建/工具配置
```

---

## 关键配置文件

### appsettings.json

```json
{
  "DatabaseProvider": "sqlite",
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=chinook.db"
  },
  "AICli": {
    "DefaultTool": "qwen",
    "TaskTimeoutSeconds": 1800,
    "MaxConcurrentTasks": 2
  },
  "HotReload": {
    "Enabled": true,
    "OnlyInDevelopment": true,
    "DebounceMs": 500
  }
}
```

### 项目配置示例 (project.yaml)

```yaml
name: todo-app
displayName: "Todo App"
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
```

---

## 核心服务与组件

### Services 目录结构

| 服务 | 职责 |
|------|------|
| `DynamicEntity/` | YAML 实体解析、SQL 生成 |
| `Hooks/` | 钩子注册、执行、链式调用 |
| `Dialect/` | SQLite/PostgreSQL/MySQL/SQL Server 方言 |
| `SqlSafetyGuard` | SQL 注入防护 |
| `YamlSchemaValidator` | YAML 文件 Schema 验证 |
| `DashboardConfigProvider` | 仪表板配置加载 |
| `DocumentPdfService` | PDF 文档生成 |

### Controllers 目录结构

| 控制器 | 职责 |
|--------|------|
| `DynamicEntityController` | 通用 CRUD 操作 |
| `DashboardController` | 仪表板统计/图表 |
| `PageController` | 自定义 YAML 页面 |
| `ApiEntityController` | REST API 端点 |
| `BatchJobController` | 批处理作业管理 |
| `AIController` | AI CLI 集成接口 |

---

## 测试指南

### 主要测试文件

| 文件 | 测试对象 |
|------|---------|
| `DynamicEntityControllerTests.cs` | 控制器集成测试 |
| `EntityCrudExecutionServiceTests.cs` | 钩子执行/事务 |
| `YamlSchemaValidationTests.cs` | YAML Schema 验证 |
| `SqlGenerationSnapshotTests.cs` | SQL 生成回帰测试 |
| `YamlConfigStartupValidatorTests.cs` | 启动时类型验证 |
| `ListStateUrlBuilderTests.cs` | URL 状态构建器 |

### 测试最佳实践

```bash
# 运行特定测试类
dotnet test --filter "FullyQualifiedName~EntityCrudExecutionServiceTests"

# 运行包含特定关键词的测试
dotnet test --filter "FullyQualifiedName~Hook"

# 并行运行测试（默认开启）
dotnet test --configuration Release -- --parallel-threads 4
```

---

## 安全与配置

### 环境变量

| 变量 | 说明 | 默认值 |
|------|------|--------|
| `ASPNETCORE_ENVIRONMENT` | 运行环境 | `Production` |
| `NYFORGE_<PROJECT>_DB_TYPE` | 项目数据库类型 | `sqlite` |
| `NYFORGE_<PROJECT>_CONNECTION_STRING` | 项目连接字符串 | - |

### 安全建议

- **不要** 提交敏感信息到版本控制
- 使用 `appsettings.Development.json` 进行本地配置覆盖
- 生产环境使用环境变量管理敏感值
- 首次运行的默认管理员凭据应更改

---

## AI 助手集成

项目内置了 AI CLI 工具支持：

```yaml
# AI 助手配置
AICli:
  DefaultTool: qwen
  TaskTimeoutSeconds: 1800
  MaxConcurrentTasks: 2
  DefaultAllowedTools: ["Read", "Write", "Edit", "Bash", "Git"]
  # 本地模型配置
  Ollama:
    BaseUrl: "http://localhost:11434"
    Model: "qwen2.5-coder:7b"
    UseApi: true
    ContextSize: 4096
    Temperature: 0.7
  LmStudio:
    BaseUrl: "http://localhost:1234"
    Model: "qwen2.5-coder-7b-instruct"
    ContextSize: 4096
    Temperature: 0.7
```

支持多种 AI 工具：

| 工具 | 类型 | 配置项 |
|------|------|--------|
| **Qwen Code** | 云端 | `QwenCode` |
| **Claude Code** | 云端 | `Claude` |
| **OpenAI Codex** | 云端 | `Codex` |
| **Google Gemini** | 云端 | `Gemini` |
| **Ollama** 🆕 | 本地 | `Ollama` |
| **LM Studio** 🆕 | 本地 | `LmStudio` |
| **Mock** | 测试 | `mock` |

### 本地模型快速开始

**Ollama:**
```bash
# 安装
curl -fsSL https://ollama.com/install.sh | sh

# 下载模型
ollama pull qwen2.5-coder:7b

# 启动服务
ollama serve
```

**LM Studio:**
1. 从 [lmstudio.ai](https://lmstudio.ai) 下载安装
2. 下载模型并启动 Local Server

---

## 文档资源

### 核心文档（日语）

- [5 分钟快速入门](docs/quickstart-ja.md)
- [架构映射](docs/architecture-map-ja.md)
- [框架概览](docs/framework-overview-tutorial-ja.md)
- [开发者教程](docs/developer-tutorial-ja.md)

### 技术指南

- [通用钩子列表](docs/COMMON_HOOKS.md)
- [YAML 示例集](docs/chinook-yaml-examples.md)
- [批处理作业实现](docs/guides/batch-jobs.md)
- [热重载说明](docs/HOTRELOAD.md)

### 参考文档

- [为什么不用 ORM](docs/why-no-orm-zh.md) (中文)
- [YAML 驱动设计思想](docs/yaml-driven-design-zh.md) (中文)

---

## 常见问题排查

### 构建问题

```bash
# 清理并重新构建
dotnet clean && dotnet build

# 还原 NuGet 包
dotnet restore
```

### 数据库问题

```bash
# 重置 SQLite 数据库
rm projects/<name>/database/*.db
# 重新启动应用会自动初始化
```

### 热重载问题

如果 YAML 更改未生效：
1. 检查 `appsettings.json` 中 `HotReload.Enabled` 是否为 `true`
2. 确认文件修改时间戳已更新
3. 开发模式下默认 500ms 防抖

---

## 版本信息

- **.NET 版本**: 10.0
- **主要依赖版本**:
  - Dapper: 2.1.66
  - YamlDotNet: 16.3.0
  - JsonSchema.Net: 8.0.0
  - Serilog.AspNetCore: 9.0.0
  - PDFsharp: 7.0.0-preview-1

---

*本文档最后更新：2026 年 4 月*
