# R2-04 — 性能基线：编译访问器缓存 + BenchmarkDotNet

> 范围: `SqlExpressionParser` / `SqlExpression/*` / DynamicEntity 反射热路径 / Connection 池 / 新增 benchmark 工程
> 类型: 性能（数据驱动） · 风险: 中（缓存引入需保证正确性） · 依赖: 无
> **行为变化: 局部** — 引入缓存（结果等价、仅加速）；benchmark 工程为纯新增，不影响运行时。

## 1. 现状（实测）

- **无 BenchmarkDotNet**，无 benchmark 工程——性能决策目前靠"感觉"，缺基线。
- 动态实体大量走**反射 / 表达式**：`SqlExpressionParser`（第一轮已拆到 `Services/SqlExpression/`）每次解析可能重复构建。
- 路线图里悬而未决的一条："**自定义连接池 vs 驱动内置池**"——一直没有数据支撑。

## 2. 目标

**先量后改**：先建立可复现的 benchmark 基线，再针对性做缓存/优化，用数据而非直觉决策。

1. 新增 `NetYamlForge.Benchmarks` 工程（BenchmarkDotNet），固化关键热路径基线。
2. 对可缓存的编译产物（表达式访问器、解析结果）加缓存，benchmark 前后对比。
3. 用 benchmark 数据结论化"连接池"路线图那条决策。

## 3. 设计

### 3.1 Benchmark 工程
新增 `NetYamlForge.Benchmarks/`（控制台 + BenchmarkDotNet），引用主工程。基准场景：

| Benchmark | 目标 | 关注指标 |
|-----------|------|----------|
| `SqlExpressionParseBench` | 解析典型 filter/expression（冷/热） | ns/op、Gen0 分配 |
| `EntityAccessorBench` | 反射读写 vs 编译委托读写 实体字段 | ns/op、分配 |
| `EntityQueryBuildBench` | 从 YAML 元数据构建查询（不含 DB IO） | ns/op |
| `ConnectionPoolBench` | 自定义池 vs 驱动内置池（取/还连接） | ns/op、吞吐、竞争下延迟 |

- 用 `[MemoryDiagnoser]`，输出 markdown 存 `docs/refactor-2026-07-r2/benchmarks/`（带机器信息与日期）。
- **不进 CI 主门禁**（benchmark 噪声大）；提供 `dotnet run -c Release --project NetYamlForge.Benchmarks` 手动触发说明。

### 3.2 编译访问器缓存
- **表达式/解析缓存**：`SqlExpressionParser` 拆分后（第一轮 04），对"相同表达式字符串 → 解析结果/编译委托"加 `ConcurrentDictionary` 缓存。**key 必须完整反映影响输出的因素**（表达式文本 + 方言 + 参数化上下文），否则缓存命中会串味。
- **实体字段访问器**：DynamicEntity 反射读写热点改为 `Expression`/`Delegate` 编译并按 `(Type, PropertyName)` 缓存（一次编译多次复用）。
- 缓存需**有界**：用容量上限 + 简单淘汰（或 `MemoryCache`），避免恶意/超多动态 YAML 导致无界增长。

### 3.3 缓存正确性护栏
- 每个缓存点必须有单测证明：**缓存命中结果 == 无缓存直算结果**（用两条路径对拍）。
- 方言相关缓存必须把方言纳入 key（复用第一轮 08 的方言契约测试 fixture 交叉验证）。

### 3.4 连接池决策
- 用 `ConnectionPoolBench` 在**并发取/还**场景下对比自定义池与驱动内置池。
- 产出一页结论文档 `benchmarks/connection-pool-decision.md`：数据 + 建议（保留自定义池 / 迁回内置池 / 混合），供路线图拍板。**本项只给数据与建议，不强制改架构**。

## 4. 落地顺序（分 PR）

1. **PR-1**：`NetYamlForge.Benchmarks` 工程 + `SqlExpressionParseBench` + `EntityAccessorBench`，跑出**改造前基线**并存档。
2. **PR-2**：实体字段访问器缓存 + 正确性对拍单测；benchmark 对比，附加速数据。
3. **PR-3**：`SqlExpressionParser` 解析/编译缓存 + 单测 + benchmark 对比。
4. **PR-4**：`ConnectionPoolBench` + 决策文档（纯调研，不改运行时）。

## 5. 边界与风险

- **缓存串味是头号风险**：key 不完整会导致跨方言/跨上下文返回错误结果。所有缓存必须过"对拍单测"。
- **基线可复现性**：benchmark 结果随机器波动，存档需记录 CPU/OS/.NET 版本；跨 PR 比较尽量同机。
- **过早优化**：没有 benchmark 数据前**不动**任何热路径；PR-1 的基线是后续所有优化的前提。
- **有界缓存**：动态 YAML 场景下缓存 key 空间可能很大，必须设上限，防内存泄漏。

## 6. 验收标准

- [ ] `NetYamlForge.Benchmarks` 工程可 `dotnet run -c Release` 复现，输出 markdown 存档（含机器信息）
- [ ] 关键热路径有"改造前基线"存档，缓存 PR 附前后对比数据
- [ ] 每个缓存点有"命中==直算"对拍单测；方言相关缓存 key 含方言
- [ ] 缓存有容量上限，无无界增长风险
- [ ] `connection-pool-decision.md` 给出数据支撑的明确建议
- [ ] benchmark 工程不进 CI 主门禁；现有测试全绿
