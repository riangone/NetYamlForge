# 複数フックサポートガイド

## 概要

NetYamlForge では、1 つの操作に対して複数のフックを登録できるようになりました。

## 機能

### サポートされるフックポイント

- `beforeCreate` - 新規作成前の検証・変換
- `afterCreate` - 新規作成後の処理
- `beforeUpdate` - 更新前の検証・変換
- `afterUpdate` - 更新後の処理
- `beforeDelete` - 削除前の検証
- `afterDelete` - 削除後の処理

### 実行順序

フックは YAML で指定した順序で順次実行されます。

```
beforeCreate フック 1 → フック 2 → フック 3 → DB 書き込み → afterCreate フック 1 → フック 2
```

### エラー処理

- **Before フック**: どれか 1 つでもキャンセルを返すと、以降のフックと DB 操作が中止されます
- **After フック**: エラーが発生してもログに記録され、処理は継続されます

## YAML 設定例

### Customer（顧客管理）

```yaml
entities:
  customer:
    hooks:
      beforeCreate:
        - "validate_email"      # 1. メール形式検証
        - "validate_required"   # 2. 必須項目検証
        - "trim"                # 3. 文字列トリム
      afterCreate:
        - "chinook_customer_welcome"  # 1. ウェルカムメール
        - "audit_log"                 # 2. 監査ログ記録
      beforeUpdate:
        - "validate_email"
        - "validate_required"
        - "trim"
      afterUpdate:
        - "audit_log"
```

### Invoice（請求書管理）

```yaml
entities:
  invoice:
    hooks:
      beforeCreate:
        - "chinook_invoice_validation"  # 1. 金額検証
        - "validate_required"           # 2. 必須項目検証
      afterCreate:
        - "audit_log"                   # 1. 監査ログ
        - "console_log_after"           # 2. コンソールログ
      beforeUpdate:
        - "chinook_invoice_validation"
        - "validate_required"
      afterUpdate:
        - "audit_log"
```

### Track（トラック管理）

```yaml
entities:
  track:
    hooks:
      beforeCreate:
        - "validate_required"
        - "trim"
      afterCreate:
        - "audit_log"
      beforeUpdate:
        - "chinook_track_duration"  # 再生時間変換
        - "validate_required"
        - "trim"
      afterUpdate:
        - "audit_log"
```

### Artist（アーティスト管理）

```yaml
entities:
  artist:
    hooks:
      beforeDelete:
        - "chinook_artist_delete_check"  # 関連チェック
      afterDelete:
        - "audit_log"
```

## 使用可能なフック

### 汎用フック

| フック名 | 説明 | 使用例 |
|---------|------|--------|
| `validate_email` | メールアドレス形式検証 | beforeCreate, beforeUpdate |
| `validate_phone` | 電話番号形式検証 | beforeCreate, beforeUpdate |
| `validate_required` | 必須項目検証 | beforeCreate, beforeUpdate |
| `validate_range` | 範囲検証 | beforeCreate, beforeUpdate |
| `validate_unique` | 一意性検証 | beforeCreate, beforeUpdate |
| `trim` | 文字列トリム | beforeCreate, beforeUpdate |
| `uppercase` | 大文字変換 | beforeCreate, beforeUpdate |
| `lowercase` | 小文字変換 | beforeCreate, beforeUpdate |
| `audit_log` | 監査ログ記録 | afterCreate, afterUpdate, afterDelete |
| `console_log_after` | コンソールログ出力 | afterCreate, afterUpdate |

### プロジェクト固有フック（Chinook）

| フック名 | 説明 | 使用例 |
|---------|------|--------|
| `chinook_customer_welcome` | 顧客ウェルカムメール | afterCreate |
| `chinook_invoice_validation` | 請求書金額検証 | beforeCreate, beforeUpdate |
| `chinook_track_duration` | 再生時間変換（ms→秒） | beforeUpdate |
| `chinook_artist_delete_check` | アーティスト削除時関連チェック | beforeDelete |

## 実装詳細

### モデル（EntityMetadata.cs）

```csharp
public class EntityHooksDefinition
{
    public List<string>? BeforeCreate { get; set; }
    public List<string>? AfterCreate { get; set; }
    public List<string>? BeforeUpdate { get; set; }
    public List<string>? AfterUpdate { get; set; }
    public List<string>? BeforeDelete { get; set; }
    public List<string>? AfterDelete { get; set; }
}
```

### 実行ロジック（DynamicEntityController.cs）

```csharp
private async Task<HookResult> RunBeforeHookAsync(List<string>? hookNames, EntityHookContext ctx)
{
    foreach (var hookName in hookNames)
    {
        // プロジェクト固有フックまたは汎用フックを実行
        var result = await hook.BeforeAsync(ctx, _db, null);
        if (result.Cancel)
        {
            return result; // エラーの場合は中断
        }
    }
    return HookResult.Continue();
}
```

## 注意事項

1. **YAML 形式**: 必ずリスト形式を使用してください
   ```yaml
   # 正しい形式
   hooks:
     beforeCreate:
       - "validate_email"
       - "validate_required"
   
   # 誤り（単一文字列）
   hooks:
     beforeCreate: "validate_email"
   ```

2. **実行順序**: フックは YAML で記述した順序で実行されます

3. **エラー処理**: Before フックでエラーが発生すると、その後の処理がすべてキャンセルされます

4. **パフォーマンス**: 多数のフックを登録すると、処理時間が増加する可能性があります

## 関連ドキュメント

- [`docs/chinook-yaml-examples.md`](chinook-yaml-examples.md) - Chinook YAML 設定例
- [`docs/project-hooks-guide.md`](project-hooks-guide.md) - プロジェクト固有フックガイド
