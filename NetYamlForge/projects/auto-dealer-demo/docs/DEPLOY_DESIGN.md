# auto-dealer-demo 環境導入 詳細設計・作業指示書

## 概要

本書は `auto-dealer-demo` サブプロジェクトを本番環境へ導入するための詳細設計と実装作業内容を定義します。

---

## Phase 1: 設定・ナビゲーション補完（即時実施）

### 1-A: docker-compose.yml へ auto-dealer-demo DB 変数追加

**対象ファイル**: `/NetYamlForge/docker-compose.yml`

**問題**: 他プロジェクト（todo-app, ui-showcase, biz-docs）は DB 切替用環境変数が登録済みだが、`auto-dealer-demo` は未登録。PostgreSQL/MySQL 移行不可。

**修正内容**:

```yaml
# x-app-base &app-base の environment セクションに追加
NYFORGE_AUTO_DEALER_DEMO_DB_TYPE: ${NYFORGE_AUTO_DEALER_DEMO_DB_TYPE:-sqlite}
NYFORGE_AUTO_DEALER_DEMO_CONNECTION_STRING: ${NYFORGE_AUTO_DEALER_DEMO_CONNECTION_STRING:-}
```

- `app` (SQLite デフォルト): volumes に `auto_dealer_demo_sqlite` マウント追加
- `app-postgres`: `NYFORGE_AUTO_DEALER_DEMO_DB_TYPE: postgresql` + 接続文字列追加
- `app-mysql`: `NYFORGE_AUTO_DEALER_DEMO_DB_TYPE: mysql` + 接続文字列追加
- `app-sqlserver`: `NYFORGE_AUTO_DEALER_DEMO_DB_TYPE: sqlserver` + 接続文字列追加
- `volumes:` セクションに `auto_dealer_demo_sqlite:` 追加

---

### 1-B: project.yaml ナビゲーション補完

**対象ファイル**: `project.yaml`

**問題**: 以下のページがナビゲーションに未登録でアクセス不可。

| ページ | ロール |
|--------|--------|
| SalesLeads | sales_rep, sales_manager |
| CustomerAppointments | customer |
| ServiceRequests | service_staff, sales_manager |
| Employees | ai_admin, executive |

**修正内容**: `navigation.items` に上記4エントリを追加。

---

## Phase 2: 従業員管理ページ・シードデータ（1日）

### 2-A: pages/Employees.yaml 新規作成

**役割**: 全ロール（operator, sales_rep, sales_manager, service_staff, executive）の従業員マスタ管理CRUD画面。

**主要コンポーネント**:
- 従業員一覧テーブル（検索・ページング付き）
- 新規登録フォーム
- 編集・詳細表示
- ロール別アクセス制御（ai_admin, executive のみ作成・削除可）

---

### 2-B: init_seed.sql へ従業員データ追加

**対象ファイル**: `database/init_seed.sql`

**追加するロール別サンプルデータ**:

| employee_id | ロール | 氏名 |
|-------------|--------|------|
| EMP-001 | sales_manager | 田中部長 |
| EMP-002 | sales_rep | 鈴木営業 |
| EMP-003 | sales_rep | 佐藤営業 |
| EMP-004 | service_staff | 山田整備 |
| EMP-005 | operator | 高橋オペ |
| EMP-006 | executive | 伊藤役員 |

---

## Phase 3: CSVインポート機能（2-3日）

### 3-A: pages/DataImport.yaml 新規作成

**機能**:
- 顧客データ CSV 一括登録
- 車両データ CSV 一括登録
- インポート前バリデーション・プレビュー表示
- エラー行の詳細表示

**アクセス権限**: ai_admin, executive, sales_manager

---

## 実装チェックリスト

- [ ] docker-compose.yml: auto-dealer-demo 環境変数追加（x-app-base）
- [ ] docker-compose.yml: auto-dealer-demo 環境変数追加（app-postgres）
- [ ] docker-compose.yml: auto-dealer-demo 環境変数追加（app-mysql）
- [ ] docker-compose.yml: auto-dealer-demo 環境変数追加（app-sqlserver）
- [ ] docker-compose.yml: auto_dealer_demo_sqlite volume 追加
- [ ] project.yaml: SalesLeads nav エントリ追加
- [ ] project.yaml: CustomerAppointments nav エントリ追加
- [ ] project.yaml: ServiceRequests nav エントリ追加
- [ ] project.yaml: Employees nav エントリ追加
- [ ] init_seed.sql: 従業員6名追加
- [ ] pages/Employees.yaml: 新規作成
- [ ] pages/DataImport.yaml: 新規作成

---

## 環境変数設定例（.env）

```dotenv
# auto-dealer-demo
NYFORGE_AUTO_DEALER_DEMO_DB_TYPE=sqlite
NYFORGE_AUTO_DEALER_DEMO_CONNECTION_STRING=
```

PostgreSQL 切替時:
```dotenv
NYFORGE_AUTO_DEALER_DEMO_DB_TYPE=postgresql
NYFORGE_AUTO_DEALER_DEMO_CONNECTION_STRING=Host=postgres;Port=5432;Database=auto_dealer_demo;Username=nyforge;Password=nyforge_pass
```
