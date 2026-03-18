# 例01: 既存エンティティへの列追加（YAMLのみ）

## 概要

既存テーブルに新しい列を追加して一覧・フォームに表示する。
**コード変更は一切不要。YAMLのみ変更する。**

---

## 前提条件

- DBテーブルに列が既に存在する（または `ALTER TABLE` でDBに追加済み）
- エンティティYAMLが `projects/<name>/entities/<entity>.yml` に存在する

---

## 変更ファイル

```
projects/<name>/entities/customer.yml  ← 変更するのはこれだけ
```

---

## 変更手順と差分

### ケース1: 単純な文字列列を一覧に表示する

```yaml
# Before
entities:
  Customer:
    table: customer
    key: id
    displayName: "Customers"
    columns:
      firstName: {type: string}
      email: {type: string}

# After（phone 列を追加）
entities:
  Customer:
    table: customer
    key: id
    displayName: "Customers"
    columns:
      firstName: {type: string}
      email: {type: string}
      phone: {type: string, displayName: "電話番号"}  # ← 追加
```

### ケース2: 一覧表示 + フォーム編集も可能にする

```yaml
    columns:
      phone: {type: string, displayName: "電話番号"}  # 一覧に表示

    forms:
      create:
        fields: [firstName, email, phone]  # ← phone を追加
      edit:
        fields: [firstName, email, phone]  # ← phone を追加
```

### ケース3: 結合テーブルの列を表示する（Expression使用）

```yaml
    # joins セクションで結合を定義
    joins:
      - table: country
        alias: c
        on: "customer.country_id = c.id"
        type: left

    columns:
      # DBの実列
      firstName: {type: string}
      # 結合テーブルの列（Expression で指定）
      countryName:
        type: string
        displayName: "国"
        expression: "c.name"   # ← 結合テーブルの列を指定
        sortable: false         # 結合列はソート無効推奨
```

### ケース4: 計算列（DB関数を使う）

```yaml
    columns:
      fullName:
        type: string
        displayName: "氏名"
        expression: "firstName || ' ' || lastName"  # SQLite
        # SQL Server の場合: "firstName + ' ' + lastName"
        # ※ SQL Server 用は entities-sqlserver/<entity>.yml にも追記
```

---

## 検証チェックリスト

- [ ] `dotnet build` が通る
- [ ] 一覧ページで新しい列が表示される
- [ ] フォームで新しい列が入力できる（forms に追加した場合）
- [ ] 検索フィルターが必要なら `filters` セクションに追加したか確認

---

## よくある間違い

| 間違い | 正しい方法 |
|--------|-----------|
| `table:` の値を変更した | `table:` はDBの実テーブル名なので変更禁止 |
| 結合列に `sortable: true` を設定 | 結合列のソートはSQLが複雑になるので `sortable: false` |
| `key:` フィールドを `forms` に含めた | PKは自動管理されるので forms に含めない |
| `expression` に未サニタイズの値を埋め込む | expression は英数字・ドット・演算子のみ許可 |
