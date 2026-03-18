# 確認ダイアログ・前処理・後処理フック 設計・実装ドキュメント

## 概要

新規作成・更新操作に対して、以下の 3 つの機能を YAML 設定のみで制御できます。

| 機能 | 設定キー | 説明 |
|------|----------|------|
| 確認ダイアログ | `confirmation` | 送信前にカスタムメッセージの確認モーダルを表示 |
| 前処理フック | `hooks.beforeCreate` / `hooks.beforeUpdate` | DB 書き込み前に任意ロジックを実行。キャンセル可 |
| 後処理フック | `hooks.afterCreate` / `hooks.afterUpdate` | DB 書き込み後（同一トランザクション内）に任意ロジックを実行 |

---

## YAML 設定リファレンス

```yaml
entities:
  customer:
    table: Customer
    key: CustomerId
    # ...（既存設定）

    # 確認ダイアログ（どちらか一方だけ設定することも可能）
    confirmation:
      create: "新しい顧客を登録してよろしいですか？"
      update: "顧客情報を更新してよろしいですか？"

    # フック設定（登録名は Program.cs に対応する IEntityHook が必要）
    hooks:
      beforeCreate: "customer_email_domain"   # 前処理：Email ドメイン検証
      afterCreate:  "console_log_after"       # 後処理：ログ記録
      beforeUpdate: "customer_email_domain"
      afterUpdate:  "console_log_after"
```

### 設定項目詳細

#### `confirmation`

| プロパティ | 型 | 省略時 | 説明 |
|------------|-----|--------|------|
| `create` | string | 確認なし | 新規作成時に表示するメッセージ |
| `update` | string | 確認なし | 更新時に表示するメッセージ |

#### `hooks`

| プロパティ | 型 | 省略時 | 説明 |
|------------|-----|--------|------|
| `beforeCreate` | string | なし | 新規作成の DB 書き込み前フック名 |
| `afterCreate`  | string | なし | 新規作成の DB 書き込み後フック名 |
| `beforeUpdate` | string | なし | 更新の DB 書き込み前フック名 |
| `afterUpdate`  | string | なし | 更新の DB 書き込み後フック名 |

> フック名は大文字・小文字を区別しません。

---

## 処理フロー

```
[ユーザーが Submit ボタンを押す]
        │
        ▼
[確認ダイアログ表示]  ← confirmation が設定されている場合のみ
    │ OK      │ キャンセル
    ▼         └─→ (処理中断)
[サーバーへ送信]
        │
        ▼
[入力値バリデーション]  ← ValueConverter によるフィールド検証
    │ エラーあり
    └─→ フォームを再描画（エラー表示）
        │ 正常
        ▼
[BeforeAsync フック実行]  ← hooks.beforeCreate / beforeUpdate
    │ HookResult.Abort() 返却
    └─→ フォームを再描画（エラーバナー表示）
        │ HookResult.Continue() 返却
        ▼
[トランザクション開始]
        │
        ├─→ INSERT / UPDATE
        ├─→ 監査ログ書き込み (AuditLog)
        └─→ [AfterAsync フック実行]  ← hooks.afterCreate / afterUpdate
                │ 例外発生 → Rollback
                ▼
        [トランザクションコミット]
```

---

## 確認ダイアログの仕組み

### クライアントサイド実装

`_Layout.cshtml` に配置された DaisyUI モーダルと JavaScript が、ページモード・モーダルモードで別の仕組みで動作します。

#### ページモード（通常 POST フォーム）

```
フォームに data-confirm-msg 属性あり
        │
        ▼
submit イベント（キャプチャフェーズ）でインターセプト
        │
        ▼
showConfirmDialog(msg, callback) を呼び出し
        │
   ┌────┴────┐
   │ OK      │ キャンセル
   ▼         └─→ (フォーム送信をキャンセル)
form.dataset.skipConfirm = '1' をセットして
form.requestSubmit() で再送（2重確認防止）
```

#### モーダルモード（HTMX フォーム）

```
フォームに hx-confirm="メッセージ" 属性あり
        │
        ▼
htmx:confirm イベントが発火
  ├─ メッセージが空 → evt.detail.issueRequest(true) で即リクエスト
  └─ メッセージあり →
        │
        ▼
    showConfirmDialog(msg, callback) を呼び出し
        │
   ┌────┴────┐
   │ OK      │ キャンセル
   ▼         └─→ (リクエストをキャンセル)
evt.detail.issueRequest(true)  ← HTMX 経由でリクエストを発行
```

> **重要**: HTMX フォームでは `submit` イベントの `evt.preventDefault()` を呼んでも XHR を止められません。
> そのため HTMX 組み込みの `hx-confirm` + `htmx:confirm` イベントを使います。

- **ページモード**: `data-confirm-msg` 属性 + `submit` イベントキャプチャ
- **モーダルモード（HTMX）**: `hx-confirm` 属性 + `htmx:confirm` イベント + `evt.detail.issueRequest(true)`
- `hx-confirm=""` 空文字の場合は確認なしで即リクエスト（確認不要エンティティで `hx-confirm` を省略できない場合の対応）

---

## フック実装ガイド

### 1. IEntityHook インターフェース

```csharp
public interface IEntityHook
{
    string Name { get; }

    // DB 書き込み前。HookResult.Abort() でキャンセル可能。
    Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx);

    // DB 書き込み後（同一 Tx 内）。例外でロールバック可能。
    Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx);
}
```

### 2. EntityHookContext（コンテキスト情報）

```csharp
public class EntityHookContext
{
    public string Entity { get; set; }            // エンティティ名（例: "customer"）
    public CrudOperation Operation { get; set; }  // Create or Update
    public int? Id { get; set; }                  // Update 時のレコード ID
    public Dictionary<string, object?> Values { get; set; }  // フォーム値（変換済み）
    public string? UserName { get; set; }         // 操作ユーザー名
    public Dictionary<string, object?> Data { get; set; }    // Before→After 間データ受け渡し用
}
```

### 3. HookResult（前処理の実行結果）

```csharp
// 続行する場合
return HookResult.Continue();

// キャンセルする場合（フォームにエラーバナーを表示）
return HookResult.Abort("エラーメッセージ");
```

### 4. 新しいフックを追加する手順

**Step 1: フッククラスを実装する**

```csharp
// Services/Hooks/MyHook.cs
public class MyHook : IEntityHook
{
    public string Name => "my_hook";  // entities.yml で参照するフック名

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // フォーム値の読み取り例
        if (ctx.Values.TryGetValue("FieldName", out var value))
        {
            // 検証ロジック
            if (/* 条件 */)
            {
                return Task.FromResult(HookResult.Abort("エラーメッセージ"));
            }

            // 値の書き換えも可能
            ctx.Values["FieldName"] = "変換後の値";
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 後処理ロジック（DB アクセスは db / tx を使用）
        return Task.CompletedTask;
    }
}
```

**Step 2: Program.cs に登録する**

```csharp
builder.Services.AddSingleton<IEntityHook, MyHook>();
// ※ IEntityHookRegistry の登録より前に追加すること
```

**Step 3: entities.yml に設定する**

```yaml
your_entity:
  hooks:
    beforeCreate: "my_hook"
    afterUpdate:  "my_hook"
```

---

## 組み込みサンプルフック一覧

### `customer_email_domain`

- **クラス**: `CustomerEmailDomainHook`
- **種別**: 前処理
- **対象エンティティ**: customer（他のエンティティでも使用可）
- **動作**: `Email` フィールドのドメインが `blocked.example.com` / `spam.test` の場合に登録を拒否する
- **カスタマイズ**: `BlockedDomains` フィールドに拒否ドメインを追加

```yaml
hooks:
  beforeCreate: "customer_email_domain"
  beforeUpdate: "customer_email_domain"
```

---

### `customer_name_normalize`

- **クラス**: `CustomerNameNormalizeHook`
- **種別**: 前処理（値変換）
- **対象エンティティ**: customer
- **動作**: `FirstName` / `LastName` の先頭文字を大文字に正規化する
- **キャンセルなし**: `HookResult.Continue()` のみを返す（値書き換えパターンのサンプル）

```yaml
hooks:
  beforeCreate: "customer_name_normalize"
  beforeUpdate: "customer_name_normalize"
```

---

### `invoice_minimum_total`

- **クラス**: `InvoiceMinimumTotalHook`
- **種別**: 前処理
- **対象エンティティ**: invoice（他のエンティティでも使用可）
- **動作**: `Total` フィールドが $0.01 未満の場合に登録を拒否する

```yaml
hooks:
  beforeCreate: "invoice_minimum_total"
  beforeUpdate: "invoice_minimum_total"
```

---

### `console_log_after`

- **クラス**: `ConsoleLogAfterHook`
- **種別**: 後処理
- **対象エンティティ**: 任意
- **動作**: DB 書き込み完了後に操作内容（操作種別・エンティティ名・ユーザー名・フィールド名）をアプリログに記録する

```yaml
hooks:
  afterCreate: "console_log_after"
  afterUpdate: "console_log_after"
```

---

## ファイル構成

```
NetYamlForge/
├── config/
│   └── entities.yml                       # confirmation / hooks 設定を追加
├── docs/
│   └── confirmation-and-hooks.md          # 本ドキュメント
├── Models/
│   └── EntityMetadata.cs                  # ConfirmationDefinition / EntityHooksDefinition 追加
├── Services/
│   └── Hooks/
│       ├── EntityHookContext.cs           # CrudOperation / HookResult / EntityHookContext
│       ├── IEntityHook.cs                 # フックインターフェース
│       ├── IEntityHookRegistry.cs         # レジストリインターフェース
│       ├── EntityHookRegistry.cs          # レジストリ実装（DI で IEntityHook[] を受け取り）
│       └── SampleHooks.cs                 # 4 種類のサンプルフック実装
├── Controllers/
│   └── DynamicEntityController.cs        # Create / Edit にフック呼び出しを追加
├── Views/
│   ├── DynamicEntity/
│   │   └── _Form.cshtml                  # data-confirm-msg 属性 / エラーバナー追加
│   └── Shared/
│       └── _Layout.cshtml                # 確認ダイアログモーダル + JS 追加
└── Program.cs                             # IEntityHook 実装と IEntityHookRegistry の DI 登録
```

---

## entities.yml 設定例（全エンティティ対応バリエーション）

### 確認のみ（フックなし）

```yaml
artist:
  confirmation:
    create: "アーティストを登録しますか？"
    update: "アーティスト名を更新しますか？"
```

### 前処理のみ（確認なし）

```yaml
genre:
  hooks:
    beforeCreate: "my_validation_hook"
    beforeUpdate: "my_validation_hook"
```

### 後処理のみ（確認なし）

```yaml
album:
  hooks:
    afterCreate: "console_log_after"
    afterUpdate: "console_log_after"
```

### 確認 + 前処理 + 後処理（フル構成）

```yaml
customer:
  confirmation:
    create: "新しい顧客を登録してよろしいですか？"
    update: "顧客情報を更新してよろしいですか？"
  hooks:
    beforeCreate: "customer_email_domain"
    afterCreate:  "console_log_after"
    beforeUpdate: "customer_email_domain"
    afterUpdate:  "console_log_after"
```
