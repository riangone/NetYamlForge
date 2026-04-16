-- AssistantHub プロジェクト 初期スキーマ + テストデータ
-- 起動時に ProjectSpecificInitializer から自動実行されます
-- すべての文は冪等（CREATE TABLE IF NOT EXISTS / INSERT OR IGNORE）

-- ── AI プロバイダーテーブル ────────────────────────────────
CREATE TABLE IF NOT EXISTS ai_providers (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    name            TEXT    NOT NULL,
    slug            TEXT    NOT NULL UNIQUE,
    description     TEXT,
    api_endpoint    TEXT,
    api_key         TEXT,
    model_list      TEXT,
    default_model   TEXT,
    is_enabled      INTEGER NOT NULL DEFAULT 1,
    sort_order      INTEGER NOT NULL DEFAULT 0,
    max_tokens      INTEGER NOT NULL DEFAULT 2048,
    temperature     REAL    NOT NULL DEFAULT 0.7,
    created_at      TEXT    NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT    NOT NULL DEFAULT (datetime('now'))
);

-- ── 会話テーブル ────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS conversations (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    title           TEXT    NOT NULL,
    description     TEXT,
    ai_provider_id  INTEGER,
    model_name      TEXT,
    system_prompt   TEXT,
    temperature     REAL    NOT NULL DEFAULT 0.7,
    max_tokens      INTEGER NOT NULL DEFAULT 2048,
    total_messages  INTEGER NOT NULL DEFAULT 0,
    total_tokens    INTEGER NOT NULL DEFAULT 0,
    is_archived     INTEGER NOT NULL DEFAULT 0,
    tags            TEXT,
    last_activity   TEXT,
    created_at      TEXT    NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT    NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (ai_provider_id) REFERENCES ai_providers(id)
);

-- ── メッセージテーブル ──────────────────────────────────────
CREATE TABLE IF NOT EXISTS messages (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    conversation_id INTEGER NOT NULL,
    role            TEXT    NOT NULL,
    content         TEXT    NOT NULL,
    ai_model        TEXT,
    tokens_used     INTEGER NOT NULL DEFAULT 0,
    processing_time_ms INTEGER,
    is_important    INTEGER NOT NULL DEFAULT 0,
    created_at      TEXT    NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (conversation_id) REFERENCES conversations(id)
);

-- ── ユーザー設定テーブル ──────────────────────────────────────
CREATE TABLE IF NOT EXISTS user_settings (
    id                      INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id                 TEXT    NOT NULL UNIQUE,
    theme                   TEXT    NOT NULL DEFAULT 'auto',
    language                TEXT    NOT NULL DEFAULT 'ja',
    default_ai_provider_id  INTEGER,
    items_per_page          INTEGER NOT NULL DEFAULT 20,
    auto_save               INTEGER NOT NULL DEFAULT 1,
    notifications_enabled   INTEGER NOT NULL DEFAULT 1,
    keyboard_shortcuts      INTEGER NOT NULL DEFAULT 1,
    font_size               TEXT    NOT NULL DEFAULT 'normal',
    editor_height           INTEGER NOT NULL DEFAULT 150,
    created_at              TEXT    NOT NULL DEFAULT (datetime('now')),
    updated_at              TEXT    NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (default_ai_provider_id) REFERENCES ai_providers(id)
);

-- ── インデックス ────────────────────────────────────────────
CREATE INDEX IF NOT EXISTS idx_conversations_is_archived ON conversations(is_archived);
CREATE INDEX IF NOT EXISTS idx_conversations_ai_provider_id ON conversations(ai_provider_id);
CREATE INDEX IF NOT EXISTS idx_conversations_created_at ON conversations(created_at);
CREATE INDEX IF NOT EXISTS idx_messages_conversation_id ON messages(conversation_id);
CREATE INDEX IF NOT EXISTS idx_messages_created_at ON messages(created_at);
CREATE INDEX IF NOT EXISTS idx_messages_is_important ON messages(is_important);
CREATE INDEX IF NOT EXISTS idx_user_settings_user_id ON user_settings(user_id);

-- ── AI プロバイダー テストデータ ───────────────────────────
INSERT OR IGNORE INTO ai_providers (id, name, slug, description, default_model, is_enabled, sort_order, max_tokens, temperature)
VALUES
(1, 'OpenAI', 'openai', 'GPT-4, GPT-3.5 Turbo など OpenAI の言語モデル', 'gpt-4', 1, 10, 4096, 0.7),
(2, 'Anthropic', 'anthropic', 'Claude 3 Opus / Sonnet / Haiku', 'claude-3-opus', 1, 20, 4096, 0.7),
(3, 'Google', 'google', 'Gemini Pro など Google の言語モデル', 'gemini-pro', 1, 30, 2048, 0.8),
(4, 'Mistral', 'mistral', 'Mistral Large など Mistral の言語モデル', 'mistral-large', 1, 40, 8000, 0.7),
(5, 'Local LLM', 'local', 'ローカルで実行する言語モデル (Ollama など)', 'llama2', 0, 50, 2048, 0.7);

-- ── 会話 テストデータ ────────────────────────────────────────
INSERT OR IGNORE INTO conversations (id, title, ai_provider_id, model_name, system_prompt, total_messages, total_tokens, is_archived, tags, last_activity)
VALUES
(1, '会話A: Python学習', 1, 'gpt-4', 'あなたはPython技術者です。', 5, 450, 0, 'Python,学習', datetime('now')),
(2, '会話B: 機械学習プロジェクト', 2, 'claude-3-opus', 'あなたは機械学習エキスパートです。', 8, 820, 0, 'ML,プロジェクト', datetime('now', '-1 day')),
(3, '会話C: Web開発', 3, 'gemini-pro', 'あなたはWebフロントエンド開発者です。', 3, 280, 0, 'Web,React', datetime('now', '-2 day')),
(4, '会話D: アーカイブ済み', 1, 'gpt-4', '通常のアシスタント', 2, 150, 1, 'アーカイブ', datetime('now', '-30 days')),
(5, '会話E: テスト会話', 2, 'claude-3-opus', 'テストデータ', 10, 1200, 0, 'テスト,デモ', datetime('now', '-12 hours'));

-- ── メッセージ テストデータ ────────────────────────────────
-- 会話A
INSERT OR IGNORE INTO messages (id, conversation_id, role, content, ai_model, tokens_used, is_important)
VALUES
(1, 1, 'user', 'Pythonで関数型プログラミングを学びたいのですが、どのようにアプローチすべきですか？', 'gpt-4', 45, 0),
(2, 1, 'assistant', 'Pythonで関数型プログラミングを学ぶには、以下のステップをお勧めします：\n1. map、filter、reduce の理解\n2. ラムダ関数の使用\n3. decorators の活用\n4. functools モジュールの習熟', 'gpt-4', 89, 1),
(3, 1, 'user', 'map と filter の違いについて、具体例を教えてください。', 'gpt-4', 35, 0),
(4, 1, 'assistant', 'map は各要素に関数を適用して新しいリストを作成します。\nfilter は条件を満たす要素のみをフィルタリングします。\n\n例:\nnumbers = [1, 2, 3, 4, 5]\nmapped = list(map(lambda x: x * 2, numbers))  # [2, 4, 6, 8, 10]\nfiltered = list(filter(lambda x: x > 2, numbers))  # [3, 4, 5]', 'gpt-4', 105, 0),
(5, 1, 'user', 'ありがとうございます。非常に参考になりました！', 'gpt-4', 25, 0);

-- 会話B
INSERT OR IGNORE INTO messages (id, conversation_id, role, content, ai_model, tokens_used, is_important)
VALUES
(6, 2, 'user', '機械学習プロジェクトで、モデルの過学習を防ぐ方法は？', 'claude-3-opus', 50, 0),
(7, 2, 'assistant', '過学習を防ぐための主な方法：\n- 正則化（L1, L2）\n- ドロップアウト\n- 早期停止（Early Stopping）\n- データの増強\n- クロスバリデーション', 'claude-3-opus', 110, 1),
(8, 2, 'user', 'ドロップアウトについて詳しく説明してください。', 'claude-3-opus', 40, 0),
(9, 2, 'assistant', 'ドロップアウトは、トレーニング中にニューロンをランダムに無視する技術です。これにより：\n1. ニューロン間の依存関係が減少\n2. ネットワークが堅牢になる\n3. 過学習が減少', 'claude-3-opus', 95, 0),
(10, 2, 'user', 'PyTorchでの実装方法は？', 'claude-3-opus', 35, 0),
(11, 2, 'assistant', 'PyTorchでのドロップアウト実装：\nimport torch.nn as nn\n\nclass MyModel(nn.Module):\n    def __init__(self):\n        super().__init__()\n        self.fc1 = nn.Linear(784, 128)\n        self.dropout = nn.Dropout(0.5)\n        self.fc2 = nn.Linear(128, 10)\n    \n    def forward(self, x):\n        x = self.fc1(x)\n        x = self.dropout(x)\n        x = self.fc2(x)\n        return x', 'claude-3-opus', 125, 0),
(12, 2, 'user', 'dropout の確率 0.5 はどのように決めるのですか？', 'claude-3-opus', 40, 0),
(13, 2, 'assistant', 'ドロップアウト確率の決定方法：\n- 標準的には 0.5 または 0.3\n- 隠れ層では 0.5、入出力層では 0.1\n- ハイパーパラメータチューニングで最適値を探索\n- クロスバリデーションで評価', 'claude-3-opus', 100, 0);

-- 会話C
INSERT OR IGNORE INTO messages (id, conversation_id, role, content, ai_model, tokens_used, is_important)
VALUES
(14, 3, 'user', 'React でパフォーマンスを最適化するコツは？', 'gemini-pro', 45, 0),
(15, 3, 'assistant', 'React のパフォーマンス最適化\n1. React.memo で不要なレンダリング防止\n2. useCallback で関数メモ化\n3. useMemo で計算結果のキャッシュ', 'gemini-pro', 85, 1),
(16, 3, 'user', 'ありがとうございます！', 'gemini-pro', 20, 0);

-- 会話E（テスト会話）
INSERT OR IGNORE INTO messages (id, conversation_id, role, content, ai_model, tokens_used, is_important)
VALUES
(17, 5, 'system', 'このテスト会話は複数のメッセージを含んでいます。', 'claude-3-opus', 30, 0),
(18, 5, 'user', 'これはテストメッセージ1です。', 'claude-3-opus', 20, 0),
(19, 5, 'assistant', 'テスト応答1です。', 'claude-3-opus', 15, 0),
(20, 5, 'user', 'テストメッセージ2です。', 'claude-3-opus', 22, 0),
(21, 5, 'assistant', 'テスト応答2です。', 'claude-3-opus', 18, 0),
(22, 5, 'user', 'テストメッセージ3です。', 'claude-3-opus', 21, 0),
(23, 5, 'assistant', 'テスト応答3です。', 'claude-3-opus', 17, 0),
(24, 5, 'user', 'テストメッセージ4です。', 'claude-3-opus', 23, 0),
(25, 5, 'assistant', 'テスト応答4です。', 'claude-3-opus', 19, 0),
(26, 5, 'user', 'テストメッセージ5です。', 'claude-3-opus', 20, 1);

-- ── ユーザー設定 テストデータ ───────────────────────────────
INSERT OR IGNORE INTO user_settings (id, user_id, theme, language, default_ai_provider_id, items_per_page, auto_save, notifications_enabled, keyboard_shortcuts, font_size, editor_height)
VALUES
(1, 'admin', 'dark', 'ja', 2, 20, 1, 1, 1, 'normal', 150),
(2, 'user1', 'light', 'en', 1, 25, 1, 1, 1, 'large', 200),
(3, 'user2', 'auto', 'ja', 3, 15, 0, 1, 0, 'normal', 120);
