# 例02: バリデーション追加

## 概要

既存エンティティの作成・更新時にバリデーションを追加する。

**まず `docs/COMMON_HOOKS.md` の既存フックで対応できないか確認すること。**
既存フックで対応可能ならYAMLのみ変更。不可能な場合のみコードを追加する。

---

## パターンA: 既存フックをYAMLで追加（コード不要）

### 対応できる既存フック

| フック名 | 内容 |
|---------|------|
| `validate_email` | メールアドレス形式検証 |
| `validate_phone` | 電話番号形式検証 |
| `validate_url` | URL形式検証 |
| `validate_required` | 必須チェック |
| `validate_unique` | 一意制約チェック（DB参照） |
| `validate_range` | 数値範囲チェック |
| `trim` | 前後の空白除去 |
| `now` | 現在日時の自動セット |
| `audit_log` | 監査ログ書き込み |

### 変更ファイル

```
projects/<name>/entities/<entity>.yml  ← YAMLのみ
```

### 変更差分

```yaml
# Before
entities:
  Customer:
    table: customer
    key: id
    displayName: "Customers"
    columns:
      email: {type: string}

# After（メール形式検証 + 保存前にtrim）
entities:
  Customer:
    table: customer
    key: id
    displayName: "Customers"
    columns:
      email: {type: string}
    hooks:
      beforeCreate:         # ← camelCase を使う
        - trim              # 先にtrimしてからvalidate
        - validate_email
      beforeUpdate:
        - trim
        - validate_email
```

**注意: フックキー名は camelCase（`beforeCreate`, `afterCreate`, `beforeUpdate` 等）を使う。**

### フックの設定パラメータ（フィールド指定）

一部のフックはパラメータで対象フィールドを指定できる：

```yaml
    hooks:
      beforeCreate:
        # フィールド指定あり（フック名__hookConfig の形式）
        - name: validate_range
          config: "amount:0:1000000"   # フィールド名:最小値:最大値
        - name: validate_unique
          config: "email"              # 一意チェック対象フィールド
```

---

## パターンB: 独自フックの実装（既存フックで対応不可の場合）

### 変更ファイル

```
projects/<name>/Hooks/<ProjectName>Hooks.cs  ← 新規作成
projects/<name>/entities/<entity>.yml         ← フック名追加
NetYamlForge/Program.cs                  ← DI登録
NetYamlForge.Tests/Hooks/<Name>HookTests.cs ← テスト追加（必須）
```

### ステップ1: フッククラスを作成

```csharp
// projects/myproject/Hooks/MyProjectHooks.cs

using System.Data;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.MyProject.Hooks;

/// <summary>
/// 責務: myproject 専用のバリデーション・変換フックを提供する。
/// フレームワーク共通フックで対応不可の業務ロジックのみここに実装する。
/// </summary>
public sealed class ValidateInventoryHook : IEntityHook
{
    // YAML の hooks セクションで参照する名前（大文字小文字無視）
    public string Name => "validate_inventory";

    public async Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        // ctx.Values: フォームから送信されたフィールド値
        // ctx.Operation: CrudOperation.Create / Update / Delete
        // ctx.Entity: エンティティ名

        if (!ctx.Values.TryGetValue("quantity", out var rawQty))
            return HookResult.Continue();

        if (!int.TryParse(rawQty?.ToString(), out var qty) || qty < 0)
            return HookResult.Abort("数量は0以上の整数を入力してください。");

        // DB参照が必要な場合は db / tx を使う
        // var stock = await db.QueryFirstOrDefaultAsync<int>(
        //     "SELECT stock FROM inventory WHERE product_id = @id",
        //     new { id = ctx.Values["product_id"] }, tx);

        return HookResult.Continue();
    }

    public Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        // 書き込み後処理（通知・連携等）
        // トランザクションがまだ開いているので DB 更新も可能
        return Task.CompletedTask;
    }
}
```

### ステップ2: Program.cs に登録

```csharp
// NetYamlForge/Program.cs の AddProjectServices() 内に追加
builder.Services.AddSingleton<IEntityHook, ValidateInventoryHook>();
```

### ステップ3: YAMLにフック名を追加

```yaml
    hooks:
      beforeCreate: [validate_inventory]
      beforeUpdate: [validate_inventory]
```

### ステップ4: テストを追加（必須）

```csharp
// NetYamlForge.Tests/Hooks/ValidateInventoryHookTests.cs

public class ValidateInventoryHookTests
{
    private static EntityHookContext MakeContext(Dictionary<string, object?> values)
        => new() { Entity = "product", Operation = CrudOperation.Create, Values = values };

    [Fact]
    public async Task BeforeAsync_RejectsNegativeQuantity()
    {
        var sut = new ValidateInventoryHook();
        var ctx = MakeContext(new() { ["quantity"] = "-1" });

        var result = await sut.BeforeAsync(ctx, null!, null);

        Assert.True(result.Cancel);
        Assert.Contains("0以上", result.CancelMessage);
    }

    [Fact]
    public async Task BeforeAsync_AllowsZeroQuantity()
    {
        var sut = new ValidateInventoryHook();
        var ctx = MakeContext(new() { ["quantity"] = "0" });

        var result = await sut.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
    }

    [Fact]
    public async Task BeforeAsync_SkipsWhenFieldMissing()
    {
        var sut = new ValidateInventoryHook();
        var ctx = MakeContext(new());  // quantity フィールドなし

        var result = await sut.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);  // フィールドがなければスキップ
    }
}
```

---

## 検証チェックリスト

- [ ] `dotnet build` が通る
- [ ] `dotnet test` が全件パス
- [ ] フォームで不正値を入力したとき、適切なエラーメッセージが表示される
- [ ] 正常値を入力したとき、正常に保存される
- [ ] `before_create` と `before_update` の両方に追加したか確認
