# SQL Server セットアップガイド

## 概要

`NetYamlForge` は SQLite（デフォルト）と SQL Server の両方に対応しています。
`appsettings.json` の `DatabaseProvider` を変更するだけで切り替えられます。

---

## 切り替え手順

### 1. appsettings.json を編集

```json
{
  "DatabaseProvider": "sqlserver",
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=Chinook;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

> `DefaultConnection` を SQL Server の接続文字列で上書きします。
> `SqlServer` キーは参考用のコメントであり、アプリは `DefaultConnection` のみを使用します。

#### 主な接続文字列パターン

| 認証方式 | 接続文字列例 |
|---------|-------------|
| Windows 認証 | `Server=.\SQLEXPRESS;Database=Chinook;Trusted_Connection=True;TrustServerCertificate=True;` |
| SQL 認証 | `Server=localhost;Database=Chinook;User Id=sa;Password=YourPass;TrustServerCertificate=True;` |
| Azure SQL | `Server=myserver.database.windows.net;Database=Chinook;Authentication=Active Directory Default;TrustServerCertificate=True;` |

### 2. Chinook データベースを SQL Server に作成

SQL Server 向けの Chinook スクリプトを [公式リリース](https://github.com/lerocha/chinook-database/releases) からダウンロードし、SQL Server Management Studio または `sqlcmd` で実行します。

```bash
# sqlcmd を使う場合
sqlcmd -S localhost -E -i Chinook_SqlServer.sql
```

### 3. アプリを起動

```bash
dotnet run
```

起動時に `DbInitializer` が SQL Server モードで動作し、以下を実行します：
- `AppUser` テーブル作成（`IF NOT EXISTS` で冪等）
- `AuditLog` テーブル作成（`IF NOT EXISTS` で冪等）
- デフォルト管理者作成（`admin` / `Admin@123`）

---

## アーキテクチャ

### ISqlDialect インターフェース

```csharp
public interface ISqlDialect
{
    // numbered ページング句を追加（ORDER BY 未指定時は defaultOrderByExpr で補完）
    void AppendNumberedPagination(List<string> sqlParts, DynamicParameters param,
        int effectivePageSize, int offset, string defaultOrderByExpr);

    // keyset ページング句を追加（ORDER BY は呼び出し側で追加済み）
    void AppendKeysetPagination(List<string> sqlParts, DynamicParameters param,
        int effectivePageSize);

    // 文字列連結演算子
    string ConcatOperator { get; }  // SQLite: "||" / SQL Server: "+"
}
```

### 方言ごとのページング SQL

#### SQLite（`SqliteDialect`）

```sql
SELECT ... FROM ...
WHERE ...
ORDER BY Name ASC        -- ソート指定時
LIMIT 10 OFFSET 20
```

#### SQL Server（`SqlServerDialect`）

```sql
SELECT ... FROM ...
WHERE ...
ORDER BY Name ASC        -- ソート指定時（なければ主キーで自動補完）
OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY
```

> SQL Server の `OFFSET / FETCH NEXT` には `ORDER BY` が**必須**です。
> ソートが未指定の場合、`SqlServerDialect` が主キーカラムで `ORDER BY` を自動挿入します。

### DI 登録（`Program.cs`）

```csharp
var dbProvider = (builder.Configuration["DatabaseProvider"] ?? "sqlite").ToLowerInvariant();

if (dbProvider == "sqlserver")
{
    builder.Services.AddSingleton<ISqlDialect, SqlServerDialect>();
    builder.Services.AddScoped<IDbConnection>(_ =>
        new SqlConnection(configuration.GetConnectionString("DefaultConnection")));
}
else
{
    builder.Services.AddSingleton<ISqlDialect, SqliteDialect>();
    builder.Services.AddScoped<IDbConnection>(_ =>
        new SqliteConnection(cs));
}
```

---

## YAML の差分管理

SQL Server と SQLite で異なる箇所は **文字列連結演算子**のみです。

| 演算子 | SQLite | SQL Server |
|--------|--------|------------|
| 連結 | `\|\|` | `+` |
| 例 | `"e.LastName \|\| ', ' \|\| e.FirstName"` | `"e.LastName + ', ' + e.FirstName"` |

### ディレクトリ構成

```
config/
├── entities/                    # SQLite 版（デフォルト）
│   ├── artist.yml
│   ├── album.yml
│   ├── customer.yml             # expression に || を使用
│   ├── employee.yml
│   ├── genre.yml
│   ├── invoice.yml              # expression に || を使用
│   ├── invoiceline.yml
│   ├── mediatype.yml
│   ├── playlist.yml
│   └── track.yml
└── entities-sqlserver/          # SQL Server 版の差分ファイルのみ
    ├── customer.yml             # expression の || を + に変更
    └── invoice.yml              # expression の || を + に変更
```

### マージ戦略

`EntityMetadataProvider` は以下の順で読み込みます：

1. `entities-sqlserver/` を先読み（SQL Server モード時のみ）
2. `entities/` で不足エンティティを補完（`skipExisting: true`）

新しい SQL Server 専用エンティティが必要な場合は `entities-sqlserver/` にファイルを追加するだけで対応できます。

---

## 制約と注意事項

| 項目 | SQLite | SQL Server |
|------|--------|------------|
| Chinook 自動ダウンロード | ✅ | ❌（手動でスクリプトを実行） |
| keyset ページング | `LIMIT N` | `OFFSET 0 ROWS FETCH NEXT N ROWS ONLY` |
| 文字列連結 | `\|\|` | `+` |
| 認証テーブル自動作成 | ✅ | ✅（IF NOT EXISTS） |
| LIKE 検索 | ✅ | ✅（同じ構文） |
| 日付型 | TEXT（ISO8601） | DATE / DATETIME2 |

> 日付フィルター（`date-range`）で `DateTime.ToString("yyyy-MM-dd")` を使ったパラメータを渡しているため、SQL Server でも正常に動作します。
