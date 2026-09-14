# 08 — 跨方言 SQL 契约测试

> 范围: `NetYamlForge/Services/Dialect/`（5 实现，**0 契约测试**） · 风险: 低 · 依赖: 无
> 背景: 近期提交涉及 MySQL/SQL Server/PostgreSQL/SQLite 双引号兼容修复——正是缺测试导致的线上回归。

## 1. 现状分析（实测）

`Services/Dialect/` 现有：
```
ISqlDialect.cs
MySqlDialect.cs  PostgreSqlDialect.cs  SqlServerDialect.cs  SqliteDialect.cs
```
`ISqlDialect` 契约面（实测）：
```csharp
public interface ISqlDialect {
    void AppendNumberedPagination(List<string> sqlParts, DynamicParameters param,
        int effectivePageSize, int offset, string defaultOrderByExpr);
    void AppendKeysetPagination(List<string> sqlParts, DynamicParameters param, int effectivePageSize);
    string ConcatOperator { get; }         // sqlite: "||"
    string LastInsertIdExpression { get; } // sqlite: "last_insert_rowid()"
}
```
**问题**：4 个方言各自实现分页/拼接/自增，行为差异只能在运行时对应数据库上暴露。没有一层测试保证"同一契约在各方言下语义一致 + 各自语法正确"。

## 2. 目标：两层测试

### 2.1 契约一致性测试（无需真实数据库，主力）
新建 `NetYamlForge.Tests/Dialect/SqlDialectContractTests.cs`，用 `[Theory]` 遍历全部 4 个方言实例，断言**契约层不变式**：

- `ConcatOperator` 非空；`LastInsertIdExpression` 非空。
- `AppendNumberedPagination`：给定 `pageSize=20, offset=40, orderBy="id"`：
  - 结果被追加进 `sqlParts`（数量增加）。
  - `DynamicParameters` 里注入了预期参数名（如 `@__limit`/`@__offset` 或该实现约定名——先读实现确认命名，再断言）。
  - **各方言语法关键字断言**：
    | 方言 | 期望片段（子串断言） |
    |------|----------------------|
    | SQLite / MySQL | `LIMIT` + `OFFSET` |
    | PostgreSQL | `LIMIT` + `OFFSET`（或 `FETCH`，以实现为准） |
    | SQL Server | `OFFSET ... ROWS FETCH NEXT ... ROWS ONLY` + 需要 `ORDER BY` |
- `AppendKeysetPagination`：断言生成 keyset 分页片段且不含 `OFFSET`（keyset 的意义）。

> 实现方式：
> ```csharp
> public static IEnumerable<object[]> AllDialects => new[] {
>   new object[]{ new SqliteDialect() }, new object[]{ new MySqlDialect() },
>   new object[]{ new PostgreSqlDialect() }, new object[]{ new SqlServerDialect() },
> };
> ```
> 先 `Read` 每个实现，把"实际参数名/关键字"填进断言，避免臆测。

### 2.2 SQL 生成快照测试（跨方言矩阵）
项目已有 `SqlGenerationSnapshotTests`（在 Tests 根目录）。扩展它：
- 对同一批**代表性查询意图**（分页列表、keyset 翻页、insert 后取自增 id、字符串拼接表达式），为 **4 个方言各生成一份快照**。
- 快照命名：`{Scenario}.{Dialect}.approved.sql`。
- 任何方言 SQL 变化→快照 diff→CI 拦截（把"某方言语法差异"从线上 bug 变成 review 决策点）。

### 2.3 （可选）真实数据库冒烟
若 CI 具备容器：对 4 库各跑 1 条"分页 + 拼接 + 自增回读"端到端断言。**标记 `[Trait("Category","DbIntegration")]`，默认不在单元测试轮次跑**（避免拖慢 441 测试）。SQLite 用 `chinook.db`/内存库可无条件跑。

## 3. 覆盖矩阵（最小集）

| 场景 | SQLite | MySQL | PostgreSQL | SQL Server |
|------|:-:|:-:|:-:|:-:|
| Numbered 分页 | ✓ | ✓ | ✓ | ✓ |
| Keyset 分页 | ✓ | ✓ | ✓ | ✓ |
| ConcatOperator 语法 | ✓ | ✓ | ✓ | ✓ |
| LastInsertId 表达式 | ✓ | ✓ | ✓ | ✓ |
| 标识符引用/双引号兼容（近期 bug 点） | ✓ | ✓ | ✓ | ✓ |

> **标识符引用**：近期双引号兼容修复是回归高发区。若引用逻辑不在 `ISqlDialect` 而在别处（如 `FilterSqlBuilder`/`SqlSafetyGuard`），把断言加到对应组件的跨方言测试里；必要时**把 `QuoteIdentifier` 提升为 `ISqlDialect` 成员**，让"引用规则"有单一契约入口（这是本项唯一可能的接口扩展，需单独评估）。

## 4. 验收标准

- [ ] `SqlDialectContractTests` 覆盖全部 4 方言 × 上述不变式，全绿
- [ ] `SqlGenerationSnapshotTests` 扩为 4 方言快照矩阵
- [ ] 近期"双引号兼容"场景有明确断言锁定
- [ ] 契约层测试**不依赖真实数据库**，可在标准测试轮次运行
- [ ] （可选）DbIntegration 类别测试隔离，不拖慢主轮次
