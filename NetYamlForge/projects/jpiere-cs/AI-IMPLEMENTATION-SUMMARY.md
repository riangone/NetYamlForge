# JPiere AI 助手实现总结

> **バージョン**: 1.0  
> **作成日**: 2026-04-07  
> **ステータス**: 実装完了  
> **参考**: 自動車販売子プロジェクト (auto-dealer-demo) の AI 実装

---

## 実装概要

JPiere 契約サービスに、異なる業務役割に特化した AI アシスタントを実装しました。  
自動車販売子プロジェクトのアーキテクチャを参考し、JPiere の業務ドメインに合わせてカスタマイズしました。

---

## 実装ファイル一覧

### 1. 設計ドキュメント

| ファイル | パス | 行数 | 説明 |
|---------|------|------|------|
| AI-ASSISTANT-DESIGN.md | `projects/jpiere-cs/` | - | AI 助手詳細設計ドキュメント |
| AI-IMPLEMENTATION-SUMMARY.md | `projects/jpiere-cs/` | - | 本ファイル（実装总结） |

---

### 2. AI コアエンティティ（5ファイル）

| エンティティ | ファイルパス | 行数 | 説明 |
|-------------|-------------|------|------|
| ai_conversations | `entities/ai_conversations.yml` | ~200 | AI 会話セッション管理 |
| ai_messages | `entities/ai_messages.yml` | ~160 | AI メッセージ記録 |
| ai_knowledge | `entities/ai_knowledge.yml` | ~180 | ナレッジベース |
| ai_feedback | `entities/ai_feedback.yml` | ~170 | ユーザーフィードバック |
| ai_handovers | `entities/ai_handovers.yml` | ~220 | 人工引継ぎ管理 |

**主要フィールド**:
- `ai_conversations`: conversation_id, channel, status, user_role, sentiment_score, last_intent
- `ai_messages`: message_id, sender, content, intent, confidence_score, sentiment_score, tool_called
- `ai_handovers`: handover_id, reason, priority, target_department, status, assigned_to

---

### 3. AI 設定ファイル

| ファイル | パス | 行数 | 説明 |
|---------|------|------|------|
| ai-config.yaml | `projects/jpiere-cs/` | ~280 | AI 設定ガイド（プロバイダー、しきい値、役割別設定） |

**主要設定**:
- プロバイダー優先順位: claude, qwen, gemini, ollama
- エスカレーションしきい値: -0.5
- 低信頼度しきい値: 0.6
- 機能フラグ: EnableProactiveSuggestions, EnableSentimentAnalysis, EnableEscalation, EnableAutoTodoCreation

---

### 4. Skills 提示詞ファイル（6役割 + 共通）

| ファイル | パス | 行数 | 説明 |
|---------|------|------|------|
| _system-prompt-employee.md | `skills/jpiere/` | ~200 | 一般社員向けプロンプト |
| _system-prompt-contract-manager.md | `skills/jpiere/` | ~300 | 契約担当向けプロンプト |
| _system-prompt-accountant.md | `skills/jpiere/` | ~280 | 会計担当向けプロンプト |
| _system-prompt-purchaser.md | `skills/jpiere/` | ~280 | 購買担当向けプロンプト |
| _system-prompt-approver.md | `skills/jpiere/` | ~250 | 承認者向けプロンプト |
| _system-prompt-admin.md | `skills/jpiere/` | ~260 | 管理者向けプロンプト |
| _tools-definition.md | `skills/jpiere/` | ~120 | ツール定義（query_data, create_record, update_record, approve_record） |
| _entity-reference.md | `skills/jpiere/` | ~400 | エンティティ定義リファレンス |

**スキルファイル** (skills/jpiere/skills/):
- jpiere-contract/SKILL.md - 契約管理スキル
- jpiere-billing/SKILL.md - 請求管理スキル (TODO)
- jpiere-accounting/SKILL.md - 会計スキル (TODO)
- jpiere-purchase/SKILL.md - 購買スキル (TODO)
- jpiere-approval/SKILL.md - 承認スキル (TODO)
- jpiere-todo/SKILL.md - TODO管理スキル (TODO)

---

### 5. サービス層

| ファイル | パス | 行数 | 説明 |
|---------|------|------|------|
| JpiereChatService.cs | `Services/AI/` | ~480 | AI チャットサービス（セッション管理、クエリ実行、エスカレーション） |

**主要メソッド**:
- `StartSessionAsync` - AI 会話開始
- `SendMessageAsync` - ユーザーメッセージ処理
- `GenerateAiResponseAsync` - AI 応答生成
- `BuildSystemPrompt` - 役割別システムプロンプト構築
- `ExecuteQueryDataToolAsync` - DB クエリ実行
- `DetectEscalation` - エスカレーション判定
- `HandleEscalationAsync` - エスカレーション処理

**依存関係**:
- CLIServiceFactory - グローバル AI CLI サービス
- SkillLoader - 提示詞ローダー
- QueryExecutionService - クエリ実行サービス
- ChatHistoryService - チャット履歴サービス

---

### 6. フック処理

| ファイル | パス | 行数 | 説明 |
|---------|------|------|------|
| JpiereAIHooks.cs | `projects/jpiere-cs/Hooks/` | ~300 | AI 関連フック処理 |

**フック一覧**:
| フック名 | トリガー | 説明 |
|---------|---------|------|
| ValidateAiConversationHook | beforeCreate/Update | AI 会話データ検証 |
| SetConversationTimestampsHook | beforeCreate | 時間戳自動設定 |
| AutoEscalationHook | afterCreate (ai_messages) | 感情スコア←0.5で自動エスカレーション |
| AutoCreateTodoFromAiHook | afterCreate (ai_conversations) | AI 提案から自動 TODO 作成 |
| LinkAiToBusinessEntityHook | afterCreate/Update | AI 会話を業務エンティティに関連付け |
| UpdateSentimentTrendHook | afterUpdate (ai_messages) | 感情トレンド更新 |
| AutoAssignHandoverHook | afterCreate (ai_handovers) | 引継ぎ自動割り当て |
| UpdateResolutionMetricsHook | afterUpdate (ai_handovers) | 解決指標更新 |

---

### 7. ページ設定

| ページ | ファイルパス | 行数 | 説明 |
|--------|-------------|------|------|
| AI ダッシュボード | `pages/AIDashboard.yaml` | ~240 | KPI・感情トレンド・エスカレーション状況 |
| 会話詳細 | `pages/ChatDetail.yaml` | ~220 | メッセージ履歴・関連業務データ |

**AI ダッシュボード セクション**:
- 利用状況 KPI カード（アクティブセッション、エスカレーション、解決率）
- 役割別対話数（円グラフ）
- 感情スコアトレンド（折れ線グラフ）
- エスカレーション状況（棒グラフ）
- 保留中エスカレーション一覧
- 最近の AI 対話一覧
- エンティティ別クエリ数
- ナレッジベース使用 TOP 5

---

### 8. プロジェクト設定更新

| ファイル | 変更内容 |
|---------|---------|
| `projects/jpiere-cs/project.yaml` | AI 設定・役割定義・ナビゲーションメニュー追加 |

**追加項目**:
- `aiConfig`: AI アシスタント設定（プロバイダー、しきい値、機能フラグ）
- `roles`: 6つの役割定義（employee, contract_manager, accountant, purchaser, approver, admin）
- `navigation`: AI・分析メニューグループ

---

## 役割体系

### 役割一覧

| 役割 ID | 役割名 | 説明 | リダイレクト先 |
|---------|--------|------|---------------|
| employee | 一般社員 | 全般的な業務サポート | /Page/MyPage |
| contract_manager | 契約担当 | 契約・見積・請求の管理 | /Page/ContractDetail |
| accountant | 会計担当 | 仕訳・会計・資金管理 | /Page/AccountBalance |
| purchaser | 購買担当 | 購買フロー管理 | /Entity/PurchaseOrder |
| approver | 承認者 | 承認ワークフロー管理 | /Page/ApprovalInquiry |
| admin | 管理者 | システム全体管理 | /Page/Dashboard |

### 役割別アクセス許可

| エンティティ | employee | contract_manager | accountant | purchaser | approver | admin |
|-------------|----------|------------------|------------|-----------|----------|-------|
| contracts | 参照(自身) | 全部 | 参照 | ❌ | 参照 | 全部 |
| estimations | 参照(自身) | 全部 | 参照 | ❌ | 参照 | 全部 |
| bills | 参照(関連) | 全部 | 全部 | ❌ | 参照 | 全部 |
| journals | ❌ | ❌ | 全部 | ❌ | ❌ | 全部 |
| purchase_orders | ❌ | ❌ | 参照 | 全部 | 参照 | 全部 |
| approval_requests | ❌ | ❌ | ❌ | ❌ | 全部 | 全部 |
| todos | 参照/更新(自身) | 参照/作成(関連) | ❌ | ❌ | 参照/更新(関連) | 全部 |

---

## AI プロバイダー

### サポートプロバイダー

| プロバイダー | 種別 | 設定項目 |
|-------------|------|---------|
| Claude (Anthropic) | クラウド | ApiKey, Model, MaxTokens |
| Qwen Code (Alibaba) | クラウド | ApiKey, Model |
| Gemini (Google) | クラウド | ApiKey, Model |
| Ollama | ローカル | BaseUrl, Model, ContextSize |
| LM Studio | ローカル | BaseUrl, Model, ContextSize |

### 設定優先順位

```
環境変数 > appsettings.Production.json > appsettings.json > ai-config.yaml
```

---

## 主要機能

### 1. 役割別 AI 応答

- ユーザー役割に応じてシステムプロンプトを動的に構築
- アクセス権限外のデータは返却しない
- 役割別の推奨アクションを提示

### 2. 感情分析

- メッセージ内のキーワードから感情スコアを計算 (-1.0 〜 +1.0)
- 感情スコアがしきい値未満の場合、自動エスカレーション
- 会話全体の感情トレンドを記録・分析

### 3. エスカレーション

- **自動検出**: 感情スコア ←0.5 または緊急キーワード
- **優先度分類**: high (即時対応), medium (30分以内), low (営業日以内)
- **部門割り当て**: 役割に応じて適切な部門に自動割り当て
- **追跡管理**: 引継ぎから解決までの時間を記録

### 4. DB クエリ実行

- AI が query_data ツールを呼び出して安全に DB クエリを実行
- 役割権限に応じてアクセス可能エンティティを制限
- クエリ結果を Markdown 形式で整形して表示

### 5. 自動 TODO 作成

- AI 会話の意図に応じて自動的に関連 TODO を作成
- 例: 有効期限切れ契約 → 「契約更新確認」TODO
- 例: 未請求契約 → 「請求書作成」 TODO

---

## 自動車販売子プロジェクトとの差分

| 項目 | 自動車販売 | JPiere | 差分理由 |
|------|-----------|--------|---------|
| **角色数** | 7 (customer, operator, sales_rep, ...) | 6 (employee, contract_manager, ...) | JPiereは業務役割に特化 |
| **プロンプトファイル** | 2 (staff/customer) | 6 (役割別) | 役割ごとに詳細な権限・業務ルール定義が必要 |
| **业务实体** | vehicles, sales_leads, customers | contracts, bills, journals, purchase_orders | 業務ドメインが異なる |
| **分析焦点** | 成约率・顧客フォロー・在庫回転 | 契約状況・会計平衡・購買フロー・承認状況 | 分析視点が異なる |
| **エスカレーション** | 顧客→スタッフ | 低レベル担当者→上位担当者/管理者 | 階層エスカレーション |
| **データ操作** | 顧客情報更新など | 契約作成・仕訳起票など | 業務操作が異なる |

---

## テスト計画

### 単体テスト

| テスト対象 | テスト項目 | 期待結果 |
|-----------|-----------|---------|
| JpiereChatService.StartSessionAsync | 役割別セッション開始 | 役割に応じてwelcome messageが変更される |
| JpiereChatService.SendMessageAsync | メッセージ処理 | AI 応答が生成され、会話に保存される |
| JpiereChatService.BuildSystemPrompt | 役割別プロンプト構築 | 役割に応じて異なるプロンプトが構築される |
| JpiereChatService.IsEntityAccessible | 権限チェック | 権限外のエンティティはアクセス拒否 |
| JpiereAIHooks.ValidateAiConversationHook | データ検証 | 無効なデータはエラー |
| JpiereAIHooks.AutoEscalationHook | 自動エスカレーション | 感情スコア←0.5でエスカレーション |

### 統合テスト

| テスト対象 | シナリオ | 期待結果 |
|-----------|---------|---------|
| AI 会話フロー | 役割別ログイン → メッセージ送信 → 応答取得 | 役割に応じた応答が返却される |
| エスカレーション | 苦情メッセージ送信 → 感情スコア低下 → 引継ぎ作成 | ai_handovers レコードが作成される |
| DB クエリ | 権限外エンティティをクエリ | アクセス拒否エラー |
| 自動 TODO | 有効期限切れ契約に関する会話 | 関連 TODO が自動作成される |

---

## 今後の課題

### 短期課題（1-2週間）

- [ ] 残りのスキルファイル実装（billing, accounting, purchase, approval, todo）
- [ ] AIAnalytics.yaml ページ作成
- [ ] 単体テスト追加（カバレッジ 80%以上）
- [ ] 統合テスト実施

### 中期課題（1ヶ月）

- [ ] 感情分析精度向上（キーワードベース → ML モデル）
- [ ] ナレッジベース自動構築（会話からFAQ自動生成）
- [ ] バッチジョブ連携（AI 分析結果を定期レポート）
- [ ] パフォーマンス最適化（キャッシュ・インデックス）

### 長期課題（3ヶ月）

- [ ] 多言語対応（日本語 → 英語・中国語）
- [ ] 音声入力対応
- [ ] 予測分析（契約流失予測、資金繰り予測）
- [ ] チャットボットUI改善（ストリーミング表示・タイピング表示）

---

## 効果測定指標

| 指標 | 説明 | 目標値 | 測定方法 |
|-----|------|--------|---------|
| 采纳率 | 推奨アクションの採用率 | >60% | TODO 作成数 / 推奨数 |
| 解決率 | AI 単独解決率 | >70% | 完了セッション / 総セッション |
| エスカレーション率 | 人工引継ぎ比率 | <15% | エスカレーション数 / 総セッション |
| 响应时间 | AI 平均応答時間 | <3 秒 | 応答時間の平均 |
| 満足度 | ユーザー評価（1-5） | >4.0 | ai_feedback.rating 平均 |
| 感情改善率 | 会話終了時感情スコア改善 | >+0.3 | 終了時 - 開始時 |

---

## 関連ドキュメント

- [AI 助手詳細設計](AI-ASSISTANT-DESIGN.md)
- [AI 設定ガイド](ai-config.yaml)
- [プロジェクト設定](project.yaml)
- [自動車販売AI実装](../auto-dealer-demo/)
- [フレームワーク共通プロンプト](../../skills/_system-prompt.md)

---

*最終更新：2026年4月7日*
