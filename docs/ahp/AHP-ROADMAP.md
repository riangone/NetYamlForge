# AHP Roadmap — AI Handshake Protocol 実行ロードマップ

| Field       | Value                            |
|-------------|----------------------------------|
| **Status**  | Draft                            |
| **Version** | 0.1.0                            |
| **Date**    | 2026-07-09                       |
| **Horizon** | 12 ヶ月 (3 Phase)                |

---

## Executive Summary

AHP (AI Handshake Protocol) は「AI 時代の通信プロトコル」— AI エージェント同士が初めて出会い、安全に関係を構築するための標準を定義する。本ロードマップは、NetYamlForge 上の PoC から、オープンプロトコルとして外部開発者を巻き込むまでの道筋を示す。

---

## Phase 1: Foundation（Month 1-2）— 動くものを作る

### 目標
> **「名刺を渡した後に何も起きない」問題を、1 つの QR コードで解決する**

### Month 1: Core Infrastructure

| Week | Task                                    | Deliverable                         | Owner    |
|------|-----------------------------------------|-------------------------------------|----------|
| W1   | AHP-0001 RFC 最終化                      | プロトコル仕様書 v0.1                | Spec     |
| W1   | DB マイグレーション作成                    | `002_ahp_tables.sql`                | Dev      |
| W1   | AI ID エンティティ YAML                   | `ai_identities.yml`                 | Dev      |
| W2   | Handshake セッション YAML                 | `handshake_sessions.yml`            | Dev      |
| W2   | チャット履歴エンティティ YAML              | `chat_conversations.yml`            | Dev      |
| W2   | AI ID 発行 CRUD 画面                      | AI ID 管理ページ                     | Dev      |
| W3   | QR コード生成 Hook                        | `QrGeneratorHook.cs`                | Dev      |
| W3   | QR コード生成ページ                       | `QrGenerator.yaml`                  | Dev      |
| W4   | Business Profile 基本実装                 | Profile CRUD + JSON スキーマ         | Dev      |

### Month 2: Asymmetric Handshake

| Week | Task                                    | Deliverable                         | Owner    |
|------|-----------------------------------------|-------------------------------------|----------|
| W5   | 公開チャット UI（HTML + HTMX）            | `HandshakeChat.yaml` + フロント      | Dev      |
| W5   | AI チャット Hook                          | `AiChatHook.cs`                     | Dev      |
| W6   | Handshake 状態遷移ロジック                 | `HandshakeHook.cs`                  | Dev      |
| W6   | AI 会話要約生成                            | 会話完了時の自動要約                   | Dev      |
| W7   | ネクストアクション提案                      | AI 提案 → JSON 保存                  | Dev      |
| W7   | biz_activities 自動連携                    | Handshake → 商業活動レコード          | Dev      |
| W8   | E2E フロー結合テスト                       | QR → Chat → Summary → CRM           | QA       |
| W8   | デモ動画撮影                               | 2 分のデモビデオ                      | Product  |

### Phase 1 完了基準（Exit Criteria）

- [ ] QR コードをスキャンすると AI チャット画面が開く
- [ ] AI が自己紹介 + 目的確認 + 情報提供を行う
- [ ] 会話終了後に要約が自動生成される
- [ ] 名刺管理の商業活動に自動でレコードが作成される
- [ ] 1 つの完全なデモシナリオが動作する

---

## Phase 2: Protocol（Month 3-4）— プロトコルを確立する

### 目標
> **対称 Handshake を実装し、AHP を再利用可能なプロトコルとして確立する**

### Month 3: Symmetric Handshake + Permission

| Task                                          | Deliverable                                |
|-----------------------------------------------|-------------------------------------------|
| 対称 Handshake 実装（双方 AHP 対応）             | AI → AI 通信フロー                          |
| Permission モデル詳細実装                        | Public/Trusted/Private 3 層モデル            |
| Permission 同意 UI                              | 「このデータを共有しますか？」画面             |
| Permission 取り消し（Revocation）                | 即時取り消し + データ削除                      |
| Ed25519 署名の基本実装                           | メッセージ署名 + 検証                          |
| `/.well-known/ahp.json` エンドポイント           | Discovery の最初のステップ                     |

### Month 4: Ecosystem Integration

| Task                                          | Deliverable                                |
|-----------------------------------------------|-------------------------------------------|
| AHP-0002 Business Profile Schema RFC           | JSON-LD Schema 仕様書                       |
| AHP-0003 Permission Model RFC                  | Permission 仕様書                           |
| カレンダー連携（Google Calendar / Outlook）      | 会議提案 → カレンダー登録                     |
| メール通知                                      | Handshake 完了時のメール送信                  |
| Handshake ダッシュボード強化                     | 統計・フィルター・検索                         |
| API ドキュメント自動生成                          | OpenAPI Specification                       |

### Phase 2 完了基準

- [ ] 2 つの AHP インスタンス間で Handshake が成立する
- [ ] Permission の 3 層モデルが動作する
- [ ] Permission の取り消しが即時反映される
- [ ] `/.well-known/ahp.json` で AI ID が解決できる
- [ ] AHP-0002, AHP-0003 の RFC が公開されている

---

## Phase 3: Ecosystem（Month 5-8）— エコシステムを構築する

### 目標
> **外部開発者がAHPに接続できるSDK・ドキュメント・コミュニティを構築する**

### Month 5-6: Developer Experience

| Task                                          | Deliverable                                |
|-----------------------------------------------|-------------------------------------------|
| GitHub `ahp-spec` リポジトリ公開                | RFC + リファレンス実装                       |
| TypeScript SDK                                 | `npm install @ahp/sdk`                      |
| Python SDK                                     | `pip install ahp-sdk`                       |
| Handshake Playground                           | ブラウザで 2 AI ID の握手を体験できるデモ      |
| 開発者ドキュメントサイト                         | docs.ahp.dev (仮)                           |
| CI/CD パイプライン                              | テスト自動化 + デプロイ                       |

### Month 7-8: Partnerships & Growth

| Task                                          | Deliverable                                |
|-----------------------------------------------|-------------------------------------------|
| CRM 連携プラグイン（Salesforce / HubSpot）      | AHP → CRM レコード自動作成                   |
| AI プラットフォーム連携（ChatGPT Plugin 等）     | ChatGPT / Claude から AHP Handshake          |
| DNS TXT ベースの ID 検証                        | ドメイン所有者による AI ID 認証                |
| AHP Registry (Central) プロトタイプ              | AI ID の中央レジストリ                        |
| セキュリティ監査                                 | 第三者によるセキュリティレビュー                |
| 最初のパートナー企業 3 社との PoC                 | 実ビジネス環境での検証                        |

---

## Phase 4: Scale（Month 9-12）— スケールさせる

### 目標
> **AHP を業界標準として確立し、自律的にエコシステムが成長する状態を作る**

| Task                                          | Deliverable                                |
|-----------------------------------------------|-------------------------------------------|
| AHP-0004 〜 AHP-0007 RFC 策定                  | 完全なプロトコルスイート                      |
| マルチ AI エンジン対応（Agent Endpoint 切替）     | ChatGPT/Claude/Gemini/自社 AI 切替           |
| AI 自律交渉プロトコル（AHP-0007）                | AI 同士が Permission を交渉                   |
| エンタープライズ版                               | SSO / 監査ログ / コンプライアンス              |
| モバイルアプリ（iOS / Android）                  | ネイティブ QR スキャン + Handshake             |
| 国際カンファレンスでの発表                        | AI Handshake Protocol の認知拡大               |
| 標準化団体への提案                               | W3C / IETF への RFC 提出検討                   |

---

## KPI（Key Performance Indicators）

### Phase 1 (Month 1-2)

| Metric                     | Target    |
|----------------------------|-----------|
| 動作する Handshake フロー数  | 1         |
| エンドツーエンド完了率       | 100%      |
| デモシナリオ数               | 1         |

### Phase 2 (Month 3-4)

| Metric                     | Target    |
|----------------------------|-----------|
| 対称 Handshake 成功率       | > 95%     |
| 公開 RFC 数                 | 3         |
| API レスポンス時間           | < 500ms   |

### Phase 3 (Month 5-8)

| Metric                     | Target    |
|----------------------------|-----------|
| SDK ダウンロード数           | > 100     |
| GitHub Stars                | > 50      |
| 外部開発者数                 | > 10      |
| パートナー企業数              | > 3       |

### Phase 4 (Month 9-12)

| Metric                     | Target    |
|----------------------------|-----------|
| AHP 対応サービス数           | > 10      |
| 月間 Handshake 数            | > 1,000   |
| AI ID 登録数                 | > 500     |

---

## リスクと緩和策

| Risk                                | Impact  | Likelihood | Mitigation                                      |
|-------------------------------------|---------|------------|--------------------------------------------------|
| 大手プラットフォームが類似機能を発表  | High    | Medium     | オープンプロトコルとして先行し、標準化を推進         |
| 冷起動問題（ユーザーが集まらない）    | High    | High       | 非対称 Handshake で片方だけで価値を提供              |
| セキュリティ脆弱性                   | High    | Medium     | Phase 3 で第三者監査 + バグバウンティ               |
| NetYamlForge の制約                  | Medium  | Low        | カスタム Hook で柔軟に拡張可能                      |
| AI 生成品質のばらつき                 | Medium  | Medium     | プロンプトテンプレート + フォールバック応答            |
| 法規制（個人情報保護）               | Medium  | Medium     | GDPR/個人情報保護法準拠の設計を Phase 2 で実装       |

---

## 技術的負債の管理

| Phase | 許容する負債                              | 解消タイミング              |
|-------|------------------------------------------|----------------------------|
| 1     | ハードコードされた AI プロンプト            | Phase 2 で設定ファイル化     |
| 1     | 署名なしメッセージ                         | Phase 2 で Ed25519 実装     |
| 1     | SQLite のみ対応                           | Phase 3 で PostgreSQL 対応   |
| 2     | 自前の ID 体系                            | Phase 4 で DID 統合検討      |
| 2     | 単一サーバー構成                           | Phase 4 でスケールアウト     |

---

## 競合分析

| Competitor           | What They Do              | AHP Differentiator                         |
|----------------------|---------------------------|--------------------------------------------|
| Sansan / Eight       | デジタル名刺管理            | AI 対 AI 通信プロトコル（名刺の先にある）    |
| LinkedIn             | プロフェッショナルネットワーク | AI エージェント間の自動接続                  |
| Cal.com / Calendly   | スケジューリング             | Intent + Permission ベースの関係構築         |
| Google A2A           | AI 間タスク委任              | 「関係が存在しない」状態からの接続           |
| Anthropic MCP        | AI → ツール接続              | 人間同士の AI 代理接続                       |

**AHP のユニークポジション**: 上記のすべてが「既に関係がある」ことを前提とする。AHP は **関係が存在しない状態から関係を生成する唯一のプロトコル** である。

---

## 次のアクション（今すぐ）

1. ✅ AHP-0001 RFC 草案 — 完了
2. ✅ PoC アーキテクチャ設計 — 完了
3. ✅ ロードマップ — 完了（本ドキュメント）
4. ⬜ DB マイグレーション SQL の作成
5. ⬜ エンティティ YAML の作成（ai_identities, handshake_sessions, chat_conversations）
6. ⬜ project.yaml へのナビゲーション追加
7. ⬜ QR コード生成 Hook の実装
8. ⬜ 非対称 Handshake チャット UI の実装

---

*This roadmap is a living document and will be updated as priorities evolve.*
