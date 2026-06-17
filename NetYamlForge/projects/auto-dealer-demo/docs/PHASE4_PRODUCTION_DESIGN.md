# auto-dealer-demo Phase 4 — 本番ディーラー導入 詳細設計・作業指示書

## 概要

本フェーズは実際の自動車ディーラーへの本番導入に向けて必須な機能を追加実装する。  
前フェーズ（Phase 1-3）で完了済みの設定・ナビゲーション・Employees・DataImport を基盤とする。

---

## 実装対象（優先度順）

| # | 機能 | 優先度 | 実装形態 |
|---|------|--------|--------|
| 4-A | 多拠点管理（branches） | 🔴 高 | entity + page + migration |
| 4-B | 車両複数画像管理 | 🔴 高 | entity + page + migration |
| 4-C | 試乗記録管理 | 🟡 中 | entity + page + migration |
| 4-D | 売上目標・配額管理 | 🟡 中 | entity + page + migration |
| 4-E | 通知設定管理ページ | 🟡 中 | page（既存 jobs.yml 拡張） |
| 4-F | ローン・支払プラン管理 | 🟢 低 | entity + page + migration |

---

## 4-A: 多拠点管理

### 背景
現状、全テーブルに `branch_id` が存在せず、複数店舗での運用が不可能。
実ディーラーでは本店・支店・サービスセンターの分離が必須。

### 実装ファイル
- `database/migration_phase4_branches.sql` — branches テーブル + 既存テーブルへの branch_id 追加
- `entities/branches.yml` — 拠点エンティティ定義
- `pages/Branches.yaml` — 拠点管理ページ

### DB スキーマ（branches テーブル）
```sql
CREATE TABLE branches (
  branch_id   TEXT PRIMARY KEY,
  branch_name TEXT NOT NULL,
  branch_type TEXT NOT NULL DEFAULT 'main',   -- main/sub/service
  address     TEXT,
  phone       TEXT,
  manager_id  TEXT,                           -- FK employees.employee_id
  is_active   INTEGER NOT NULL DEFAULT 1,
  created_at  TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);
```

---

## 4-B: 車両複数画像管理

### 背景
現在 `vehicles.image_url` は単一 URL のみ。実ディーラーでは外装・内装・エンジンルーム等
複数枚のギャラリー表示が必須。

### 実装ファイル
- `database/migration_phase4_vehicle_images.sql`
- `entities/vehicle_images.yml`
- `pages/VehicleImages.yaml`

### DB スキーマ（vehicle_images テーブル）
```sql
CREATE TABLE vehicle_images (
  image_id      TEXT PRIMARY KEY,
  vehicle_id    TEXT NOT NULL,               -- FK vehicles.vehicle_id
  image_url     TEXT NOT NULL,
  caption       TEXT,
  image_type    TEXT DEFAULT 'exterior',    -- exterior/interior/engine/detail/other
  sort_order    INTEGER DEFAULT 0,
  is_primary    INTEGER DEFAULT 0,
  uploaded_by   TEXT,
  created_at    TEXT NOT NULL DEFAULT (datetime('now'))
);
```

---

## 4-C: 試乗記録管理

### 背景
試乗はリード転換の最重要ポイント。現在トラッキング手段が存在しない。
試乗後のフォローアップを自動化するためのデータ基盤が必要。

### 実装ファイル
- `database/migration_phase4_test_drives.sql`
- `entities/test_drives.yml`
- `pages/TestDrives.yaml`

### DB スキーマ
```sql
CREATE TABLE test_drives (
  test_drive_id     TEXT PRIMARY KEY,
  lead_id           TEXT,                    -- FK sales_leads.lead_id
  customer_id       TEXT NOT NULL,           -- FK customers.customer_id
  vehicle_id        TEXT NOT NULL,           -- FK vehicles.vehicle_id
  assigned_staff_id TEXT,                    -- FK employees.employee_id
  scheduled_at      TEXT NOT NULL,
  actual_start_at   TEXT,
  actual_end_at     TEXT,
  status            TEXT DEFAULT 'scheduled', -- scheduled/completed/cancelled/no_show
  feedback_score    INTEGER,                  -- 1-5 顧客満足度
  feedback_notes    TEXT,
  staff_notes       TEXT,
  next_action       TEXT,                    -- quote/revisit/lost/other
  branch_id         TEXT,
  created_at        TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at        TEXT NOT NULL DEFAULT (datetime('now'))
);
```

---

## 4-D: 売上目標・配額管理

### 背景
月次売上目標と実績の対比をリアルタイムで可視化する必要がある。
現在は ManagerDashboard に実績のみ表示、目標値が存在しない。

### 実装ファイル
- `database/migration_phase4_quotas.sql`
- `entities/sales_quotas.yml`
- `pages/SalesQuota.yaml`

### DB スキーマ
```sql
CREATE TABLE sales_quotas (
  quota_id      TEXT PRIMARY KEY,
  employee_id   TEXT NOT NULL,               -- FK employees.employee_id
  branch_id     TEXT,
  year          INTEGER NOT NULL,
  month         INTEGER NOT NULL,
  quota_amount  DECIMAL(12,2) NOT NULL,      -- 目標売上金額
  quota_units   INTEGER,                     -- 目標台数
  achieved_amount DECIMAL(12,2) DEFAULT 0,
  achieved_units  INTEGER DEFAULT 0,
  notes         TEXT,
  created_at    TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at    TEXT NOT NULL DEFAULT (datetime('now')),
  UNIQUE(employee_id, year, month)
);
```

---

## 4-E: 通知設定管理ページ

### 背景
Jobs は CSV 出力止まり（通知未送信）。実運用では Webhook/Email/LINE への
実通知が必要。設定 UI を提供し、接続テスト機能を持たせる。

### 実装ファイル
- `pages/NotificationSettings.yaml` — 通知チャンネル設定・テスト送信

---

## 4-F: ローン・支払プラン管理

### 背景
`ai_quotes` テーブルには見積金額が存在するが、ローン計算・月次支払シミュレーション
および実際の契約後の支払追跡手段がない。

### 実装ファイル
- `database/migration_phase4_payments.sql`
- `entities/payment_plans.yml`
- `pages/PaymentPlans.yaml`

### DB スキーマ
```sql
CREATE TABLE payment_plans (
  plan_id         TEXT PRIMARY KEY,
  lead_id         TEXT,                      -- FK sales_leads.lead_id
  customer_id     TEXT NOT NULL,
  vehicle_id      TEXT NOT NULL,
  plan_type       TEXT DEFAULT 'loan',       -- cash/loan/lease
  total_amount    DECIMAL(12,2) NOT NULL,
  down_payment    DECIMAL(12,2) DEFAULT 0,
  loan_amount     DECIMAL(12,2),
  interest_rate   DECIMAL(5,3),             -- 年利(%)
  term_months     INTEGER,                   -- ローン期間（月）
  monthly_payment DECIMAL(10,2),
  balloon_payment DECIMAL(12,2),            -- 残価設定
  status          TEXT DEFAULT 'simulation', -- simulation/approved/active/completed
  contract_date   TEXT,
  created_at      TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);
```

---

## ナビゲーション追加（project.yaml）

```yaml
# 追加先: 运营与服务 セクション
- label: 拠点管理
  url: /auto-dealer-demo/Page/Branches
  icon: 🏢
  section: 运营与服务
  roles: [ai_admin, executive]

- label: 車両画像管理
  url: /auto-dealer-demo/Page/VehicleImages
  icon: 📷
  section: 运营与服务
  roles: [sales_rep, sales_manager, ai_admin]

- label: 試乗管理
  url: /auto-dealer-demo/Page/TestDrives
  icon: 🚘
  section: 运营与服务
  roles: [sales_rep, sales_manager, service_staff]

- label: 売上目標管理
  url: /auto-dealer-demo/Page/SalesQuota
  icon: 🎯
  section: 运营与服务
  roles: [sales_manager, executive]

- label: 通知設定
  url: /auto-dealer-demo/Page/NotificationSettings
  icon: 🔔
  section: 运营与服务
  roles: [ai_admin, executive]

- label: ローン・支払管理
  url: /auto-dealer-demo/Page/PaymentPlans
  icon: 💳
  section: 运营与服务
  roles: [sales_rep, sales_manager, executive]
```

---

## 実装チェックリスト

### DB マイグレーション
- [ ] `migration_phase4_branches.sql` — branches テーブル + seed 2件
- [ ] `migration_phase4_vehicle_images.sql` — vehicle_images テーブル + seed 10件
- [ ] `migration_phase4_test_drives.sql` — test_drives テーブル + seed 5件
- [ ] `migration_phase4_quotas.sql` — sales_quotas テーブル + 今月目標 seed
- [ ] `migration_phase4_payments.sql` — payment_plans テーブル + seed 3件

### エンティティ YAML
- [ ] `entities/branches.yml`
- [ ] `entities/vehicle_images.yml`
- [ ] `entities/test_drives.yml`
- [ ] `entities/sales_quotas.yml`
- [ ] `entities/payment_plans.yml`

### ページ YAML
- [ ] `pages/Branches.yaml`
- [ ] `pages/VehicleImages.yaml`
- [ ] `pages/TestDrives.yaml`
- [ ] `pages/SalesQuota.yaml`
- [ ] `pages/NotificationSettings.yaml`
- [ ] `pages/PaymentPlans.yaml`

### project.yaml
- [ ] 6件のナビゲーションエントリ追加

---

## init_seed.sql への追加データ

### branches シード（2件）
```sql
INSERT INTO branches VALUES ('BR-001','本店','main','東京都渋谷区...','03-1234-5678','EMP-001',1,...);
INSERT INTO branches VALUES ('BR-002','横浜支店','sub','神奈川県横浜市...','045-1234-5678','EMP-002',1,...);
```

### sales_quotas シード（今月・営業担当分）
今月（2026-06）の目標を EMP-002/003/007/008/009 の5名分登録。
