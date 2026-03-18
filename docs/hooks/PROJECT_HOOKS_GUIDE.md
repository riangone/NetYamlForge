# プロジェクト固有 Hooks 使用ガイド

このドキュメントでは、各プロジェクト固有の Entity Hooks を作成・使用方法について説明します。

## 概要

プロジェクト固有 Hooks を使用すると、フレームワーク共通の Hooks を汚染することなく、各プロジェクト専用のロジックを実装できます。

## アーキテクチャ

```
NetYamlForge/
├── Services/Hooks/
│   └── CommonHooks.cs          # フレームワーク共通フック
├── Services/
│   ├── ProjectHookRegistry.cs  # プロジェクト別フック管理
│   └── ProjectHookLoader.cs    # 動的フックローダー
└── projects/
    ├── chinook/
    │   └── Hooks/              # プロジェクト固有フック
    │       └── SampleHooks.cs
    ├── blog/
    │   └── Hooks/
    └── todo/
        └── Hooks/
```

## 仕組み

1. **分離されたレジストリ**: 
   - フレームワーク共通フック → `IEntityHookRegistry`
   - プロジェクト固有フック → `IProjectHookRegistry`

2. **優先順位**:
   - フック実行時はプロジェクト固有フックを優先
   - 見つからない場合のみフレームワーク共通フックを使用

3. **動的読み込み**:
   - 起動時に各プロジェクトの `Hooks/` ディレクトリをスキャン
   - Roslyn を使用して実行時にコンパイル
   - `IEntityHook` 実装を自動登録

## 作成方法

### ステップ 1: フックディレクトリの作成

プロジェクトルディレクトリに `Hooks/` サブディレクトリを作成します。

```bash
mkdir -p projects/your-project/Hooks
```

### ステップ 2: フッククラスの実装

```csharp
using System.Data;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.YourProject.Hooks;

/// <summary>
/// プロジェクト固有のフック
/// </summary>
public class YourCustomHook : IEntityHook
{
    private readonly ILogger<YourCustomHook> _logger;

    public YourCustomHook(ILogger<YourCustomHook> logger)
    {
        _logger = logger;
    }

    // フック名（entities.yml で参照）
    public string Name => "your_custom_hook";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 前処理ロジック
        _logger.LogInformation("Before hook executed");
        
        // 必要に応じて ctx.Values を変更
        // ctx.Values["FieldName"] = "NewValue";
        
        // キャンセルする場合は HookResult.Abort を返す
        // return Task.FromResult(HookResult.Abort("エラーメッセージ"));
        
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 後処理ロジック
        _logger.LogInformation("After hook executed");
        return Task.CompletedTask;
    }
}
```

### ステップ 3: entities.yml での設定

プロジェクトの entities.yml でフックを参照します。

```yaml
entities:
  your_entity:
    table: YourTable
    key: Id
    hooks:
      beforeCreate: "your_custom_hook"
      afterCreate: "your_custom_hook"
```

## 使用例

### 例 1: 独自検証ロジック

```csharp
public class CustomerAgeValidationHook : IEntityHook
{
    public string Name => "customer_age_validation";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("Age", out var ageObj) && ageObj is int age)
        {
            if (age < 0 || age > 150)
            {
                return Task.FromResult(HookResult.Abort("年齢は 0 以上 150 以下である必要があります。"));
            }
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
```

### 例 2: 外部 API 連携

```csharp
public class SyncToExternalApiHook : IEntityHook
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SyncToExternalApiHook> _logger;

    public SyncToExternalApiHook(
        IHttpClientFactory httpClientFactory,
        ILogger<SyncToExternalApiHook> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Name => "sync_to_external_api";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var client = _httpClientFactory.CreateClient();
        var payload = new
        {
            entity = ctx.Entity,
            operation = ctx.Operation,
            data = ctx.Values
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        await client.PostAsync("https://api.example.com/sync", content);
        _logger.LogInformation("外部 API と同期完了");
    }
}
```

### 例 3: 複雑なビジネスロジック

```csharp
public class OrderDiscountCalculationHook : IEntityHook
{
    public string Name => "order_discount_calculation";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 顧客の購入履歴に基づいて割引率を計算
        if (ctx.Values.TryGetValue("CustomerId", out var customerId) &&
            ctx.Values.TryGetValue("Total", out var totalObj))
        {
            var total = Convert.ToDecimal(totalObj);
            
            // 購入履歴から累計金額を取得
            var sql = "SELECT SUM(Total) FROM Orders WHERE CustomerId = @CustomerId";
            var cumulativeTotal = db.ExecuteScalar<decimal?>(sql, new { CustomerId = customerId }, tx) ?? 0;

            // 累計金額に応じて割引率を設定
            decimal discountRate = cumulativeTotal switch
            {
                >= 10000 => 0.15m,
                >= 5000 => 0.10m,
                >= 1000 => 0.05m,
                _ => 0m
            };

            ctx.Values["DiscountRate"] = discountRate;
            ctx.Values["DiscountAmount"] = total * discountRate;
            ctx.Values["FinalTotal"] = total - (total * discountRate);
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
```

## 依存関係の注入

プロジェクト固有フックでも DI を使用できます：

```csharp
public class NotificationHook : IEntityHook
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<NotificationHook> _logger;

    // DI コンテナから依存関係を自動注入
    public NotificationHook(
        INotificationService notificationService,
        ILogger<NotificationHook> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public string Name => "send_notification";

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        await _notificationService.NotifyAsync(ctx.Entity, ctx.Operation);
    }
}
```

## 共通フックとの使い分け

### フレームワーク共通フックを使用する場合

- 複数のプロジェクトで共通のロジック
- 汎用的な検証（メール形式、電話番号など）
- 標準的なデータ変換（trim、uppercase など）

### プロジェクト固有フックを使用する場合

- 特定のプロジェクト専用のビジネスロジック
- プロジェクト固有の外部システム連携
- 実験的な機能（他のプロジェクトに影響を与えたくない場合）

## 注意事項

1. **パフォーマンス**: 
   - 実行時のコンパイルにより初回起動時に時間がかかります
   - 本番環境では事前コンパイルを検討してください

2. **エラーハンドリング**:
   - フック内の例外は適切に処理してください
   - 未処理の例外はトランザクションをロールバックします

3. **スレッドセーフティ**:
   - フッククラスはシングルトンとして登録されます
   - 状態を持つ場合はスレッドセーフにしてください

4. **命名規則**:
   - フック名はプロジェクト内で一意である必要があります
   - 他プロジェクトと重複しても問題ありません

## デバッグ

プロジェクトフックの読み込み状況はログで確認できます：

```
[Information] プロジェクト 'chinook' のフックを読み込みました：2 件 (1 ファイル)
[Debug] プロジェクト 'chinook' にフック 'track_convert_duration' (TrackConvertDurationHook) を登録しました
```

フック実行時のログ：

```
[Debug] プロジェクトフック 'track_convert_duration' を実行 (Project=chinook)
```

## テスト

プロジェクトフックは単体テストでテストできます：

```csharp
public class TrackConvertDurationHookTests
{
    [Fact]
    public async Task BeforeAsync_ConvertsMillisecondsToSeconds()
    {
        // Arrange
        var logger = new Mock<ILogger<TrackConvertDurationHook>>();
        var hook = new TrackConvertDurationHook(logger.Object);
        var ctx = new EntityHookContext
        {
            Values = new Dictionary<string, object?> { ["Milliseconds"] = 5000 }
        };

        // Act
        await hook.BeforeAsync(ctx, null, null);

        // Assert
        Assert.Equal(5, ctx.Values["Seconds"]);
    }
}
```
