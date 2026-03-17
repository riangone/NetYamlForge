# Salesforce CRM Clone

## 概要
`salesforce-crm` は、既存の Northwind データを利用して Salesforce 風の CRM 運用体験を再現するサブプロジェクトです。

## ドキュメント一覧
- 開発・利用チュートリアル: `docs/development-and-usage-tutorial-ja.md`
- 業務フロー設計: `docs/business-flow-design-ja.md`
- 実装ステータス: `docs/crm-implementation-status-ja.md`
- 実装ガイド: `docs/implementation-guide-ja.md`
- UATシナリオ: `docs/uat-scenarios-ja.md`

## 実装方針
- CRUD は既存エンティティ（order/customer/orderdetail など）をそのまま利用
- 専用業務画面は `pages/*.yaml` で構成
- 主要導線:
  - Executive Cockpit
  - Lead Command Center
  - Opportunity Workspace
  - Service Desk 360
  - Pipeline Board

## 多言語
`config/i18n.yml` に `en-US / zh-CN / ja-JP / ko-KR` を定義し、韓国語表示を含む多言語 UI を有効化しています。

## データソース
`../northwind-sqlite3/database/northwind.db` を参照します。
