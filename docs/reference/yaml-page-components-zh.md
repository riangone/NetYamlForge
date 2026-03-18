# pages/*.yaml 能否通过 UI 组件实现完整页面定义？

> 本文分析当前 pages/*.yaml 的能力边界，以及引入组件类型系统后可以达到的范围。

---

## 现状的准确梳理

`pages/*.yaml` 已经在运行，但复杂 UI 需要用 `template:` 逃出去：

```
pages/*.yaml
  ↓
PageController.cs
  ↓
无 template: → PageView.cshtml（通用表格渲染）
有 template: → projects/{name}/views/{template}.cshtml（需手写实现）
```

```yaml
# CalendarWorkbench.yaml → template: CalendarWorkbench → 需要对应的 .cshtml
template: CalendarWorkbench

# PipelineBoard.yaml → 无 template → 用通用表格渲染
# （只能输出表格）
```

**当前限制：通用渲染只支持表格。** `PageView.cshtml` 只渲染 `<table>`，没有其他组件。

---

## 引入组件类型系统后的变化

### 当前 YAML Schema（能做的）

```yaml
sections:
  - id: sales_kpi
    source: SELECT ...
    columns: [...]
    # → 只能渲染为表格
```

### 扩展后的 Schema（增加 component 字段）

```yaml
sections:
  - id: sales_kpi
    component: stat_cards        # KPI 卡片
    source: SELECT metric, value FROM ...
    value_field: value
    label_field: metric

  - id: monthly_trend
    component: line_chart        # 折线图
    source: SELECT month, revenue FROM ...
    x_field: month
    y_field: revenue

  - id: order_board
    component: kanban            # 看板
    source: SELECT id, title, status FROM orders
    group_by: status
    card_title: title

  - id: staff_list
    component: table             # 传统表格（现有）
    source: SELECT ...
    columns: [...]

  - id: detail_form
    component: detail_card       # 详情卡片（单条记录展示）
    source: SELECT * FROM customer WHERE id = @id
    fields: [name, email, phone]
```

### 实现所需的新增内容

```
现状                              扩展后
──────────────────────────────────────────────────
PageView.cshtml                  PageView.cshtml（按组件类型分发）
（只有表格）                            ↓
                              _StatCards.cshtml
                              _LineChart.cshtml
                              _KanbanBoard.cshtml
                              _DetailCard.cshtml
                              _Table.cshtml（现有）
```

---

## 各 UI 组件的可行性分析

```
UI 组件                          YAML 定义可行性
──────────────────────────────────────────────────────
表格（现有）                      ✅ 已实现
KPI 卡片 / stat_cards             ✅ dashboard 已有，可移植
柱状图 / 折线图                   ✅ dashboard 已有，可移植
标签页切换                        ✅ 将 sections 渲染为 tab
详情卡片（单条记录）              ✅ component: detail_card 可实现
带过滤器的表格                    ✅ 已实现
日历（纯展示）                    ✅ component: calendar + 数据注入
时间线                            ✅ component: timeline 可实现
──────────────────────────────────────────────────────
看板（拖拽移动）                  ⚠️ 展示可 YAML，保存拖拽结果需 hook
树形视图（展开/折叠）             ⚠️ 递归结构在 YAML 中表达复杂
地图（地理坐标展示）              ⚠️ component: map 可定义，需坐标数据
内联编辑表格                      ⚠️ update-row API 已有，UI 定义可行
──────────────────────────────────────────────────────
向导表单（多步骤）                ❌ 状态管理难以用 YAML 表达
实时更新（WebSocket）             ❌ YAML 无法表达
高级图表（复合坐标轴等）          ❌ 配置项会爆炸性增长
自定义像素级布局                  ❌ 用 YAML 写 CSS 是本末倒置
```

---

## 与 Retool / AppSmith 的对比

这正是现有低代码工具的核心思路：

```
Retool / AppSmith / Budibase
  ↓ 做的事情
将组件类型 + 属性保存为 JSON/YAML
  → 表格、图表、表单、按钮均通过类 YAML 格式定义
  → 可视化编辑器在背后生成该数据

与本项目的区别：
  Retool      → 可视化编辑器生成 JSON/YAML
  本项目       → 人工手写 YAML 或由 AI 生成
  本项目优势   → AI 直接从设计书生成 YAML，无需拖拽操作
```

---

## 实际扩展所需的改动

**只需改 3 处，Controller 完全不用动：**

### 1. YAML Schema 增加 component 字段

```csharp
// Models/PageDefinition.cs
public class SectionDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string ComponentType { get; set; } = "table"; // ← 新增，默认 table
    public string? XField { get; set; }                  // ← 图表用
    public string? YField { get; set; }
    public string? GroupBy { get; set; }                 // ← 看板用
    public string? CardTitle { get; set; }
    // ...现有字段
}
```

### 2. PageView.cshtml 增加组件分发

```csharp
@foreach (var sec in pageDef.Sections)
{
    @switch (sec.ComponentType)
    {
        case "stat_cards":
            <partial name="_StatCards" model="sectionData" />
            break;
        case "line_chart":
            <partial name="_LineChart" model="sectionData" />
            break;
        case "kanban":
            <partial name="_KanbanBoard" model="sectionData" />
            break;
        default:
            <partial name="_Table" model="sectionData" />
            break;
    }
}
```

### 3. 实现各组件的 Partial View

```
Views/Page/
├── PageView.cshtml        （现有，加分发逻辑）
├── _Table.cshtml          （从现有提取）
├── _StatCards.cshtml      （新增）
├── _LineChart.cshtml      （新增，复用 Chart.js）
├── _KanbanBoard.cshtml    （新增）
└── _DetailCard.cshtml     （新增）
```

---

## 结论

| 维度 | 判断 |
|------|------|
| 技术上是否可行 | ✅ 可行，架构已经为此准备好 |
| 能否完全废除 `template:` | ⚠️ 80% 的情况可以废除，剩余 20% 的高度定制 UI 仍需保留 |
| 实现成本 | 中等（需逐一实现各组件的 Partial View） |
| 与现有 Retool 等工具的差异 | "AI 直接从设计书生成 YAML" 是核心差异化优势 |

**推荐策略：** 不要完全废除 `template:`，而是将其保留为"高度定制 UI 的最后手段"，同时逐步引入组件类型系统，按需扩展组件库。

---

## 延伸阅读

- `docs/yaml-driven-design-zh.md` — YAML 驱动设计的边界与扩展性
- `Services/PageDataQueryService.cs` — 页面数据查询服务
- `Views/Page/PageView.cshtml` — 当前通用页面渲染器
- `Controllers/PageController.cs` — 页面控制器
