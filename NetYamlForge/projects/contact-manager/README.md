# Contact Manager - 联系人管理系统

NetYamlForge 框架完整功能演示项目。

## 功能特点

| 功能 | 说明 |
|------|------|
| **实体定义** | 3 个实体（公司、联系人、交互记录） |
| **外键关联** | contact→company, interaction→contact/company |
| **字段验证** | 必填字段、枚举值 |
| **枚举字段** | 状态、优先级、类型、评级 |
| **钩子系统** | trim、now 等内置钩子 |
| **仪表板** | 统计卡片和数据表格 |
| **批处理作业** | 每周统计报告（Cron 调度） |
| **多语言** | 日语、英语、中文 |

## 快速开始

### 初始化数据库

```bash
cd NetYamlForge/projects/contact-manager
sqlite3 database/contact-manager.db < database/init.sql
```

### 启动应用

```bash
dotnet run --project NetYamlForge
```

### 访问

http://localhost:5000/contact-manager

## 项目结构

```
contact-manager/
├── project.yaml           # 项目定义
├── dashboard.yml          # 仪表板配置
├── entities/
│   ├── company.yml        # 公司实体
│   ├── contact.yml        # 联系人实体
│   └── interaction.yml    # 交互记录实体
├── jobs/
│   ├── weekly_report.yml  # 每周报告作业
│   └── sql/
│       └── weekly_report.sql
├── database/
│   └── init.sql           # 初始化脚本
└── README.md              # 本文档
```

## 示例数据

- **公司**: 3 家（科技、贸易、咨询）
- **联系人**: 5 位
- **交互记录**: 3 条

## 批处理作业

### 每周统计报告

- **执行时间**: 每周一 8:00（日本时间）
- **输出**: `jobs/output/weekly_YYYY-MM-DD.csv`

---

*Contact Manager 示例项目 - NetYamlForge Framework*
