# Contact Manager - 联系人管理系统

NetYamlForge 框架的完整功能演示项目。

## 功能特点

### 核心功能展示

| 功能 | 说明 | 文件位置 |
|------|------|----------|
| **实体定义** | 3 个实体（公司、联系人、交互记录） | `entities/*.yml` |
| **外键关联** | 联系人→公司，交互→联系人/公司 | `entities/contact.yml` |
| **字段验证** | 邮箱、电话、URL 验证 | `entities/*.yml` |
| **枚举字段** | 状态、优先级、类型等 | `entities/*.yml` |
| **钩子系统** | Before/After 钩子 | `Hooks/*.cs` |
| **页面定义** | 统计报表页面 | `pages/statistics.yaml` |
| **仪表板** | 首页仪表板配置 | `config/dashboard.yml` |
| **批处理作业** | 每周统计报告 | `jobs/*.yml` |
| **国际化** | 中日英多语言支持 | `project.yaml` |

### 实体关系

```
┌─────────────┐       ┌─────────────┐       ┌─────────────┐
│   Company   │       │   Contact   │       │ Interaction │
│   公司表     │◄──────│   联系人表   │◄──────│   交互记录表 │
├─────────────┤       ├─────────────┤       ├─────────────┤
│ id          │       │ id          │       │ id          │
│ name        │       │ firstName   │       │ contactId   │
│ industry    │       │ lastName    │       │ companyId   │
│ website     │       │ companyId   │       │ type        │
│ phone       │       │ email       │       │ subject     │
│ email       │       │ phone       │       │ scheduledAt │
│ ...         │       │ ...         │       │ ...         │
└─────────────┘       └─────────────┘       └─────────────┘
      ▲                     │                     │
      │                     │                     │
      └─────────────────────┴─────────────────────┘
```

## 快速开始

### 1. 初始化数据库

```bash
# 在项目根目录执行
cd NetYamlForge/projects/contact-manager

# 使用 SQLite 命令行
sqlite3 database/contact-manager.db < database/init.sql
```

### 2. 启动应用

```bash
# 返回解决方案根目录
cd ../../..

# 启动应用
dotnet run --project NetYamlForge
```

### 3. 访问应用

浏览器访问：`http://localhost:5000/contact-manager`

## 项目结构

```
contact-manager/
├── project.yaml           # 项目定义
├── entities/
│   ├── company.yml        # 公司实体
│   ├── contact.yml        # 联系人实体
│   └── interaction.yml    # 交互记录实体
├── pages/
│   └── statistics.yaml    # 统计报表页面
├── config/
│   ├── dashboard.yml      # 仪表板配置
│   └── layout.yml         # 布局配置
├── jobs/
│   ├── weekly_contact_report.yml  # 每周报告作业
│   └── sql/
│       └── weekly_contact_report.sql
├── Hooks/
│   ├── ContactGenerateFullNameHook.cs
│   ├── ValidateContactEmailHook.cs
│   ├── LogContactCreatedHook.cs
│   ├── TrimFieldsHook.cs
│   └── InteractionHooks.cs
└── database/
    ├── init.sql           # 初始化脚本
    └── contact-manager.db # SQLite 数据库（运行时生成）
```

## 钩子说明

| 钩子 | 触发时机 | 功能 |
|------|----------|------|
| `ContactGenerateFullNameHook` | beforeCreate/Update | 自动生成全名（姓 + 名） |
| `ValidateContactEmailHook` | beforeCreate/Update | 邮箱格式验证 |
| `LogContactCreatedHook` | afterCreate | 创建交互日志记录 |
| `TrimFieldsHook` | beforeCreate/Update | 修剪字符串字段 |
| `TrimCompanyFieldsHook` | beforeCreate/Update | 修剪公司字段 |
| `InteractionSetDefaultsHook` | beforeCreate | 自动填充公司信息 |
| `InteractionUpdateTimestampHook` | beforeUpdate | 自动设置完成时间 |

## 批处理作业

### 每周联系人统计报告

- **执行时间**: 每周一 8:00（日本时间）
- **输出**: `jobs/output/weekly_report_YYYY-MM-DD.csv`
- **内容**: 上周新增/更新联系人、交互统计等

## 示例数据

项目包含以下示例数据：

- **公司**: 5 家（科技、贸易、咨询、制造、设计）
- **联系人**: 10 位
- **交互记录**: 10 条

## API 端点

| 端点 | 说明 |
|------|------|
| `/contact-manager/company` | 公司列表/管理 |
| `/contact-manager/contact` | 联系人列表/管理 |
| `/contact-manager/interaction` | 交互记录列表/管理 |
| `/contact-manager/statistics` | 统计报表页面 |

## 扩展建议

1. **添加用户认证**: 修改 `project.yaml` 启用 `userAuthentication`
2. **添加更多验证**: 在实体 YAML 中添加 `validateRegex` 等
3. **自定义页面**: 在 `pages/` 目录添加新页面
4. **更多批处理作业**: 复制 `jobs/weekly_contact_report.yml` 修改

## 故障排除

### 问题：启动时提示 "No entity yaml found"

**解决**: 确保 `entities/` 目录存在且包含有效的 YAML 文件

### 问题：数据库为空

**解决**: 运行 `database/init.sql` 初始化脚本

### 问题：钩子不执行

**检查**:
1. 钩子类名与 YAML 中引用的名称一致
2. 钩子已正确注册到 DI 容器（自动发现）
3. 检查日志输出

---

*Contact Manager 示例项目 - NetYamlForge Framework*
