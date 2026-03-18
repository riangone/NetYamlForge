# 例05: 独自フックの実装テンプレート

## 概要

`docs/COMMON_HOOKS.md` に記載された既存フックで対応できない業務ロジックを
プロジェクト固有フックとして実装する手順とテンプレート。

**前提: 先に `docs/COMMON_HOOKS.md` を確認し、既存フックで対応不可であることを確認すること。**

---

## ファイル構成

```
projects/<name>/
└── Hooks/
    └── <ProjectName>Hooks.cs     ← プロジェクト固有フックをまとめて定義

NetYamlForge/
└── Program.cs                    ← DI登録を追加

NetYamlForge.Tests/
└── Hooks/
    └── <HookName>HookTests.cs    ← テストは必須
```

---

## フックテンプレート

```csharp
// projects/<name>/Hooks/<ProjectName>Hooks.cs

using System.Data;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.<ProjectName>.Hooks;

/// <summary>
/// 責務: <プロジェクト名> 専用フックを提供する。
/// 各フッククラスは単一責任（1クラス1目的）を維持すること。
/// </summary>

// ─────────────────────────────────────────────────────────────
// バリデーションフックのテンプレート
// ─────────────────────────────────────────────────────────────
public sealed class <HookName>Hook : IEntityHook
{
    // YAML hooks セクションで使う名前（スネークケース推奨、大文字小文字無視）
    public string Name => "<hook_name>";

    public async Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        // ctx.Values     : フォームフィールド値 Dictionary<string, object?>
        // ctx.Operation  : CrudOperation.Create / Update / Delete
        // ctx.Entity     : エンティティ名（例: "Customer"）
        // ctx.ExistingId : 更新/削除時の主キー値

        // --- フィールド値の取得パターン ---
        if (!ctx.Values.TryGetValue("fieldName", out var rawValue))
            return HookResult.Continue();  // フィールドがなければスキップ

        var value = rawValue?.ToString()?.Trim();
        if (string.IsNullOrEmpty(value))
            return HookResult.Continue();

        // --- バリデーション失敗時 ---
        // return HookResult.Abort("エラーメッセージ（ユーザーに表示される）");

        // --- DB参照が必要な場合（同一トランザクション内で実行）---
        // var count = await db.ExecuteScalarAsync<int>(
        //     "SELECT COUNT(*) FROM table WHERE col = @val AND id != @id",
        //     new { val = value, id = ctx.ExistingId }, tx);
        // if (count > 0) return HookResult.Abort("既に存在します。");

        return HookResult.Continue();
    }

    public Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        // 書き込み成功後の後処理（通知・連携・カウント更新等）
        // トランザクション内なのでDB更新も可能（tx を渡すこと）
        return Task.CompletedTask;
    }
}

// ─────────────────────────────────────────────────────────────
// データ変換フックのテンプレート（BeforeAsync でフィールド値を変換）
// ─────────────────────────────────────────────────────────────
public sealed class <TransformName>Hook : IEntityHook
{
    public string Name => "<transform_name>";

    public Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        // ctx.Values は参照型なので直接書き換えられる
        if (ctx.Values.TryGetValue("fieldName", out var raw) && raw is string s)
            ctx.Values["fieldName"] = s.Trim().ToUpperInvariant();

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
```

---

## Program.cs への登録

```csharp
// NetYamlForge/DI/ProjectServices.cs (AddProjectServices メソッド内)

// または Program.cs の builder.Services セクション
builder.Services.AddSingleton<IEntityHook, <HookName>Hook>();
builder.Services.AddSingleton<IEntityHook, <TransformName>Hook>();
```

---

## YAMLへの追加

```yaml
# projects/<name>/entities/<entity>.yml

    hooks:
      beforeCreate:             # ← camelCase を使う
        - trim                  # 既存フックは先に実行
        - <hook_name>           # 独自フックは後に実行
      beforeUpdate:
        - trim
        - <hook_name>
      afterCreate:
        - audit_log
```

---

## テストテンプレート（必須）

```csharp
// NetYamlForge.Tests/Hooks/<HookName>HookTests.cs

using System.Data;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Projects.<ProjectName>.Hooks;
using Xunit;

namespace NetYamlForge.Tests.Hooks;

public class <HookName>HookTests
{
    // ヘルパー: テスト用コンテキスト生成
    private static EntityHookContext MakeCtx(
        Dictionary<string, object?> values,
        CrudOperation op = CrudOperation.Create)
        => new() { Entity = "<entity>", Operation = op, Values = values };

    // ─── 正常系 ───
    [Fact]
    public async Task BeforeAsync_ValidInput_ReturnsContinue()
    {
        var sut = new <HookName>Hook();
        var ctx = MakeCtx(new() { ["fieldName"] = "valid_value" });

        var result = await sut.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
    }

    // ─── 異常系 ───
    [Fact]
    public async Task BeforeAsync_InvalidInput_ReturnsAbort()
    {
        var sut = new <HookName>Hook();
        var ctx = MakeCtx(new() { ["fieldName"] = "invalid_value" });

        var result = await sut.BeforeAsync(ctx, null!, null);

        Assert.True(result.Cancel);
        Assert.NotEmpty(result.CancelMessage);
    }

    // ─── フィールドなし（スキップ確認）───
    [Fact]
    public async Task BeforeAsync_MissingField_ReturnsContinue()
    {
        var sut = new <HookName>Hook();
        var ctx = MakeCtx(new());  // フィールドなし

        var result = await sut.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);  // フィールドがなければスキップする
    }

    // ─── 操作種別ごとの分岐がある場合 ───
    [Theory]
    [InlineData(CrudOperation.Create)]
    [InlineData(CrudOperation.Update)]
    public async Task BeforeAsync_AppliesTo_CreateAndUpdate(CrudOperation op)
    {
        var sut = new <HookName>Hook();
        var ctx = MakeCtx(new() { ["fieldName"] = "valid" }, op);

        var result = await sut.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
    }
}
```

---

## フックの命名規則

| 種別 | 命名パターン | 例 |
|-----|------------|-----|
| バリデーション | `validate_<対象>` | `validate_inventory`, `validate_age` |
| 変換・正規化 | `normalize_<対象>` または動詞形 | `normalize_phone`, `format_postal_code` |
| 通知・連携 | `notify_<対象>` | `notify_manager`, `notify_slack` |
| 監査 | `audit_<対象>` | `audit_status_change` |
| 自動セット | `set_<フィールド>` | `set_approved_by`, `set_sequence_no` |

---

## 禁止事項

```csharp
// ❌ フック内でHTTPリクエストを同期的にブロック
var resp = httpClient.GetAsync("...").Result;

// ❌ フック内で例外をキャッチして握りつぶす
try { ... } catch { }  // ← エラーが隠蔽される

// ❌ フック名にスペースや特殊文字を使う
public string Name => "my hook!";  // ← YAMLで参照できない

// ✅ 非同期チェーンを維持し、エラーは HookResult.Abort() で返す
var resp = await httpClient.GetAsync("...");
if (!resp.IsSuccessStatusCode) return HookResult.Abort("外部サービス連携に失敗しました。");
```
