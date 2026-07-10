# AHP PoC Architecture — 基于 NetYamlForge 的 Demo 実装設計

| Field       | Value                                            |
|-------------|--------------------------------------------------|
| **Status**  | Draft                                            |
| **Version** | 0.1.0                                            |
| **Date**    | 2026-07-09                                       |
| **Base**    | NetYamlForge (ASP.NET Core + YAML-Driven CRUD)   |

---

## 1. 設計方針

### 1.1 既存フレームワーク活用の原則

NetYamlForge は YAML 定義から CRUD インターフェース・ダッシュボード・Hook・ページを自動生成するフレームワークである。AHP PoC は **既存の `biz-card` プロジェクトを拡張する形** で実装し、フレームワークの能力を最大限に活用する。

### 1.2 PoC のスコープ

```
✅ In Scope (6 週間)
├── AI ID の発行・管理
├── Business Profile の作成・公開
├── QR コード生成・スキャン
├── 非対称 Handshake（片方ブラウザのみ）
├── AI チャット UI（Handshake 対話）
├── Handshake 状態管理
├── CRM レコード自動作成（biz_activities 連携）
└── 会話要約・次アクション提案

❌ Out of Scope (v0.1)
├── 分散型 ID (DID)
├── AI 対 AI 完全自律交渉
├── マルチ AI エンジン切替
├── 外部 CRM 連携（Salesforce 等）
└── Production レベルの暗号化
```

---

## 2. システムアーキテクチャ

```
┌─────────────────────────────────────────────────────────────────┐
│                    NetYamlForge Application                      │
│                                                                   │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────────────────┐ │
│  │  biz-card    │  │  ahp-core    │  │  ahp-handshake        │ │
│  │  (既存)      │  │  (新規)      │  │  (新規)               │ │
│  │              │  │              │  │                        │ │
│  │ ・名刺管理    │  │ ・AI ID 管理  │  │ ・Handshake State     │ │
│  │ ・会社管理    │  │ ・Profile 管理│  │ ・非対称 Handshake    │ │
│  │ ・ベクトル検索 │  │ ・QR 生成    │  │ ・AI Chat UI         │ │
│  │ ・商業活動    │  │ ・Permission  │  │ ・会話要約            │ │
│  └──────┬───────┘  └──────┬───────┘  └──────────┬─────────────┘ │
│         │                 │                      │               │
│         └─────────────────┼──────────────────────┘               │
│                           │                                       │
│                    ┌──────┴───────┐                               │
│                    │   SQLite DB  │                               │
│                    │  (Unified)   │                               │
│                    └──────────────┘                               │
└─────────────────────────────────────────────────────────────────┘
```

### 2.1 プロジェクト構成の選択

**方式 A（推奨）**: `biz-card` プロジェクトに AHP エンティティとページを追加

- 利点: 既存の名刺データ・商業活動データとシームレスに連携
- 利点: 名刺 → AI ID → Handshake という自然なフロー
- NetYamlForge のマルチテナント機能で独立 DB を維持

**方式 B**: 新規 `ahp-demo` プロジェクトとして独立作成

- 利点: 既存プロジェクトに影響しない
- 欠点: biz-card データとの連携に追加実装が必要

---

## 3. データモデル（YAML エンティティ定義）

### 3.1 新規エンティティ一覧

| Entity              | Table Name          | Description                      |
|---------------------|---------------------|----------------------------------|
| `ai_identities`    | `ai_identities`    | AI ID の登録・管理                 |
| `ai_profiles`      | `ai_profiles`      | Business Profile（JSON 構造）     |
| `handshake_sessions`| `handshake_sessions`| Handshake セッション管理          |
| `handshake_messages`| `handshake_messages`| Handshake メッセージ履歴          |
| `permissions`       | `ahp_permissions`  | Permission 設定                   |
| `chat_conversations`| `chat_conversations`| 非対称 Handshake のチャット履歴   |

### 3.2 ai_identities — AI ID 管理

```yaml
# entities/ai_identities.yml
name: ai_identities
displayName: "AI ID"
icon: "🤖"
description: "AI Handshake Protocol の ID 管理"

table:
  name: ai_identities
  columns:
    - name: id
      type: integer
      primaryKey: true
      autoIncrement: true
    - name: ai_id
      type: text
      required: true
      unique: true
      description: "ai://namespace/identifier 形式"
    - name: owner_type
      type: text
      required: true
      description: "individual | organization | service"
    - name: display_name
      type: text
      required: true
    - name: organization
      type: text
    - name: role
      type: text
    - name: endpoint_url
      type: text
      description: "AI エージェントのエンドポイント URL"
    - name: public_key
      type: text
      description: "Ed25519 公開鍵（Base64）"
    - name: qr_code_data
      type: text
      description: "QR コードにエンコードされるデータ"
    - name: is_active
      type: boolean
      default: true
    - name: verified
      type: boolean
      default: false
    - name: verification_method
      type: text
    - name: linked_business_card_id
      type: integer
      description: "biz-card の business_cards テーブルとの外部キー"
    - name: created_at
      type: datetime
      default: "CURRENT_TIMESTAMP"
    - name: updated_at
      type: datetime
      default: "CURRENT_TIMESTAMP"

list:
  columns: [ai_id, display_name, organization, role, is_active, verified]
  defaultSort: created_at DESC
  searchColumns: [ai_id, display_name, organization]

form:
  sections:
    - title: "基本情報"
      fields: [ai_id, display_name, owner_type, organization, role]
    - title: "技術設定"
      fields: [endpoint_url, public_key, qr_code_data]
    - title: "ステータス"
      fields: [is_active, verified, verification_method, linked_business_card_id]
```

### 3.3 handshake_sessions — Handshake セッション

```yaml
# entities/handshake_sessions.yml
name: handshake_sessions
displayName: "Handshake セッション"
icon: "🤝"
description: "AI Handshake のセッション管理"

table:
  name: handshake_sessions
  columns:
    - name: id
      type: integer
      primaryKey: true
      autoIncrement: true
    - name: session_id
      type: text
      required: true
      unique: true
      description: "UUID v7"
    - name: initiator_ai_id
      type: text
      required: true
    - name: responder_ai_id
      type: text
      description: "非対称 Handshake の場合は NULL"
    - name: responder_name
      type: text
      description: "非対称時のブラウザ側ユーザー名"
    - name: responder_email
      type: text
    - name: state
      type: text
      required: true
      default: "PENDING"
      description: "IDLE|PENDING|CONNECTED|UPGRADED|REJECTED|EXPIRED|REVOKED|COMPLETED"
    - name: handshake_type
      type: text
      required: true
      default: "asymmetric"
      description: "symmetric | asymmetric"
    - name: intent_type
      type: text
    - name: intent_topic
      type: text
    - name: intent_context_json
      type: text
      description: "Intent コンテキスト（JSON）"
    - name: offered_permissions_json
      type: text
      description: "提供する Permission（JSON）"
    - name: granted_permissions_json
      type: text
      description: "付与された Permission（JSON）"
    - name: conversation_summary
      type: text
      description: "AI 生成の会話要約"
    - name: next_actions_json
      type: text
      description: "提案されたネクストアクション（JSON）"
    - name: linked_biz_activity_id
      type: integer
      description: "生成された商業活動レコードの ID"
    - name: expires_at
      type: datetime
    - name: created_at
      type: datetime
      default: "CURRENT_TIMESTAMP"
    - name: updated_at
      type: datetime
      default: "CURRENT_TIMESTAMP"

list:
  columns: [session_id, initiator_ai_id, responder_name, state, intent_type, created_at]
  defaultSort: created_at DESC
  searchColumns: [session_id, initiator_ai_id, responder_name]

form:
  sections:
    - title: "セッション情報"
      fields: [session_id, state, handshake_type]
    - title: "当事者"
      fields: [initiator_ai_id, responder_ai_id, responder_name, responder_email]
    - title: "Intent"
      fields: [intent_type, intent_topic, intent_context_json]
    - title: "Permission"
      fields: [offered_permissions_json, granted_permissions_json]
    - title: "結果"
      fields: [conversation_summary, next_actions_json, linked_biz_activity_id]
```

### 3.4 chat_conversations — チャット履歴

```yaml
# entities/chat_conversations.yml
name: chat_conversations
displayName: "チャット履歴"
icon: "💬"
description: "非対称 Handshake のチャット会話ログ"

table:
  name: chat_conversations
  columns:
    - name: id
      type: integer
      primaryKey: true
      autoIncrement: true
    - name: session_id
      type: text
      required: true
      description: "handshake_sessions.session_id への参照"
    - name: role
      type: text
      required: true
      description: "ai | human | system"
    - name: content
      type: text
      required: true
    - name: metadata_json
      type: text
      description: "追加メタデータ（JSON）"
    - name: created_at
      type: datetime
      default: "CURRENT_TIMESTAMP"

list:
  columns: [session_id, role, content, created_at]
  defaultSort: created_at ASC
```

---

## 4. ページ設計（YAML ページ定義）

### 4.1 新規ページ一覧

| Page               | Path                          | Description                      |
|--------------------|-------------------------------|----------------------------------|
| `AiIdDashboard`    | `/biz-card/Page/AiIdDashboard`| AI ID 管理ダッシュボード           |
| `QrGenerator`      | `/biz-card/Page/QrGenerator`  | QR コード生成・表示               |
| `HandshakeChat`    | `/biz-card/Page/HandshakeChat`| 非対称 Handshake の AI チャット UI |
| `HandshakeHistory` | `/biz-card/Page/HandshakeHistory`| Handshake 履歴一覧             |
| `ProfileEditor`    | `/biz-card/Page/ProfileEditor`| Business Profile 編集            |

### 4.2 非対称 Handshake チャット画面の設計

```
┌─────────────────────────────────────────────┐
│  🤝 AI Handshake                            │
│  ─────────────────────────────────────────── │
│                                              │
│  ┌─────────────────────────────────────────┐ │
│  │ 🤖 山田太郎の AI アシスタント            │ │
│  │                                          │ │
│  │ はじめまして。ABC Corporation の         │ │
│  │ 山田太郎の AI アシスタントです。          │ │
│  │                                          │ │
│  │ 本日はどのようなご用件でしょうか？        │ │
│  └─────────────────────────────────────────┘ │
│                                              │
│  ┌─────────────────────────────────────────┐ │
│  │                         👤 ゲスト        │ │
│  │                                          │ │
│  │ 製造業の DX について相談したいです。      │ │
│  └─────────────────────────────────────────┘ │
│                                              │
│  ┌─────────────────────────────────────────┐ │
│  │ 🤖 AI                                   │ │
│  │                                          │ │
│  │ ありがとうございます。                    │ │
│  │ 山田は製造業 DX の専門家です。            │ │
│  │                                          │ │
│  │ 📄 会社概要をお送りします:               │ │
│  │ [ABC Corp 概要.pdf]                      │ │
│  │                                          │ │
│  │ 次のステップとして、以下を提案します:      │ │
│  │ 1. 📅 オンライン会議の設定                │ │
│  │ 2. 📋 詳細要件のヒアリング               │ │
│  │ 3. 🔗 連絡先の交換                       │ │
│  └─────────────────────────────────────────┘ │
│                                              │
│  ┌──────────────────────────────┐ ┌────────┐ │
│  │ メッセージを入力...          │ │  送信  │ │
│  └──────────────────────────────┘ └────────┘ │
│                                              │
│  ┌─────────────────────────────────────────┐ │
│  │ 💡 この会話は AI により要約され、         │ │
│  │    山田太郎に通知されます。               │ │
│  │    個人情報の取り扱いについて →           │ │
│  └─────────────────────────────────────────┘ │
└─────────────────────────────────────────────┘
```

### 4.3 QR コード生成画面の設計

```
┌─────────────────────────────────────────────┐
│  🔲 AI QR コード生成                        │
│  ─────────────────────────────────────────── │
│                                              │
│  ┌──────────────┐  ┌───────────────────────┐ │
│  │              │  │ AI ID:                │ │
│  │   [QR CODE]  │  │ ai://hiroshi          │ │
│  │              │  │                       │ │
│  │              │  │ 名前:                 │ │
│  │              │  │ 山田 太郎             │ │
│  │              │  │                       │ │
│  │              │  │ 会社:                 │ │
│  │              │  │ ABC Corporation       │ │
│  │              │  │                       │ │
│  │              │  │ 目的 (Intent):        │ │
│  │              │  │ [▼ business_meeting]  │ │
│  │              │  │                       │ │
│  │              │  │ トピック:             │ │
│  │              │  │ [製造業のDX推進      ]│ │
│  └──────────────┘  └───────────────────────┘ │
│                                              │
│  ┌─────────────────────────────────────────┐ │
│  │ [📋 コピー] [📥 ダウンロード] [📧 共有] │ │
│  └─────────────────────────────────────────┘ │
│                                              │
│  QR URL: https://host/biz-card/ahp/hs/xxxxx │
└─────────────────────────────────────────────┘
```

---

## 5. API 設計

### 5.1 AHP 専用 API エンドポイント

既存の `ApiEntityController` を活用しつつ、Handshake 固有のロジックは **Hook** と **カスタムページ Action** で実装する。

| Method | Path                                        | Description                          | Auth       |
|--------|---------------------------------------------|--------------------------------------|------------|
| GET    | `/biz-card/ahp/hs/{token}`                  | QR スキャン → チャット UI 表示        | Public     |
| POST   | `/biz-card/ahp/hs/{token}/chat`             | チャットメッセージ送信                 | Public     |
| GET    | `/biz-card/ahp/hs/{token}/status`           | セッション状態確認                     | Public     |
| POST   | `/biz-card/ahp/handshake`                   | Handshake 開始（AI ID 所有者用）       | Auth       |
| PUT    | `/biz-card/ahp/handshake/{session_id}/state`| Handshake 状態更新                     | Auth       |
| GET    | `/biz-card/ahp/profile/{ai_id}`             | 公開 Profile 取得                      | Public     |
| PUT    | `/biz-card/ahp/profile/{ai_id}`             | Profile 更新                           | Auth       |
| POST   | `/biz-card/ahp/qr/generate`                | QR コード生成                          | Auth       |
| GET    | `/.well-known/ahp.json`                     | AHP Discovery エンドポイント           | Public     |

### 5.2 実装方式

NetYamlForge のアーキテクチャに合わせて、以下の 3 層で実装：

```
┌────────────────────────────────────────────┐
│  Layer 1: Custom Pages (YAML)              │
│  - HandshakeChat.yaml (UI レイアウト)       │
│  - QrGenerator.yaml (QR 生成 UI)           │
│  - AiIdDashboard.yaml (管理画面)           │
├────────────────────────────────────────────┤
│  Layer 2: Hooks (C#)                       │
│  - HandshakeHook.cs (状態遷移ロジック)      │
│  - AiChatHook.cs (AI 対話処理)             │
│  - QrGeneratorHook.cs (QR 生成ロジック)     │
│  - ProfileHook.cs (Profile 管理)           │
├────────────────────────────────────────────┤
│  Layer 3: Entities (YAML)                  │
│  - ai_identities.yml                       │
│  - handshake_sessions.yml                  │
│  - chat_conversations.yml                  │
└────────────────────────────────────────────┘
```

---

## 6. 非対称 Handshake フロー（詳細）

### 6.1 Initiator 側（AHP 対応ユーザー）

```
1. AI ID を作成（ai://hiroshi）
2. Business Profile を設定
3. Intent を選択（例: business_meeting）
4. QR コード生成
   → URL: https://host/biz-card/ahp/hs/{token}
   → token は handshake_sessions.session_id にマッピング
5. QR コードを相手に見せる / 送信する
```

### 6.2 Responder 側（ブラウザのみ）

```
1. QR コードをスマホカメラでスキャン
2. ブラウザが開き、AI チャット UI が表示
3. AI が自己紹介 + Intent を確認
4. ユーザーが目的を入力
5. AI が適切な情報を提供（Public Permission の範囲内）
6. AI が次のアクションを提案
7. ユーザーが連絡先を共有（オプション）
8. セッション完了
```

### 6.3 バックエンド処理

```
1. Handshake セッション作成 (state: PENDING)
2. QR スキャン検出 → state: CONNECTED
3. チャットメッセージを chat_conversations に保存
4. AI がメッセージを処理（既存の AI 統合を利用）
5. 会話完了時:
   a. AI が conversation_summary を生成
   b. AI が next_actions_json を生成
   c. biz_activities にレコード自動作成
   d. business_cards に相手情報を自動登録（同意時）
   e. state: COMPLETED
6. Initiator に通知（ダッシュボード + メール）
```

---

## 7. 技術選定

| Component               | Technology                                 | Reason                                  |
|-------------------------|--------------------------------------------|-----------------------------------------|
| Backend Framework       | ASP.NET Core 10 (NetYamlForge)             | 既存フレームワーク                        |
| Database                | SQLite                                     | 既存 biz-card と統一                      |
| AI Integration          | 既存 AI Service (Gemini/Claude/Qwen)       | NetYamlForge 内蔵の AI サービス           |
| QR Code Generation      | QRCoder (.NET Library)                     | C# ネイティブ、依存少                     |
| Real-time Chat          | Server-Sent Events (SSE) or Polling        | WebSocket より実装が軽量                  |
| Frontend Chat UI        | HTMX + Vanilla JS                          | NetYamlForge のページシステムに適合        |
| Session Token           | UUID v7 (Time-ordered)                     | ソート可能 + 一意性                       |

---

## 8. ファイル構成（予定）

```
NetYamlForge/projects/biz-card/
├── entities/
│   ├── ai_identities.yml          # 新規
│   ├── handshake_sessions.yml     # 新規
│   ├── chat_conversations.yml     # 新規
│   ├── business_cards.yml         # 既存（AI ID リンク列追加）
│   ├── biz_activities.yml         # 既存（Handshake リンク列追加）
│   └── companies.yml              # 既存
├── pages/
│   ├── AiIdDashboard.yaml         # 新規
│   ├── QrGenerator.yaml           # 新規
│   ├── HandshakeChat.yaml         # 新規（公開ページ）
│   ├── HandshakeHistory.yaml      # 新規
│   ├── ProfileEditor.yaml         # 新規
│   ├── CardGallery.yaml           # 既存
│   ├── CardImport.yaml            # 既存
│   ├── BizActivities.yaml         # 既存
│   └── VectorSearch.yaml          # 既存
├── Hooks/
│   ├── HandshakeHook.cs           # 新規
│   ├── AiChatHook.cs              # 新規
│   ├── QrGeneratorHook.cs         # 新規
│   └── ProfileHook.cs             # 新規
├── database/
│   └── migrations/
│       └── 002_ahp_tables.sql     # 新規
├── config/
│   └── ahp_config.yaml            # 新規（AHP 設定）
├── dashboard.yml                  # 既存（AHP セクション追加）
└── project.yaml                   # 既存（ナビゲーション追加）
```

---

## 9. マイルストーン

| Week | Deliverable                                              |
|------|----------------------------------------------------------|
| 1    | DB マイグレーション + エンティティ YAML + AI ID 基本 CRUD   |
| 2    | QR コード生成 + 公開チャット UI（静的モック）               |
| 3    | AI チャット統合 + 非対称 Handshake フロー完成               |
| 4    | 会話要約 + ネクストアクション提案 + biz_activities 連携      |
| 5    | Profile Editor + Permission モデル基本実装                  |
| 6    | E2E テスト + デモ環境構築 + ドキュメント整備                 |

---

## 10. 設計上の決定事項（ADR）

### ADR-001: biz-card プロジェクト拡張 vs 新規プロジェクト

**決定**: biz-card プロジェクトを拡張する

**理由**:
- 名刺データ → AI ID → Handshake という自然なデータフロー
- 既存の商業活動管理との直接連携
- ユーザーが「名刺管理」から「AI Handshake」へ自然に進化する体験

### ADR-002: チャット通信方式

**決定**: Server-Sent Events (SSE) + POST

**理由**:
- 非対称 Handshake では Responder がブラウザのみ
- WebSocket は接続管理が複雑
- SSE は HTTP/2 と相性が良く、リトライ機能内蔵

### ADR-003: AI エンジン選択

**決定**: NetYamlForge 内蔵の AI サービスを使用

**理由**:
- 既に Gemini/Claude/Qwen/Ollama 等を統合済み
- プロジェクト設定で AI エンジンを切り替え可能
- 追加の AI 統合コードが不要

---

*This architecture document will be updated as implementation progresses.*
