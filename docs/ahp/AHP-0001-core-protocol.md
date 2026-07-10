# AHP-0001: AI Handshake Protocol — Core Protocol Specification

| Field       | Value                                      |
|-------------|--------------------------------------------|
| **RFC**     | AHP-0001                                   |
| **Title**   | Core Protocol — Handshake State Machine    |
| **Status**  | Draft                                      |
| **Version** | 0.1.0                                      |
| **Date**    | 2026-07-09                                 |
| **Author**  | AHP Working Group                          |

---

## 1. Abstract

AI Handshake Protocol (AHP) は、AI エージェント同士が**初めて出会う瞬間**に、安全かつ構造化された方法で相互認証・意図共有・権限合意を行うためのオープンプロトコルである。

既存の MCP (Model Context Protocol) が「AI → ツール接続」を、Google A2A が「AI → AI タスク委任」を解決するのに対し、AHP は**関係が存在しない状態から関係を生成する**という空白領域を定義する。

---

## 2. Design Principles

1. **Asymmetric First** — 片方だけが AHP 対応でも機能する（冷起動問題の解決）
2. **Human-in-the-Loop** — すべての Permission 変更には人間の明示的同意が必要
3. **Transport Agnostic** — HTTP/WebSocket/libp2p 等、特定のトランスポートに依存しない
4. **AI Vendor Neutral** — ChatGPT, Claude, Gemini, 自社 AI 等、任意の AI エンジンを接続可能
5. **Progressive Trust** — 信頼は段階的に構築され、即座に全権限を付与しない

---

## 3. Core Concepts: The IIP Triad

AHP の基盤は **IIP 三元組 (Identity, Intent, Permission)** である。

### 3.1 Identity（誰なのか）

```json
{
  "ai_id": "ai://hiroshi",
  "type": "individual",
  "display_name": "山田 太郎",
  "organization": "ABC Corporation",
  "role": "Sales Director",
  "verified": true,
  "verification_method": "domain_dns_txt",
  "public_key": "ed25519:base64_encoded_public_key",
  "endpoint": "https://ahp.example.com/agents/hiroshi",
  "created_at": "2026-07-01T00:00:00Z"
}
```

**AI ID スキーム**: `ai://{namespace}/{identifier}`

| レベル       | 例                             | 説明                     |
|-------------|--------------------------------|--------------------------|
| 個人        | `ai://hiroshi`                 | 個人の AI エンドポイント   |
| 組織        | `ai://abc-corp/sales-team`     | 組織のチーム AI            |
| サービス    | `ai://abc-corp/support-bot`    | 公開サービス AI            |

### 3.2 Intent（何をしたいのか）

```json
{
  "intent_type": "business_meeting",
  "category": "sales",
  "topic": "製造業の DX 推進に関する協業提案",
  "urgency": "normal",
  "expected_outcome": "follow_up_meeting",
  "context": {
    "event": "Tech Expo 2026",
    "location": "東京ビッグサイト",
    "date": "2026-07-15"
  }
}
```

**Intent Types（定義済み）**:

| Type              | Description          |
|-------------------|----------------------|
| `business_meeting`| 商談・打ち合わせ       |
| `recruitment`     | 採用・人材紹介         |
| `partnership`     | 協業・提携提案         |
| `investment`      | 投資・資金調達         |
| `information`     | 情報交換               |
| `support`         | サポート・問い合わせ    |
| `custom`          | カスタム（自由定義）    |

### 3.3 Permission（何を任せてよいのか）

```json
{
  "sharing_policy": {
    "public": ["company_overview", "product_catalog", "public_contact"],
    "trusted": ["pricing_range", "case_studies", "team_structure"],
    "private": ["exact_pricing", "financial_data", "internal_roadmap"]
  },
  "agent_capabilities": {
    "allowed": [
      "schedule_meeting",
      "share_public_documents",
      "create_crm_record",
      "send_follow_up_email"
    ],
    "requires_approval": [
      "share_trusted_documents",
      "commit_to_timeline",
      "disclose_pricing"
    ],
    "denied": [
      "sign_contract",
      "share_private_data",
      "make_payment"
    ]
  },
  "expires_at": "2026-08-09T00:00:00Z",
  "revocable": true
}
```

---

## 4. Handshake State Machine

### 4.1 状態遷移図

```
                          ┌─────────────┐
                          │   IDLE      │
                          └──────┬──────┘
                                 │ initiate()
                                 ▼
                          ┌─────────────┐
                   ┌──────│  PENDING    │──────┐
                   │      └──────┬──────┘      │
                   │ reject()    │ accept()     │ timeout()
                   ▼             ▼              ▼
            ┌──────────┐  ┌───────────┐  ┌──────────┐
            │ REJECTED │  │ CONNECTED │  │ EXPIRED  │
            └──────────┘  └─────┬─────┘  └──────────┘
                                │
                    ┌───────────┼───────────┐
                    │ upgrade() │           │ revoke()
                    ▼           │           ▼
             ┌───────────┐     │    ┌────────────┐
             │ UPGRADED  │     │    │  REVOKED   │
             └───────────┘     │    └────────────┘
                               │
                               │ complete()
                               ▼
                        ┌─────────────┐
                        │ COMPLETED   │
                        └─────────────┘
```

### 4.2 状態定義

| State         | Description                                    | Transitions                        |
|---------------|------------------------------------------------|------------------------------------|
| `IDLE`        | 初期状態。Handshake 未開始                       | → `PENDING`                        |
| `PENDING`     | Initiator が要求送信済み。Responder の応答待ち    | → `CONNECTED` / `REJECTED` / `EXPIRED` |
| `CONNECTED`   | 双方が Handshake を承認。通信チャネル確立済み     | → `UPGRADED` / `REVOKED` / `COMPLETED` |
| `UPGRADED`    | Permission レベルが引き上げられた状態             | → `REVOKED` / `COMPLETED`          |
| `REJECTED`    | Responder が Handshake を拒否                    | Terminal                           |
| `EXPIRED`     | タイムアウト（デフォルト: 72 時間）               | Terminal                           |
| `REVOKED`     | いずれかの当事者が Permission を取り消し           | Terminal                           |
| `COMPLETED`   | 双方合意の上で Handshake セッションを終了          | Terminal                           |

### 4.3 非対称 Handshake（片方のみ AHP 対応）

冷起動時の最重要フロー。Responder が AHP クライアントを持たない場合：

```
┌──────────┐    QR Code     ┌──────────────┐    HTTP GET    ┌──────────────┐
│ Initiator│ ──────────────→│ Responder    │ ─────────────→│ AHP Web      │
│ (AHP)    │                │ (Browser)    │               │ Endpoint     │
└──────────┘                └──────┬───────┘               └──────┬───────┘
                                   │                              │
                                   │     AI Chat UI (HTML)        │
                                   │◄─────────────────────────────│
                                   │                              │
                                   │  User inputs intent          │
                                   │─────────────────────────────→│
                                   │                              │
                                   │  AI processes & responds     │
                                   │◄─────────────────────────────│
                                   │                              │
                                   │  Handshake proposal          │
                                   │◄─────────────────────────────│
                                   │                              │
                                   │  Human approves / rejects    │
                                   │─────────────────────────────→│
                                   │                              │
                                   │  Connection established      │
                                   │◄─────────────────────────────│
```

---

## 5. Message Format

### 5.1 Envelope

すべての AHP メッセージは以下のエンベロープ構造に従う：

```json
{
  "ahp_version": "0.1.0",
  "message_id": "msg_uuid_v7",
  "timestamp": "2026-07-09T12:00:00Z",
  "type": "handshake.initiate",
  "from": "ai://hiroshi",
  "to": "ai://tanaka",
  "signature": "ed25519_signature_base64",
  "payload": { }
}
```

### 5.2 Message Types

| Type                        | Direction              | Description                        |
|-----------------------------|------------------------|------------------------------------|
| `handshake.initiate`        | Initiator → Responder  | Handshake 要求                      |
| `handshake.accept`          | Responder → Initiator  | Handshake 承認                      |
| `handshake.reject`          | Responder → Initiator  | Handshake 拒否                      |
| `handshake.upgrade`         | Either → Either        | Permission レベル変更要求            |
| `handshake.revoke`          | Either → Either        | Permission 取り消し                  |
| `handshake.complete`        | Either → Either        | セッション終了                       |
| `profile.request`           | Either → Either        | Profile 情報要求                     |
| `profile.response`          | Either → Either        | Profile 情報応答                     |
| `intent.declare`            | Either → Either        | Intent の明示的宣言                  |
| `permission.request`        | Either → Either        | 追加 Permission 要求                 |
| `permission.grant`          | Either → Either        | Permission 付与                      |
| `permission.deny`           | Either → Either        | Permission 拒否                      |
| `meeting.propose`           | Either → Either        | 会議提案                             |
| `meeting.summary`           | Either → Either        | 会議要約の共有                        |

### 5.3 Handshake Initiate Payload

```json
{
  "type": "handshake.initiate",
  "payload": {
    "initiator_profile": {
      "ai_id": "ai://hiroshi",
      "display_name": "山田 太郎",
      "organization": "ABC Corporation",
      "role": "Sales Director"
    },
    "intent": {
      "intent_type": "business_meeting",
      "topic": "製造業のDX推進について",
      "context": {
        "event": "Tech Expo 2026"
      }
    },
    "offered_permissions": {
      "public": ["company_overview", "product_catalog"]
    },
    "requested_permissions": {
      "public": ["company_overview", "contact_info"]
    },
    "handshake_options": {
      "timeout_hours": 72,
      "allow_agent_negotiation": true,
      "require_human_approval": true
    }
  }
}
```

### 5.4 Handshake Accept Payload

```json
{
  "type": "handshake.accept",
  "payload": {
    "responder_profile": {
      "ai_id": "ai://tanaka",
      "display_name": "田中 花子",
      "organization": "XYZ Industries",
      "role": "CTO"
    },
    "granted_permissions": {
      "public": ["company_overview", "contact_info"]
    },
    "accepted_permissions": {
      "public": ["company_overview", "product_catalog"]
    },
    "session": {
      "session_id": "sess_uuid_v7",
      "established_at": "2026-07-09T12:05:00Z",
      "expires_at": "2026-08-09T12:05:00Z"
    }
  }
}
```

---

## 6. Security Model

### 6.1 認証メカニズム

| Layer                | Method                        | Description                              |
|----------------------|-------------------------------|------------------------------------------|
| **AI ID 検証**       | DNS TXT Record                | `_ahp.example.com TXT "ai_id=ai://hiroshi"` |
| **メッセージ署名**    | Ed25519                       | すべてのメッセージに署名を付与              |
| **セッション暗号化**  | TLS 1.3 + Perfect Forward Secrecy | トランスポート層での暗号化             |
| **Capability Token** | HMAC-SHA256 signed JWT        | 特定操作の認可トークン                     |

### 6.2 Trust Levels

```
Level 0: Anonymous    — 未検証。QR スキャン直後の状態
Level 1: Identified   — AI ID が DNS/ドメインで検証済み
Level 2: Verified     — 組織の法人格が第三者機関で確認済み
Level 3: Trusted      — 過去の Handshake 履歴に基づく信頼スコア
```

### 6.3 Threat Model

| Threat                   | Mitigation                                              |
|--------------------------|---------------------------------------------------------|
| AI ID なりすまし          | DNS TXT + Ed25519 署名で検証                              |
| 中間者攻撃               | TLS 1.3 + メッセージレベル署名                             |
| Permission エスカレーション | Human-in-the-Loop 必須 + 監査ログ                        |
| データ過剰共有            | Sharing Policy の厳格な分類 (Public/Trusted/Private)       |
| セッションハイジャック     | セッション ID + タイムスタンプ + HMAC による検証              |
| Replay 攻撃              | Nonce + Timestamp window (5 分) による検出                  |
| 同意なき AI 自律行動      | `require_human_approval` フラグ + 操作ごとの承認フロー       |

### 6.4 Revocation Protocol

Permission の取り消しは即時かつ双方向で実行される：

```json
{
  "type": "handshake.revoke",
  "payload": {
    "session_id": "sess_uuid_v7",
    "reason": "business_relationship_ended",
    "revoke_scope": "all",
    "effective_at": "2026-07-09T15:00:00Z",
    "data_retention": {
      "delete_shared_data": true,
      "retention_period_days": 0
    }
  }
}
```

---

## 7. Discovery Protocol

### 7.1 AI ID 解決

AI ID からエンドポイントを解決する 3 つの方法：

1. **DNS-based Discovery**
   ```
   _ahp.hiroshi.example.com TXT "endpoint=https://ahp.example.com/agents/hiroshi"
   ```

2. **Well-Known URL**
   ```
   GET https://example.com/.well-known/ahp.json
   ```
   ```json
   {
     "agents": [
       {
         "ai_id": "ai://hiroshi",
         "endpoint": "https://ahp.example.com/agents/hiroshi",
         "public_key": "ed25519:..."
       }
     ]
   }
   ```

3. **AHP Registry (Central)**
   ```
   GET https://registry.ahp.dev/resolve/ai://hiroshi
   ```

### 7.2 解決優先度

```
1. DNS TXT (最優先 — 分散型)
2. Well-Known URL (ドメイン所有者が管理)
3. AHP Registry (フォールバック)
```

---

## 8. API Endpoints (Reference)

以下は AHP 対応サーバーが実装すべき最小限の API：

| Method | Path                    | Description                      |
|--------|-------------------------|----------------------------------|
| POST   | `/ahp/handshake`        | Handshake の開始・応答            |
| GET    | `/ahp/profile/{ai_id}` | 公開 Profile の取得               |
| POST   | `/ahp/permission`       | Permission の要求・付与・拒否      |
| POST   | `/ahp/meeting`          | 会議の提案・要約共有               |
| GET    | `/ahp/session/{id}`     | セッション状態の確認               |
| DELETE | `/ahp/session/{id}`     | セッションの取り消し (Revoke)      |
| GET    | `/ahp/health`           | ヘルスチェック                     |

---

## 9. Interoperability

### 9.1 既存プロトコルとの関係

```
┌─────────────────────────────────────────────────┐
│                 Application Layer                │
│  ┌───────────┐  ┌───────────┐  ┌───────────┐   │
│  │    AHP    │  │    MCP    │  │   A2A     │   │
│  │(Handshake)│  │(Tool Use) │  │(Task Del.)│   │
│  └─────┬─────┘  └─────┬─────┘  └─────┬─────┘   │
│        │              │              │           │
│        └──────────────┼──────────────┘           │
│                       │                          │
│              ┌────────┴────────┐                 │
│              │  Transport      │                 │
│              │  (HTTP/WS/etc.) │                 │
│              └─────────────────┘                 │
└─────────────────────────────────────────────────┘
```

- **AHP → MCP**: Handshake 完了後、MCP を使って相手のツールにアクセス
- **AHP → A2A**: Handshake 完了後、A2A を使ってタスクを委任
- **AHP は関係構築のみに責任を持つ**。通信確立後のタスク実行は他のプロトコルに委譲する。

### 9.2 CRM 連携

```json
{
  "crm_integration": {
    "on_handshake_complete": {
      "action": "create_contact",
      "target": "salesforce",
      "mapping": {
        "Name": "{responder.display_name}",
        "Company": "{responder.organization}",
        "Title": "{responder.role}",
        "LeadSource": "AHP Handshake",
        "Description": "{intent.topic}"
      }
    }
  }
}
```

---

## 10. Conformance

AHP 対応実装は以下のレベルに分類される：

| Level    | Requirements                                           |
|----------|--------------------------------------------------------|
| **Basic**    | Handshake Initiate/Accept/Reject + Profile 公開      |
| **Standard** | Basic + Permission Model + Session Management        |
| **Full**     | Standard + Discovery + Revocation + CRM Integration  |

---

## 11. Future Work (Out of Scope for v0.1)

- `AHP-0002`: Business Profile Schema (JSON-LD 定義)
- `AHP-0003`: Permission & Consent Model (詳細仕様)
- `AHP-0004`: Discovery Protocol (レジストリ仕様)
- `AHP-0005`: Security — 暗号化・署名・取り消しの詳細
- `AHP-0006`: Transport Binding (HTTP, WebSocket バインディング)
- `AHP-0007`: AI Agent Negotiation Protocol (AI 同士の自律交渉)

---

## 12. References

- [W3C Decentralized Identifiers (DID)](https://www.w3.org/TR/did-core/)
- [W3C Verifiable Credentials](https://www.w3.org/TR/vc-data-model/)
- [Google A2A Protocol](https://github.com/google/A2A)
- [Anthropic MCP (Model Context Protocol)](https://modelcontextprotocol.io/)
- [RFC 7519 - JSON Web Token](https://tools.ietf.org/html/rfc7519)
- [RFC 8032 - Edwards-Curve Digital Signature Algorithm](https://tools.ietf.org/html/rfc8032)

---

*This document is a living specification. Comments and contributions are welcome via the AHP GitHub repository.*
