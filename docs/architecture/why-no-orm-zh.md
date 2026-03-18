# 为什么不引入 ORM？——Dapper 与 EF Core 的架构选择

> 本文说明本框架为何选择 Dapper（微型 ORM）而非 EF Core 等全功能 ORM，以及在哪些场景下才值得重新考虑。

---

## 结论：不应引入

一句话说明原因：

> **本框架的设计是"运行时从 YAML 动态生成实体"，而 ORM 的设计是"编译时将 C# 类与数据库对应"。两者在根本上相互矛盾。**

---

## 现状的准确理解

首先，**Dapper 已经在使用中**（微型 ORM）。

```
Dapper（现状）              EF Core（典型 ORM）
──────────────────────────────────────────────
自己写 SQL                  用 LINQ 写查询
用 dynamic 接收结果         用强类型类接收结果
运行时灵活                  编译时类型安全
轻量、高性能                功能丰富、较重
```

---

## 无法引入 EF Core 的根本原因

### ORM 以"静态类型"为前提

```csharp
// EF Core 的典型用法
public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }  // ← 编译时需要类型
    public DbSet<Order> Orders { get; set; }
}

// LINQ 查询
var result = await _db.Customers
    .Where(c => c.Status == "active")
    .Include(c => c.Orders)
    .ToListAsync();
```

### 本框架的实际情况

```yaml
# 只需添加 customer.yml，新实体就自动存在
entities:
  Customer:
    table: customer
    columns:
      name: {type: string}
      status: {type: string}
```

```csharp
// DynamicCrudRepository 在"运行时"读取 YAML 并动态组装 SQL
// → 编译时 Customer 类根本不存在
// → 返回值是 IEnumerable<dynamic>
return await _db.QueryAsync(statement, param);  // ← Dapper
```

如果要使用 EF Core，**就必须手动编写 `Customer.cs` 类** → YAML 定义的意义就消失了。

---

## 功能逐项对比

```
EF Core 的功能               本框架中的情况
─────────────────────────────────────────────────────
变更追踪（Change Tracking）   不需要（直接 UPDATE）
LINQ 查询                    不可行（表名/列名动态决定）
延迟加载（Lazy Loading）      不需要
Migrations（Schema 管理）    ✅ 有一定参考价值（见后文）
类型安全的导航属性            不可行（实体未在编译时定义）
数据校验                      YAML + IEntityHook 已覆盖
连接管理                      IDbConnection DI 已解决
多数据库支持                  ISqlDialect 已实现
```

---

## 当前 DynamicCrudRepository 的核心逻辑

```csharp
// SQL 在运行时根据 YAML 元数据动态组装
var selectList = string.Join(", ",
    meta.Columns.Select(c =>
        c.Value.Expression != null
            ? $"{c.Value.Expression} AS {c.Key}"
            : $"{meta.Table}.{c.Key}"));

var sql = new List<string> { $"SELECT {selectList} {BuildFromClause(meta)}" };
// BuildFromClause 会根据 YAML 的 joins 配置生成 JOIN 子句

// 所有标识符（表名、列名）经过正则验证，防止 SQL 注入
static void EnsureIdentifier(string value, string name)
{
    if (!IdentifierRegex.IsMatch(value))
        throw new InvalidOperationException($"Unsafe identifier '{name}': {value}");
}
```

这种动态 SQL 生成方式是 EF Core 根本无法替代的。

---

## 唯一值得考虑的部分：数据库迁移管理

不引入整个 EF Core，但**仅用于 Schema 版本管理**的方案值得讨论：

```bash
# 现状：手动编写 SQL / init-db.sql
# EF Core Migrations 方案：
dotnet ef migrations add AddStatusColumn
dotnet ef database update
```

但这个方案同样存在问题：

```
YAML 定义实体
   ↓
还需要另写 EF Core Entity 类
   ↓
生成 Migration
   ↓
YAML 与 C# 类双重维护 → 容易出现不一致
```

**更简单的替代方案：Flyway 或 DbUp**（只需管理 SQL 文件，无需 C# 类）。

---

## 正确的判断标准

```
适合使用 EF Core 的系统：
  ✓ 实体在设计时已确定
  ✓ 需要类型安全的 LINQ 查询
  ✓ 通过导航属性遍历关联
  ✓ 需要变更追踪（乐观并发控制等）

本框架的特性：
  ✗ 实体由 YAML 在运行时决定
  ✗ 返回值为 dynamic（无静态类型）
  ✗ SQL 动态组装
  ✗ 多数据库通过单一连接切换
  → 继续使用 Dapper 才是正确答案
```

---

## 总结

| 选项 | 判断 | 原因 |
|------|------|------|
| 全面引入 EF Core | ❌ 否决 | 与框架核心设计思想矛盾 |
| 仅用 EF Core Migrations | ⚠️ 可讨论 | 但存在双重维护风险，Flyway/DbUp 更轻量 |
| 继续使用 Dapper | ✅ 正确 | 最适合动态 SQL 生成，已支持多数据库 |

**如果想使用 ORM，应该选择普通的 ASP.NET Core + EF Core 应用，而不是本框架。**
本框架的价值在于"一切都能用 YAML 定义"，ORM 与这一价值在根本上相互矛盾。

---

## 延伸阅读

- `docs/yaml-driven-design-zh.md` — YAML 驱动设计的边界与扩展性
- `Services/DynamicCrudRepository.cs` — 动态 SQL 生成的核心实现
- `Services/Dialect/ISqlDialect.cs` — 多数据库方言抽象层
