# プロジェクト固有フック・ビジネスロジックガイド

## 概要

NetYamlForge では、**フレームワークのコードを変更せずに**、各プロジェクト固有の処理を実装できます。

- **プロジェクト固有フック**：CRUD 操作の前後に実行される処理
- **プロジェクト固有ビジネスロジック**：プロジェクト全体の横断的処理
- **プロジェクト固有礼儀検証**：独自のバリデーションルール
- **プロジェクト固有データ変換**：データ加工・正規化

これらはすべて `projects/{project}/Hooks/` ディレクトリに配置し、**動的コンパイル**されて実行時に読み込まれます。

---

## アーキテクチャ

```
┌─────────────────────────────────────────────────────────────┐
│                    Framework Layer                          │
│  ┌─────────────┐  ┌──────────────┐  ┌─────────────────┐   │
│  │IEntityHook  │  │IProjectHook  │  │IProjectBusiness │   │
│  │(Generic)    │  │Registry      │  │LogicRegistry    │   │
│  └─────────────┘  └──────────────┘  └─────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                            ▲
                            │ 動的読み込み
                            │
┌─────────────────────────────────────────────────────────────┐
│              Project Layer (projects/chinook/)              │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  Hooks/                                             │   │
│  │  ├── SampleHooks.cs         (IEntityHook 実装)      │   │
│  │  ├── ChinookBusinessLogic.cs (IProjectBusinessLogic)│   │
│  │  ├── ChinookEntityHooks.cs  (IEntityHook 実装)      │   │
│  │  └── ...                                            │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

---

## 実装方法

### 1. プロジェクト固有フック（IEntityHook）

CRUD 操作の前後に実行されるフックを実装します。

```csharp
using System.Data;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Chinook.Hooks;

/// <summary>
/// Chinook 固有：顧客作成時にウェルカムメールを送信
/// </summary>
public class ChinookCustomerWelcomeHook : IEntityHook
{
    private readonly ILogger<ChinookCustomerWelcomeHook> _logger;

    public string Name => "chinook_customer_welcome";

    public ChinookCustomerWelcomeHook(ILogger<ChinookCustomerWelcomeHook> logger)
    {
        _logger = logger;
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 前処理：バリデーションなど
        return Task.FromResult(HookResult.Continue());
    }

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 後処理：メール送信、ログ記録など
        if (ctx.Id is int customerId)
        {
            var customer = await db.QueryFirstOrDefaultAsync(
                "SELECT * FROM Customer WHERE CustomerId = @Id", 
                new { Id = customerId }, tx);
            
            _logger.LogInformation("顧客 {Name} さん、ようこそ！", customer.FirstName);
        }
    }
}
```

**entities.yml での使用例：**

```yaml
entities:
  customer:
    hooks:
      afterCreate: "chinook_customer_welcome"
```

---

### 2. プロジェクト固有ビジネスロジック（IProjectBusinessLogic）

プロジェクト全体の横断的処理を実装します。

```csharp
using System.Data;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Chinook.Hooks;

public class ChinookBusinessLogic : IProjectBusinessLogic
{
    private readonly ILogger<ChinookBusinessLogic> _logger;

    public string ProjectName => "chinook";

    public ChinookBusinessLogic(ILogger<ChinookBusinessLogic> logger)
    {
        _logger = logger;
    }

    public Task InitializeAsync()
    {
        _logger.LogInformation("Chinook ビジネスロジックを初期化しました");
        return Task.CompletedTask;
    }

    public Task BeforeEntityOperationAsync(
        string entity, 
        CrudOperation operation, 
        IDictionary<string, object?> values, 
        IDbConnection db, 
        IDbTransaction? tx)
    {
        // エンティティ固有の前処理
        return entity.ToLowerInvariant() switch
        {
            "invoice" => ValidateInvoiceAsync(operation, values, db, tx),
            "customer" => ValidateCustomerAsync(operation, values, db, tx),
            _ => Task.CompletedTask
        };
    }

    public Task AfterEntityOperationAsync(
        string entity, 
        CrudOperation operation, 
        object? id, 
        IDbConnection db, 
        IDbTransaction? tx)
    {
        // エンティティ固有の後処理
        return entity.ToLowerInvariant() switch
        {
            "invoice" => AfterInvoiceOperationAsync(operation, id, db, tx),
            _ => Task.CompletedTask
        };
    }

    private Task ValidateInvoiceAsync(...)
    {
        // 請求書固有の検証ロジック
    }
}
```

---

### 3. プロジェクト固有礼儀検証（IProjectValidator）

独自のバリデーションルールを実装します。

```csharp
public class ChinookValidator : IProjectValidator
{
    public string ProjectName => "chinook";

    public async Task<IEnumerable<string>> ValidateAsync(
        string entity, 
        IDictionary<string, object?> values, 
        IDbConnection db, 
        IDbTransaction? tx)
    {
        var errors = new List<string>();

        if (entity == "invoice")
        {
            // 請求書固有の検証
            if (values.TryGetValue("Total", out var total) && total is decimal t && t < 0)
            {
                errors.Add("請求書合計は 0 以上である必要があります。");
            }
        }

        return errors;
    }
}
```

---

### 4. プロジェクト固有データ変換（IProjectDataTransformer）

データ加工・正規化ロジックを実装します。

```csharp
public class ChinookDataTransformer : IProjectDataTransformer
{
    public string ProjectName => "chinook";

    public Task TransformAsync(
        string entity, 
        IDictionary<string, object?> values, 
        IDbConnection db, 
        IDbTransaction? tx)
    {
        // 顧客名のトリム処理
        if (entity == "customer")
        {
            if (values.TryGetValue("FirstName", out var fn) && fn is string firstName)
            {
                values["FirstName"] = firstName.Trim();
            }
        }

        return Task.CompletedTask;
    }
}
```

---

## フックの実行順序

CRUD 操作実行時のフック実行順序は以下の通りです：

### 登録時（Create）

```
1. 汎用フック（beforeCreate）
2. プロジェクト固有フック（beforeCreate）
3. プロジェクト固有礼儀検証（IProjectValidator）
   └─ エラーがあれば中止
4. プロジェクト固有データ変換（IProjectDataTransformer）
5. プロジェクト固有ビジネスロジック（BeforeEntityOperationAsync）
6. 【DB 書き込み実行】
7. プロジェクト固有ビジネスロジック（AfterEntityOperationAsync）
8. 汎用フック（afterCreate）
9. プロジェクト固有フック（afterCreate）
```

### 更新時（Update）

```
1. 汎用フック（beforeUpdate）
2. プロジェクト固有フック（beforeUpdate）
3. プロジェクト固有礼儀検証
4. プロジェクト固有データ変換
5. プロジェクト固有ビジネスロジック（Before）
6. 【DB 更新実行】
7. プロジェクト固有ビジネスロジック（After）
8. 汎用フック（afterUpdate）
9. プロジェクト固有フック（afterUpdate）
```

---

## 実装例：Chinook プロジェクト

### ディレクトリ構造

```
projects/chinook/
├── Hooks/
│   ├── SampleHooks.cs              # 基本サンプル
│   ├── ChinookBusinessLogic.cs     # ビジネスロジック
│   └── ChinookEntityHooks.cs       # エンティティフック
├── entities/
│   └── customer.yml
└── project.yaml
```

### customer.yml でのフック使用例

```yaml
entities:
  customer:
    hooks:
      beforeCreate:
        - "validate_email"              # 汎用フック
        - "chinook_customer_validation" # プロジェクト固有フック
      afterCreate:
        - "chinook_customer_welcome"    # ウェルカムメール
        - "audit_log"                   # 監査ログ
      beforeUpdate:
        - "validate_email"
        - "chinook_customer_validation"
```

---

## 注意事項

### 1. 依存関係の解決

プロジェクト固有フックは、DI コンテナから依存関係を解決できます：

```csharp
public class ChinookCustomerWelcomeHook : IEntityHook
{
    private readonly ILogger<ChinookCustomerWelcomeHook> _logger;
    private readonly IEmailService _emailService; // DI から注入

    public ChinookCustomerWelcomeHook(
        ILogger<ChinookCustomerWelcomeHook> logger,
        IEmailService emailService)
    {
        _logger = logger;
        _emailService = emailService;
    }
}
```

### 2. 例外処理

フック内で例外を投げると、トランザクションがロールバックされます：

```csharp
public async Task<HookResult> BeforeAsync(...)
{
    if (/* 検証エラー */)
    {
        throw new InvalidOperationException("検証エラー");
        // または
        return HookResult.Abort("エラーメッセージ");
    }
    return HookResult.Continue();
}
```

### 3. パフォーマンス

プロジェクト固有フックは起動時に動的コンパイルされます。初回起動時に少し時間がかかりますが、その後はキャッシュされます。

---

## テスト方法

プロジェクト固有フックは単体テスト可能です：

```csharp
[Test]
public async Task ChinookCustomerWelcomeHook_AfterAsync_发送邮件()
{
    // Arrange
    var logger = new Mock<ILogger<ChinookCustomerWelcomeHook>>();
    var hook = new ChinookCustomerWelcomeHook(logger.Object);
    var context = new EntityHookContext
    {
        Entity = "customer",
        Operation = CrudOperation.Create,
        Id = 1,
        Values = new Dictionary<string, object?>
        {
            ["FirstName"] = "Test",
            ["LastName"] = "User",
            ["Email"] = "test@example.com"
        }
    };

    // Act
    await hook.AfterAsync(context, dbConnection, null);

    // Assert
    // メール送信確認など
}
```

---

## 関連ドキュメント

- [`docs/confirmation-and-hooks.md`](confirmation-and-hooks.md) - 確認ダイアログと汎用フック
- [`docs/composite-key-example.md`](composite-key-example.md) - 複合主鍵ガイド
- `Services/Hooks/` - フックインターフェース定義
