# Chinook プロジェクト YAML 設定例

このドキュメントでは、プロジェクト固有フックとビジネスロジックを使用した YAML 設定例を紹介します。

## 目次

1. [Customer（顧客管理）](#customer顧客管理)
2. [Invoice（請求書管理）](#invoice請求書管理)
3. [Track（トラック管理）](#trackトラック管理)
4. [Artist（アーティスト管理）](#artistアーティスト管理)
5. [フック設定の構文](#フック設定の構文)
6. [使用可能なフック一覧](#使用可能なフック一覧)

---

## Customer（顧客管理）

**ファイル**: `projects/chinook/entities/customer.yml`

```yaml
entities:
  customer:
    table: Customer
    key: CustomerId
    displayName: Customer
    displayNameI18n:
      en-US: Customer
      zh-CN: 客户
      ja-JP: 顧客
    
    # ページング設定
    paging:
      pageSize: 10
      mode: numbered
    
    # フック設定
    hooks:
      beforeCreate: "validate_email"           # メール形式検証
      afterCreate: "chinook_customer_welcome"  # ウェルカムメール送信
      beforeUpdate: "validate_email"           # メール形式検証
      afterUpdate: "audit_log"                 # 監査ログ記録
    
    # 確認ダイアログ
    confirmation:
      create: "新しい顧客を登録してよろしいですか？"
      update: "顧客情報を更新してよろしいですか？"
    
    # 列定義
    columns:
      CustomerId:
        type: int
        identity: true
        label: ID
      FirstName:
        type: string
        required: true
        label: First Name
      LastName:
        type: string
        required: true
        label: Last Name
      Email:
        type: email
        required: true
        label: Email
      Country:
        type: string
        label: Country
    
    # フォーム定義
    forms:
      FirstName:
        type: string
        required: true
        editable: true
      Email:
        type: email
        required: true
        editable: true
    
    # フィルター定義
    filters:
      Country:
        type: multi-select
        options: [USA, Canada, Brazil, France, Germany]
    
    # リンク定義
    links:
      invoices:
        label: Invoices
        targetEntity: invoice
        filter:
          CustomerId: CustomerId
```

---

## Invoice（請求書管理）

**ファイル**: `projects/chinook/entities/invoice.yml`

```yaml
entities:
  invoice:
    table: Invoice
    key: InvoiceId
    displayName: Invoice
    displayNameI18n:
      en-US: Invoice
      zh-CN: 发票
      ja-JP: 請求書
    
    # フック設定
    hooks:
      beforeCreate: "chinook_invoice_validation"  # 請求書金額検証
      afterCreate: "audit_log"                    # 監査ログ記録
      beforeUpdate: "chinook_invoice_validation"  # 請求書金額検証
      afterUpdate: "audit_log"                    # 監査ログ記録
    
    # 確認ダイアログ
    confirmation:
      create: "新しい請求書を作成してよろしいですか？"
      update: "請求書を更新してよろしいですか？"
    
    # 列定義（一部）
    columns:
      InvoiceId:
        type: int
        identity: true
      CustomerId:
        type: int
        label: Customer
      Total:
        type: decimal
        label: Total
    
    # フォーム定義
    forms:
      CustomerId:
        type: int
        foreignKey:
          entity: customer
          displayColumn: LastName
      Total:
        type: decimal
        editable: true
    
    # フィルター定義
    filters:
      BillingCountry:
        type: multi-select
        options: [USA, Canada, Brazil, France, Germany]
      InvoiceDate:
        type: date-range
      Total:
        type: range
```

---

## Track（トラック管理）

**ファイル**: `projects/chinook/entities/track.yml`

```yaml
entities:
  track:
    table: Track
    key: TrackId
    displayName: Track
    displayNameI18n:
      en-US: Track
      zh-CN: 曲目
      ja-JP: トラック
    
    # フック設定
    hooks:
      beforeCreate: "validate_required"       # 必須項目検証
      afterCreate: "audit_log"                # 監査ログ記録
      beforeUpdate: "chinook_track_duration"  # 再生時間変換（ms→秒）
      afterUpdate: "audit_log"                # 監査ログ記録
    
    # 確認ダイアログ
    confirmation:
      create: "新しいトラックを追加してよろしいですか？"
      update: "トラック情報を更新してよろしいですか？"
    
    # 列定義（一部）
    columns:
      TrackId:
        type: int
        identity: true
      Name:
        type: string
        label: Name
      Milliseconds:
        type: int
        label: Milliseconds
      UnitPrice:
        type: decimal
        label: Unit Price
    
    # フォーム定義
    forms:
      Name:
        type: string
        required: true
        editable: true
      Milliseconds:
        type: int
        required: true
        editable: true
    
    # フィルター定義
    filters:
      GenreId:
        type: dropdown
        foreignKey:
          entity: genre
          displayColumn: Name
```

---

## Artist（アーティスト管理）

**ファイル**: `projects/chinook/entities/artist.yml`

```yaml
entities:
  artist:
    table: Artist
    key: ArtistId
    displayName: Artist
    displayNameI18n:
      en-US: Artist
      zh-CN: 艺术家
      ja-JP: アーティスト
    
    # フック設定（削除フック）
    hooks:
      beforeDelete: "chinook_artist_delete_check"  # 関連アルバムチェック
      afterDelete: "audit_log"                     # 監査ログ記録
    
    # 確認ダイアログ（削除時）
    confirmation:
      create: "新しいアーティストを登録してよろしいですか？"
      update: "アーティスト情報を更新してよろしいですか？"
      delete: "このアーティストを削除してもよろしいですか？関連するアルバムも影響を受ける可能性があります。"
    
    # 列定義
    columns:
      ArtistId:
        type: int
        identity: true
      Name:
        type: string
        label: Name
        required: true
    
    # フォーム定義
    forms:
      Name:
        type: string
        required: true
        editable: true
    
    # リンク定義
    links:
      albums:
        label: Albums
        targetEntity: album
        filter:
          ArtistId: ArtistId
```

---

## フック設定の構文

### 基本構文

```yaml
hooks:
  beforeCreate: "フック名"    # 新規作成前の検証・変換
  afterCreate: "フック名"     # 新規作成後の処理
  beforeUpdate: "フック名"    # 更新前の検証・変換
  afterUpdate: "フック名"     # 更新後の処理
  beforeDelete: "フック名"    # 削除前の検証
  afterDelete: "フック名"     # 削除後の処理
```

### 確認ダイアログ

```yaml
confirmation:
  create: "新規作成確認メッセージ"
  update: "更新確認メッセージ"
  delete: "削除確認メッセージ"
```

### 注意事項

1. **単一フックのみサポート**: 現在、各操作で 1 つのフックのみ実行可能です
2. **フック名の指定**: 文字列でフック名を指定します
3. **実行順序**: 
   - before フック → DB 操作 → after フック

---

## 使用可能なフック一覧

### 汎用フック（フレームワーク標準）

| フック名 | 説明 | 使用例 |
|---------|------|--------|
| `validate_email` | メールアドレス形式検証 | beforeCreate, beforeUpdate |
| `validate_phone` | 電話番号形式検証 | beforeCreate, beforeUpdate |
| `validate_required` | 必須項目検証 | beforeCreate, beforeUpdate |
| `validate_range` | 範囲検証 | beforeCreate, beforeUpdate |
| `trim` | 文字列トリム | beforeCreate, beforeUpdate |
| `uppercase` | 大文字変換 | beforeCreate, beforeUpdate |
| `lowercase` | 小文字変換 | beforeCreate, beforeUpdate |
| `audit_log` | 監査ログ記録 | afterCreate, afterUpdate, afterDelete |
| `console_log_after` | コンソールログ出力 | afterCreate, afterUpdate |

### プロジェクト固有フック（Chinook）

| フック名 | 説明 | 使用例 |
|---------|------|--------|
| `chinook_customer_welcome` | 顧客ウェルカムメール送信 | afterCreate |
| `chinook_invoice_validation` | 請求書金額検証 | beforeCreate, beforeUpdate |
| `chinook_track_duration` | 再生時間変換（ms→秒） | beforeUpdate |
| `chinook_artist_delete_check` | アーティスト削除時関連チェック | beforeDelete |

### プロジェクト固有ビジネスロジック（Chinook）

| クラス | 説明 |
|-------|------|
| `ChinookBusinessLogic` | 売上計算検証、顧客管理、トラック検証 |
| `ChinookValidator` | 請求書・従業員の独自検証 |
| `ChinookDataTransformer` | 名前・トラック名のトリム処理 |

---

## 実装パターン

### パターン 1: 新規作成時（メール検証 + ウェルカムメール）

```yaml
hooks:
  beforeCreate: "validate_email"           # 1. メール形式検証
  afterCreate: "chinook_customer_welcome"  # 2. ウェルカムメール送信
```

### パターン 2: 更新時（金額検証 + 監査ログ）

```yaml
hooks:
  beforeUpdate: "chinook_invoice_validation"  # 1. 金額検証
  afterUpdate: "audit_log"                    # 2. 監査ログ記録
```

### パターン 3: 削除時（関連チェック + 監査ログ）

```yaml
hooks:
  beforeDelete: "chinook_artist_delete_check"  # 1. 関連データチェック
  afterDelete: "audit_log"                     # 2. 監査ログ記録
```

---

## 関連ドキュメント

- [`docs/project-hooks-guide.md`](project-hooks-guide.md) - プロジェクト固有フックガイド
- [`projects/chinook/Hooks/`](projects/chinook/Hooks/) - Chinook フック実装
