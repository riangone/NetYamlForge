# 04 — SqlExpressionParser 拆文件 + 补测试

> 目标文件: `NetYamlForge/Services/SqlExpressionParser.cs`（555 行）
> 类型: 拆文件 + 新增测试（行为不变） · 风险: 低 · 依赖: 无

## 1. 现状分析（实测）

`SqlExpressionParser` 是 `static class`，内部**已经**有清晰的分层，无需重构架构，只需拆文件 + 补测试保护：

| 组件 | 行号 | 职责 |
|------|------|------|
| `AllowedFunctions` / `CastTypes` 白名单 | 16-29 | 安全白名单（`HashSet`, 大小写不敏感） |
| `Validate(expression, context)` | 30-44 | 唯一 public 入口 |
| `TokenType` enum + `Token` record | 45-60 | 词法单元 |
| `Tokenizer`（sealed class） | 62-233 | 词法分析：`Tokenize`/`ReadString`/`ReadNumber`/`ReadPipe`/`ReadOperator`/`ReadIdentifier` + 字符判定 |
| `Parser`（sealed class） | 235-末 | 语法分析：`ParseExpression`/`ParseOrExpression`/`ParseAndExpression`/`ParseNotExpression`/`ParsePredicate`/`ParseOperand`/`ParseTerm`/`ParseCastFunction` |

**这是安全敏感组件**（SQL 表达式白名单校验，防注入）。目标是**在不改语义的前提下拆开、并用测试锁定当前行为**，为后续演进兜底。

## 2. 目标结构

`static class SqlExpressionParser` 用 **`partial`** 跨文件拆分，或把内部类各自成文件（内部类无法直接跨命名空间，用嵌套 partial 或提升为同命名空间 internal 类）。推荐后者：

```
Services/SqlExpression/
  SqlExpressionParser.cs        // public static Validate + 白名单常量（入口，~60 行）
  SqlExpressionTokens.cs        // TokenType enum + Token record
  SqlExpressionTokenizer.cs     // Tokenizer（internal sealed）
  SqlExpressionSyntaxParser.cs  // Parser（internal sealed，避免与文件同名混淆，命名为 SyntaxParser）
```
- 命名空间保持 `NetYamlForge.Services`（或统一 `.SqlExpression`，全一致）。
- 将原 `private enum`/`private sealed record`/`private sealed class` 提升为 `internal`，以便跨文件可见；**public 表面仍只有 `Validate`**。

## 3. 操作步骤

1. 先建立测试网（见 §4）——**在拆分之前**跑一遍，记录基线。
2. 把 `Tokenizer`、`Parser`、`Token`/`TokenType` 各自剪切到新文件，`private`→`internal`。
3. `Validate` 与白名单留在主文件。
4. 白名单 `AllowedFunctions`/`CastTypes` 若被 Tokenizer/Parser 引用，暴露为 `internal static readonly` 或通过参数传入——**保持集合内容与比较器（`OrdinalIgnoreCase`）不变**。
5. build + test。

## 4. 测试策略（本项的重点 —— 安全回归网）

在 `NetYamlForge.Tests/` 下新增 `SqlExpressionParserTests.cs`，**先补测试再拆分**，覆盖：

- ✅ 合法表达式通过：列引用、`AND`/`OR`/`NOT`、括号、比较运算、`IN (...)`、`LIKE`、`CAST(x AS <允许类型>)`、允许的函数调用、`||` 拼接、字符串/数字字面量。
- ⛔ 非法表达式抛异常（安全断言，逐条）：
  - 未在白名单的函数（如 `SLEEP(...)`、`LOAD_FILE(...)`）
  - 非法 CAST 目标类型
  - 分号 / 注释（`--`、`/* */`）注入尝试
  - 不配对括号 / 悬空操作符 / 空表达式
  - 未闭合字符串
- 参数化用例，`context` 参数覆盖出现在异常信息里（便于排障）。

> 目标：**拆分前后同一批用例结果完全一致**。这批测试即使不拆分也有独立价值（当前 0 覆盖）。

## 5. 验收标准

- [ ] 新增 `SqlExpressionParserTests`，合法/非法用例各 ≥ 8 条，全绿
- [ ] public 表面仍只有 `Validate`
- [ ] 无单文件 > 250 行
- [ ] 白名单集合内容与比较器零变化
