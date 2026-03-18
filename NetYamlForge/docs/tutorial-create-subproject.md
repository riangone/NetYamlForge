# 教程：从 CLI 开始创建子项目

本教程以 **「任务管理系统（task-tracker）」** 为例，
讲解如何通过 CLI 从零构建一个完整的 NetYamlForge 子项目。

---

## 最终效果

| 功能 | 内容 |
|------|------|
| 实体 | Category（分类）/ Task（任务）/ Comment（评论） |
| 仪表盘 | 4 个统计卡片 + 2 个图表 |
| 自定义页面 | 逾期任务列表 |
| 内置钩子 | 校验 + 自动时间戳 |
| 自定义钩子 | 任务完成时自动设置完成时间 |

---

## Step 1：初始化项目

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj -- \
  --init-project \
  --project=task-tracker \
  --display-name="任务管理" \
  --db-type=sqlite \
  --db-path=database/task-tracker.db
```

执行后，自动生成以下目录结构：

```
projects/task-tracker/
├── project.yaml          # 项目配置
├── config/
│   ├── dashboard.yml     # 仪表盘统计配置
│   ├── layout.yml        # 导航配置
│   └── i18n.yml          # 多语言标签
├── entities/             # 实体 YAML（在此添加文件）
├── pages/                # 自定义页面 YAML
├── Hooks/                # 项目专属钩子
├── database/             # SQLite 数据库文件
└── views/                # 项目专属视图
```

### 查看生成的 `project.yaml`

```yaml
name: task-tracker
displayName: 任务管理
version: "1.0.0"
database:
  type: sqlite
  path: database/task-tracker.db
features:
  multiLanguage: false
  userAuthentication: true
```

---

## Step 2：创建数据库

新建 `projects/task-tracker/database/init.sql`，定义表结构：

```sql
-- 分类
CREATE TABLE IF NOT EXISTS category (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT    NOT NULL,
    color       TEXT    DEFAULT '#6c757d',
    created_at  TEXT    DEFAULT (datetime('now','localtime'))
);

-- 任务
CREATE TABLE IF NOT EXISTS task (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    title        TEXT    NOT NULL,
    description  TEXT,
    status       TEXT    NOT NULL DEFAULT 'todo',   -- todo / in_progress / done
    priority     TEXT    NOT NULL DEFAULT 'medium', -- low / medium / high
    category_id  INTEGER REFERENCES category(id),
    due_date     TEXT,
    completed_at TEXT,
    created_at   TEXT    DEFAULT (datetime('now','localtime')),
    updated_at   TEXT,
    is_deleted   INTEGER DEFAULT 0
);

-- 评论
CREATE TABLE IF NOT EXISTS comment (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    task_id    INTEGER NOT NULL REFERENCES task(id),
    body       TEXT    NOT NULL,
    author     TEXT    NOT NULL,
    created_at TEXT    DEFAULT (datetime('now','localtime'))
);

-- 示例数据
INSERT INTO category(name, color) VALUES ('开发', '#0d6efd'), ('设计', '#6f42c1'), ('运维', '#dc3545');
INSERT INTO task(title, status, priority, category_id, due_date)
  VALUES ('首页开发', 'in_progress', 'high', 1, date('now', '+3 days')),
         ('Logo 设计', 'todo', 'medium', 2, date('now', '+7 days')),
         ('服务器配置', 'done', 'high', 3, date('now', '-1 day'));
```

用 SQLite CLI 创建数据库：

```bash
sqlite3 projects/task-tracker/database/task-tracker.db < projects/task-tracker/database/init.sql
```

---

## Step 3：自动生成实体 YAML（脚手架）

数据库存在后，执行脚手架命令，从表结构自动生成 YAML 骨架：

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj -- \
  --scaffold-entities \
  --project=task-tracker
```

`projects/task-tracker/entities/` 下会生成 `category.yml`、`task.yml`、`comment.yml`。
后续步骤中我们逐一完善这些文件。

---

## Step 4：定义 Category 实体

编辑 `projects/task-tracker/entities/category.yml`：

```yaml
entities:
  category:
    table: category
    key: id
    displayName: 分类

    paging:
      pageSize: 20
      mode: numbered

    layout:
      forms:
        columns: 1
        order: [name, color]

    columns:
      id:         { type: int,    identity: true, label: ID,   sortable: true }
      name:       { type: string, required: true,  label: 分类名, searchable: true, sortable: true }
      color:      { type: string, label: 颜色 }
      created_at: { type: string, label: 创建时间, sortable: true }

    forms:
      name:  { type: string, required: true, label: 分类名,                editable: true }
      color: { type: string, label: 颜色代码（例: #0d6efd）, editable: true }

    filters: {}

    links:
      tasks:
        label: 任务列表
        entity: task
        filter:
          category_id: "{id}"

    hooks:
      beforeCreate:
        - trim:name
        - now:created_at
      beforeUpdate:
        - trim:name
```

---

## Step 5：定义 Task 实体（JOIN / 外键 / 软删除）

编辑 `projects/task-tracker/entities/task.yml`：

```yaml
entities:
  task:
    table: task
    key: id
    displayName: 任务
    softDelete: true    # 删除时更新 is_deleted=1（不物理删除）

    paging:
      pageSize: 25
      mode: numbered
      enableCount: true

    layout:
      forms:
        columns: 2
        order: [title, status, priority, category_id, due_date, description, completed_at]
      filters:
        columns: 4
        order: [status, priority, category_id, due_date]

    confirmation:
      delete: "确定要删除该任务吗？"

    joins:
      - table: category
        alias: cat
        on: "task.category_id = cat.id"
        type: left

    columns:
      id:           { type: int,    identity: true, label: ID,   sortable: true }
      title:        { type: string, required: true,  label: 标题, searchable: true, sortable: true }
      status:       { type: string, label: 状态,     sortable: true }
      priority:     { type: string, label: 优先级,   sortable: true }
      category_name:
        type: string
        expression: "cat.name"
        label: 分类
        sortable: true
      due_date:     { type: string, label: 截止日期, sortable: true }
      completed_at: { type: string, label: 完成时间, sortable: true }
      created_at:   { type: string, label: 创建时间, sortable: true }

    forms:
      title:
        type: string
        required: true
        label: 标题
        editable: true
      description:
        type: string
        label: 详情
        editable: true
      status:
        type: string
        label: 状态
        editable: true
        options: [todo, in_progress, done]
      priority:
        type: string
        label: 优先级
        editable: true
        options: [low, medium, high]
      category_id:
        type: int
        label: 分类
        editable: true
        foreignKey:
          entity: category
          displayColumn: name
      due_date:
        type: date
        label: 截止日期
        editable: true
      completed_at:
        type: string
        label: 完成时间
        editable: false           # 由钩子自动设置
      created_at:
        type: string
        label: 创建时间
        editable: false

    filters:
      status:
        type: dropdown
        label: 状态
        options: [todo, in_progress, done]
      priority:
        type: dropdown
        label: 优先级
        options: [low, medium, high]
      category_id:
        type: dropdown
        label: 分类
        foreignKey:
          entity: category
          displayColumn: name
      due_date:
        type: date-range
        label: 截止日期

    links:
      comments:
        label: 评论
        entity: comment
        filter:
          task_id: "{id}"

    hooks:
      beforeCreate:
        - validate_required:title
        - trim:title
        - now:created_at
        - now:updated_at
      beforeUpdate:
        - validate_required:title
        - trim:title
        - now:updated_at
        - task_complete_timestamp    # ← 自定义钩子（Step 8 实现）
```

---

## Step 6：定义 Comment 实体

编辑 `projects/task-tracker/entities/comment.yml`：

```yaml
entities:
  comment:
    table: comment
    key: id
    displayName: 评论

    paging:
      pageSize: 50
      mode: numbered

    layout:
      forms:
        columns: 1
        order: [task_id, author, body]

    joins:
      - table: task
        alias: t
        on: "comment.task_id = t.id"
        type: left

    columns:
      id:         { type: int,    identity: true, label: ID,   sortable: true }
      task_title:
        type: string
        expression: "t.title"
        label: 任务
        sortable: true
      author:     { type: string, label: 作者,   searchable: true, sortable: true }
      body:       { type: string, label: 内容,   searchable: true }
      created_at: { type: string, label: 发布时间, sortable: true }

    forms:
      task_id:
        type: int
        label: 任务
        editable: true
        foreignKey:
          entity: task
          displayColumn: title
      author:
        type: string
        required: true
        label: 作者
        editable: true
      body:
        type: string
        required: true
        label: 内容
        editable: true

    filters:
      author:
        type: like
        label: 作者

    hooks:
      beforeCreate:
        - validate_required:author,body
        - trim:author,body
        - now:created_at
```

---

## Step 7：配置仪表盘

编辑 `projects/task-tracker/config/dashboard.yml`：

```yaml
# 统计卡片
stats:
  - label: 任务总数
    entity: task
    aggregate: count
    icon: 📋
    color: badge-primary

  - label: 待处理
    entity: task
    aggregate: count
    filter: "status = 'todo'"
    icon: 🔵
    color: badge-info

  - label: 进行中
    entity: task
    aggregate: count
    filter: "status = 'in_progress'"
    icon: 🟡
    color: badge-warning

  - label: 已完成
    entity: task
    aggregate: count
    filter: "status = 'done'"
    icon: ✅
    color: badge-success

# 图表
charts:
  - title: 按优先级统计
    type: doughnut
    entity: task
    valueAggregate: count
    groupExpression: priority
    orderBy: value
    orderDir: desc

  - title: 按分类统计
    type: bar
    entity: task
    valueAggregate: count
    groupExpression: "cat.name"
    joinClause: "LEFT JOIN category cat ON task.category_id = cat.id"
    orderBy: value
    orderDir: desc
    colorBg: rgba(13, 110, 253, 0.6)
    colorBorder: rgba(13, 110, 253, 1)
```

---

## Step 8：脚手架自定义钩子

用 CLI 生成钩子类和测试文件骨架：

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj -- \
  --scaffold-hook \
  --name=TaskCompleteTimestamp \
  --project=task-tracker \
  --with-tests
```

生成文件：

```
projects/task-tracker/Hooks/TaskCompleteTimestampHook.cs
NetYamlForge.Tests/Hooks/TaskCompleteTimestampHookTests.cs
```

### 实现钩子

编辑 `projects/task-tracker/Hooks/TaskCompleteTimestampHook.cs`：

```csharp
using System.Data;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.TaskTracker.Hooks;

/// <summary>
/// 当任务 status 变为 "done" 时，自动设置 completed_at。
///
/// 在 entities/task.yml 中的用法：
///   hooks:
///     beforeUpdate:
///       - task_complete_timestamp
/// </summary>
public class TaskCompleteTimestampHook : IEntityHook
{
    public string Name => "task_complete_timestamp";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var newStatus = ctx.Values.TryGetValue("status", out var s) ? s as string : null;

        if (string.Equals(newStatus, "done", StringComparison.OrdinalIgnoreCase))
        {
            if (!ctx.Values.TryGetValue("completed_at", out var existing) ||
                existing == null || string.IsNullOrWhiteSpace(existing.ToString()))
            {
                ctx.Values["completed_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
        }
        else
        {
            // 状态从 done 改回时清除完成时间
            ctx.Values["completed_at"] = null;
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
```

### 注册到 DI

在 `NetYamlForge/Program.cs` 的 DI 注册区添加：

```csharp
// projects/task-tracker/Hooks
builder.Services.AddSingleton<IEntityHook, TaskCompleteTimestampHook>();
```

> **注意**：钩子名称（`Name` 属性）与 YAML 中的 `hooks.beforeUpdate` 条目不区分大小写匹配。

---

## Step 9：创建自定义页面（逾期任务）

新建 `projects/task-tracker/pages/OverdueTasks.yml`：

```yaml
title: 逾期任务
description: 已超过截止日期且未完成的任务列表。

ui:
  page:
    layout: stack
    density: comfortable

sections:
  - id: overdue_tasks
    title: 逾期任务
    source_type: custom
    source: |
      SELECT
        t.id        AS 任务ID,
        t.title     AS 标题,
        t.status    AS 状态,
        t.priority  AS 优先级,
        cat.name    AS 分类,
        t.due_date  AS 截止日期,
        CAST(julianday('now') - julianday(t.due_date) AS INTEGER) AS 逾期天数
      FROM task t
      LEFT JOIN category cat ON cat.id = t.category_id
      WHERE t.due_date < date('now','localtime')
        AND t.status != 'done'
        AND t.is_deleted = 0
    columns:
      - 任务ID
      - 标题
      - 状态
      - 优先级
      - 分类
      - 截止日期
      - 逾期天数
    page_size: 50
    editable: false
    read_only: true
    filters:
      priority:
        label: 优先级
        type: eq
      分类:
        label: 分类
        type: like
```

---

## Step 10：添加导航

编辑 `projects/task-tracker/config/layout.yml`：

```yaml
nav:
  - label: 仪表盘
    href: /task-tracker/Dashboard
    icon: 🏠

  - label: 任务
    href: /task-tracker/DynamicEntity/task
    icon: 📋

  - label: 分类
    href: /task-tracker/DynamicEntity/category
    icon: 🏷️

  - label: 评论
    href: /task-tracker/DynamicEntity/comment
    icon: 💬

  - label: 逾期任务
    href: /task-tracker/Page/OverdueTasks
    icon: ⚠️
```

---

## Step 11：启动并验证

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj
```

浏览器打开 `http://localhost:5000/task-tracker`

- 登录：`admin` / `Admin@123`
- 仪表盘、任务列表、自定义页面均正常显示

---

## 实体 YAML 完整选项参考

```yaml
entities:
  <entity_name>:
    table: <数据库表名>           # 必填
    key: <主键列名>               # 必填
    displayName: <显示名>
    softDelete: true              # true → DELETE 更新为 is_deleted=1

    paging:
      pageSize: 20
      mode: numbered              # numbered（页码）或 cursor
      enableCount: true

    confirmation:
      create: "确认消息"
      update: "确认消息"
      delete: "确认消息"

    layout:
      forms:
        columns: 2                # 表单列数（1 或 2）
        order: [field1, field2]
      filters:
        columns: 4
        order: [filter1, filter2]

    joins:
      - table: other_table
        alias: ot
        on: "main_table.fk_id = ot.id"
        type: left                # left / inner

    columns:
      <col>:
        type: string | int | decimal | boolean | date | email
        label: 显示名
        required: true
        searchable: true
        sortable: true
        identity: true            # 自增主键
        expression: "ot.col"      # 引用 JOIN 列

    forms:
      <col>:
        type: <type>
        label: 显示名
        required: true
        editable: true
        options: [val1, val2]
        foreignKey:
          entity: <entity>
          displayColumn: <col>

    filters:
      <col>:
        type: dropdown | like | range | date-range
        label: 显示名
        options: [val1, val2]
        foreignKey:
          entity: <entity>
          displayColumn: <col>

    links:
      <link_name>:
        label: 链接标签
        entity: <entity>
        filter:
          <col>: "{id}"

    hooks:
      beforeCreate: [hook1, hook2]
      afterCreate:  [hook1]
      beforeUpdate: [hook1, hook2]
      afterUpdate:  [hook1]
      beforeDelete: [hook1]
      afterDelete:  [hook1]
```

---

## 常用内置钩子

| 钩子名 | 用途 | 示例 |
|--------|------|------|
| `validate_required:f1,f2` | 必填校验 | `validate_required:title,author` |
| `validate_email:f` | 邮件格式校验 | `validate_email:email` |
| `validate_range:f:min=0,max=100` | 数值范围校验 | `validate_range:price:min=0` |
| `validate_unique:f` | 唯一性校验 | `validate_unique:name` |
| `trim:f1,f2` | 去除首尾空格 | `trim:title,body` |
| `uppercase:f` | 转大写 | `uppercase:code` |
| `lowercase:f` | 转小写 | `lowercase:email` |
| `now:f` | 自动设置当前时间 | `now:created_at` |
| `current_user:f` | 设置当前登录用户 | `current_user:author` |
| `audit_log` | 记录变更日志 | `afterCreate: [audit_log]` |

详细文档请参考 `docs/COMMON_HOOKS.md`。

---

## 自定义钩子实现模板

```csharp
public class MyHook : IEntityHook
{
    public string Name => "my_hook_name";   // YAML 中引用的名称

    // 写入数据库前执行（校验/值转换）
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // ctx.Values  : 表单输入值（可修改）
        // ctx.Entity  : 实体名称
        // ctx.Action  : Create / Update / Delete
        // ctx.RowId   : 现有记录主键（Update/Delete 时）

        if (someConditionFails)
            return Task.FromResult(HookResult.Abort("错误消息"));

        ctx.Values["my_field"] = "自动设置的值";
        return Task.FromResult(HookResult.Continue());
    }

    // 写入数据库后执行（同一事务内）
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 更新关联表、发送通知等
        return Task.CompletedTask;
    }
}
```

在 `Program.cs` 注册：

```csharp
builder.Services.AddSingleton<IEntityHook, MyHook>();
```

---

## 常见错误排查

| 错误现象 | 解决方法 |
|---------|---------|
| YAML `columns` 定义了字段但表单不显示 | 还需在 `forms` 中定义（`columns` 是列表视图，`forms` 是输入表单） |
| JOIN 列在列表中不显示 | 在 `columns` 中添加 `expression: "alias.col"` |
| 钩子不执行 | 检查 `Program.cs` 是否有 `AddSingleton<IEntityHook, XxxHook>()` |
| `softDelete: true` 仍然物理删除 | 确认数据库表有 `is_deleted` 列 |
| 自定义页面 404 | 检查文件名大小写与 URL 是否一致 |
| 过滤器不生效 | `date-range` 参数为 `key_from`/`key_to`，`range` 为 `key_min`/`key_max` |

---

## 延伸阅读

| 文档 | 内容 |
|------|------|
| `docs/COMMON_HOOKS.md` | 全部 20 种内置钩子详解 |
| `docs/examples/02-add-validation-hook.md` | 添加校验钩子的实际示例 |
| `docs/examples/05-add-custom-hook.md` | 自定义钩子实现模板 |
| `docs/architecture-map-ja.md` | 请求处理流程全景图 |
| `docs/runbook-index-ja.md` | 运维手册索引 |
| `docs/how-to-create-subproject.md` | AI 指令最佳实践模式 |
