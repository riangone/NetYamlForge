# AI 聊天历史记录隔离修复报告

**日期**: 2026-04-08  
**状态**: ✅ 已完成

---

## 问题

之前，所有 AI 聊天历史记录（包括子项目的）都通过 Python 脚本 `scripts/save_last_response.py` 保存到 `system.db`，导致不同项目的聊天记录混在一起。

## 修复内容

### 1. 删除 Python 脚本保存机制

- ❌ 删除 `scripts/save_last_response.py`
- ❌ 从 `skills/_system-prompt.md` 中移除 Python 脚本调用指示

### 2. 统一使用 C# API

现在所有聊天历史记录都通过 `ChatHistoryService` 保存，该服务根据 `projectName` 参数自动选择正确的数据库：

| projectName | 数据库路径 |
|-------------|-----------|
| `null` 或空 | `system.db` |
| `auto-dealer-demo` | `projects/auto-dealer-demo/chat.db` |
| 其他项目名 | `projects/<name>/chat.db` |

### 3. 数据迁移

运行 `scripts/migrate_chat_history.py` 将 `system.db` 中错误保存的子项目记录迁移到正确的项目数据库。

迁移结果：
- `system.db`: 271 条 `framework` 上下文记录 ✅
- `auto-dealer-demo/chat.db`: 96 条 `dealer-staff` + 60 条 `dealer-customer` 记录 ✅

## 架构说明

### Web API 路径

```
POST /api/AI/chat              → system.db (framework)
POST /{project}/api/AI/chat    → projects/<project>/chat.db
```

### 数据库选择逻辑

```csharp
// ChatHistoryService.GetConnectionString(projectName)
if (string.IsNullOrEmpty(projectName))
    return system.db;
else
    return projects/<name>/chat.db;
```

### ChatContext 用途

在项目数据库内部，通过 `ChatContext` 字段进一步区分不同的业务场景：

- `dealer-staff`: 销售人员 AI 助手
- `dealer-customer`: 客户 AI 客服

---

## 相关文件

| 文件 | 说明 |
|------|------|
| `Services/AI/ChatHistoryService.cs` | 核心服务，负责数据库选择和 CRUD 操作 |
| `Controllers/AIController.cs` | API 端点，从路由绑定项目名 |
| `scripts/migrate_chat_history.py` | 一次性迁移脚本 |

---

*修复完成: 2026-04-08*
