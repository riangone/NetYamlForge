# 例03: 新エンティティの追加（全手順）

## 概要

既存プロジェクトに新しいエンティティ（テーブル + CRUD画面）を追加する。
基本的にYAMLとDBスキーマの変更のみ。コードは原則不要。

---

## 変更ファイル

```
projects/<name>/entities/<new-entity>.yml  ← 新規作成
projects/<name>/database/<name>.db         ← テーブル追加（SQLで実行）
projects/<name>/project.yaml               ← ナビゲーションに追加（任意）
```

---

## ステップ1: DBテーブルを追加

```sql
-- projects/<name>/database/<name>.db に対して実行
CREATE TABLE IF NOT EXISTS product (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT NOT NULL,
    price       REAL NOT NULL DEFAULT 0,
    category_id INTEGER,
    is_active   INTEGER NOT NULL DEFAULT 1,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    is_deleted  INTEGER NOT NULL DEFAULT 0
);
```

**規則:**
- 主キーは `INTEGER PRIMARY KEY AUTOINCREMENT`（SQLite）
- ソフトデリートを使う場合: `is_deleted INTEGER NOT NULL DEFAULT 0` を追加
- 作成日時: `created_at TEXT NOT NULL DEFAULT (datetime('now'))`

---

## ステップ2: エンティティYAMLを作成

```yaml
# projects/<name>/entities/product.yml

entities:
  Product:
    table: product
    key: id
    displayName: "Products"
    displayNameI18n:
      en-US: "Products"
      ja-JP: "商品"

    softDelete: true   # is_deleted 列がある場合

    # --- 一覧表示列 ---
    columns:
      name:
        type: string
        displayName: "商品名"
        searchable: true    # 検索バーの対象にする
        sortable: true
      price:
        type: decimal
        displayName: "価格"
        sortable: true
      categoryName:
        type: string
        displayName: "カテゴリ"
        expression: "c.name"   # JOINした列（下の joins 参照）
        sortable: false
      isActive:
        type: boolean
        displayName: "有効"

    # --- テーブル結合 ---
    joins:
      - table: category
        alias: c
        on: "product.category_id = c.id"
        type: left

    # --- フィルター ---
    filters:
      isActive:
        column: is_active
        type: boolean
        displayName: "有効のみ"

    # --- 作成/編集フォーム ---
    forms:
      create:
        fields: [name, price, category_id, isActive]
      edit:
        fields: [name, price, category_id, isActive]

    # --- 外部キー（フォームのセレクトボックス）---
    # category_id の入力をカテゴリのセレクトボックスにする
    # columns セクションで FK として定義
    # （entities/category.yml が存在する必要がある）
    columns:
      category_id:
        type: int
        displayName: "カテゴリID"
        foreignKey:
          entity: Category
          displayColumn: name

    # --- フック（camelCase を使う）---
    hooks:
      beforeCreate: [trim, validate_required]
      beforeUpdate: [trim]
      afterCreate: [audit_log]
      afterUpdate: [audit_log]

    # --- ページング ---
    paging:
      defaultPageSize: 20
```

---

## ステップ3: ナビゲーションに追加（任意）

```yaml
# projects/<name>/project.yaml

layout:
  navigation:
    entities:
      - customer
      - product    # ← 追加
    items:
      - label: Products
        url: /<project>/DynamicEntity/Index?entity=product
        icon: 📦
```

---

## ステップ4: スキャフォールドを活用する場合

既存DBからYAMLを自動生成できる（その後手動で調整）：

```bash
dotnet run -- --scaffold-entities --project=<name> --no-overwrite
```

---

## 最小構成YAML（迷ったらここから始める）

外部キー・結合・フックなしの最小構成：

```yaml
entities:
  Product:
    table: product
    key: id
    displayName: "Products"
    columns:
      name: {type: string, searchable: true}
      price: {type: decimal}
    forms:
      create: {fields: [name, price]}
      edit: {fields: [name, price]}
```

---

## 検証チェックリスト

- [ ] `dotnet build` が通る
- [ ] `/project/DynamicEntity/Index?entity=Product` が表示される
- [ ] 一覧ページで列が表示される
- [ ] 新規作成フォームが正常動作する
- [ ] 編集フォームが正常動作する
- [ ] （softDelete: true の場合）削除後に一覧から消える
- [ ] 検索・フィルターが動作する

---

## よくある間違い

| 間違い | 正しい方法 |
|--------|-----------|
| `key:` にDB列名と違う名前を指定 | `key:` はDB主キー列名と完全一致させる |
| `forms` に `key` フィールドを含める | PKは自動付与されるので forms には含めない |
| joinの alias なしで expression を書く | alias を定義してから `alias.column` の形式で書く |
| `softDelete: true` だが `is_deleted` 列がない | DBテーブルに `is_deleted` 列を追加する |
