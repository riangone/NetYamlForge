# UI設計ガイド（YAML駆動コンポーネント）

## 1. 目的

本ガイドは、画面実装を「個別Razorの都度実装」から「共通コンポーネント + YAML定義」へ寄せるための基準です。  
対象は NetYamlForge の `DynamicEntity` / `Page` 系画面です。

## 2. 基本方針

1. 見た目は共通コンポーネントで統一する。
2. 画面差分は YAML で宣言する。
3. 状態（search/filter/sort/page）は URL に反映する。
4. 文言は `labelI18n` で管理し、画面ロジックに直書きしない。

## 3. レイヤ構成

1. デザインレイヤ（トークン）
- 色、余白、角丸、タイポ、ブレークポイント

2. コンポーネントレイヤ（再利用部品）
- `FilterBar` / `DataTable` / `FormPanel` / `EntityPicker` / `StatsCard` / `ActionBar` / `ConfirmDialog`

3. ページDSLレイヤ（YAML）
- 「どのコンポーネントを、どのデータで、どう配置するか」を定義

## 4. コンポーネント最小仕様

1. `FilterBar`
- 入力型: `text`, `dropdown`, `multi-select`, `range`, `date-range`, `entity-picker`, `entity-multi-picker`
- 必須挙動: `Search` / `Clear` / `count=true` 維持

2. `DataTable`
- カラム定義、ソート可否、ページング、総件数表示
- 空データ時の Empty state

3. `FormPanel`
- 作成・編集の共通フォーム
- 変換エラー表示、確認ダイアログ、保存後の戻り先

4. `EntityPicker`
- 単一・複数選択、`displayColumns` 対応
- hidden 値と表示ラベルの同期

## 5. YAML DSL設計ルール

1. 互換性
- 既存 `pages/*.yaml` の `title` / `sections` 形式を壊さない。
- 拡張キーは `ui` 配下に置く（現行実装では無視されても安全）。

2. 可読性
- 画面構造は `layout`、見た目は `ui`、データは `source` 系に分離する。

3. 検証
- `Schemas/ui-page-schema.json` で最低限のキーと型を検証する。

## 6. 推奨YAML骨子

```yaml
title: 受発注ワークベンチ
description: 受注と明細を同一画面で確認する
main_table: Orders

ui:
  page:
    layout: two-pane
    density: comfortable

sections:
  - id: orders
    source_type: table
    source: Orders
    columns: [OrderId, CustomerId, OrderDate, Status]
    page_size: 20
    ui:
      component: DataTable
      selectable: single
```

## 7. 実装時チェック

1. `Clear` 後に picker 値が残らない。
2. 再検索後も `Total` 表示が維持される。
3. ブラウザ戻るで状態復元できる。
4. 主要操作のURLを共有して同じ結果を再現できる。
