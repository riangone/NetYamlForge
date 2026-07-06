# 自動車ディーラー AI 窓口システム — ロールベース UI/UX 設計書

> バージョン: 1.0.0
> 作成日: 2026-03-28
> 対象ブランチ: feature/ai-window-system

---

## 1. 問題の背景と課題

### 1.1 現状の問題

ログイン後に **全ユーザーが同一のナビゲーション（13ページ）** を見ており、以下の問題が生じていた。

| 問題 | 影響 |
|------|------|
| どこから始めればいいか分からない | 操作迷子・教育コスト増大 |
| 全ページが一覧に表示される | 関係のないページへの誤アクセス |
| ロール別の優先タスクが不明瞭 | 重要案件の見落とし |
| ログイン後のリダイレクト先が一つ | ロールに関係なくホームへ |

### 1.2 解決アプローチ

1. **ロール別ランディングページ** — ログイン後に自動で最適ページへリダイレクト
2. **ロール別ナビゲーションフィルタリング** — 関係あるメニュー項目のみ表示
3. **スタートガイドページ** — 全ロール共通のオンボーディングハブ
4. **デモユーザー** — 各ロールを体験できるデモアカウントを標準提供

---

## 2. ユーザーペルソナ定義

### 2.1 AI オペレーター (`operator`)

**担当業務**: AI チャットでは解決できなかったエスカレーション案件への対応

**ログイン後の最初のアクション**:
1. オペレーター・コンソールを開く
2. 待機中エスカレーションキューを確認（SLA 超過がないか）
3. 自分のアクティブな対話を確認・対応

**重要 KPI**:
- 待機中エスカレーション件数
- 対応中チャット数
- SLA 超過件数
- 本日解決件数

**主要ページ**: `OperatorConsole` → `ChatDetail`

---

### 2.2 営業担当者 (`sales_rep`)

**担当業務**: リードのフォローアップ、商談推進、成約管理

**ログイン後の最初のアクション**:
1. 担当者パフォーマンスダッシュボードを開く
2. 自分のホットリード（スコア 70↑）を確認
3. フォローアップ期限が来ているリードを対応
4. 停滞リード（7日以上放置）を解消

**重要 KPI**:
- 担当リード総数
- 今月成約件数
- 成約率
- 停滞リード件数

**主要ページ**: `SalesRepDashboard` → `SalesLeads` → `LeadKanban`

---

### 2.3 営業管理者 (`sales_manager`)

**担当業務**: チームのパフォーマンス管理、リード割り当て、KPI 追跡

**ログイン後の最初のアクション**:
1. リードパイプライン（カンバン）を開く
2. チーム全体のファネル状況を確認
3. 停滞リード・ホットリードを確認して担当者に指示
4. 担当者別パフォーマンスをレビュー

**重要 KPI**:
- 新規リード件数
- 各ステージの滞留時間
- チーム成約率
- 今月の目標達成率

**主要ページ**: `LeadKanban` → `SalesRepDashboard` → `ExecDashboard`

---

### 2.4 サービス部門スタッフ (`service_staff`)

**担当業務**: 予約管理、車検・修理・試乗などのサービス対応、依頼処理

**ログイン後の最初のアクション**:
1. サービス予約ページを開く
2. 本日の予約タイムラインを確認（朝一の必須作業）
3. 確認待ちの予約を承認・スケジュール調整
4. 緊急・高優先度のサービス依頼を対応

**重要 KPI**:
- 本日の予約件数
- 確認待ち予約件数
- 緊急依頼件数
- 今月のサービス完了件数

**主要ページ**: `Appointments` → `ServiceRequests` → `Customers`

---

### 2.5 AI 管理者・アナリスト (`ai_admin`)

**担当業務**: AI システムの監視・パフォーマンス改善、ナレッジベース管理

**ログイン後の最初のアクション**:
1. AI 窓口ダッシュボードを開く
2. エスカレーション率・AI 解決率を確認
3. 低置信度の対話を分析
4. ナレッジベースの未解決質問をレビューして FAQ 追加

**重要 KPI**:
- AI 自動解決率（目標: 75%↑）
- 平均置信度（目標: 0.80↑）
- エスカレーション率（目標: 15%以下）
- 顧客満足度平均

**主要ページ**: `AIDashboard` → `AIAnalytics` → `KnowledgeBase` → `OperatorConsole`

---

### 2.6 経営層 (`executive`)

**担当業務**: ROI 分析、KPI モニタリング、経営判断のための情報収集

**ログイン後の最初のアクション**:
1. 経営ダッシュボード（ROI 分析）を開く
2. AI 導入効果・コスト削減額を確認
3. 全体ファネル（AI 対話→成約）を把握
4. 担当者別・チャネル別パフォーマンスを確認

**重要 KPI**:
- AI 対応コスト削減額（推計）
- AI 起点の成約件数・成約率
- 顧客 LTV・ランク分布
- AI 自動解決率

**主要ページ**: `ExecDashboard` → `AIAnalytics` → `LeadKanban`

---

## 3. 実装内容

### 3.1 フレームワーク機能拡張

#### 3.1.1 カスタムロール Claims（AccountController.cs）

ログイン時に `AppUserRole` テーブルからカスタムロールを取得し、Cookie クレームに追加するよう変更。

```csharp
// ログイン後にカスタムロールを取得してクレームに追加
var customRoles = await _users.GetUserRolesAsync(user.UserName);
foreach (var role in customRoles)
{
    claims.Add(new Claim(ClaimTypes.Role, role));
}
```

これにより `User.IsInRole("operator")` などのチェックが可能になった。

#### 3.1.2 ロール別ランディングページ（AccountController.cs）

`project.yaml` の `layout.landingPageByRole` 設定を参照し、ユーザーのロールに応じて適切なページへリダイレクト。

```
ログイン
  ↓
ReturnUrl がある → そこへリダイレクト
  ↓
landingPageByRole でロール一致 → 対応する URL へリダイレクト
  ↓
デフォルト → プロジェクトホーム（ダッシュボード）
```

#### 3.1.3 ロール別ナビゲーションフィルタリング（_Layout.cshtml）

`ProjectNavigationItemConfig.Roles` フィールドを追加し、ナビゲーション項目をロールでフィルタリング。

```yaml
- label: AI 分析レポート
  url: /auto-dealer-demo/Page/AIAnalytics
  roles: [ai_admin]   # ai_admin のみ表示（Admin は常に全件表示）
```

#### 3.1.4 新規 API: GetUserRolesAsync（IUserAuthService）

```csharp
Task<IReadOnlyList<string>> GetUserRolesAsync(string userName);
```

### 3.2 auto-dealer-demo 固有変更

#### 3.2.1 デモユーザー（AutoDealerDemoSeeder.cs）

アプリ初回起動時に自動作成されるデモアカウント。

| ユーザー名 | 表示名 | ロール | パスワード | 初回ランディングページ |
|-----------|--------|--------|-----------|----------------------|
| `admin` | System Admin | Admin | `Admin@123` | ダッシュボード |
| `operator1` | 田中オペレーター | operator | `Demo@123` | オペレーター・コンソール |
| `sales1` | 鈴木営業担当 | sales_rep | `Demo@123` | 担当者パフォーマンス |
| `manager1` | 佐藤営業部長 | sales_manager | `Demo@123` | リードパイプライン |
| `service1` | 高橋サービス担当 | service_staff | `Demo@123` | サービス予約 |
| `exec1` | 山田部長 | executive | `Demo@123` | 経営ダッシュボード |
| `aiadmin1` | 伊藤AI管理者 | ai_admin | `Demo@123` | AI 窓口ダッシュボード |

#### 3.2.2 ナビゲーション設定（project.yaml）

各ナビゲーション項目に `roles` を追加し、関係あるロールのみ表示。

| ナビ項目 | 表示されるロール |
|---------|----------------|
| スタートガイド | 全員 |
| AI 窓口ダッシュボード | operator, ai_admin, sales_manager, executive |
| オペレーター・コンソール | operator, ai_admin |
| AI 分析レポート | ai_admin のみ |
| ナレッジベース管理 | ai_admin のみ |
| リードパイプライン | sales_rep, sales_manager, executive |
| セールスリード管理 | sales_rep, sales_manager |
| 担当者パフォーマンス | sales_rep, sales_manager |
| 車両在庫管理 | sales_rep, sales_manager, service_staff |
| 顧客管理 | sales_rep, sales_manager, service_staff |
| サービス予約 | service_staff, sales_manager |
| サービス依頼管理 | service_staff のみ |
| 経営ダッシュボード | executive, sales_manager |

> **注**: Admin ユーザーは常に全ナビゲーション項目を表示。

#### 3.2.3 スタートガイドページ（pages/Welcome.yaml）

全ロールがアクセスできる汎用オンボーディングページ。以下のセクションで構成。

1. **本日のシステム状況** — システム全体 KPI（アクティブ対話, 待機エスカレーション, 本日予約, ホットリード等）
2. **今すぐ対応が必要な事項** — 緊急エスカレーション + 高優先度サービス依頼の一覧
3. **本日の予約** — 今日のサービス予約タイムライン
4. **AI オペレーター業務フロー** — ロールガイド（エスカレーション件数等）
5. **営業担当者業務フロー** — ロールガイド（ホットリード, 停滞リード件数等）
6. **サービス部門業務フロー** — ロールガイド（予約件数, 未解決依頼等）
7. **経営層業務フロー** — ロールガイド（今月 AI 解決数, 成約実績等）

---

## 4. ロール別画面遷移フロー

### 4.1 AI オペレーター の業務フロー

```
ログイン
  ↓ 自動リダイレクト
オペレーター・コンソール
  ├─ 待機中エスカレーション一覧
  │    ↓ クリック
  │  ChatDetail（対話詳細・対応画面）
  │    ├─ メッセージ履歴確認
  │    ├─ 顧客プロフィール確認
  │    └─ 解決・エスカレーションメモ記録
  └─ AI 分析データ確認（KPI 概要）
```

### 4.2 営業担当者 の業務フロー

```
ログイン
  ↓ 自動リダイレクト
担当者パフォーマンス
  ├─ 自分のホットリード（スコア70↑）
  │    ↓ 対応
  │  セールスリード管理（詳細・アクション記録）
  │    ↓ ステージ更新
  │  リードパイプライン（全体俯瞰）
  └─ フォローアップリマインダー確認
```

### 4.3 サービス部門スタッフ の業務フロー

```
ログイン
  ↓ 自動リダイレクト
サービス予約
  ├─ 本日の予約タイムライン確認
  │    ↓ 作業開始
  │  ステータス更新（confirmed → in_progress → completed）
  ├─ 確認待ち予約の承認
  └─ サービス依頼管理（緊急依頼の確認・対応）
```

### 4.4 AI 管理者 の業務フロー

```
ログイン
  ↓ 自動リダイレクト
AI 窓口ダッシュボード
  ├─ KPI 確認（エスカレーション率, AI 解決率）
  │    ↓ 異常があれば
  │  AI 分析レポート（詳細分析）
  │    ↓ 改善箇所を特定
  │  ナレッジベース管理（FAQ 追加・更新）
  └─ 低置信度対話の確認
```

### 4.5 経営層 の業務フロー

```
ログイン
  ↓ 自動リダイレクト
経営ダッシュボード（ROI 分析）
  ├─ AI 導入効果・コスト削減確認
  ├─ 全体ファネル（対話→成約）確認
  ├─ 担当者別成果確認
  └─ 顧客 LTV ポートフォリオ確認
```

---

## 5. 技術的な実装詳細

### 5.1 変更ファイル一覧

| ファイル | 変更種別 | 内容 |
|---------|---------|------|
| `Services/Auth/IUserAuthService.cs` | 修正 | `GetUserRolesAsync` メソッド追加 |
| `Services/Auth/UserAuthService.cs` | 修正 | `GetUserRolesAsync` 実装 |
| `Models/ProjectConfig.cs` | 修正 | `ProjectNavigationItemConfig.Roles` + `ProjectLayoutConfig.LandingPageByRole` 追加 |
| `Controllers/AccountController.cs` | 修正 | カスタムロール Claims 追加 + ロール別リダイレクト |
| `Views/Shared/_Layout.cshtml` | 修正 | ロール別ナビゲーションフィルタリング |
| `Data/Seeders/AutoDealerDemoSeeder.cs` | **新規** | デモユーザー作成シーダー |
| `Data/ProjectSpecificInitializer.cs` | 修正 | AutoDealerDemoSeeder 呼び出し |
| `projects/auto-dealer-demo/project.yaml` | 修正 | `landingPageByRole` + nav item `roles` 追加 |
| `projects/auto-dealer-demo/pages/Welcome.yaml` | **新規** | スタートガイドページ |

### 5.2 project.yaml 設定リファレンス

```yaml
layout:
  # ロール別ランディングページ設定
  landingPageByRole:
    operator:      /auto-dealer-demo/Page/OperatorConsole
    sales_rep:     /auto-dealer-demo/Page/SalesRepDashboard
    sales_manager: /auto-dealer-demo/Page/LeadKanban
    service_staff: /auto-dealer-demo/Page/Appointments
    ai_admin:      /auto-dealer-demo/Page/AIDashboard
    executive:     /auto-dealer-demo/Page/ExecDashboard

  navigation:
    showDashboard: false
    items:
      - label: AI 分析レポート
        url: /auto-dealer-demo/Page/AIAnalytics
        icon: 📈
        section: AI 窓口
        roles: [ai_admin]          # ai_admin のみ表示
      - label: リードパイプライン
        url: /auto-dealer-demo/Page/LeadKanban
        icon: 🔥
        section: 販売管理
        roles: [sales_rep, sales_manager, executive]  # 複数ロールに表示
```

### 5.3 RBAC 動作ルール

| 条件 | ナビゲーション表示 | ランディングページ |
|-----|-----------------|-----------------|
| Admin ユーザー | 全項目表示（roles 無視） | プロジェクトホーム（デフォルト）|
| カスタムロールあり | roles に一致する項目のみ | `landingPageByRole` で一致した URL |
| カスタムロールなし（User） | `roles` 未指定の項目のみ | プロジェクトホーム（デフォルト）|
| ReturnUrl 指定あり | （リダイレクト前なので無関係）| ReturnUrl が優先 |

---

## 6. デモ手順

### 6.1 各ロールを体験する手順

1. アプリを起動（初回は DB が自動初期化される）
2. `/auto-dealer-demo/Account/Login` にアクセス
3. 以下のアカウントでログインして体験

```
【AI オペレーター体験】
  ユーザー名: operator1  パスワード: Demo@123
  → ログイン後: オペレーター・コンソールへ自動移動
  → 表示されるメニュー: AI 窓口ダッシュボード、オペレーター・コンソール

【営業担当者体験】
  ユーザー名: sales1  パスワード: Demo@123
  → ログイン後: 担当者パフォーマンスへ自動移動
  → 表示されるメニュー: リードパイプライン、セールスリード管理、担当者パフォーマンス、車両在庫、顧客管理

【営業管理者体験】
  ユーザー名: manager1  パスワード: Demo@123
  → ログイン後: リードパイプラインへ自動移動
  → 表示されるメニュー: AI 窓口ダッシュボード、販売管理全般、経営ダッシュボード

【サービス部門体験】
  ユーザー名: service1  パスワード: Demo@123
  → ログイン後: サービス予約へ自動移動
  → 表示されるメニュー: 車両在庫、顧客管理、サービス予約、サービス依頼管理

【経営層体験】
  ユーザー名: exec1  パスワード: Demo@123
  → ログイン後: 経営ダッシュボードへ自動移動
  → 表示されるメニュー: AI 窓口、リードパイプライン、経営ダッシュボード

【AI 管理者体験】
  ユーザー名: aiadmin1  パスワード: Demo@123
  → ログイン後: AI 窓口ダッシュボードへ自動移動
  → 表示されるメニュー: AI 窓口全般（4ページ）

【システム管理者体験】
  ユーザー名: admin  パスワード: Admin@123
  → ログイン後: プロジェクトホーム（全ページ閲覧可）
```

### 6.2 スタートガイドへのアクセス

どのロールからでもサイドバーの「🏠 スタートガイド」からアクセス可能。
URL: `/auto-dealer-demo/Page/Welcome`

---

## 7. 今後の改善提案

### 7.1 短期（すぐ実装可能）

- [ ] ヘッダーにロールバッジを表示（「田中オペレーター｜🎧 AI オペレーター」）
- [ ] スタートガイドページのロール自動検出（SQL で `@CurrentUser` を利用）
- [ ] ページアクセス制限の設定（AppRolePermission へのシードデータ追加）

### 7.2 中期

- [ ] ユーザー自身によるロール申請フロー
- [ ] ロール別通知・アラート設定
- [ ] チーム別ダッシュボード（部門単位の集計ビュー）

### 7.3 長期

- [ ] ロール別カスタムウィジェット（ドラッグ&ドロップ）
- [ ] モバイルアプリ対応（PWA）
- [ ] LINE 通知とのロール別連携

---

## 8. 関連ドキュメント

- [AUTO-DEALER-BUSINESS-FLOW.md](auto-dealer-BUSINESS-FLOW.md) — 業務フロー詳細
- [AUTO-DEALER-COMPREHENSIVE-REDESIGN.md](AUTO-DEALER-COMPREHENSIVE-REDESIGN.md) — システム全体設計
- [ai-window-system-design.md](ai-window-system-design.md) — AI 窓口システム設計
