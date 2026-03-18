# Tutorial: Creating a Subproject in NetYamlForge

This tutorial walks you through building a **Task Management System (task-tracker)** from scratch using the NetYamlForge framework CLI. It covers all major features step by step.

---

## What You Will Build

| Feature | Description |
|---------|-------------|
| Entities | Category / Task / Comment |
| Dashboard | 4 stat cards + 2 charts |
| Custom page | Overdue tasks list |
| Built-in hooks | Validation and auto-timestamps |
| Custom hook | Auto-set task completion time |

---

## Framework Structure

A subproject lives under `projects/<name>/` and is configured entirely through YAML files. The framework reads these files at startup and generates a full CRUD admin panel — no hand-written controllers or views required.

```
projects/<name>/
├── project.yaml          # Project settings
├── config/
│   ├── dashboard.yml     # Dashboard stats and charts
│   ├── layout.yml        # Navigation menu
│   └── i18n.yml          # Multilingual labels
├── entities/             # Entity YAML definitions
├── pages/                # Custom page YAML
├── Hooks/                # Project-specific hook classes
├── database/             # SQLite DB file
└── views/                # Project-specific views
```

---

## Step 1: Initialize the Project

Run the `--init-project` command to scaffold the directory structure and generate `project.yaml`.

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj -- \
  --init-project \
  --project=task-tracker \
  --display-name="Task Manager" \
  --db-type=sqlite \
  --db-path=database/task-tracker.db
```

The following directory structure is created automatically:

```
projects/task-tracker/
├── project.yaml
├── config/
│   ├── dashboard.yml
│   ├── layout.yml
│   └── i18n.yml
├── entities/
├── pages/
├── Hooks/
├── database/
└── views/
```

### Review the generated `project.yaml`

```yaml
name: task-tracker
displayName: Task Manager
version: "1.0.0"
database:
  type: sqlite
  path: database/task-tracker.db
features:
  multiLanguage: false
  userAuthentication: true
```

---

## Step 2: Create the Database

Create `projects/task-tracker/database/init.sql` with your table definitions:

```sql
-- Categories
CREATE TABLE IF NOT EXISTS category (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT    NOT NULL,
    color       TEXT    DEFAULT '#6c757d',
    created_at  TEXT    DEFAULT (datetime('now','localtime'))
);

-- Tasks
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

-- Comments
CREATE TABLE IF NOT EXISTS comment (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    task_id    INTEGER NOT NULL REFERENCES task(id),
    body       TEXT    NOT NULL,
    author     TEXT    NOT NULL,
    created_at TEXT    DEFAULT (datetime('now','localtime'))
);

-- Sample data
INSERT INTO category(name, color) VALUES ('Development', '#0d6efd'), ('Design', '#6f42c1'), ('Operations', '#dc3545');
INSERT INTO task(title, status, priority, category_id, due_date)
  VALUES ('Implement top page', 'in_progress', 'high', 1, date('now', '+3 days')),
         ('Logo design', 'todo', 'medium', 2, date('now', '+7 days')),
         ('Server configuration', 'done', 'high', 3, date('now', '-1 day'));
```

Initialize the SQLite database using the SQLite CLI:

```bash
sqlite3 projects/task-tracker/database/task-tracker.db < projects/task-tracker/database/init.sql
```

---

## Step 3: Scaffold Entities

With the database in place, run the scaffold command to auto-generate YAML skeletons from the table schema:

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj -- \
  --scaffold-entities \
  --project=task-tracker
```

This generates `category.yml`, `task.yml`, and `comment.yml` under `projects/task-tracker/entities/`. The following steps walk through editing each file.

---

## Step 4: Define Entity YAML

Edit each generated file to configure columns, forms, filters, and hooks.

### `entities/category.yml`

```yaml
entities:
  category:
    table: category
    key: id
    displayName: Category

    paging:
      pageSize: 20
      mode: numbered

    layout:
      forms:
        columns: 1
        order: [name, color]

    columns:
      id:         { type: int,    identity: true, label: ID,         sortable: true }
      name:       { type: string, required: true,  label: Name,      searchable: true, sortable: true }
      color:      { type: string, label: Color }
      created_at: { type: string, label: Created At, sortable: true }

    forms:
      name:  { type: string, required: true, label: Category Name, editable: true }
      color: { type: string, label: "Color code (e.g. #0d6efd)", editable: true }

    filters: {}

    links:
      tasks:
        label: Tasks
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

### `entities/task.yml` (with JOIN, foreign key, soft delete)

```yaml
entities:
  task:
    table: task
    key: id
    displayName: Task
    softDelete: true    # DELETE updates is_deleted=1 instead of removing the row

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
      delete: "Are you sure you want to delete this task?"

    joins:
      - table: category
        alias: cat
        on: "task.category_id = cat.id"
        type: left

    columns:
      id:           { type: int,    identity: true, label: ID,           sortable: true }
      title:        { type: string, required: true,  label: Title,       searchable: true, sortable: true }
      status:       { type: string, label: Status,   sortable: true }
      priority:     { type: string, label: Priority, sortable: true }
      category_name:
        type: string
        expression: "cat.name"     # Reference a JOINed column via expression
        label: Category
        sortable: true
      due_date:     { type: string, label: Due Date,      sortable: true }
      completed_at: { type: string, label: Completed At,  sortable: true }
      created_at:   { type: string, label: Created At,    sortable: true }

    forms:
      title:
        type: string
        required: true
        label: Title
        editable: true
      description:
        type: string
        label: Description
        editable: true
      status:
        type: string
        label: Status
        editable: true
        options: [todo, in_progress, done]
      priority:
        type: string
        label: Priority
        editable: true
        options: [low, medium, high]
      category_id:
        type: int
        label: Category
        editable: true
        foreignKey:
          entity: category
          displayColumn: name       # Show category.name in the dropdown
      due_date:
        type: date
        label: Due Date
        editable: true
      completed_at:
        type: string
        label: Completed At
        editable: false             # Not editable in forms; set automatically by hook
      created_at:
        type: string
        label: Created At
        editable: false

    filters:
      status:
        type: dropdown
        label: Status
        options: [todo, in_progress, done]
      priority:
        type: dropdown
        label: Priority
        options: [low, medium, high]
      category_id:
        type: dropdown
        label: Category
        foreignKey:
          entity: category
          displayColumn: name
      due_date:
        type: date-range            # Date range filter (_from / _to)
        label: Due Date

    links:
      comments:
        label: Comments
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
        - task_complete_timestamp    # Custom hook implemented in Step 6
```

### `entities/comment.yml`

```yaml
entities:
  comment:
    table: comment
    key: id
    displayName: Comment

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
      id:         { type: int,    identity: true, label: ID,         sortable: true }
      task_title:
        type: string
        expression: "t.title"
        label: Task
        sortable: true
      author:     { type: string, label: Author,  searchable: true, sortable: true }
      body:       { type: string, label: Body,    searchable: true }
      created_at: { type: string, label: Posted At, sortable: true }

    forms:
      task_id:
        type: int
        label: Task
        editable: true
        foreignKey:
          entity: task
          displayColumn: title
      author:
        type: string
        required: true
        label: Author
        editable: true
      body:
        type: string
        required: true
        label: Body
        editable: true

    filters:
      author:
        type: like
        label: Author

    hooks:
      beforeCreate:
        - validate_required:author,body
        - trim:author,body
        - now:created_at
```

---

## Step 5: Configure the Dashboard

Edit `projects/task-tracker/config/dashboard.yml`:

```yaml
# Stat cards
stats:
  - label: Total Tasks
    entity: task
    aggregate: count
    icon: 📋
    color: badge-primary

  - label: Todo
    entity: task
    aggregate: count
    filter: "status = 'todo'"
    icon: 🔵
    color: badge-info

  - label: In Progress
    entity: task
    aggregate: count
    filter: "status = 'in_progress'"
    icon: 🟡
    color: badge-warning

  - label: Done
    entity: task
    aggregate: count
    filter: "status = 'done'"
    icon: ✅
    color: badge-success

# Charts
charts:
  # Tasks by priority (doughnut)
  - title: Tasks by Priority
    type: doughnut
    entity: task
    valueAggregate: count
    groupExpression: priority
    orderBy: value
    orderDir: desc

  # Tasks by category (bar chart)
  - title: Tasks by Category
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

### Dashboard configuration options

| Key | Description |
|-----|-------------|
| `aggregate` | `count` / `sum` / `avg` |
| `filter` | SQL condition appended to the WHERE clause (only validated identifiers allowed) |
| `type` | `bar` / `doughnut` / `pie` / `line` |
| `groupExpression` | GROUP BY expression (supports aliased JOIN columns) |
| `joinClause` | JOIN clause for charts |
| `valueColumn` | Column to aggregate when using `sum` or `avg` |

---

## Step 6: Add Custom Hooks

Use the CLI to scaffold a hook class and its test file:

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj -- \
  --scaffold-hook \
  --name=TaskCompleteTimestamp \
  --project=task-tracker \
  --with-tests
```

Generated files:

```
projects/task-tracker/Hooks/TaskCompleteTimestampHook.cs
NetYamlForge.Tests/Hooks/TaskCompleteTimestampHookTests.cs
```

### Implement the hook

Edit `projects/task-tracker/Hooks/TaskCompleteTimestampHook.cs`:

```csharp
using System.Data;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.TaskTracker.Hooks;

/// <summary>
/// Automatically sets completed_at when a task's status is changed to "done".
///
/// Usage in entities/task.yml:
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
            // Set completion time only if not already set
            if (!ctx.Values.TryGetValue("completed_at", out var existing) ||
                existing == null || string.IsNullOrWhiteSpace(existing.ToString()))
            {
                ctx.Values["completed_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
        }
        else
        {
            // Clear completion time if status is changed back from "done"
            ctx.Values["completed_at"] = null;
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
```

### Register the hook in `Program.cs`

Add the following line to the DI registration section in `NetYamlForge/Program.cs`:

```csharp
// projects/task-tracker/Hooks
builder.Services.AddSingleton<IEntityHook, TaskCompleteTimestampHook>();
```

> **Note**: The hook name (the `Name` property) is matched case-insensitively against the name listed in the YAML `hooks` section.

---

## Step 7: Create Custom Pages

Create `projects/task-tracker/pages/OverdueTasks.yml`:

```yaml
title: Overdue Tasks
description: Tasks past their due date that have not been completed.

ui:
  page:
    layout: stack
    density: comfortable

sections:
  - id: overdue_tasks
    title: Overdue Tasks
    source_type: custom
    source: |
      SELECT
        t.id        AS TaskId,
        t.title     AS Title,
        t.status    AS Status,
        t.priority  AS Priority,
        cat.name    AS Category,
        t.due_date  AS DueDate,
        CAST(julianday('now') - julianday(t.due_date) AS INTEGER) AS DaysOverdue
      FROM task t
      LEFT JOIN category cat ON cat.id = t.category_id
      WHERE t.due_date < date('now','localtime')
        AND t.status != 'done'
        AND t.is_deleted = 0
    columns:
      - TaskId
      - Title
      - Status
      - Priority
      - Category
      - DueDate
      - DaysOverdue
    page_size: 50
    editable: false
    read_only: true
    filters:
      Priority:
        label: Priority
        type: eq
      Category:
        label: Category
        type: like
```

### Custom page configuration options

| Key | Description |
|-----|-------------|
| `source_type` | `custom` (arbitrary SQL) or `table` (direct table reference) |
| `source` | Custom SQL query (when `source_type: custom`) |
| `editable` | Set to `true` to allow row editing |
| `updatable_fields` | Restrict which fields can be edited |
| `page_size` | Rows per page |
| `filters` | Page-level filters (`like` / `eq` / `range` / `date-range` / `gte` / `lte`) |

---

## Step 8: Update Navigation

Edit `projects/task-tracker/config/layout.yml` to add menu links:

```yaml
nav:
  - label: Dashboard
    href: /task-tracker/Dashboard
    icon: 🏠

  - label: Tasks
    href: /task-tracker/DynamicEntity/task
    icon: 📋

  - label: Categories
    href: /task-tracker/DynamicEntity/category
    icon: 🏷️

  - label: Comments
    href: /task-tracker/DynamicEntity/comment
    icon: 💬

  - label: Overdue Tasks
    href: /task-tracker/Page/OverdueTasks
    icon: ⚠️
```

---

## Step 9: Run and Verify

Start the application:

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj
```

Open `http://localhost:5000/task-tracker` in your browser.

- Default login: `admin` / `Admin@123`
- Verify that the dashboard, task list, category list, and the custom Overdue Tasks page all appear correctly
- Create a task, set its status to `done`, and confirm that `completed_at` is set automatically by the custom hook

---

## Reference: Entity YAML Full Options

```yaml
entities:
  <entity_name>:
    table: <DB table name>        # Required
    key: <primary key column>     # Required
    displayName: <display name>
    softDelete: true              # true → DELETE updates is_deleted=1 instead of removing the row

    paging:
      pageSize: 20                # Rows per page
      mode: numbered              # numbered (page numbers) or cursor
      enableCount: true           # Fetch total record count

    confirmation:                 # Confirmation dialogs (optional)
      create: "Confirmation message"
      update: "Confirmation message"
      delete: "Confirmation message"

    layout:
      forms:
        columns: 2                # Form columns (1 or 2)
        order: [field1, field2]   # Display order
      filters:
        columns: 4
        order: [filter1, filter2]

    joins:                        # Table joins
      - table: other_table
        alias: ot
        on: "main_table.fk_id = ot.id"
        type: left                # left / inner

    columns:                      # List view columns
      <col>:
        type: string | int | decimal | boolean | date | email
        label: Display name
        required: true
        searchable: true          # Include in full-text search
        sortable: true
        identity: true            # Auto-increment primary key
        expression: "ot.col"      # Reference a JOINed column

    forms:                        # Create / edit form fields
      <col>:
        type: <type>
        label: Display name
        required: true
        editable: true
        options: [val1, val2]     # Dropdown choices
        foreignKey:
          entity: <entity>
          displayColumn: <col>    # Column to display from referenced entity

    filters:                      # Filter UI
      <col>:
        type: dropdown | like | range | date-range
        label: Display name
        options: [val1, val2]
        foreignKey:
          entity: <entity>
          displayColumn: <col>

    links:                        # Related links on detail pages
      <link_name>:
        label: Link label
        entity: <entity>
        filter:
          <col>: "{id}"           # {id} is replaced with the current record's primary key

    hooks:
      beforeCreate: [hook1, hook2]
      afterCreate:  [hook1]
      beforeUpdate: [hook1, hook2]
      afterUpdate:  [hook1]
      beforeDelete: [hook1]
      afterDelete:  [hook1]
```

---

## Reference: Built-in Hooks

| Hook name | Purpose | Example |
|-----------|---------|---------|
| `validate_required:f1,f2` | Required field check | `validate_required:title,author` |
| `validate_email:f` | Email format check | `validate_email:email` |
| `validate_range:f:min=0,max=100` | Numeric range check | `validate_range:price:min=0` |
| `validate_unique:f` | Duplicate check | `validate_unique:name` |
| `trim:f1,f2` | Strip leading/trailing whitespace | `trim:title,body` |
| `uppercase:f` | Convert to uppercase | `uppercase:code` |
| `lowercase:f` | Convert to lowercase | `lowercase:email` |
| `now:f` | Set current timestamp automatically | `now:created_at` |
| `current_user:f` | Set the logged-in user | `current_user:author` |
| `audit_log` | Record change log | `afterCreate: [audit_log]` |

For the full list, see `docs/COMMON_HOOKS.md`.

---

## Common Pitfalls

| Mistake | Correct approach |
|---------|-----------------|
| Defined a field in `columns` but it does not appear in the form | You must also define it in `forms` — `columns` controls the list view; `forms` controls the input form |
| A JOINed column does not appear in the list | Specify `expression: "alias.col"` in the `columns` definition |
| A hook does not run | Check that `AddSingleton<IEntityHook, XxxHook>()` has been added to `Program.cs` |
| `softDelete: true` is set but rows are physically deleted | Verify that the `is_deleted` column exists in the database table |
| A custom page returns 404 | Check that the filename (including case) matches the URL exactly |
| A filter has no effect | For `date-range`, parameters are `key_from`/`key_to`; for `range`, they are `key_min`/`key_max` |

---

## Next Steps

| Document | Description |
|----------|-------------|
| `docs/COMMON_HOOKS.md` | Details for all 20 built-in hooks |
| `docs/examples/02-add-validation-hook.md` | Worked example: adding validation |
| `docs/examples/05-add-custom-hook.md` | Custom hook implementation template |
| `docs/architecture-map-ja.md` | Full request processing flow diagram |
| `docs/runbook-index-ja.md` | Operations runbook index |
| `docs/how-to-create-subproject.md` | AI instruction patterns for creating subprojects |
| `docs/tutorial-create-project-ja.md` | Japanese version of this tutorial |
