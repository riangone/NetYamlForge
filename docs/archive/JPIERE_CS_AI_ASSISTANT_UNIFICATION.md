# jpiere-cs AIチャットUI統一 — 実装サマリー

## 概要

jpiere-cs プロジェクトのAIチャットUIを、フレームワーク全体の AI Assistant と同じパネル型UIに統一しました。

以前は `ChatDetail.yaml` で静的なテーブル表示を使用していましたが、右側からスライドインするパネル型UI（`ai-assistant.js` / `ai-assistant.css`）に切り替えました。

## 変更ファイル

| ファイル | 変更内容 |
|----------|----------|
| `NetYamlForge/Views/Shared/_Layout.cshtml` | jpiere-cs でも `ai-assistant.js` を読み込むように条件を変更。役割別設定を `window.JPIERE_CHAT_CONFIG` で渡す |
| `NetYamlForge/wwwroot/js/ai-assistant.js` | jpiere-cs 専用の役割別設定（6役割）を追加。パネルタイトルとウェルカムメッセージを役割に応じて変更 |
| `NetYamlForge/projects/jpiere-cs/pages/ChatDetail.yaml` | 削除（AI Assistant パネルに統合） |

## 変更詳細

### 1. `_Layout.cshtml`

```diff
- @* ai-assistant.js: auto-dealer-demo と jpiere-cs 以外の場合のみ読み込む *@
- @if (currentProject != "auto-dealer-demo" && currentProject != "jpiere-cs")
+ @* ai-assistant.js: auto-dealer-demo 以外の場合のみ読み込む（jpiere-cs を含む） *@
+ @if (currentProject != "auto-dealer-demo")
  {
      <script src="~/js/ai-assistant.js" asp-append-version="true"></script>
+     @* jpiere-cs: ログイン済みユーザー向け AI チャット初期化（役割別） *@
+     @if (currentProject == "jpiere-cs" && (User.Identity?.IsAuthenticated ?? false))
+     {
+         var jpiereUserRole = userCustomRoles.FirstOrDefault() ?? "employee";
+         <script>
+             // jpiere-cs 専用の役割別設定を ai-assistant.js に渡す
+             window.JPIERE_CHAT_CONFIG = {
+                 project: 'jpiere-cs',
+                 userRole: '@jpiereUserRole'
+             };
+         </script>
+     }
  }
- @* jpiere-cs: ログイン済みユーザー向け AI チャットウィジェット（役割別） *@
- @if (currentProject == "jpiere-cs" && (User.Identity?.IsAuthenticated ?? false))
- {
-     var jpiereUserRole = userCustomRoles.FirstOrDefault() ?? "employee";
-     <script src="~/js/jpiere-chat-widget.js" asp-append-version="true"></script>
-     <script>
-         JpiereChat.init({ project: 'jpiere-cs', userRole: '@jpiereUserRole' });
-     </script>
- }
```

### 2. `ai-assistant.js` — jpiere-cs 役割別設定の追加

```javascript
// jpiere-cs 役割別設定
const JPIERE_ROLE_CONFIGS = {
    employee: {
        icon: '👤',
        title: 'AI 業務アシスタント',
        subtitle: '契約・見積・TODO の照会をサポート',
        welcomeMessage: 'こんにちは！JPiere の AI 業務アシスタントです。📋\n契約・見積・TODO の照会など、業務全般を支援します！'
    },
    contract_manager: {
        icon: '💼',
        title: 'AI 契約アシスタント',
        subtitle: '契約・見積・請求の作成・分析',
        welcomeMessage: 'こんにちは！JPiere 契約担当 AI アシスタントです。💼\n契約・見積・請求の作成・分析をお手伝いします！'
    },
    accountant: {
        icon: '💰',
        title: 'AI 会計アシスタント',
        subtitle: '仕訳・会計・資金管理',
        welcomeMessage: 'こんにちは！JPiere 会計担当 AI アシスタントです。💰\n仕訳・会計・入金・支払の管理を支援します！'
    },
    purchaser: {
        icon: '📦',
        title: 'AI 購買アシスタント',
        subtitle: '発注・受入・AP請求・支払',
        welcomeMessage: 'こんにちは！JPiere 購買担当 AI アシスタントです。📦\n発注・受入・AP請求・支払のフローを支援します！'
    },
    approver: {
        icon: '✅',
        title: 'AI 承認アシスタント',
        subtitle: '承認ワークフローの確認・処理',
        welcomeMessage: 'こんにちは！JPiere 承認 AI アシスタントです。✅\n承認ワークフローの確認・処理を支援します！'
    },
    admin: {
        icon: '⚙️',
        title: 'AI 管理アシスタント',
        subtitle: 'システム管理・設定・分析',
        welcomeMessage: 'こんにちは！JPiere 管理者 AI アシスタントです。⚙️\nシステム全体的管理・設定変更を支援します！'
    }
};
```

### 3. 役割別パネルタイトルとウェルカムメッセージ

`buildPanelHTML()` 関数を更新し、jpiere-cs モードでは役割別のタイトルとウェルカムメッセージを表示するようにしました。

```javascript
function buildPanelHTML() {
    const isJpiere = CONFIG.isJpiereCS;
    const roleConfig = isJpiere
        ? (JPIERE_ROLE_CONFIGS[CONFIG.jpiereRole] || JPIERE_ROLE_CONFIGS.employee)
        : null;

    const panelTitle = roleConfig
        ? `${roleConfig.icon} ${roleConfig.title}`
        : 'AI Assistant';

    const welcomeMessage = roleConfig
        ? roleConfig.welcomeMessage
        : 'こんにちは！AI アシスタントです。以下のことをお手伝いできます：...';

    // ... パネルHTML生成
}
```

## 役割別設定一覧

| 役割 | タイトル | プレースホルダー |
|------|----------|-----------------|
| `employee` | 👤 AI 業務アシスタント | 契約・見積・TODO の照会をサポート |
| `contract_manager` | 💼 AI 契約アシスタント | 契約・見積・請求の作成・分析 |
| `accountant` | 💰 AI 会計アシスタント | 仕訳・会計・資金管理 |
| `purchaser` | 📦 AI 購買アシスタント | 発注・受入・AP請求・支払 |
| `approver` | ✅ AI 承認アシスタント | 承認ワークフローの確認・処理 |
| `admin` | ⚙️ AI 管理アシスタント | システム管理・設定・分析 |

## メリット

1. **UI 統一**: フレームワーク全体で一貫したAIチャット体験
2. **コード削減**: `jpiere-chat-widget.js` と `ai-assistant.js` の二重管理が不要に
3. **機能追加が容易**: フレームワーク側のAI機能（スキルバー、履歴ナビ、SignalR等）がそのまま利用可能
4. **役割別カスタマイズ**: ユーザーの役割に応じてウェルカムメッセージとタイトルを最適化

## 保持される機能

- `AIDashboard.yaml` — AI ダッシュボード（KPI、グラフ、テーブル表示）
- `AIAnalytics.yaml` — AI 詳細分析（性能指標、意図認識、感情分析）

これらはダッシュボード分析ページとして有用なため、そのまま保持しています。
