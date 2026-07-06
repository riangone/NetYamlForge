# ページ SQL ユーザーコンテキスト注入 — 詳細設計書

> バージョン: 1.0 | 作成日: 2026-04-07  
> 対象ブランチ: feature/jpiere-erp-subproject  
> ステータス: 設計中（コード未変更）

---

## 目次

1. [概要・目的](#1-概要目的)
2. [現状の限界と解決方針](#2-現状の限界と解決方針)
3. [変更コンポーネント一覧](#3-変更コンポーネント一覧)
4. [PageUserContext レコード設計](#4-pageusercontext-レコード設計)
5. [SQL 変数仕様](#5-sql-変数仕様)
6. [SectionDefinition 拡張（VisibleToRoles）](#6-sectiondefinition-拡張-visibletoroles)
7. [PageDataQueryService の変更](#7-pagedataqueryservice-の変更)
8. [PageController の変更](#8-pagecontroller-の変更)
9. [PageView.cshtml の変更](#9-pageviewcshtml-の変更)
10. [jpiere-cs への適用設計](#10-jpiere-cs-への適用設計)
11. [auto-dealer-demo への適用設計](#11-auto-dealer-demo-への適用設計)
12. [DB 横断制約と対処方法](#12-db-横断制約と対処方法)
13. [セキュリティ考慮事項](#13-セキュリティ考慮事項)
14. [実装ステップ（順序付き）](#14-実装ステップ順序付き)
15. [テスト計画](#15-テスト計画)

---

## 1. 概要・目的

現在の `pages/*.yaml` のカスタム SQL は、ログインユーザーの情報を参照できません。
全ユーザーが同一データを見るため、以下の業務要件を満たせません。

| 業務要件 | プロジェクト |
|---|---|
| 自分に割り当てられた TODO だけ表示 | jpiere-cs |
| 担当者（sales_rep）の契約・見積だけ表示 | jpiere-cs |
| 経理担当のみ会計セクションを表示 | jpiere-cs |
| 顧客が自分の予約・商談だけ確認 | auto-dealer-demo |
| 営業担当が自分のリードのみ表示 | auto-dealer-demo |
| 役職別（経営層/営業/サービス）のウィジェット切替 | auto-dealer-demo |

本設計はこれらを解決するために、以下の 2 機能をフレームワークに追加します。

**機能 1: SQL ユーザー変数**  
ページ定義の SQL 内で `@currentUser` などの特殊変数を使用可能にする。

**機能 2: セクション可視性制御**  
`visibleToRoles: [role1, role2]` でセクションをロール別に表示/非表示にする。

---

## 2. 現状の限界と解決方針

### 現状のデータフロー

```
HTTP リクエスト
    │
    ▼
PageController.Index()
    │ User (ClaimsPrincipal) ← ログイン情報がある
    │ ↓ 渡さない
    ▼
PageDataQueryService.LoadPageDataAsync(pageDef, filters)
    │ filters = URL クエリパラメータのみ
    │
    ▼
SQL 実行 → Dapper → DB
    ↑ @currentUser などは未定義 → エラーまたは無視
```

### 変更後のデータフロー

```
HTTP リクエスト
    │
    ▼
PageController.Index()
    │ User → PageUserContext を生成
    │         (UserName, DisplayName, UserId, Roles, IsAdmin)
    │ ↓ 渡す
    ▼
PageDataQueryService.LoadPageDataAsync(pageDef, filters, userCtx)
    │ userCtx を DynamicParameters に注入
    │
    ▼
SQL 実行
    WHERE assigned_to = @currentUser  ← 機能する
```

---

## 3. 変更コンポーネント一覧

| ファイル | 変更種別 | 内容 |
|---|---|---|
| `Models/PageDefinition.cs` | 修正 | `SectionDefinition` に `VisibleToRoles` プロパティ追加 |
| `Services/Page/PageDataQueryService.cs` | 修正 | `PageUserContext` を受け取り SQL パラメータに注入 |
| `Controllers/PageController.cs` | 修正 | `PageUserContext` を生成してサービスに渡す + セクションフィルタリング |
| `Views/Page/PageView.cshtml` | 修正 | `VisibleToRoles` チェックを追加 |
| `Models/PageUserContext.cs` | 新規 | `PageUserContext` レコード定義 |
| `jpiere-cs/pages/MyPage.yaml` | 修正 | `@currentUser` 変数・`visibleToRoles` を活用 |
| `jpiere-cs/project.yaml` | 修正 | `userAuthentication: true` に変更 |
| `auto-dealer-demo/pages/Welcome.yaml` | 修正 | `visibleToRoles` でロール別セクション制御 |
| `auto-dealer-demo/pages/CustomerDashboard.yaml` | 修正 | `@currentUser` で顧客自身のデータにフィルタ |
| `auto-dealer-demo/database/init.sql` | 修正 | `customers` / `sales_leads` に `login_username` カラム追加 |

---

## 4. PageUserContext レコード設計

### 新規ファイル: `Models/PageUserContext.cs`

```csharp
namespace NetYamlForge.Models;

/// <summary>
/// ページ SQL クエリに注入するログインユーザーのコンテキスト情報。
/// PageController で ClaimsPrincipal から生成し、PageDataQueryService に渡す。
/// </summary>
public record PageUserContext(
    /// <summary>ログインユーザー名 (ClaimTypes.Name = app_user.user_name)</summary>
    string UserName,
    /// <summary>表示名 (ClaimTypes.GivenName = app_user.display_name)</summary>
    string DisplayName,
    /// <summary>ユーザー ID (ClaimTypes.NameIdentifier = app_user.id の文字列表現)</summary>
    string UserId,
    /// <summary>所持ロール一覧 (ClaimTypes.Role の全値)</summary>
    IReadOnlyList<string> Roles,
    /// <summary>管理者フラグ</summary>
    bool IsAdmin,
    /// <summary>認証済みフラグ</summary>
    bool IsAuthenticated
)
{
    /// <summary>未認証ユーザー向けの空コンテキスト</summary>
    public static readonly PageUserContext Anonymous = new(
        UserName: "",
        DisplayName: "",
        UserId: "",
        Roles: [],
        IsAdmin: false,
        IsAuthenticated: false
    );

    /// <summary>指定ロールを所持しているか（大文字小文字を無視）</summary>
    public bool HasRole(string role) =>
        Roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    /// <summary>いずれかのロールを所持しているか</summary>
    public bool HasAnyRole(IEnumerable<string> roles) =>
        roles.Any(HasRole);
}
```

### ClaimsPrincipal からの生成ロジック（PageController 内）

```csharp
private PageUserContext BuildUserContext() => new(
    UserName:     User.Identity?.Name ?? "",
    DisplayName:  User.FindFirst(ClaimTypes.GivenName)?.Value ?? "",
    UserId:       User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "",
    Roles:        User.Claims
                      .Where(c => c.Type == ClaimTypes.Role)
                      .Select(c => c.Value)
                      .ToList(),
    IsAdmin:      UserIsAdmin(),
    IsAuthenticated: User.Identity?.IsAuthenticated == true
);
```

---

## 5. SQL 変数仕様

ページ YAML の `source:` SQL 内で使用できる予約変数一覧。
すべて Dapper の名前付きパラメータとして安全に注入される（SQL インジェクションリスクなし）。

| 変数名 | 型 | 値の例 | 説明 |
|---|---|---|---|
| `@currentUser` | TEXT | `"sales1"` | ログインユーザー名（`app_user.user_name`） |
| `@currentUserDisplayName` | TEXT | `"鈴木営業担当"` | 表示名（`app_user.display_name`） |
| `@currentUserId` | TEXT | `"11"` | ユーザー ID（文字列）|
| `@currentUserRole` | TEXT | `"sales_rep"` | 最初のロール名 |
| `@isAdmin` | INT | `1` or `0` | 管理者の場合 1 |
| `@isAuthenticated` | INT | `1` or `0` | 認証済みの場合 1 |

### 注意事項

- **未認証時**: 全変数が空文字 or 0 になる（`isPublic: true` ページで未ログインアクセスの場合）
- **ロール複数保持**: `@currentUserRole` は Claims に最初に登録されたロールのみ。複数ロールを持つユーザーの場合は `visibleToRoles` による制御を推奨
- **大文字小文字**: ユーザー名は `app_user.user_name` の値そのまま。DB 側の `assigned_to` などと大文字小文字を合わせる必要がある

### 使用例

```sql
-- jpiere-cs: 自分が担当のTODO
WHERE t.assigned_to = @currentUser

-- jpiere-cs: 自分が担当の契約または全ての契約（管理者の場合）
WHERE (@isAdmin = 1 OR c.sales_rep = @currentUser)

-- jpiere-cs: 未認証の場合はデータなし
WHERE @isAuthenticated = 1 AND t.assigned_to = @currentUser

-- auto-dealer-demo: 顧客自身のデータ
WHERE cu.login_username = @currentUser

-- auto-dealer-demo: 担当営業のリード
WHERE sl.assigned_sales = @currentUser
```

---

## 6. SectionDefinition 拡張 (VisibleToRoles)

### `Models/PageDefinition.cs` の変更

`SectionDefinition` クラスに以下を追加:

```csharp
/// <summary>
/// このセクションを表示するロールのホワイトリスト。
/// 未設定（null または空リスト）の場合は全ユーザーに表示。
/// 管理者（IsAdmin=true）は常に表示。
/// YAML: visibleToRoles: [sales_rep, manager]
/// </summary>
public List<string>? VisibleToRoles { get; set; }
```

### YAML での使い方

```yaml
sections:
  # 全員に表示（visibleToRoles 未設定）
  - id: common_kpi
    title: 全体KPI
    component: stat_cards
    source: |
      SELECT ...

  # 経理担当のみ表示
  - id: accounting_section
    title: 会計情報
    visibleToRoles: [finance, admin, manager]
    component: table
    source: |
      SELECT ...

  # 営業担当のみ表示
  - id: my_leads
    title: 自分の担当リード
    visibleToRoles: [sales_rep, sales_manager]
    component: table
    source: |
      SELECT ... WHERE assigned_sales = @currentUser

  # 顧客のみ表示
  - id: my_appointments
    title: 自分の予約
    visibleToRoles: [customer]
    component: table
    source: |
      SELECT ... WHERE cu.login_username = @currentUser
```

### 可視性判定ルール

```
セクション表示条件:
  1. visibleToRoles が null または空 → 常に表示
  2. ユーザーが IsAdmin=true → 常に表示（管理者は全セクション閲覧可）
  3. ユーザーの Roles に visibleToRoles のいずれかが含まれる → 表示
  4. 上記いずれにも該当しない → 非表示（HTML に出力しない）
```

---

## 7. PageDataQueryService の変更

### メソッドシグネチャ変更

```csharp
// 変更前
public async Task<Dictionary<string, (...)>> LoadPageDataAsync(
    PageDefinition page,
    IDictionary<string, string> filters)

// 変更後
public async Task<Dictionary<string, (...)>> LoadPageDataAsync(
    PageDefinition page,
    IDictionary<string, string> filters,
    PageUserContext? userContext = null)       // ← 追加（後方互換のためデフォルトnull）

// 変更前
public Task<(...)> LoadSectionDataAsync(
    SectionDefinition section,
    IDictionary<string, string> allFilters)

// 変更後
public Task<(...)> LoadSectionDataAsync(
    SectionDefinition section,
    IDictionary<string, string> allFilters,
    PageUserContext? userContext = null)       // ← 追加
```

### ユーザー変数注入メソッド（新規追加）

```csharp
/// <summary>
/// DynamicParameters にログインユーザー情報を注入する。
/// SQL 内で @currentUser / @isAdmin 等として参照可能になる。
/// </summary>
private static void InjectUserContext(DynamicParameters param, PageUserContext? ctx)
{
    var user = ctx ?? PageUserContext.Anonymous;
    param.Add("currentUser",            user.UserName);
    param.Add("currentUserDisplayName", user.DisplayName);
    param.Add("currentUserId",          user.UserId);
    param.Add("currentUserRole",        user.Roles.FirstOrDefault() ?? "");
    param.Add("isAdmin",                user.IsAdmin ? 1 : 0);
    param.Add("isAuthenticated",        user.IsAuthenticated ? 1 : 0);
}
```

### GetSectionDataAsync への組み込み

```csharp
private async Task<(...)> GetSectionDataAsync(
    SectionDefinition section,
    IDictionary<string, string?> filters,
    PageUserContext? userContext = null)     // ← 追加
{
    var param = new DynamicParameters();
    InjectUserContext(param, userContext);   // ← 最初に注入

    // ... 既存のフィルター処理 ...
}
```

### 呼び出し側の変更

```csharp
// LoadPageDataAsync 内
var (rows, total) = await GetSectionDataAsync(
    section,
    ExtractSectionFilters(section, filters),
    userContext);                           // ← 追加

// LoadSectionDataAsync 内
=> GetSectionDataAsync(
    section,
    ExtractSectionFilters(section, allFilters),
    userContext);                           // ← 追加
```

---

## 8. PageController の変更

### Index アクション（主要変更点）

```csharp
[HttpGet("{pageName}")]
public async Task<IActionResult> Index(string project, string pageName)
{
    // ... 既存の認証・パーミッションチェック ...

    var filters = Request.Query
        .ToDictionary(k => k.Key, v => v.Value.ToString());

    // ▼ 新規追加: ユーザーコンテキストを生成
    var userCtx = BuildUserContext();

    // ▼ 変更: userCtx を渡す
    var model = await _pageDataQueryService.LoadPageDataAsync(pageDef, filters, userCtx);

    ViewData["PageDef"]   = pageDef;
    ViewData["PageName"]  = pageName;
    // ... 既存の ViewData ...

    // ▼ 新規追加: ロール情報をビューに渡す（セクション可視性制御用）
    ViewData["UserRoles"] = userCtx.Roles;
    ViewData["IsAdmin"]   = userCtx.IsAdmin;

    // ... 既存のビュー選択ロジック ...
}
```

### SectionTable アクション（HTMX 部分更新）

```csharp
[HttpGet("{pageName}/section/{sectionId}")]
public async Task<IActionResult> SectionTable(string project, string pageName, string sectionId)
{
    // ... 既存コード ...

    // ▼ 変更: userCtx を渡す
    var userCtx = BuildUserContext();
    var (rows, total) = await _pageDataQueryService.LoadSectionDataAsync(
        section, allFilters, userCtx);     // ← userCtx 追加

    // ...
}
```

### BuildUserContext ヘルパー（新規追加）

```csharp
private PageUserContext BuildUserContext() => new(
    UserName:        User.Identity?.Name ?? "",
    DisplayName:     User.FindFirst(ClaimTypes.GivenName)?.Value ?? "",
    UserId:          User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "",
    Roles:           User.Claims
                         .Where(c => c.Type == ClaimTypes.Role)
                         .Select(c => c.Value)
                         .ToList(),
    IsAdmin:         UserIsAdmin(),
    IsAuthenticated: User.Identity?.IsAuthenticated == true
);
```

---

## 9. PageView.cshtml の変更

### セクションループへのロール判定追加

```razor
@{
    // ▼ 新規: ロール情報を取得
    var userRoles = (ViewData["UserRoles"] as IReadOnlyList<string>)
                    ?? Array.Empty<string>();
    var isAdmin = (bool)(ViewData["IsAdmin"] ?? false);
}

@foreach (var sec in pageDef.Sections)
{
    // ▼ 新規: VisibleToRoles チェック（管理者は常に表示）
    if (sec.VisibleToRoles?.Count > 0 && !isAdmin)
    {
        var hasRole = sec.VisibleToRoles
            .Any(r => userRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
        if (!hasRole) continue;
    }

    // 既存のセクションデータ取得
    var sectionData = Model.TryGetValue(sec.Id, out var sd)
        ? sd : (Enumerable.Empty<Dictionary<string, object>>(), 0);
    // ... 以降は変更なし ...
}
```

> **注**: `LoadPageDataAsync` はロール非表示セクションのデータも取得してしまう。
> パフォーマンス最適化として将来的に「非表示セクションはSQLを実行しない」改善が可能だが、
> 初期実装では HTML 出力のみをスキップする方針とする。

---

## 10. jpiere-cs への適用設計

### 10.1 project.yaml 変更

```yaml
# 変更前
features:
  userAuthentication: false

# 変更後
features:
  userAuthentication: true
```

### 10.2 ロール定義

jpiere-cs に追加するロール:

| ロール名 | 説明 | 対象ユーザー例 |
|---|---|---|
| `sales_rep` | 営業担当者 | 契約・見積・TODO の担当者 |
| `manager` | 営業管理職 | 全担当者のデータを閲覧可 |
| `finance` | 経理担当者 | 会計・仕訳・請求の確定権限 |
| `purchasing` | 購買担当者 | 発注書・受入・AP請求 |
| `admin` | システム管理者 | 全機能へのアクセス |

### 10.3 MyPage.yaml の改修設計

#### セクション 1: 要対応アクション（全員表示）

変更なし（全ロールに表示）。

#### セクション 4: 対応待ち書類一覧（ロール別フィルタ追加）

```sql
-- 変更前: 全書類を表示
SELECT '見積' AS 種類, e.document_no, ...
FROM estimations e
WHERE e.doc_status IN ('DR', 'IN') AND e.is_active = 1

-- 変更後: 自分担当 or 管理者は全件
SELECT '見積' AS 種類, e.document_no, ...
FROM estimations e
JOIN business_partners bp ON bp.id = e.business_partner_id
WHERE e.doc_status IN ('DR', 'IN')
  AND e.is_active = 1
  AND (@isAdmin = 1 OR e.sales_rep = @currentUser)
```

#### セクション 7: 未完了TODO（自分担当のみ）

```sql
-- 変更前: 全TODO
WHERE t.todo_status IN ('NY', 'IP') AND t.is_active = 1

-- 変更後: 自分担当 or 管理者
WHERE t.todo_status IN ('NY', 'IP')
  AND t.is_active = 1
  AND (@isAdmin = 1 OR t.assigned_to = @currentUser)
```

#### セクション 会計（finance・admin のみ表示）

```yaml
- id: accounting_overview
  title: 会計状況（経理担当向け）
  visibleToRoles: [finance, admin, manager]
  component: stat_cards
  source: |
    SELECT '未確定仕訳' AS metric_name,
           CAST(COUNT(*) AS TEXT) || ' 件' AS metric_value,
           '📒' AS metric_icon, NULL AS metric_delta
    FROM journals WHERE doc_status = 'DR'
    UNION ALL
    SELECT '今月の売上認識合計',
           CAST(ROUND(COALESCE(SUM(grand_total), 0) / 10000, 1) AS TEXT) || '万円',
           '💹', NULL
    FROM recognitions
    WHERE doc_status = 'CO'
      AND strftime('%Y-%m', date_acct) = strftime('%Y-%m', 'now')
```

#### 完成後の MyPage セクション構成

| セクション ID | タイトル | visibleToRoles | @currentUser 使用 |
|---|---|---|---|
| `urgent_actions` | 要対応アクション | なし（全員） | なし |
| `workflow_guide` | 業務フローガイド | なし（全員） | なし |
| `kpi_overview` | 業務状況KPI | なし（全員） | なし |
| `my_pending_docs` | 自分の対応待ち書類 | なし（全員） | `sales_rep = @currentUser` |
| `payment_alerts` | 支払期限アラート | `[finance, manager, admin]` | なし |
| `contract_expiry_alerts` | 契約期限アラート | なし（全員） | なし |
| `my_todos` | 自分のTODO | なし（全員） | `assigned_to = @currentUser` |
| `accounting_overview` | 会計状況 | `[finance, admin, manager]` | なし |
| `document_status_map` | 書類ステータスマップ | なし（全員） | なし |
| `status_reference` | ステータスコード辞書 | なし（全員） | なし |

---

## 11. auto-dealer-demo への適用設計

### 11.1 現状の問題

`CustomerDashboard.yaml` は顧客全員の予約・商談を表示しており、  
顧客 A が顧客 B の予約も見えてしまう（プライバシー問題）。

`Welcome.yaml` は全ロールのガイドセクションを全ユーザーに表示している。

### 11.2 DB 変更: login_username カラム追加

`customers` テーブルと `sales_leads` テーブルに `login_username` を追加し、  
ログインユーザー名（`app_user.user_name`）を格納する。

```sql
-- auto-dealer-demo/database/init.sql に追加

ALTER TABLE customers ADD COLUMN login_username TEXT;
ALTER TABLE sales_leads ADD COLUMN assigned_sales TEXT;  -- 営業担当者のlogin名

-- テストデータ更新例
UPDATE customers SET login_username = 'customer1' WHERE customer_id = 'C001';
UPDATE customers SET login_username = 'customer2' WHERE customer_id = 'C002';
UPDATE sales_leads SET assigned_sales = 'sales1' WHERE assigned_to_user_id IS NULL;
```

> **理由**: `app_user`（system.db）と `customers`（auto-dealer-demo.db）は  
> 別データベースのため JOIN 不可。`login_username` カラムで名前を直接保持する。

### 11.3 CustomerDashboard.yaml の改修設計

```yaml
# セクション: 自分の予約（顧客のみ表示）
- id: my_appointments
  title: 私の予約
  visibleToRoles: [customer]
  component: table
  source: |
    SELECT
      strftime('%m/%d %H:%M', a.preferred_date) AS 日時,
      CASE a.appointment_type
        WHEN 'test_drive' THEN '試乗'
        WHEN 'inspection' THEN '点検'
        WHEN 'repair'     THEN '修理'
        ELSE a.appointment_type
      END AS 種別,
      CASE a.status
        WHEN 'confirmed' THEN '確認済み'
        WHEN 'pending'   THEN '確認待ち'
        WHEN 'completed' THEN '完了'
        ELSE a.status
      END AS ステータス
    FROM service_appointments a
    JOIN customers cu ON a.customer_id = cu.customer_id
    WHERE cu.login_username = @currentUser   -- ← 自分のみ
      AND a.status IN ('pending', 'confirmed')
    ORDER BY a.preferred_date ASC
    LIMIT 10

# セクション: 自分の商談（顧客のみ表示）
- id: my_leads
  title: 進行中の商談
  visibleToRoles: [customer]
  component: table
  source: |
    SELECT
      v.make || ' ' || v.model AS 車種,
      CAST(v.price / 10000 AS TEXT) || '万円' AS 価格,
      CASE sl.status
        WHEN 'new'         THEN '新規問合せ'
        WHEN 'contacted'   THEN '連絡済み'
        WHEN 'negotiating' THEN '商談中'
        ELSE sl.status
      END AS 状況
    FROM sales_leads sl
    JOIN customers cu ON sl.customer_id = cu.customer_id
    LEFT JOIN vehicles v ON sl.vehicle_id = v.vehicle_id
    WHERE cu.login_username = @currentUser   -- ← 自分のみ
      AND sl.status NOT IN ('won', 'lost')
```

### 11.4 Welcome.yaml の改修設計（ロール別ガイド）

```yaml
# オペレーター向けガイド（現状: 全員に表示 → 変更後: operatorのみ）
- id: guide_operator
  title: AI オペレーター — 業務フロー
  visibleToRoles: [operator, ai_admin]          # ← 追加
  component: stat_cards
  source: |
    SELECT ...

# 営業担当者向けガイド
- id: guide_sales
  title: 営業担当者 — 業務フロー
  visibleToRoles: [sales_rep, sales_manager]    # ← 追加
  component: stat_cards
  source: |
    SELECT ...

# サービス部門向けガイド
- id: guide_service
  title: サービス部門 — 業務フロー
  visibleToRoles: [service_staff, sales_manager] # ← 追加
  component: stat_cards
  source: |
    SELECT ...

# 経営層向けガイド
- id: guide_exec
  title: 経営層 — 業務フロー
  visibleToRoles: [executive, ai_admin]          # ← 追加
  component: stat_cards
  source: |
    SELECT ...

# 顧客向けガイド（新規追加）
- id: guide_customer
  title: ご利用ガイド
  visibleToRoles: [customer]
  component: stat_cards
  source: |
    SELECT '予約確認' AS metric_name,
           '試乗・点検の予約状況を確認できます' AS metric_value,
           '📅' AS metric_icon,
           '/auto-dealer-demo/Page/CustomerDashboard' AS metric_delta
    UNION ALL
    SELECT 'AI 車両相談',
           '24時間 AI がご相談に対応します',
           '🤖', '/auto-dealer-demo/api/ai/chat/session'
    UNION ALL
    SELECT '車両在庫閲覧',
           '最新の在庫車両を検索できます',
           '🚗', '/auto-dealer-demo/Page/PublicVehicles'
```

### 11.5 SalesRepDashboard.yaml の改修設計

```yaml
# 自分の担当リードのみ表示
- id: my_active_leads
  title: 自分の担当リード
  visibleToRoles: [sales_rep, sales_manager]
  component: table
  source: |
    SELECT
      cu.name AS 顧客名,
      CAST(sl.lead_score AS TEXT) AS スコア,
      sl.status AS 状況,
      sl.last_contact_at AS 最終連絡日
    FROM sales_leads sl
    JOIN customers cu ON sl.customer_id = cu.customer_id
    WHERE sl.assigned_sales = @currentUser     -- ← 自分の担当のみ
      AND sl.status NOT IN ('won', 'lost')
    ORDER BY sl.lead_score DESC
    LIMIT 20
```

---

## 12. DB 横断制約と対処方法

### 制約

NetYamlForge の各プロジェクトは独立した SQLite DB を持ちます。  
`app_user`（system.db）と各プロジェクト DB（例: auto-dealer-demo.db）は  
**同一 SQL クエリで JOIN できません**。

```
system.db
  └── app_user (user_name, display_name, ...)

auto-dealer-demo.db
  └── customers (customer_id, ...)   ← app_user と直接結合不可
```

### 対処方法

**方法 A: 参照コピー（推奨）**  
プロジェクト DB の各テーブルに `login_username` カラムを追加し、  
登録・ログイン時に `app_user.user_name` を同期する。

```sql
customers.login_username = app_user.user_name  (登録時に設定)
contracts.sales_rep      = app_user.user_name  (そのまま使用可)
todos.assigned_to        = app_user.user_name  (そのまま使用可)
```

jpiere-cs は `sales_rep` / `assigned_to` フィールドが既にテキスト型で、  
ログインユーザー名（`app_user.user_name`）と同じ値を格納すれば **追加カラム不要**。

**方法 B: SQLite ATTACH（非推奨）**  
`ATTACH DATABASE '/path/system.db' AS sys` は可能だが、  
セキュリティ上のリスクと設定の複雑化から採用しない。

### jpiere-cs での対応

| フィールド | 変更内容 |
|---|---|
| `todos.assigned_to` | `app_user.user_name` を格納（変更なし、運用ルールの設定のみ） |
| `contracts.sales_rep` | 同上 |
| `estimations.sales_rep` | 同上 |
| `purchase_orders.approved_by` | 同上 |

### auto-dealer-demo での対応

| テーブル | 追加カラム | 格納値 |
|---|---|---|
| `customers` | `login_username TEXT` | `app_user.user_name` |
| `sales_leads` | `assigned_sales TEXT` | 担当営業の `user_name` |
| `service_appointments` | — | `customers.login_username` 経由で参照 |

---

## 13. セキュリティ考慮事項

### SQL インジェクション対策

`@currentUser` 等は Dapper の `DynamicParameters` 経由で注入するため、  
パラメータはプリペアドステートメントとして処理されます。  
SQL 文字列への直接埋め込みは一切行いません。

```csharp
// 安全 (Dapper が適切にエスケープ)
param.Add("currentUser", user.UserName);
// SQL: WHERE assigned_to = @currentUser

// 危険（採用しない）
sql = sql.Replace("@currentUser", user.UserName);
```

### 権限昇格防止

- `@isAdmin` は `UserIsAdmin()` メソッドの結果を使用（Controller 側の信頼済み判定）
- URL パラメータで `@isAdmin=1` を渡しても無視される（`InjectUserContext` で上書き）
- `visibleToRoles` の判定は**サーバーサイド**で実施（クライアント改ざん不可）

### データ漏洩防止

- `visibleToRoles` で非表示にしたセクションは HTML に出力されない
- ただし `LoadPageDataAsync` は非表示セクションの SQL も実行する（パフォーマンス最適化は将来課題）
- HTMX 部分更新（`SectionTable` アクション）でも同様のロールチェックを実施

---

## 14. 実装ステップ（順序付き）

### Phase A: フレームワーク側の基盤実装

```
A-1. Models/PageUserContext.cs 新規作成
     → PageUserContext レコード定義

A-2. Models/PageDefinition.cs 修正
     → SectionDefinition に VisibleToRoles プロパティ追加

A-3. Services/Page/PageDataQueryService.cs 修正
     → LoadPageDataAsync/LoadSectionDataAsync のシグネチャ変更
     → InjectUserContext メソッド追加
     → GetSectionDataAsync に userContext パラメータ追加

A-4. Controllers/PageController.cs 修正
     → BuildUserContext() ヘルパー追加
     → Index / SectionTable アクションで userCtx を生成・渡す
     → ViewData に UserRoles / IsAdmin を追加

A-5. Views/Page/PageView.cshtml 修正
     → VisibleToRoles チェックを各セクションループに追加

A-6. dotnet build & 既存テスト実行（回帰確認）
```

### Phase B: jpiere-cs への適用

```
B-1. jpiere-cs/project.yaml
     → userAuthentication: true に変更

B-2. system.db にテストユーザー追加
     → jpiere-cs 用ロール付きユーザー（sales_rep/finance/manager）

B-3. jpiere-cs/pages/MyPage.yaml 修正
     → @currentUser / @isAdmin を活用した SQL に更新
     → visibleToRoles でセクション制御

B-4. jpiere-cs/config/layout.yml
     → 将来のロール別ナビゲーション設定（オプション）

B-5. 動作確認: sales_rep ユーザーでログイン → 自分のTODOのみ表示確認
```

### Phase C: auto-dealer-demo への適用

```
C-1. auto-dealer-demo/database/init.sql 修正
     → customers に login_username カラム追加
     → sales_leads に assigned_sales カラム追加
     → テストデータ更新（既存ユーザーとの紐付け）

C-2. auto-dealer-demo/pages/CustomerDashboard.yaml 修正
     → @currentUser で自分のデータのみにフィルタ
     → visibleToRoles: [customer] を設定

C-3. auto-dealer-demo/pages/Welcome.yaml 修正
     → 各ガイドセクションに visibleToRoles を追加

C-4. auto-dealer-demo/pages/SalesRepDashboard.yaml 修正
     → assigned_sales = @currentUser フィルタ追加

C-5. 動作確認: customer1 でログイン → 自分の予約のみ表示確認
     　　　　   sales1 でログイン → 自分の担当リードのみ表示確認
```

---

## 15. テスト計画

### ユニットテスト

| テストクラス | テスト内容 |
|---|---|
| `PageUserContextTests` | Anonymous/有効ユーザーの生成、HasRole/HasAnyRole の動作 |
| `PageDataQueryServiceTests` | `@currentUser` が SQL パラメータに正しく注入される |
| `PageDataQueryServiceTests` | 未認証時（Anonymous）に空文字が注入される |
| `PageDataQueryServiceTests` | `@isAdmin=1` が管理者ユーザーで正しく設定される |

### 統合テスト（コントローラーレベル）

| シナリオ | 確認内容 |
|---|---|
| `visibleToRoles` 設定セクション、ロール非保持ユーザー | セクションが HTML に出力されないこと |
| `visibleToRoles` 設定セクション、ロール保持ユーザー | セクションが正常に表示されること |
| 管理者ユーザー | `visibleToRoles` に関わらず全セクションが表示されること |
| 未認証ユーザーが `isPublic: true` ページにアクセス | `@isAuthenticated=0` で SQL が実行されること |

### E2E シナリオ（手動確認）

**jpiere-cs:**
1. `sales1`（sales_rep）でログイン → MyPage → 自分の assigned_to のTODOのみ表示
2. `finance1`（finance）でログイン → MyPage → 会計セクションが表示される
3. `sales2`（sales_rep）でログイン → MyPage → sales1 のTODOは非表示

**auto-dealer-demo:**
1. `customer1` でログイン → CustomerDashboard → C001 の予約のみ表示
2. `customer2` でログイン → CustomerDashboard → C002 の予約のみ表示（C001 は非表示）
3. `sales1` でログイン → Welcome → 営業担当ガイドセクションのみ表示（顧客ガイドは非表示）
4. `manager1` でログイン → Welcome → 全セクション表示（管理者権限）
