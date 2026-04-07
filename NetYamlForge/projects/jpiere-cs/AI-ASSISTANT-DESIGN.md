# JPiere AI 助手详细设计文档

> **版本**: 1.0  
> **作成日**: 2026-04-07  
> **ステータス**: 設計中  
> **参考**: 自動車販売子プロジェクト (auto-dealer-demo) の AI 実装

---

## 目次

1. [設計概要](#1-設計概要)
2. [役割体系とAIアシスタント定義](#2-役割体系とAIアシスタント定義)
3. [アーキテクチャ設計](#3-アーキテクチャ設計)
4. [エンティティ設計](#4-エンティティ設計)
5. [プロンプト設計](#5-プロンプト設計)
6. [サービス層設計](#6-サービス層設計)
7. [フック設計](#7-フック設計)
8. [ページ設計](#8-ページ設計)
9. [実装計画](#9-実装計画)

---

## 1. 設計概要

### 1.1 目的

JPiere 契約サービスに、異なる業務役割に特化した AI アシスタントを導入し、以下の実現を目指す：

- **業務効率化**: 契約・見積・請求・会計・購買・承認フローのAI支援
- **インサイト提供**: データ分析・異常検知・推奨アクションの自動提示
- **権限分離**: 役割に応じてアクセス可能なデータと機能を制限
- **ユーザー体験向上**: 役割専用UI・クイックアクション・コンテキスト保持

### 1.2 自動車販売子プロジェクトとの対応関係

| 項目 | 自動車販売 (auto-dealer-demo) | JPiere 契約サービス |
|------|-------------------------------|---------------------|
| **业务領域** | 車両販売・顧客対応・サービス予約 | 契約管理・見積・請求・会計・購買・承認 |
| **主な角色** | customer, sales_rep, sales_manager, service_staff, ai_admin, executive | employee, contract_manager, accountant, purchaser, approver, admin |
| **AI实体** | ai_conversations, ai_messages, ai_knowledge, ai_feedback, ai_handovers | 同じ構造を再利用 |
| **Skills配置** | `skills/auto-dealer/` | `skills/jpiere/` |
| **ChatService** | `AutoDealerChatService.cs` | `JpiereChatService.cs` |
| **Hooks** | `AutoDealerHooks.cs` | `JpiereAIHooks.cs` |
| **页面** | AIDashboard.yaml, ChatDetail.yaml, AIAnalytics.yaml | 同じ構造をJPiere業務に合わせてカスタマイズ |

### 1.3 設計原則

1. **グローバルAI統合**: グローバルAI（CLIServiceFactory + SkillLoader）を共有
2. **役割分離**: 役割ごとにシステムプロンプト・アクセス権限・クイックアクションを分離
3. **データ駆動**: DBクエリ実行 → 分析レポート生成 → 推奨アクション提示 の一貫フロー
4. **拡張性**: 新しい役割・スキルを追加可能なモジュール構造
5. **セキュリティ**: 役割権限外のデータアクセス禁止、操作は読み取り専用

---

## 2. 役割体系とAIアシスタント定義

### 2.1 役割一覧

JPiere に以下の **6つの役割** を定義する：

| 役割ID | 役割名 | 説明 | ログイン後リダイレクト |
|--------|--------|------|------------------------|
| `employee` | 一般社員 | 全般的な業務サポート（契約・見積・TODOの照会） | `/Page/MyPage` |
| `contract_manager` | 契約担当 | 契約・見積・請求の作成・管理・分析 | `/Page/ContractDetail` |
| `accountant` | 会計担当 | 仕訳・会計・入金・支払・資金管理 | `/Page/AccountBalance` |
| `purchaser` | 購買担当 | 購買フロー（発注・受入・AP請求・支払） | `/Entity/PurchaseOrder` |
| `approver` | 承認者 | 承認ワークフローの確認・承認・却下 | `/Page/ApprovalInquiry` |
| `admin` | 管理者 | システム管理・マスタメンテナンス・権限管理 | `/Page/Dashboard` |

### 2.2 役割別AIアシスタント機能マトリックス

| 機能 | employee | contract_manager | accountant | purchaser | approver | admin |
|------|----------|------------------|------------|-----------|----------|-------|
| **契約照会** | ✅ 読み取り | ✅ 読書・分析 | ✅ 読み取り | ❌ | ✅ 読み取り | ✅ 全部 |
| **見積照会** | ✅ 読み取り | ✅ 読書・分析 | ✅ 読み取り | ❌ | ✅ 読み取り | ✅ 全部 |
| **請求照会** | ✅ 読み取り | ✅ 読書・分析 | ✅ 読書・分析 | ❌ | ✅ 読み取り | ✅ 全部 |
| **仕訳照会** | ❌ | ❌ | ✅ 読書・分析 | ❌ | ❌ | ✅ 全部 |
| **購買照会** | ❌ | ❌ | ✅ 読み取り | ✅ 読書・分析 | ✅ 読み取り | ✅ 全部 |
| **承認照会** | ❌ | ❌ | ❌ | ❌ | ✅ 読書・分析 | ✅ 全部 |
| **TODO管理** | ✅ 自分 | ✅ 関連 | ❌ | ❌ | ✅ 関連 | ✅ 全部 |
| **分析レポート** | 基本 | 詳細 | 詳細 | 詳細 | 概要 | 全種 |
| **推奨アクション** | 基本 | 詳細 | 詳細 | 詳細 | 概要 | 全種 |
| **データ操作** | ❌ | ✅ 一部 | ✅ 一部 | ✅ 一部 | ✅ 承認/却下 | ✅ 全部 |

### 2.3 役割別AIプロンプトファイル構成

```
skills/jpiere/
├── _system-prompt-employee.md          # 一般社員向けシステムプロンプト
├── _system-prompt-contract-manager.md  # 契約担当向けシステムプロンプト
├── _system-prompt-accountant.md        # 会計担当向けシステムプロンプト
├── _system-prompt-purchaser.md         # 購買担当向けシステムプロンプト
├── _system-prompt-approver.md          # 承認者向けシステムプロンプト
├── _tools-definition.md                # ツール定義（共通）
├── _entity-reference.md                # エンティティ定義（共通）
├── _response-templates.md              # 応答テンプレート（共通）
└── skills/
    ├── jpiere-contract/SKILL.md        # 契約管理スキル
    ├── jpiere-billing/SKILL.md         # 請求管理スキル
    ├── jpiere-accounting/SKILL.md      # 会計スキル
    ├── jpiere-purchase/SKILL.md        # 購買スキル
    ├── jpiere-approval/SKILL.md        # 承認スキル
    └── jpiere-todo/SKILL.md            # TODO管理スキル
```

---

## 3. アーキテクチャ設計

### 3.1 全体アーキテクチャ

```
┌─────────────────────────────────────────────────────────────┐
│                        Web ブラウザ                          │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐       │
│  │ MyPage       │  │ ContractPage │  │ AI Chat UI   │       │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘       │
└─────────┼────────────────┼─────────────────┼────────────────┘
          │                │                 │
          ▼                ▼                 ▼
┌─────────────────────────────────────────────────────────────┐
│                    ASP.NET Core MVC                          │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              AIController (AI/Chat)                   │   │
│  └──────────────────────┬───────────────────────────────┘   │
│                         │                                   │
│  ┌──────────────────────▼───────────────────────────────┐   │
│  │             JpiereChatService                         │   │
│  │  ┌─────────────┐  ┌──────────────┐  ┌────────────┐  │   │
│  │  │ Session管理  │  │ 提示詞構築    │  │ CLI実行    │  │   │
│  │  └─────────────┘  └──────────────┘  └────────────┘  │   │
│  │  ┌─────────────┐  ┌──────────────┐  ┌────────────┐  │   │
│  │  │ クエリ実行   │  │ 分析レポート  │  │ 感情分析   │  │   │
│  │  └─────────────┘  └──────────────┘  └────────────┘  │   │
│  └──────────────────────────────────────────────────────┘   │
│                         │                                   │
│  ┌──────────────────────▼───────────────────────────────┐   │
│  │           CLIServiceFactory + SkillLoader             │   │
│  │  ┌─────────────┐  ┌──────────────┐  ┌────────────┐  │   │
│  │  │ Claude      │  │ Qwen         │  │ Ollama     │  │   │
│  │  └─────────────┘  └──────────────┘  └────────────┘  │   │
│  └──────────────────────────────────────────────────────┘   │
│                         │                                   │
│  ┌──────────────────────▼───────────────────────────────┐   │
│  │         JpiereAIHooks (エスカレーション・自動処理)     │   │
│  └──────────────────────────────────────────────────────┘   │
└────────────────────────┬────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────┐
│                      SQLite データベース                     │
│  ai_conversations │ ai_messages │ ai_knowledge │ ...         │
│  contracts        │ bills       │ journals     │ ...         │
└─────────────────────────────────────────────────────────────┘
```

### 3.2 データフロー

#### 3.2.1 ユーザーメッセージ処理フロー

```
1. ユーザーがメッセージ送信
   ↓
2. AIController が JpiereChatService に転送
   ↓
3. JpiereChatService が以下を実行:
   a. ユーザーの役割を特定
   b. エスカレーション判定（苦情・緊急キーワード）
   c. 感情分析（センチメントスコア計算）
   d. 会話履歴取得（最近10件）
   ↓
4. システムプロンプト構築:
   a. グローバルAIプロンプト取得
   b. 役割専用プロンプト追加
   c. 権限情報追加
   d. 業務スキルプロンプト追加
   ↓
5. CLIサービス実行（AIモデル呼び出し）
   ↓
6. AI応答処理:
   a. query_dataツール呼び出し解析
   b. DBクエリ実行（安全なSQL生成）
   c. 分析レポート生成
   ↓
7. 応答保存・会話更新
   ↓
8. グローバルAI履歴に保存
   ↓
9. クライアントに応答返却
```

#### 3.2.2 エスカレーションフロー

```
1. 感情分析でスコア < -0.5 または苦情キーワード検出
   ↓
2. JpiereAIHooks が ai_handovers レコード作成
   ↓
3. 優先度設定（high/medium/normal）
   ↓
4. 対象部門設定（contract/accounting/purchase/management）
   ↓
5. AIが「担当者に引継ぎます」メッセージ表示
   ↓
6. 担当者のMyPageにエスカレーション通知表示
```

---

## 4. エンティティ設計

### 4.1 AIコアエンティティ（5つ）

> 自動車販売子プロジェクトと同じ構造を使用。詳細はYAML定義を参照。

| エンティティ | 役割 | 主要フィールド |
|-------------|------|---------------|
| `ai_conversations` | AI会話セッション | conversation_id, channel, status, sentiment_score, last_intent |
| `ai_messages` | メッセージ記録 | message_id, conversation_id, sender, intent, confidence_score |
| `ai_knowledge` | ナレッジベース | category, question, answer, tags, usage_count |
| `ai_feedback` | ユーザーフィードバック | rating, category, feedback_text, conversation_id |
| `ai_handovers` | 人工引継ぎ | reason, priority, target_department, status, assigned_to |

### 4.2 JPiere業務エンティティとの関連

```
ai_conversations (1:N) ai_messages
                (1:N) ai_handovers
                (1:N) ai_feedback
                
ai_conversations --(guest_user_id)--> users
                --(linked_contract_id)--> contracts
                --(linked_bill_id)--> bills
                --(linked_estimation_id)--> estimations
                
ai_handovers --(target_department)--> 部門マスタ
            --(assigned_to)--> users
            
ai_knowledge --(category)--> 知識分類（FAQ/操作手順/業務ルール/マスタ）
```

### 4.3 エンティティYAML定義

詳細は以下のファイルを作成する：

- `entities/ai_conversations.yml`
- `entities/ai_messages.yml`
- `entities/ai_knowledge.yml`
- `entities/ai_feedback.yml`
- `entities/ai_handovers.yml`

---

## 5. プロンプト設計

### 5.1 システムプロンプト構造

各役割のシステムプロンプトは以下の構造を持つ：

```markdown
---
title: "JPiere {Role} AI Assistant"
version: "1.0"
---

# 🤝 JPiere {役割名} AI アシスタント

## 役割と目的
あなたはJPiere契約サービスの{役割名} AI アシスタントです。
{役割固有の説明}

## 権限情報
- アクセス可能なエンティティ: {一覧}
- 操作権限: {読み取り/作成/更新/承認}
- コード変更・システム設定: 不可

## 応答形式
1. **優先度分類**: 🔴 緊急 / 🟡 注意 / 🟢 正常
2. **データ一覧**: 表形式で表示（最大10件）
3. **統計情報**: 合計・平均・トレンド
4. **推奨アクション**: 具体的な次の行動

## 業務ルール
{役割固有の業務ルール}

## 使用可能なツール
- query_data: DBクエリ実行
- create_record: レコード作成（権限がある場合）
- update_record: レコード更新（権限がある場合）
- approve_record: 承認（承認者のみ）

## 注意事項
- 権限外のデータアクセスは禁止
- 金額・数値は正確に表示
- 異常値・エラーは即座に報告
```

### 5.2 役割別プロンプト詳細

#### 5.2.1 一般社員 (employee)

**焦点**: 基本情報照会・TODO管理・業務ナビゲーション

```markdown
## アクセス可能なエンティティ
- contracts (読み取り・自分の担当のみ)
- estimations (読み取り・自分の作成のみ)
- bills (読み取り・関連のみ)
- todos (読み取り・更新・自分のみ)

## 業務ルール
- 自分の担当契約のみ閲覧可能
- TODOのステータス更新可能
- 新規見積・契約作成は不可（担当者に引継ぎ）

## 応答例
「あなたの担当契約は3件あります。期限が近いものを優先表示します。」
```

#### 5.2.2 契約担当 (contract_manager)

**焦点**: 契約・見積・請求の作成・管理・分析

```markdown
## アクセス可能なエンティティ
- contracts (全部・作成・更新・分析)
- estimations (全部・作成・更新・分析)
- bills (全部・作成・更新・分析)
- recognitions (読み取り)
- business_partners (読み取り)
- products (読み取り)
- todos (関連・作成)

## 業務ルール
- 契約ステータス遷移: DR→IN→CO→CL
- 見積から契約への転記可能
- 請求確定で自動仕訳起票
- 有効期限切れ契約は自動ステータス変更

## 分析機能
- 月別契約件数・金額トレンド
- 顧客別契約状況
- 有効期限切れアラート
- 未請求一覧

## 推奨アクション
- 「今月有効期限を迎える契約が5件あります。更新確認しますか？」
- 「未請求の契約が3件あります。請求書作成を推奨します。」
```

#### 5.2.3 会計担当 (accountant)

**焦点**: 仕訳・会計・入金・支払・資金管理

```markdown
## アクセス可能なエンティティ
- journals (全部・作成・更新・分析)
- accounts (読み取り)
- bills (全部・確定・分析)
- payments (全部・作成・分析)
- recognitions (全部・確定)
- contracts (読み取り)
- business_partners (読み取り)

## 業務ルール
- 仕訳の貸借一致必須
- 請求確定 → 自動仕訳: 売掛金(DR) / 売上(CR)
- 入金確定 → 自動仕訳: 銀行(DR) / 売掛金(CR)
- 支払確定 → 自動仕訳: 仕入(DR) / 買掛金(DR) / 銀行(CR)
- 月次締め処理可能

## 分析機能
- 月次損益試算
- 入金状況一覧
- 未収・未払一覧
- 資金繰り予測

## 推奨アクション
- 「今月の未収金が¥1,200,000あります。督促を推奨します。」
- 「貸借不一致の仕訳が2件あります。確認してください。」
```

#### 5.2.4 購買担当 (purchaser)

**焦点**: 購買フロー（発注・受入・AP請求・支払）

```markdown
## アクセス可能なエンティティ
- purchase_orders (全部・作成・更新・分析)
- purchase_receipts (全部・作成)
- ap_invoices (全部・作成・確定)
- payments (作成・読み取り)
- business_partners (読み取り・仕入先)
- products (読み取り)
- stock_moves (読み取り)

## 業務ルール
- 発注書作成 → 承認ワークフロー（金額≥10万）
- 受入処理 → 自動在庫入库 + 在庫移動記録
- AP請求確定 → 自動仕訳: 仕入(DR) / 買掛金(CR)
- 支払処理 → 自動仕訳: 買掛金(DR) / 銀行(CR)

## 分析機能
- 発注状況一覧
- 未受入一覧
- 仕入先別購買額
- 在庫回転率

## 推奨アクション
- 「先月の購買総額は¥5,000,000です。前月比+15%」
- 「未受入の発注が3件あります。確認を推奨します。」
```

#### 5.2.5 承認者 (approver)

**焦点**: 承認ワークフローの確認・承認・却下

```markdown
## アクセス可能なエンティティ
- approval_requests (全部・承認・却下)
- approval_steps (全部・更新)
- purchase_orders (読み取り・承認関連)
- contracts (読み取り・承認関連)
- todos (関連)

## 業務ルール
- 承認ステータス: PENDING→APPROVED/REJECTED
- 複数段階承認可能
- 却下時は理由必須
- 承認履歴は監査証として保存

## 分析機能
- 承認待ち一覧
- 承認・却下統計
- 平均承認時間
- 部門別承認状況

## 推奨アクション
- 「承認待ちが5件あります。早期対応を推奨します。」
- 「緊急度の高い発注が2件あります。」
```

#### 5.2.6 管理者 (admin)

**焦点**: システム管理・マスタメンテナンス・権限管理

```markdown
## アクセス可能なエンティティ
- 全エンティティ (全部・作成・更新・削除)
- users (全部・権限管理)
- roles (全部)
- system_config (全部)

## 業務ルール
- マスタメンテナンス可能
- ユーザー権限管理
- システム設定変更
- バッチジョブ管理
- データバックアップ・リストア

## 分析機能
- システム使用統計
- ユーザー別操作数
- エラー・異常検知
- パフォーマンス指標

## 推奨アクション
- 「システム使用率が80%に達しています。増設を検討してください。」
- 「先週のエラー率は0.5%です。正常範囲内です。」
```

---

## 6. サービス層設計

### 6.1 JpiereChatService クラス

**位置**: `NetYamlForge/Services/AI/JpiereChatService.cs`

**役割**: 
- AI会話セッション管理
- システムプロンプト構築（役割別）
- CLIサービス実行（AIモデル呼び出し）
- クエリデータツール実行（DBクエリ）
- 感情分析・エスカレーション処理

**主要メソッド**:

| メソッド | 説明 | 引数 | 戻り値 |
|---------|------|------|--------|
| `StartSessionAsync` | AI会話開始 | channel, guestSessionId, customerId | ChatSessionResult |
| `SendMessageAsync` | ユーザーメッセージ処理 | conversationId, message | ChatMessageResult |
| `GenerateAiResponseAsync` | AI応答生成 | message, role, history | (response, intent, data, nav) |
| `BuildSystemPrompt` | システムプロンプト構築 | role, dbContextMarkdown | systemPrompt |
| `ExecuteQueryDataToolAsync` | DBクエリ実行 | queryParams, userMessage | (result, data, intent, nav) |
| `DetectEscalation` | エスカレーション判定 | message | (intent, needsHandover, priority) |
| `EstimateSentiment` | 感情分析 | message | sentimentScore |

**依存関係**:
- `IDbConnection` (Dapper)
- `CLIServiceFactory`
- `SkillLoader`
- `ProjectScope`
- `QueryParserService`
- `QueryExecutionService`
- `QueryResultFormatter`
- `TaskQueueService`
- `ProgressTracker`
- `ChatHistoryService`

### 6.2 JpiereAIHooks クラス

**位置**: `NetYamlForge/projects/jpiere-cs/Hooks/JpiereAIHooks.cs`

**役割**:
- AI関連イベントのフック処理
- エスカレーション処理
- 自動TODO作成
- 感情スコア更新

**フック一覧**:

| フック名 | トリガー | 説明 |
|---------|---------|------|
| `ValidateAiConversationHook` | beforeCreate/Update | AI会話データの検証 |
| `SetConversationTimestampsHook` | beforeCreate | 会話時間戳自動設定 |
| `AutoEscalationHook` | afterCreate (ai_messages) | 感情スコア<-0.5で自動エスカレーション |
| `AutoCreateTodoFromAiHook` | afterCreate (ai_conversations) | AI提案から自動TODO作成 |
| `LinkAiToBusinessEntityHook` | afterCreate/Update | AI会話を業務エンティティに関連付け |
| `UpdateSentimentTrendHook` | afterUpdate (ai_messages) | 感情トレンド更新 |

---

## 7. ページ設計

### 7.1 AI関連ページ

| ページ | パス | 機能 |
|--------|------|------|
| AI ダッシュボード | `pages/AIDashboard.yaml` | KPIカード・会話トレンド・意図分析・感情分析 |
| 会話詳細 | `pages/ChatDetail.yaml` | 会話履歴・エスカレーション情報・関連業務データ |
| AI 分析 | `pages/AIAnalytics.yaml` | 詳細分析・低信頼度会話・FAQ効果・ユーザーフィードバック |

### 7.2 役割別ページリダイレクト

| 役割 | リダイレクト先 | 表示内容 |
|------|---------------|---------|
| employee | `/Page/MyPage` | 自分のTODO・関連契約・見積 |
| contract_manager | `/Page/ContractDetail` | 契約一覧・分析・作成 |
| accountant | `/Page/AccountBalance` | 仕訳・会計・入金状況 |
| purchaser | `/Entity/PurchaseOrder` | 発注書・購買フロー |
| approver | `/Page/ApprovalInquiry` | 承認待ち・承認履歴 |
| admin | `/Page/Dashboard` | 全体管理・マスタ・設定 |

---

## 8. 実装計画

### 8.1 フェーズ1: 基盤整備 (1-2日)

- [ ] AIエンティティYAML定義作成 (5ファイル)
- [ ] AI設定 ai-config.yaml 作成
- [ ] Skillsプロンプトファイル作成 (6役割分)
- [ ] project.yaml 更新（役割・ナビゲーション追加）

### 8.2 フェーズ2: サービス実装 (2-3日)

- [ ] JpiereChatService.cs 実装
- [ ] JpiereAIHooks.cs 実装
- [ ] AIController 修正（JPiere対応）

### 8.3 フェーズ3: ページ実装 (1-2日)

- [ ] AIDashboard.yaml 作成
- [ ] ChatDetail.yaml 作成
- [ ] AIAnalytics.yaml 作成
- [ ] 役割別ページリダイレクト設定

### 8.4 フェーズ4: テスト・確認 (1日)

- [ ] 各役割のAI応答テスト
- [ ] エスカレーションフローテスト
- [ ] 感情分析精度確認
- [ ] 権限分離テスト

---

## 9. 自動車販売子プロジェクトとの差分まとめ

| 項目 | 自動車販売 | JPiere | 備考 |
|------|-----------|--------|------|
| **角色数** | 7 | 6 | JPiereは業務役割に特化 |
| **プロンプトファイル** | 2 (staff/customer) | 6 (役割別) | 役割ごとに詳細定義 |
| **业务实体** | vehicles, sales_leads, customers | contracts, bills, journals, purchase_orders | 業務領域違い |
| **分析焦点** | 成約率・顧客フォロー・在庫回転 | 契約状況・会計平衡・購買フロー・承認状況 | 分析視点違い |
| **エスカレーション** | 顧客→スタッフ | 低レベル担当者→上位担当者/管理者 | 階層エスカレーション |
| **データ操作** | 限定（顧客情報更新など） | 限定（契約作成・仕訳起票など） | どちらも読み取り基本、操作は権限必要 |

---

*最終更新：2026年4月7日*
