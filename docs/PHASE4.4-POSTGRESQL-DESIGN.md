# Phase 4.4 — PostgreSQL 生产模式（Schema-per-Tenant）

## 目标

SQLite 保留为开发 / 単机模式。PostgreSQL（Npgsql 已引入）成为多租户生产模式的一等公民：

- 每个租户项目可选择在**同一个 PostgreSQL 数据库**内使用**独立 schema**（schema-per-tenant），从根本上规避 SQLite 写锁问题，同时避免为每个租户创建独立数据库实例。
- Phase 4.3 的迁移管线（diff → MigrationPlan → SQL → `_nyf_migrations` 版本表 → 回滚）需要同时支持 SQLite 和 PostgreSQL 两种方言。
- `SqlTypeMapper` / `TableDdlBuilder` 对 PostgreSQL 生成的 DDL 类型必须是合法的 PostgreSQL 类型（当前会生成 `NVARCHAR(MAX)` / `BIT` / `DATETIME`，这些在 PostgreSQL 中不存在）。

## 现状盘点（已确认）

- `ISqlDialect` 已有 `SqliteDialect` / `PostgreSqlDialect` / `MySqlDialect` / `SqlServerDialect`，按 `ProjectInfo.DatabaseType` 在 `ServiceCollectionExtensions.AddDatabaseServices` 中选择（`Extensions/ServiceCollectionExtensions.cs:153`）。
- `ConnectionManager.GetConnectionAsync(projectName)` 已按 `dbType` 创建 `NpgsqlConnection` 等连接，并追加连接池参数（`Services/Connection/ConnectionManager.cs`）。
- `ProjectManager.BuildConnectionString` 对非 SQLite 直接要求 `database.connectionString` 已配置（`Services/ProjectManager.cs`）。
- `SqlTypeMapper.MapYamlTypeToSqlType`：SQLite 分支正确；非 SQLite 分支（含 PostgreSQL）返回 SQL Server 风格类型（`NVARCHAR(MAX)`, `BIT`, `DATETIME`）—— **这是 bug，需要新增 PostgreSQL 专属分支**。
- `TableDdlBuilder.BuildCreateTableSql` 已经区分 `isPostgres`（`SERIAL PRIMARY KEY`），其余列依赖 `SqlTypeMapper`，因此修复 `SqlTypeMapper` 即可让 `CREATE TABLE` 对 PostgreSQL 生成正确 DDL。
- `DynamicEntitySchemaMigrationService`：
  - `GetPhysicalColumnsAsync` 硬编码 `pragma_table_info`（SQLite only）。
  - `GenerateSql` 对非 SQLite `throw new NotSupportedException`。
  - `NormalizeSqlType` 的归一化映射表是 SQLite 取向（`INTEGER`/`NUMERIC`/`TEXT`）。
  - `_nyf_migrations` 表的 DDL（`CREATE TABLE IF NOT EXISTS ... TEXT ...`）在 PostgreSQL 中语法上是合法的（PostgreSQL 也有 `TEXT`），无需改动。
- 不存在 schema-per-tenant 概念：`project.yaml` 的 `database` 段目前只有 `type` / `path` / `connectionString`。

## 设计

### 1. `SqlTypeMapper` 增加 PostgreSQL 专属分支

```csharp
public static string MapYamlTypeToSqlType(string yamlType, string dbType)
{
    var isSqlite = ...;
    if (isSqlite) { ... 不变 ... }

    var isPostgres = string.Equals(dbType, "postgresql", OrdinalIgnoreCase)
                   || string.Equals(dbType, "postgres", OrdinalIgnoreCase);
    if (isPostgres)
    {
        return yamlType.ToLowerInvariant() switch
        {
            "int" or "integer" => "INTEGER",
            "long" => "BIGINT",
            "bool" or "boolean" => "BOOLEAN",
            "decimal" => "NUMERIC(18,2)",
            "double" or "float" or "number" => "DOUBLE PRECISION",
            "datetime" or "date" => "TIMESTAMP",
            _ => "TEXT"
        };
    }

    // 既存の SQL Server / MySQL 分岐はそのまま
    return yamlType.ToLowerInvariant() switch { ... 不变 ... };
}
```

### 2. 迁移管线扩展为方言无关

#### 2.1 `ColumnSchemaInfo` 获取（`GetPhysicalColumnsAsync`）

按 `dbType` 分支：

- SQLite：现有 `pragma_table_info` 逻辑不变。
- PostgreSQL：查询 `information_schema.columns`，并结合 `information_schema.table_constraints` / `key_column_usage` 判断主键：

```sql
SELECT
    c.ordinal_position AS Cid,
    c.column_name AS Name,
    c.data_type AS Type,
    (c.is_nullable = 'NO') AS NotNullBool,
    CASE WHEN pk.column_name IS NOT NULL THEN 1 ELSE 0 END AS Pk
FROM information_schema.columns c
LEFT JOIN (
    SELECT kcu.column_name
    FROM information_schema.table_constraints tc
    JOIN information_schema.key_column_usage kcu
      ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
    WHERE tc.constraint_type = 'PRIMARY KEY' AND tc.table_name = @TableName AND tc.table_schema = current_schema()
) pk ON pk.column_name = c.column_name
WHERE c.table_name = @TableName AND c.table_schema = current_schema()
ORDER BY c.ordinal_position;
```

注意：PostgreSQL 的 `data_type` 返回如 `integer` / `character varying` / `numeric` / `text` / `timestamp without time zone` / `boolean`，需要在 `NormalizeSqlType` 中加入对应归一化分支（见 2.3）。

#### 2.2 `GenerateSql` 增加 PostgreSQL 分支

PostgreSQL 支持原地 `ALTER TABLE`，**不需要**像 SQLite 那样整表重建：

- `AddColumn` → `ALTER TABLE "t" ADD COLUMN "c" <type> <NULL|NOT NULL>`
  - 注意：若新增列 `NOT NULL` 且表中已有数据，PostgreSQL 要求提供 `DEFAULT`；Phase 4.4 范围内：若 `Required=true` 且为 AddColumn，生成两条语句：
    1. `ALTER TABLE "t" ADD COLUMN "c" <type>`
    2. `UPDATE "t" SET "c" = <类型默认值>` （字符串 `''`、数值 `0`、布尔 `false`、时间 `now()`）
    3. `ALTER TABLE "t" ALTER COLUMN "c" SET NOT NULL`
  - down: `ALTER TABLE "t" DROP COLUMN "c"`
- `DropColumn` → up: `ALTER TABLE "t" DROP COLUMN "c"`；down 仅记录无法还原数据的提示（与 SQLite 现状一致：down_sql 可重建列结构但不恢复数据，文档中需注明此限制）。
- `AlterColumnType` → `ALTER TABLE "t" ALTER COLUMN "c" TYPE <newType> USING "c"::<newType>`；down 用 `oldSqlType` 对称生成。
- `AlterNullability` → `ALTER TABLE "t" ALTER COLUMN "c" SET NOT NULL` / `DROP NOT NULL`；down 取反。

不需要 `backupTableName` / 整表重建路径（`plan.RequiresTableRebuild` 对 PostgreSQL 始终视为 false —— `MigrationPlan` 增加按 dbType 判断的逻辑，或在 `GenerateSql` 入口针对 PostgreSQL 直接走"逐操作生成"分支，忽略 `RequiresTableRebuild`）。

`ApplyAsync` / `RollbackAsync` / `GetHistoryAsync` 的 `_nyf_migrations` 表操作和事务逻辑保持方言无关（标准 SQL，Dapper + `IDbConnection` 已通过 `ISqlDialect` 注入正确的底层连接）。

#### 2.3 `NormalizeSqlType` 扩展归一化表

加入 PostgreSQL `information_schema.columns.data_type` 返回值的归一化：

```csharp
"INTEGER" => "INTEGER",
"BIGINT" => "BIGINT",
"SMALLINT" => "INTEGER",
"NUMERIC" => "NUMERIC",
"DOUBLE PRECISION" => "NUMERIC",
"REAL" => "NUMERIC",
"BOOLEAN" => "INTEGER",          // 与 SQLite 的 bool->INTEGER 对齐，避免误判类型变更
"CHARACTER VARYING" => "TEXT",
"TEXT" => "TEXT",
"TIMESTAMP WITHOUT TIME ZONE" => "TIMESTAMP",
"TIMESTAMP" => "TIMESTAMP",
```

注意 `data_type` 中含空格的值（如 `character varying`, `double precision`, `timestamp without time zone`）—— 现有的 `text.IndexOf('(')` 截断逻辑对它们不起作用（无括号），需要直接匹配整串（大写后）。

### 3. Schema-per-tenant 配置

#### 3.1 `project.yaml` 新增 `database.schema`（可选）

```yaml
database:
  type: postgresql
  connectionString: "Host=localhost;Database=netyamlforge;Username=nyf;Password=***"
  schema: tenant_inventory   # 省略时默认使用项目名（小写，'-' 替换为 '_'）
```

仅当 `type` 为 `postgresql`/`postgres` 时生效；其余 dbType 忽略该字段（SQLite 的"独立文件"已天然隔离，MySQL/SqlServer 暂不在本阶段范围内实现 schema 隔离，但配置字段不报错——仅 PostgreSQL 处理）。

#### 3.2 `ProjectManager.BuildConnectionString` / `LoadProjectAsync`

- 计算 `schemaName`：`config.Database.Schema ?? config.Name.ToLowerInvariant().Replace("-", "_")`。
- 对 PostgreSQL：在 `connectionString` 上追加 Npgsql 的 `Search Path=<schemaName>` 参数（若用户已自行指定 `Search Path` 则不覆盖）。
- `ProjectInfo` 新增只读属性 `SchemaName`（PostgreSQL 时为计算值，其它 dbType 为 `null`），供初始化/迁移代码使用。

#### 3.3 Schema 自动创建

`ProjectSpecificInitializer`（Phase 4.3 中已重构为复用共享 DDL helper）在 PostgreSQL + `SchemaName` 不为空时，启动期执行一次：

```sql
CREATE SCHEMA IF NOT EXISTS "tenant_inventory";
```

（使用与目标表相同的连接，在建表 DDL 之前执行。`Search Path` 已经指向该 schema，因此后续 `CREATE TABLE "product" (...)` 会落在该 schema 下，无需表名加 schema 前缀。）

### 4. 测试计划

- **单元测试（无需真实 PostgreSQL）**：
  - `SqlTypeMapperTests`：针对 `postgresql` dbType 验证每种 YAML 类型映射到合法 PostgreSQL 类型字符串。
  - `TableDdlBuilderTests`：针对 `postgresql` 验证 `CREATE TABLE` DDL 字符串（`SERIAL PRIMARY KEY`、`BOOLEAN`、`TIMESTAMP` 等）。
  - `DynamicEntitySchemaMigrationServiceTests`（新增 PostgreSQL 用例，使用手工构造的 `ColumnSchemaInfo` 列表模拟 `information_schema` 结果，不连接真实 DB）：
    - AddColumn（含 NOT NULL + 数据回填三段式）→ 验证生成的 up/down SQL 字符串。
    - DropColumn / AlterColumnType / AlterNullability → 验证 SQL 字符串与 `RequiresTableRebuild=false`。
    - `NormalizeSqlType` 对 `character varying` / `double precision` / `timestamp without time zone` 等的归一化。
- **集成测试（可选，按环境变量 `NYF_POSTGRES_TEST_CONNECTION` 跳过）**：
  - 若设置了该环境变量，新增 `PostgresSchemaMigrationIntegrationTests`：用真实 Npgsql 连接创建临时 schema → 应用迁移 → 校验物理列 → 回滚 → 校验恢复，测试结束 `DROP SCHEMA ... CASCADE` 清理。
  - 未设置环境变量时 `Skip`，CI 默认不跑（避免引入 PostgreSQL 服务依赖到现有 CI）。

## 文件清单（预计改动）

- `NetYamlForge/Services/DynamicEntity/SqlTypeMapper.cs` — 新增 PostgreSQL 分支。
- `NetYamlForge/Services/DynamicEntity/DynamicEntitySchemaMigrationService.cs` — `GetPhysicalColumnsAsync` / `GenerateSql` / `NormalizeSqlType` 按 dbType 分支扩展。
- `NetYamlForge/Models/ProjectConfig.cs`（或对应 Database 配置类）— 新增 `Schema` 字段。
- `NetYamlForge/Services/ProjectManager.cs` — 计算 `SchemaName`，拼接 `Search Path`。
- `NetYamlForge/Services/ProjectInfo.cs` — 新增 `SchemaName` 属性。
- `NetYamlForge/Services/.../ProjectSpecificInitializer.cs`（4.3 中重构的共享 DDL helper）— PostgreSQL 时执行 `CREATE SCHEMA IF NOT EXISTS`。
- `docs/configuration-reference.md` — 补充 `database.schema` 配置说明。
- `NetYamlForge.Tests/Services/DynamicEntity/` — 新增/扩展上述单元测试；可选集成测试。
- `docs/EVOLUTION_PLAN.md` — 4.4 标记 ✅。

## 范围外（明确不做）

- MySQL / SQL Server 的 schema-per-tenant（仅 PostgreSQL）。
- 迁移管线对 MySQL/SqlServer 的支持（`GenerateSql` 对这两者仍可 `throw NotSupportedException`，留待未来阶段）。
- 跨 schema 的连接池/统计 UI 改动。
- 真正的生产部署脚本（docker-compose 中的 PostgreSQL 服务等）——如需要可作为后续单独任务。
