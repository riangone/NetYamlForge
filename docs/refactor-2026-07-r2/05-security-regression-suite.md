# R2-05 — 安全回归套件：SQL 注入 / Hook 沙箱逃逸

> 范围: `SqlExpressionParser` / `SqlExpression/*` / 各方言 / Roslyn Hook 校验器 / `NetYamlForge.Analyzers`
> 类型: 新增安全测试（对抗性用例） · 风险: 低（纯测试） · 依赖: 无（可最先做）
> **行为变化: 否** — 只加测试；若测试暴露真实漏洞，修复走独立 PR。

## 1. 现状（实测）

- 第一轮 04 提到给 `SqlExpressionParser` 补安全测试，且已有 Roslyn Hook 校验器 + `NetYamlForge.Analyzers/ForbiddenPatternAnalyzer`。
- 现有 441 测试中**对抗性 / 逃逸类用例偏薄**——多为功能正确性测试，缺"攻击者视角"的负向套件。
- 低代码框架的两大攻击面：
  1. **SQL 注入**：用户可控的 filter / expression / 排序 / 分页 字段进入 `SqlExpressionParser`。
  2. **Hook 沙箱逃逸**：项目侧 C# Hook 经 Roslyn 编译加载（ALC），恶意 Hook 可能触碰 IO/反射/进程。

## 2. 目标

建立两套**专门的对抗性回归套件**，把安全约束从"隐性假设"变成"CI 拦截的显式断言"。

## 3. 设计

### 3.1 SQL 注入回归套件
新增 `NetYamlForge.Tests/Security/SqlInjectionRegressionTests.cs`：
- **语料库**：整理经典 + 框架特定注入向量（`' OR '1'='1`、`;DROP TABLE`、`--` 注释、`UNION SELECT`、堆叠查询、方言特有转义、二次编码、超长/Unicode 同形字符等）。
- **断言维度**（对每个方言 `Sqlite/PostgreSql/MySql/SqlServer` 各跑一遍）：
  1. 恶意输入**要么被拒绝**（抛可控异常），**要么被参数化**（进 `DbParameter`，绝不拼进 SQL 文本）。
  2. 生成的 SQL 文本中**不出现**未参数化的用户输入片段。
  3. 排序 / 列名 / 表名等**不能参数化**的位置，必须走**白名单**校验（只允许已知元数据字段），拒绝任意标识符。
- 与第一轮 08 的**方言契约测试**共用 fixture，形成"功能契约 + 安全契约"双层。

```csharp
[Theory]
[MemberData(nameof(InjectionVectors))]
public void Filter_input_is_never_concatenated_into_sql(string dialect, string maliciousInput)
{
    var (sql, parameters) = BuildFilterSql(dialect, field: "name", op: "like", value: maliciousInput);
    Assert.DoesNotContain(maliciousInput, sql);          // 未拼接
    Assert.Contains(parameters, p => (string?)p.Value == maliciousInput); // 已参数化
}
```

### 3.2 Hook 沙箱逃逸回归套件
新增 `NetYamlForge.Tests/Security/HookSandboxEscapeTests.cs`：
- **恶意 Hook 语料**（源码片段，喂给 Roslyn 校验器 / `ForbiddenPatternAnalyzer`）：
  - 文件系统逃逸：`System.IO.File.*`、`Directory.*`、路径穿越。
  - 进程/命令执行：`Process.Start`、`System.Diagnostics`。
  - 反射突破：`typeof(...).Assembly`、`Activator.CreateInstance`、`Type.GetType("System....")`、动态加载程序集。
  - 网络外联：`HttpClient`、`Socket`（若策略禁止）。
  - 环境/密钥读取：`Environment.GetEnvironmentVariable`、访问连接串。
  - 反序列化/`unsafe`/P/Invoke（`DllImport`）。
- **断言**：以上每类必须被**校验器拒绝**（编译前静态拦截）或在受限 ALC 中**无权限执行**。
- **正向对照**：合法 Hook（只用允许的 API）必须**通过**，防止规则过严误杀。

### 3.3 Analyzer 规则补强
`ForbiddenPatternAnalyzer` 增补/核对上述禁用 API 清单为诊断规则（如 `NYF-SEC-0xx`），
使**项目侧 Hook 源码**在编译期就被拦截，与运行期 ALC 权限形成纵深防御。
- 更新 `AnalyzerReleases.Unshipped.md` 登记新规则号。

### 3.4 CI 集成
- 两套套件进 `dotnet test` 主流程（属于单元测试，无外部依赖）。
- 可加 test 分类 `[Trait("Category","Security")]`，便于单独运行与报告。

## 4. 落地顺序（分 PR）

1. **PR-1**：SQL 注入回归套件（跨 4 方言）。若发现真实拼接点 → 记 issue，修复走独立 PR（不混在测试 PR）。
2. **PR-2**：Hook 沙箱逃逸套件 + 正向对照。
3. **PR-3**：`ForbiddenPatternAnalyzer` 规则补强 + `AnalyzerReleases` 登记 + 对应 analyzer 测试。

## 5. 边界与风险

- **测试暴露真实漏洞**：这是本项的价值所在。发现即**独立 PR 修复**，测试 PR 只负责"能稳定复现"，保持职责单一。
- **误杀合法用法**：Hook 规则必须带**正向对照用例**，避免把合法 API 一刀切禁用。
- **方言差异**：注入向量在不同方言下转义/行为不同，断言需分方言参数化，不可一套断言套所有方言。
- **不追求穷尽**：安全套件是"已知向量回归网"，非渗透测试替代品；新向量随发现增量补入。

## 6. 验收标准

- [ ] `Security/SqlInjectionRegressionTests` 覆盖 4 个方言 × 多类注入向量，全绿
- [ ] 断言证明用户输入恒被参数化或拒绝，SQL 文本无未参数化用户片段
- [ ] 不可参数化位置（列名/排序）走元数据白名单，任意标识符被拒
- [ ] `Security/HookSandboxEscapeTests` 覆盖 IO/进程/反射/网络/环境/反序列化各类逃逸，且有合法正向对照
- [ ] `ForbiddenPatternAnalyzer` 新规则登记于 `AnalyzerReleases.Unshipped.md` 并有 analyzer 测试
- [ ] 全部安全用例带 `Category=Security` trait；现有测试全绿
