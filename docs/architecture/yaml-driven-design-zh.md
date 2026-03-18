# YAML 驱动设计的边界与扩展性

> 本文整理了关于"YAML能做什么、不能做什么"以及"设计书能否转成YAML自动生成代码"的架构讨论。

---

## 第一部分：YAML 能否驱动更丰富的 UI 和业务逻辑？

### 现状

本项目已实现多层 YAML 驱动：

```
entities/*.yml        → CRUD 定义（表格、表单、过滤器、钩子）
pages/*.yaml          → 自定义页面（SQL + 布局 + 过滤器）
config/dashboard.yml  → 统计卡片定义
project.yaml          → 项目元数据
calendar_ui: ...      → 日历 UI 组件设置
```

### YAML 擅长的领域

```yaml
# ✅ 数据结构与显示定义
columns: [id, name, created_at]
filters:
  status: {type: eq}

# ✅ 布局与样式设置
ui:
  page:
    layout: stack
    density: comfortable

# ✅ 只读查询（报表、仪表盘）
source: |
  SELECT ... FROM ... WHERE ...

# ✅ 静态配置值
calendar_ui:
  show_japan_holidays: true
  mobile_month_count: 1
```

### YAML 不擅长的领域（应放到代码中）

```yaml
# ❌ 复杂的条件分支逻辑
# YAML 不是编程语言，一旦开始这样写就会陷入"自定义 DSL 地狱"
on_submit:
  if: "quantity > stock"
  then: abort
  else: proceed

# ❌ 多步骤工作流（审批→通知→更新库存→写日志）
workflow:
  step1: validate
  step2: notify_email
  step3: update_stock

# ❌ 实时 UI（拖拽、WebSocket）
# ❌ 外部 API 集成
```

### 架构决策矩阵

```
                   应该用 YAML               应该用代码
                 ┌──────────────────┐        ┌──────────────────┐
 数据定义        │ ✅ 表结构         │        │                  │
 显示规则        │ ✅ 列、过滤器     │        │                  │
 布局            │ ✅ 页面构成       │        │                  │
 只读 SQL        │ ✅ 报表查询       │        │                  │
 校验            │ ✅ 简单必填/类型  │ ←边界→ │ ✅ 复杂业务规则   │
 业务逻辑        │                  │        │ ✅ IEntityHook   │
 外部集成        │                  │        │ ✅ 服务类         │
 条件流程        │                  │        │ ✅ C# 代码        │
                 └──────────────────┘        └──────────────────┘
```

### 核心结论

> 当前架构的 YAML/代码分工是合理的。
> 比起继续扩大 YAML 化范围，更重要的是守住**"YAML 写结构、代码写逻辑"**这条边界。

---

## 第二部分：n8n 等工作流系统如何存储逻辑？YAML 为何不适用？

### n8n 的内部表示：图（DAG）存为 JSON

```json
{
  "nodes": [
    {
      "id": "node-1",
      "type": "n8n-nodes-base.httpRequest",
      "name": "调用 API",
      "parameters": {
        "url": "https://api.example.com/orders",
        "method": "POST"
      }
    },
    {
      "id": "node-2",
      "type": "n8n-nodes-base.if",
      "name": "判断状态",
      "parameters": {
        "conditions": {
          "string": [{"value1": "={{$json.status}}", "operation": "equal", "value2": "success"}]
        }
      }
    }
  ],
  "connections": {
    "调用 API": {
      "main": [[{"node": "判断状态", "index": 0}]]
    },
    "判断状态": {
      "main": [
        [{"node": "成功处理器", "index": 0}],
        [{"node": "错误重试",   "index": 0}]
      ]
    }
  }
}
```

**关键点：** 数据结构是**图（邻接表）**，而不是 YAML 擅长的**树**。

### YAML 为何难以表达复杂工作流

#### 结构差异

```
YAML/JSON 擅长的形态:       工作流需要的形态:
      root                       A
     /    \                    / | \
    A      B                  B  C  D
   / \      \                  \ | /
  C   D      E                   E
                                 |
                            （也可以有回边）
                                 F

  ↑ 树结构（一个父节点）       ↑ DAG（多个父节点、多路输出分支）
```

#### 具体困难

**1. 多父节点（Fan-in）难以表达**

```yaml
# 怎么表达"等待 A 和 B 都完成后才执行 D"？
steps:
  - name: A
    next: [C, D]
  - name: B
    next: [D]
  - name: D
    wait_for: [A, B]   # 这个语义需要运行时额外解释
```

**2. 循环与重试**

```yaml
# YAML 是树结构，后面的节点无法引用前面的
steps:
  - name: fetch
    on_error:
      retry: 3
      back_to: fetch   # 自引用/循环 → YAML anchor 也难以表达
```

**3. 表达式语言必须内嵌**

```yaml
# 不得不在 YAML 值中嵌入另一种语言
condition: "={{$json.orders[0].status === 'shipped' && $now.diff($json.created_at, 'days') > 7}}"
# 这已经是 JavaScript，不是 YAML
```

### 各系统的选择与理由

| 系统 | 存储格式 | 原因 |
|------|---------|------|
| **n8n** | JSON（图结构） | UI 生成，人不手写 |
| **Airflow** | Python 代码 | 逻辑用代码表达，类型安全 |
| **Temporal** | 代码（Go/Java/TS） | 执行状态以事件日志持久化 |
| **Step Functions** | JSON (Amazon States Language) | 形式化为状态机 |
| **GitHub Actions** | YAML | 仅限简单线性/并行步骤，无复杂分支 |

### 核心结论

> 工作流本质上是**有状态语义的图**，而 YAML 是**层次数据的序列化格式**。
> 用 YAML 表达工作流，最终会发明一门新的 DSL（领域专用语言），
> 不如直接用代码（Airflow）或可视化编辑器（n8n）。

---

## 第三部分：程序设计书能否转成 YAML 自动生成代码？

### 现实中已有的成功案例

#### OpenAPI (Swagger) → 代码生成

```yaml
openapi: 3.0.0
paths:
  /orders:
    post:
      requestBody:
        content:
          application/json:
            schema:
              properties:
                customerId: {type: integer}
                amount:     {type: number}
```

```bash
openapi-generator generate -i api.yaml -g csharp-aspnetcore
# → Controller.cs, Model.cs, IApi.cs 全部自动生成
```

#### Prisma（DB 设计书 → ORM + 迁移）

```prisma
model Order {
  id         Int      @id @default(autoincrement())
  customerId Int
  amount     Decimal
  status     String   @default("pending")
  createdAt  DateTime @default(now())
}
```

```bash
prisma generate    # → 类型安全的 ORM 客户端
prisma migrate dev # → SQL 迁移脚本
```

#### 本项目本身就在做这件事

```yaml
# entities/order.yml = 就是设计书本身
entities:
  Order:
    table: orders
    columns:
      customerId: {type: integer, displayName: "客户ID"}
      amount:     {type: decimal, displayName: "金额"}
      status:     {type: enum, values: [pending, shipped, done]}
    forms:
      create: {fields: [customerId, amount]}
    hooks:
      beforeCreate: [validate_amount, set_default_status]
```

→ 列表页面、表单、CRUD API、SQL、校验 全部自动生成

### 可转化程度分析

```
设计书内容                  可转化程度    方式
────────────────────────────────────────────────────
实体/数据模型定义            ✅ 高        entities/*.yml
数据库 Schema               ✅ 高        Prisma / Flyway
API 端点定义                ✅ 高        OpenAPI
页面布局与表单               ✅ 高        本项目
简单校验规则                 ✅ 高        hooks: [validate_email]
权限与角色定义               ✅ 高        RBAC YAML
报表与聚合查询               ✅ 中        pages/*.yaml
线性工作流                   ✅ 中        类 GitHub Actions 格式
简单状态机                   ✅ 中        状态机 YAML
────────────────────────────────────────────────────
复杂业务逻辑                 ⚠️ 低       需要写代码
外部 API 集成详情            ⚠️ 低       需要写代码
复杂算法/计算                ❌ 不可     必须写代码
动态 UI（SPA 级别）          ❌ 不可     需要前端代码
```

### 现实的管道设计

```
程序设计书（中文/日文自然语言）
       │
       │ AI（Claude 等）辅助转换
       ▼
  YAML 文件群
  ├── entities/*.yml      （数据模型）
  ├── pages/*.yaml        （页面定义）
  ├── openapi.yaml        （API 规格）
  └── workflows/*.yaml    （业务流程）
       │
       │ 框架自动处理
       ▼
  ┌──────────────────────────────────┐
  │  自动生成的产物                   │
  │  · CRUD 页面与 API               │
  │  · DB Schema 与迁移              │
  │  · 校验逻辑                      │
  │  · 测试脚手架                    │
  └──────────────────────────────────┘
       │
       │ 剩余 20% 由工程师实现
       ▼
  IEntityHook（C# 代码）
  自定义服务
  外部 API 集成
```

### 三个现实局限

#### 局限 1：设计书本身的模糊性

```
设计书写的:
  "库存不足时返回错误"

转成 YAML 需要明确:
  - 与什么比较？(stock_quantity > 0?)
  - 错误信息是什么？
  - 部分有库存怎么处理？
  - 多仓库合计还是单仓？

→ YAML 化要求设计书本身足够精确
```

#### 局限 2：转化精度（AI 也无法做到 100%）

```
设计书 → YAML 的转化率:
  结构性内容（表、页面、API）:    90%+ 可自动化
  业务逻辑部分:                  40–60% 可自动化
  错误处理与边界情况:             20–30% 可自动化
```

#### 局限 3：YAML 是"配置"，代码是"逻辑"

```
YAML 能做的:
  声明"创建订单时执行库存检查钩子"

YAML 不能做的:
  库存检查的具体逻辑本身
       ↓
  最终还是需要写 IEntityHook 的 C# 实现
```

### 在本项目中的实践

设计书 → AI 生成 YAML → 脚手架命令 这套流程已经可以运转：

```bash
# 1. 把设计书给 AI → AI 生成 YAML
# 2. 从现有 DB 生成实体 YAML
dotnet run -- --scaffold-entities --project=myproject

# 3. 生成钩子脚手架（含测试文件）
dotnet run -- --scaffold-hook --name=ValidateStock --project=myproject --with-tests

# → 只需填写 ValidateStockHook.cs 的业务逻辑即可
```

### 核心结论

> **现实可达的目标：** 从设计书出发，80% 的产物通过 YAML + 自动生成完成，
> 剩余 20% 的逻辑由工程师编写——这与 Salesforce 的配置化开发思路相同。
>
> YAML 化是"降低重复劳动"的利器，但不是"消灭工程师"的银弹。

---

## 总结对比

| 维度 | YAML 驱动 | 代码实现 |
|------|----------|---------|
| 适合对象 | 结构、配置、元数据 | 逻辑、算法、集成 |
| 修改成本 | 低（无需重新编译） | 高（需编译、测试） |
| 表达能力 | 有限（树/平铺结构） | 无限 |
| 可测试性 | 有限 | 强（单元测试） |
| AI 生成难度 | 低（结构化输出） | 中（需要上下文） |
| 调试难度 | 高（运行时错误） | 低（编译器辅助） |

**最优策略：** 能用 YAML 的用 YAML，不能用 YAML 的用 `IEntityHook` + 服务类，两者之间用钩子名称（字符串）解耦。
