# Hermes Agent 設計思想分析

**作成者**: Copilot AI  
**参照リポジトリ**: https://github.com/nousresearch/hermes-agent  
**分析日**: 2026-04-16

---

## 📌 概要

**Hermes Agent** は Nous Research によって開発された、**自己改善型の AI エージェント** です。最大の特徴は、**経験から スキルを自動生成し、使用中に改善を行い、セッション間で深まるユーザーモデルを構築する組み込み学習ループ** を備えていることです。

### 核心的な特徴

| 特徴 | 説明 |
|------|------|
| **自己改善学習ループ** | 経験からスキルを動的に生成・改善 |
| **マルチプラットフォーム対応** | CLI、Telegram、Discord、Slack、WhatsApp など複数の入口 |
| **モデル非依存** | OpenAI、Anthropic、OpenRouter など 200+ のモデルをサポート |
| **スキルシステム** | コードレス MARKDOWN ベースのスキル定義 |
| **統合メモリシステム** | セッション横断的な永続的記憶（ユーザープロフィール、学習） |
| **クラウド対応** | $5 VPS やサーバーレスインフラで動作（Modal、Daytona） |
| **スケーラビリティ** | マルチテナント・マルチプロフィル対応 |

---

## 🏗️ アーキテクチャ設計

### 1. システムレイアウト

```
┌─────────────────────────────────────────────────────────────┐
│                    エントリーポイント                        │
├─────────────────────────────────────────────────────────────┤
│ CLI (cli.py) | Gateway | ACP (IDE統合) | Batch Runner      │
└──────────────────────────┬──────────────────────────────────┘
                          │
┌──────────────────────────▼──────────────────────────────────┐
│                    AIAgent (run_agent.py)                    │
├─────────────────────────────────────────────────────────────┤
│ • Prompt Builder   • Provider Resolution • Tool Dispatch    │
│ • Compression      • Caching             • Callbacks        │
│ • Retry/Fallback   • Budget Management                      │
└──────────────────────────┬──────────────────────────────────┘
                          │
┌──────────────────────────┴──────────────────────────────────┐
│              永続層・実行環境                                 │
├─────────────────────────────────────────────────────────────┤
│ • Session Storage (SQLite + FTS5)                           │
│ • Tool Backends (Terminal、Browser、Web、MCP)              │
│ • 6 ターミナルバックエンド                                   │
└─────────────────────────────────────────────────────────────┘
```

### 2. ディレクトリ構造

```
hermes-agent/
├── run_agent.py              # 💎 コア AIAgent クラス (~10,700 行)
├── cli.py                    # CLI 端末 UI (~10,000 行)
├── model_tools.py            # ツール検出・ディスパッチ
├── hermes_state.py           # SQLite セッション DB (FTS5 対応)
│
├── agent/                    # エージェント内部実装
│   ├── prompt_builder.py     # システムプロンプト組立
│   ├── context_engine.py     # コンテキスト管理 (プラグイン可能)
│   ├── context_compressor.py # デフォルト圧縮エンジン
│   ├── prompt_caching.py     # Anthropic プロンプトキャッシング
│   ├── auxiliary_client.py   # 補助 LLM クライアント
│   └── memory_manager.py     # メモリ管理オーケストレーション
│
├── tools/                    # ツール実装 (47 個)
│   ├── registry.py           # 中央ツールレジストリ
│   ├── terminal_tool.py      # ターミナル実行
│   ├── file_tools.py         # ファイル操作
│   ├── web_tools.py          # ウェブ検索・抽出
│   ├── browser_tool.py       # ブラウザ自動化
│   ├── mcp_tool.py           # MCP クライアント
│   └── environments/         # 6 つのターミナルバックエンド
│
├── gateway/                  # メッセージングゲートウェイ
│   ├── run.py                # ゲートウェイランナー (~9,000 行)
│   ├── session.py            # セッションストア
│   ├── platforms/            # 18 プラットフォームアダプター
│   └── hooks.py              # ライフサイクルフック
│
├── skills/                   # 組み込みスキル (MARKDOWN ベース)
│   ├── research/
│   ├── productivity/
│   └── ...
│
└── tests/                    # 3,000+ pytest テスト
```

---

## 🧠 設計哲学

### 原則 1: プロンプト安定性

- **システムプロンプトは会話中に変更されない**
- キャッシュ破壊は明示的なユーザーアクション（`/model` コマンド）でのみ発生
- Anthropic プロンプトキャッシング対応で API コスト削減

```python
# プロンプトはビルド時に一度だけ組立
system_prompt = prompt_builder.build_system_prompt()
# → 会話を通じて変わらない
```

### 原則 2: 観察可能な実行

- **すべてのツール呼び出しがユーザーに可視**
- CLI スピナー、ゲートウェイ進捗メッセージで追跡可能
- 実行結果がリアルタイムで表示される

### 原則 3: 割り込み可能性

```python
# API 呼び出しは背景スレッドで実行
# メインスレッドはユーザー入力を監視
api_call_thread = ThreadPoolExecutor()
while not response_ready and not interrupted:
    time.sleep(0.1)  # ユーザー入力待機
```

- ユーザーが `/stop` で即座に中断可能
- シグナルハンドリング対応

### 原則 4: プラットフォーム非依存コア

- **単一の `AIAgent` クラスが全エントリーポイントをサーブ**
- CLI、Gateway、ACP、Batch、API Server は共通コアを使用
- プラットフォーム固有の処理はエントリーポイント層に限定

```python
# AIAgent は CLI でも Gateway でも同じ
response = agent.run_conversation(
    user_message="...",
    system_message=None,       # 自動生成
    conversation_history=None  # セッションから自動ロード
)
```

### 原則 5: 疎結合アーキテクチャ

- オプション機能 (MCP、プラグイン、メモリプロバイダー) は
  **レジストリパターン + ゲーティング関数** で統合
- ハード依存なし
- プラグイン可能 → 拡張性が極めて高い

```python
# ツール登録 = レジストリパターン（自動検出）
@registry.register()
def my_tool(args):
    pass
```

### 原則 6: プロファイル分離

- 各プロフィール（`hermes -p <name>`）は独立
- `HERMES_HOME`、設定、メモリ、セッション、Gateway PID が独立
- 複数プロフィルが同時実行可能

---

## 🔄 エージェントループ（ターンサイクル）

### ターン生命周期

```
1. タスク ID 生成
   ↓
2. ユーザーメッセージを会話履歴に追加
   ↓
3. システムプロンプト構築 or キャッシュ利用
   ↓
4. プリフライトチェック (>50% コンテキスト?)
   → 必要なら圧縮実行
   ↓
5. API メッセージをフォーマット
   • chat_completions:   OpenAI 形式
   • codex_responses:    Responses API
   • anthropic_messages: Anthropic 形式
   ↓
6. エフェメラルプロンプト層を注入
   • 予算警告
   • コンテキスト圧力インジケータ
   ↓
7. プロンプトキャッシング マーカー適用 (Anthropic)
   ↓
8. 割り込み可能 API 呼び出し
   ┌─────────────────┐
   │ メインスレッド  │ (ユーザー入力監視)
   │  wait on:       │
   │  • 応答完了     │─→ [背景スレッド: HTTP POST]
   │  • 割り込みイベント │
   │  • タイムアウト  │
   └─────────────────┘
   ↓
9. 応答解析
   ├─ ツール呼び出しあり?
   │  → 実行（順序実行 or 並行実行）
   │  → 結果を履歴に追加
   │  → ステップ 5 へループ
   │
   └─ テキスト応答のみ?
      → セッション永続化
      → メモリフラッシュ
      → 応答返却
```

### メッセージ形式（内部）

すべて OpenAI 互換形式で統一：

```python
{"role": "system", "content": "..."}
{"role": "user", "content": "..."}
{"role": "assistant", "content": "...", "tool_calls": [...]}
{"role": "tool", "tool_call_id": "...", "content": "..."}
```

### ツール実行の詳細

```python
# 単一ツール呼び出し → メインスレッド実行
# 複数ツール呼び出し → ThreadPoolExecutor で並行実行
#   ※ interactive ツール (e.g., clarify) は順序実行

for tool_call in response.tool_calls:
    1. ツール検出
    2. pre_tool_call フック発火
    3. 危険コマンドチェック (tools/approval.py)
    4. ユーザー承認 (必要なら)
    5. ハンドラー実行
    6. post_tool_call フック発火
    7. {"role": "tool", "content": result} を履歴に追加
```

---

## 🛠️ コア機構

### 1. プロンプト構築システム

システムプロンプトは以下の層から構築：

```python
system_prompt = {
    "personality": load("SOUL.md"),           # パーソナリティ
    "memory": load("MEMORY.md", "USER.md"),   # 永続記憶
    "skills": list_active_skills(),           # スキル説明
    "context_files": load("AGENTS.md", ".hermes.md"),  # プロジェクト文脈
    "tool_guidance": format_tool_schemas(),   # ツール使用法
    "model_instructions": get_model_specific()  # モデル固有命令
}
```

**キャッシング最適化**:
- Anthropic: プロンプトキャッシングマーカーを自動挿入
- OpenAI: `hash()` ベースのメモリ内キャッシュ

### 2. プロバイダー解決ランタイム

```python
# 解決順序
1. 明示的 api_mode 指定        (最優先)
2. プロバイダー固有検出        (e.g., anthropic → anthropic_messages)
3. Base URL ヒューリスティック (e.g., api.anthropic.com)
4. デフォルト: chat_completions

# 18+ プロバイダーをサポート
{
    "openai": {...},
    "anthropic": {...},
    "openrouter": {...},
    "custom": {...},
    # + ローカル Ollama、LM Studio など
}
```

### 3. ツールレジストリシステム

```python
# tools/registry.py (中央レジストリ)
TOOL_REGISTRY = {
    "terminal": TerminalTool,
    "file_read": FileReadTool,
    "web_search": WebSearchTool,
    # ... 47 個のツール
}

# 自動登録パターン (tools/*.py)
@registry.register()
class MyTool(BaseTool):
    def execute(self, args):
        pass
```

**19 のツールセット**:
- `basic` (コア ツール)
- `web` (web_search、web_extract)
- `browser` (自動化)
- `dev` (コード実行、git)
- `etc.`

### 4. セッション永続化

**SQLite + FTS5** による設計：

```sql
-- セッションテーブル
CREATE TABLE sessions (
    id TEXT PRIMARY KEY,
    user_id TEXT,
    messages JSONL,           -- 各メッセージが 1 行
    metadata JSON,            -- 圧縮履歴、親セッション ID など
    created_at TIMESTAMP,
    last_updated TIMESTAMP
);

-- FTS5 フルテキストインデックス
CREATE VIRTUAL TABLE session_fts USING fts5(
    content,
    session_id,
    message_id
);
```

**機能**:
- 会話履歴の完全な永続化
- クロスセッション検索（`/resume` で復帰）
- 圧縮時のリネージ追跡（親→子セッション）
- 並行アクセス対応（ロック機構）

### 5. コンテキスト圧縮

**トリガー**:
- **プリフライト** (API 呼び出し前): >50% コンテキスト
- **ゲートウェイ自動** (ターン間): >85% コンテキスト

**圧縮プロセス**:

```python
1. メモリを先にディスクにフラッシュ
   (データ損失防止)

2. 中間の会話ターンを要約
   - 最後の N メッセージは保護 (compress.protect_last_n = 20)
   - ツール呼び出し/結果ペアは分割しない

3. 新しいセッションリネージ ID を生成
   (圧縮 = "子"セッション作成)

4. メモリ効率を大幅改善
   (例: 200K トークン → 30K トークン)
```

### 6. 割り込みメカニズム

```python
def _api_call_with_interrupt(self, request):
    # メインスレッド
    interrupt_event = threading.Event()
    
    # 背景スレッド
    api_thread = ThreadPoolExecutor().submit(
        lambda: self.client.chat.completions.create(**request)
    )
    
    # メインはユーザー入力待機
    while not response_ready:
        if user_input_received():  # /stop コマンド
            interrupt_event.set()
            api_thread.abandon()    # ※ キャンセル (レスポンス破棄)
            break
        time.sleep(0.1)
```

---

## 🎯 スキルシステム

### スキルとは

**スキル** = YAML フロントマター + Markdown 形式の手順書

```yaml
---
name: arxiv-search
description: arXiv 論文検索
version: 1.0.0
author: Nous Research
platforms: [linux, macos]     # OS 制限 (オプション)
metadata:
  hermes:
    tags: [Research, Academia]
    requires_tools: [web_search]    # ツール依存
    config:
      - key: arxiv.max_results
        default: 10
required_environment_variables:
  - name: ARXIV_API_KEY
    help: Get from https://...
---

# arXiv 論文検索

## いつに使うか
ユーザーが学術論文を探すとき

## 手順
1. `web_search` ツールで arXiv クエリ実行
2. JSON パース
3. ...
```

### スキル vs ツール

| 基準 | スキル | ツール |
|------|---------|---------|
| 定義方法 | MARKDOWN（コードレス） | Python コード |
| API キー管理 | CLI での対話的入力 | コード内（難しい） |
| 変更頻度 | 頻繁（ユーザーが追加可能） | 稀（コアチーム） |
| 例 | arXiv検索、Docker 管理 | ブラウザ自動化、ビジョン |

### スキル機能

```yaml
# 条件付きロード
requires_toolsets: [web]        # web toolset が有効なとき表示
requires_tools: [web_search]    # web_search ツールがあるとき表示

fallback_for_tools: [browser]   # browser ツール がないとき表示
                                # (フォールバック用途)

# 環境変数パススルー
required_environment_variables:
  - name: MY_KEY
    prompt: "API Key?"
    help: "Get at https://..."
```

---

## 📱 マルチプラットフォーム対応

### ゲートウェイアーキテクチャ

```
Telegram ─┐
Discord  ├─→ [GatewayRunner] ─→ AIAgent
Slack    ├─→ Message Router
WhatsApp ├─→ Session Store (SQLite)
Signal   ├─→ Message Delivery
...      └─
```

**18 プラットフォームアダプター**:
- Telegram、Discord、Slack、WhatsApp、Signal
- Matrix、Mattermost、Email、SMS
- DingTalk、Feishu、WeChat、QQ Bot
- Home Assistant、Webhook、API Server

### セッション統一性

```python
# CLI で開始した会話
$ hermes chat
> "何をすればいい?"

# Telegram で同じセッションを再開
@hermes_bot: /resume
> 前回は ... をしていました

# セッション履歴が保持される
# ユーザーモデルが永続化される
```

---

## 🔐 セキュリティ & 権限管理

### コマンド承認フロー

```python
tools/approval.py:
├─ execute_code  (危険)      → ユーザー承認待機
├─ terminal      (危険)      → 承認必須 or 許可リスト
├─ rm -rf        (超危険)    → 強制承認
└─ read_file     (安全)      → 自動承認
```

### ユーザー認証

```python
# Gateway での認証
pairing.py:
├─ 初回 DM: ペアリングコード生成
├─ ユーザーが本人確認
└─ 承認リスト に追加
```

### プロファイル分離

```bash
hermes -p alice config set provider openai
hermes -p bob config set provider anthropic

# alice と bob は独立
# ~/.hermes/alice/
# ~/.hermes/bob/
# (config、メモリ、セッション 分離)
```

---

## ⚙️ 構成管理

### コンテキスト構文

```yaml
# ~/.hermes/config.yaml
profiles:
  default:
    provider: openrouter
    model: openrouter/meta-llama/llama-3-70b-instruct
    
    api:
      timeout: 30
      retries: 3
      fallback_providers:
        - anthropic/claude-3-sonnet
        - openai/gpt-4-turbo
    
    compression:
      trigger_percent: 0.5      # 50% でトリガー
      protect_last_n: 20        # 最後の 20 メッセージ保護
    
    gateway:
      telegram:
        token: $TELEGRAM_BOT_TOKEN
        allowlist: [alice, bob]
      discord:
        token: $DISCORD_BOT_TOKEN
    
    terminal:
      backend: docker           # local, docker, ssh, modal, daytona
      env_passthrough:
        - MY_API_KEY
        - DB_PASSWORD
```

### 動的設定

```bash
hermes config set provider anthropic
hermes config set models.default claude-3-opus
hermes config set compression.trigger_percent 0.6
hermes config show

# 設定ファイルが自動更新
```

---

## 🎓 学習ループと自己改善

### 1. スキル自動生成

```python
# 複雑なタスク完了後
if is_complex_task(messages):
    # ユーザーのワークフロー分析
    workflow = analyze_conversation()
    
    # スキル候補生成
    skill_template = {
        "name": workflow.name,
        "description": workflow.summary,
        "procedure": workflow.steps
    }
    
    # ユーザーに提案
    user_confirm = ask_user("新しいスキルを作成しますか?")
    if user_confirm:
        save_skill(skill_template)
```

### 2. スキル改善

```python
# スキル使用時の失敗を追跡
if skill_failed():
    # 失敗理由を記録
    failure_log.append({
        "skill": skill_name,
        "reason": error_msg,
        "context": conversation
    })
    
    # 定期的に改善提案
    suggestions = analyze_failures()
    agent.update_skill(skill_name, suggestions)
```

### 3. ユーザーモデル

```python
# USER.md に永続化
---
name: Alice
role: Software Engineer
expertise: [Python, Go, Kubernetes]
communication_style: Direct, prefers code examples
timezone: UTC+9
---

# 会話から自動更新
if "I prefer JSON over YAML":
    user_profile.preferences.append("JSON format")
    
# 次のセッションで参照
{system_prompt includes} Alice's profile
```

---

## 🚀 スケーラビリティ

### ターミナルバックエンド

| バックエンド | ユースケース | コスト |
|-------------|-----------|------|
| **Local** | 開発・個人用 | 無料（ローカル CPU） |
| **Docker** | 隔離実行 | 中程度（コンテナ） |
| **SSH** | リモートサーバー | 従量課金 |
| **Modal** | サーバーレス | $0.03/GPU時間（アイドル時 $0） |
| **Daytona** | クラウド開発環境 | 従量課金 |
| **Singularity** | HPC クラスター | カスタム |

### 複数プロファイル & マルチテナント

```bash
hermes -p alice          # Alice のセッション
hermes -p bob            # Bob のセッション
hermes -p team_bot       # チームボット

# 同時実行可能
# 各プロファイルが独立した Gateway、メモリ、セッション管理
```

---

## 📊 メモリと圧縮の詳細

### メモリ層

```
┌─────────────────────────────┐
│ 1. ショートターム           │
│    (会話中の変数)           │
└──────────┬──────────────────┘
           │
┌──────────▼──────────────────┐
│ 2. セッションメモリ          │
│    (SQLite 現在セッション)  │
└──────────┬──────────────────┘
           │
┌──────────▼──────────────────┐
│ 3. 永続メモリ               │
│    (MEMORY.md, USER.md)     │
│    (ディスク、~1000 行)     │
└─────────────────────────────┘
```

### 圧縮アルゴリズム

```python
# 損失圧縮（トークン削減）
before_compression:
  Turn 1: User: "こんにちは、Python 学んでます"
  Turn 2: Assistant: "素晴らしい! Python の..."
  ... (50 ターン)
  Turn 50: User: "次のステップは?"
  [← ここまでが要約対象]
  Turn 51: Assistant: "コンテキスト制限により要約します..."

after_compression:
  [SUMMARY]
  ユーザーは Python を学んでおり、
  以下の項目をカバーしました:
  - 基本構文
  - リスト・辞書
  - 関数定義
  
  Turn 50: User: "次のステップは?"
  Turn 51: Assistant: "コンテキスト制限により要約します..."
```

---

## 🎛️ プラグインと拡張性

### プラグイン検出源

1. `~/.hermes/plugins/` (ユーザー)
2. `.hermes/plugins/` (プロジェクト)
3. pip エントリーポイント

### 特殊プラグインタイプ

```python
# メモリプロバイダー (単一選択)
plugins/memory/
├── default_memory.py
├── pinecone_memory.py      # ← 選択可能
└── weaviate_memory.py

# コンテキストエンジン (単一選択)
plugins/context_engine/
├── compressor_engine.py    # ← デフォルト
└── hybrid_engine.py        # ← 選択可能

# 設定
hermes plugins select memory pinecone
hermes plugins select context_engine hybrid
```

---

## 📈 実装パターン

### レジストリパターン（自動検出）

```python
# tools/registry.py
class ToolRegistry:
    _registry = {}
    
    @classmethod
    def register(cls, name=None):
        def decorator(func):
            cls._registry[name or func.__name__] = func
            return func
        return decorator

# tools/file_tools.py
@ToolRegistry.register()
def read_file(path):
    """ファイル読み込み"""
    pass

@ToolRegistry.register()
def write_file(path, content):
    """ファイル書き込み"""
    pass

# → 自動的に registry に登録（インポート時）
```

### コンテキストエンジン（プラグイン可能）

```python
# agent/context_engine.py
class ContextEngine(ABC):
    @abstractmethod
    def compress(self, messages, compression_ratio):
        """メッセージを圧縮"""
        pass

# plugins/context_engine/my_engine.py
class MyContextEngine(ContextEngine):
    def compress(self, messages, compression_ratio):
        # カスタム圧縮ロジック
        pass
```

---

## 🔍 NetYamlForge への参考

### 適用可能な設計原則

| 原則 | NetYamlForge での応用 |
|------|-------------------|
| **プロンプト安定性** | YAML 設定は会話中に変更しない。明示的な再読み込みのみ |
| **観察可能な実行** | スキャフォールディング、バッチジョブ実行を可視化 |
| **割り込み可能性** | 長実行スキャフォールディング、バッチ処理で キャンセル対応 |
| **プラットフォーム非依存** | ASP.NET Core は複数プロジェクト対応（テナント分離） |
| **疎結合 + プラグイン** | フック システム、カスタムバリデータで拡張可能 |
| **レジストリパターン** | ツール レジストリ、フック レジストリの参考に |
| **永続メモリ層** | セッション → YAML スキーマ履歴、ユーザープロフィル |
| **マルチテナント** | `ProjectScope` による テナント分離（参考） |

### 実装例

```csharp
// NetYamlForge での適用
// 1. YAML スキーマは変更しない（build 時のみ検証）
// 2. フック実行は完全に可視化（ログ、デバッグUI）
// 3. バッチジョブは割り込み可能（CancellationToken）
// 4. DynamicCrudRepository は複数 DB ダイアレクト対応
// 5. IEntityHook で プラグイン拡張（CommonHooks が参考）
```

---

## 📚 参考資料

| ドキュメント | 説明 | URL |
|-----------|------|-----|
| Architecture | システム概要 | docs/developer-guide/architecture.md |
| Agent Loop | エージェント実装詳細 | docs/developer-guide/agent-loop.md |
| Creating Skills | スキル開発ガイド | docs/developer-guide/creating-skills.md |
| Prompt Assembly | プロンプト構築 | docs/developer-guide/prompt-assembly.md |
| Tools Runtime | ツール実行機構 | docs/developer-guide/tools-runtime.md |
| Session Storage | セッション永続化 | docs/developer-guide/session-storage.md |
| Gateway Internals | ゲートウェイ実装 | docs/developer-guide/gateway-internals.md |
| Context Compression | コンテキスト圧縮 | docs/developer-guide/context-compression-and-caching.md |

---

## 🎯 まとめ

Hermes Agent の設計は以下の核心的な特徴を持つ：

1. **単一エージェントコア** - CLI/Gateway/Batch で共通化
2. **プロンプト安定性** - キャッシュ最適化＆予測可能性
3. **スキルベースの拡張** - コードレス MARKDOWN で追加可能
4. **自己改善ループ** - 経験からスキルを自動生成・改善
5. **マルチプラットフォーム** - 18 種類のメッセージング統合
6. **永続メモリ層** - セッション横断的なユーザーモデル構築
7. **高度な圧縮** - スマートなコンテキスト管理で長期会話対応
8. **疎結合・プラグイン** - レジストリパターンで拡張性確保

これらは、**大規模 AI エージェント システムの設計** における ベストプラクティスを代表している。

---

*生成日時*: 2026-04-16T02:06:06.439Z  
*リポジトリ参照*: https://github.com/nousresearch/hermes-agent  
*ライセンス*: MIT (Hermes Agent)
