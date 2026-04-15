# AI 窗口系统（CustomerAI）详细设计书

**版本**: 1.0  
**作成日**: 2026 年 3 月 28 日  
**対象プロジェクト**: NetYamlForge × 自動車ディーラー統合管理システム  
**ドキュメント種別**: 詳細設計書  

---

## 改訂履歴

| 版数 | 改訂日 | 改訂者 | 改訂内容 |
|------|--------|--------|----------|
| 1.0 | 2026-03-28 | NetYamlForge AI Team | 初版作成 |

---

## 目次

1. [システム概要](#1-システム概要)
2. [業務要件定義](#2-業務要件定義)
3. [システムアーキテクチャ](#3-システムアーキテクチャ)
4. [インフラ設計](#4-インフラ設計)
5. [データモデル設計](#5-データモデル設計)
6. [API 設計](#6-api 設計)
7. [コアモジュール設計](#7-コアモジュール設計)
8. [UI/UX 設計](#8-uiux 設計)
9. [セキュリティ設計](#9-セキュリティ設計)
10. [実装フェーズ](#10-実装フェーズ)
11. [テスト戦略](#11-テスト戦略)
12. [運用設計](#12-運用設計)
13. [付録](#13-付録)

---

## 1. システム概要

### 1.1 背景と目的

自動車ディーラーにおける顧客対応の効率化と顧客体験の向上を目的とし、AI 技術を活用した 24 時間 365 日対応可能な智能客服システムを構築する。

**背景:**
- 顧客からの問い合わせが増加傾向（電話・メール・来店）
- 営業時間外の問い合わせに対応できない
- 単純な問い合わせに人的リソースを割いている
- 顧客待ち時間の発生による満足度低下

**目的:**
1. **24/7 自動化**: AI による自動応答で営業時間外も対応
2. **コスト削減**: 単純問い合わせの自動化で人工コスト 40% 削減
3. **顧客体験向上**: 即時応答で顧客満足度 25% 向上
4. **データ蓄積**: 対話履歴を分析し業務改善に活用

### 1.2 システム範囲

```
┌─────────────────────────────────────────────────────────────┐
│                    AI 窗口システム（CustomerAI）              │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  【対象業務】                                                │
│  • 営業相談（営業時間、料金、サービス内容）                   │
│  • 予約受付（サービス予約、試乗予約、点検予約）               │
│  • 顧客照会（契約情報、支払い状況、サービス履歴）             │
│  • 車両照会（在庫検索、車両仕様、価格）                       │
│  • 苦情対応（クレーム受付、エスカレーション）                 │
│                                                             │
│  【対象チャネル】                                            │
│  • Web チャット（ウェブサイト埋め込み）                       │
│  • LINE（公式アカウント連携）                                │
│  • メール（自動仕分け・返信）                                │
│  • 音声通話（将来的に拡張）                                  │
│                                                             │
│  【統合システム】                                            │
│  • 顧客管理システム（customers.yml）                         │
│  • 車両在庫システム（vehicles.yml）                          │
│  • サービス予約システム（service_requests.yml）              │
│  • サポートチケット（support_tickets.yml）                   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 1.3 基本方針

| 方針 | 説明 |
|------|------|
| **YAML 駆動設計** | 設定・ルール・ナレッジを YAML で定義し、コード修正なしで変更可能に |
| **フックシステム活用** | 予約作成・エスカレーション時に業務ロジックを実行 |
| **多テナント対応** | NetYamlForge の ProjectScope を活用し複数ディーラーで利用可能に |
| **LLM ハイブリッド** | ルールベース（高速）と LLM（高精度）を組み合わせコスト最適化 |
| **段階的導入** | 8 週間 4 フェーズで段階的に機能追加 |

### 1.4 用語定義

| 用語 | 定義 |
|------|------|
| **AI 窗口** | AI による顧客対応窓口 |
| **Conversation** | 1 回の顧客対応セッション（複数のメッセージを含む） |
| **Intent** | 顧客の意図（例：価格照会、予約依頼） |
| **Entity** | 会話から抽出された情報（例：日付、車種） |
| **Handover** | AI から人間オペレーターへの引き継ぎ |
| **LLM** | Large Language Model（Qwen、Claude など） |

---

## 2. 業務要件定義

### 2.1 機能要件

#### 2.1.1 AI 対話機能

| ID | 機能名 | 説明 | 優先度 |
|----|--------|------|--------|
| F-01 | 自然言語理解 | 顧客メッセージから意図と情報を抽出 | 高 |
| F-02 | 文脈管理 | 複数ターンにわたる会話の文脈を維持 | 高 |
| F-03 | 自動応答生成 | 意図に応じた適切な応答を生成 | 高 |
| F-04 | マルチターン対話 | 追加質問による情報収集 | 高 |
| F-05 | 感情分析 | 顧客の感情（満足・不満）を検出 | 中 |
| F-06 | 多言語対応 | 日本語・英語・中国語に対応 | 低 |

#### 2.1.2 業務統合機能

| ID | 機能名 | 説明 | 優先度 |
|----|--------|------|--------|
| F-11 | 顧客認証 | 電話番号/メールで本人確認 | 高 |
| F-12 | 顧客情報照会 | 契約情報、支払い状況の照会 | 高 |
| F-13 | 車両在庫照会 | 在庫車両の検索・案内 | 高 |
| F-14 | サービス予約 | 予約枠の検索・作成・変更・キャンセル | 高 |
| F-15 | 試乗予約 | 試乗の予約受付 | 中 |
| F-16 | 納車日程案内 | 納車予定日の案内 | 中 |

#### 2.1.3 エスカレーション機能

| ID | 機能名 | 説明 | 優先度 |
|----|--------|------|--------|
| F-21 | 自動判定 | 置信度・感情・顧客ランクに基づき自動判定 | 高 |
| F-22 | ルーティング | 部門（販売・サービス・品質）への自動振り分け | 高 |
| F-23 | キュー管理 | オペレーターの負荷を考慮した配分 | 高 |
| F-24 | 引継ぎ情報 | 会話履歴をオペレーターに引き継ぎ | 高 |
| F-25 | 通知 | Slack/メールでのオペレーター通知 | 中 |

#### 2.1.4 ナレッジ管理機能

| ID | 機能名 | 説明 | 優先度 |
|----|--------|------|--------|
| F-31 | FAQ 管理 | FAQ の登録・編集・削除 | 高 |
| F-32 | 回答テンプレート | チャネル別・トーン別テンプレート | 高 |
| F-33 | 学習機能 | 未回答質問の蓄積と分析 | 中 |
| F-34 | A/B テスト | 複数回答のパフォーマンス比較 | 低 |

#### 2.1.5 分析・レポート機能

| ID | 機能名 | 説明 | 優先度 |
|----|--------|------|--------|
| F-41 | 対話ダッシュボード | リアルタイムの対話状況表示 | 高 |
| F-42 | 分析レポート | 回答率、満足度、転接率の分析 | 高 |
| F-43 | 対話ログ検索 | 会話履歴の検索・エクスポート | 中 |
| F-44 | 品質モニタリング | AI 回答の品質をサンプリングチェック | 中 |

### 2.2 非機能要件

#### 2.2.1 性能要件

| 項目 | 目標値 | 測定方法 |
|------|--------|----------|
| **応答時間** | 2 秒以内（P95） | API レイテンシー |
| **同時接続数** | 500 ユーザー | 同時セッション数 |
| **スループット** | 100 TPS | 1 秒間リクエスト数 |
| **可用性** | 99.5% | 月間稼働率 |

#### 2.2.2 セキュリティ要件

| 項目 | 要件 |
|------|------|
| **認証** | 顧客認証（電話番号/メール + 認証コード） |
| **認可** | 顧客は自身の情報のみ閲覧可能 |
| **暗号化** | 通信 TLS 1.3、保存データ AES-256 |
| **監査** | 全操作のログ記録（7 年間保存） |
| **プライバシー** | 個人情報マスキング、GDPR/個人情報保護法準拠 |

#### 2.2.3 運用要件

| 項目 | 要件 |
|------|------|
| **監視** | 24 時間 365 日自動監視 |
| **バックアップ** | 日次バックアップ（30 日間保持） |
| **障害復旧** | RTO 4 時間、RPO 1 時間 |
| **ログ収集** | 構造化ログ（ELK Stack） |
| **アラート** | Slack/メールで即時通知 |

---

## 3. システムアーキテクチャ

### 3.1 全体構成図

```
┌─────────────────────────────────────────────────────────────────┐
│                         クライアント層                           │
├──────────────┬──────────────┬──────────────┬────────────────────┤
│  Web チャット │  LINE 公式    │  メールゲート  │  客服ダッシュボード  │
│  (React)     │  (Messaging)  │  (POP3/IMAP)  │  (Vue 3)           │
└──────────────┼──────────────┼──────────────┼────────────────────┘
               │              │              │
          ┌────▼──────────────▼──────────────▼────┐
          │         API Gateway (Kong)            │
          │  - 認証/認可  - レート制限  - ロギング  │
          └────┬───────────────────────────────┘
               │
    ┌──────────▼──────────────────────────────┐
    │         AI 编排層 (.NET 10)              │
    ├─────────────────────────────────────────┤
    │  AIController (REST API)                │
    │  AIChatHub (SignalR WebSocket)          │
    ├─────────────────────────────────────────┤
    │  ConversationManager  │  IntentClassifier │
    │  ResponseGenerator    │  SentimentAnalyzer│
    │  HandoverManager      │  FeedbackService  │
    └───────────────────────────────────────────┘
               │
    ┌──────────┴──────────┬──────────┬──────────┐
    │                     │          │          │
┌───▼────┐   ┌────────────▼──┐  ┌────▼────┐  ┌▼──────────────┐
│  LLM   │   │  NetYamlForge │  │  Redis  │  │  RabbitMQ     │
│  API   │   │  Framework    │  │  Cache  │  │  Queue        │
│        │   │               │  │         │  │               │
└───┬────┘   └────┬─────────┘  └────┬────┘  └┬──────────────┘
    │             │                 │        │
    │    ┌────────▼─────────────────▼────────▼────┐
    │    │         データ永続層                    │
    │    │  ┌──────────────┐  ┌──────────────┐   │
    │    │  │  PostgreSQL  │  │   ELK Stack  │   │
    │    │  │  (本データ)   │  │  (ログ収集)  │   │
    │    │  └──────────────┘  └──────────────┘   │
    │    └───────────────────────────────────────┘
    │
    └─► 外部サービス
         ├─ Qwen/Claude API (LLM)
         ├─ LINE Messaging API
         ├─ Twilio (SMS)
         └─ Slack API (通知)
```

### 3.2 モジュール構成

```
NetYamlForge/
├── Controllers/
│   └── Api/
│       └── AIWindowController.cs          # REST API エンドポイント
├── Hubs/
│   └── AIChatHub.cs                       # SignalR WebSocket ハブ
├── Models/
│   └── AI/
│       ├── Conversation.cs                # 対話モデル
│       ├── Message.cs                     # メッセージモデル
│       ├── IntentResult.cs                # 意図判定結果
│       └── HandoverRequest.cs             # エスカレーション要求
├── Services/
│   └── AI/
│       ├── IConversationManager.cs        # 対話管理インターフェース
│       ├── ConversationManager.cs         # 対話管理実装
│       ├── IIntentClassifier.cs           # 意図分類インターフェース
│       ├── HybridIntentClassifier.cs      # ハイブリッド分類器
│       ├── IResponseGenerator.cs          # 応答生成インターフェース
│       ├── LlmResponseGenerator.cs        # LLM 応答生成実装
│       ├── ICustomerDataService.cs        # 顧客データサービス
│       ├── IAppointmentService.cs         # 予約サービス
│       ├── IHandoverManager.cs            # エスカレーション管理
│       └── ISentimentAnalyzer.cs          # 感情分析サービス
├── Hooks/
│   └── AI/
│       ├── OnAppointmentCreatedHook.cs    # 予約作成時フック
│       └── OnHandoverCreatedHook.cs       # エスカレーション時フック
└── projects/
    └── auto-dealer-demo/
        ├── entities/
        │   ├── ai-conversations.yml       # 対話セッション定義
        │   ├── ai-messages.yml            # メッセージ履歴定義
        │   └── ai-handovers.yml           # エスカレーション定義
        └── config/
            └── ai-window.yml              # AI 設定（LLM/ナレッジ/ルール）
```

### 3.3 データフロー

#### 3.3.1 通常対話フロー

```
顧客メッセージ
    ↓
[1] API Gateway 認証・レート制限
    ↓
[2] AIWindowController.ReceiveMessage()
    ↓
[3] ConversationManager.GetContext()  ← Redis キャッシュ
    ↓
[4] IntentClassifier.Classify()
    ├─ ルールマッチング（高速）
    └─ LLM 分析（高精度）
    ↓
[5] ResponseGenerator.Generate()
    ├─ ナレッジベース検索
    └─ LLM 応答生成
    ↓
[6] 応答を DB 保存（PostgreSQL）
    ↓
[7] SignalR でリアルタイム送信
    ↓
顧客に返信
```

#### 3.3.2 エスカレーションフロー

```
AI 応答生成
    ↓
[1] HandoverManager.Evaluate()
    ├─ 置信度 < 0.6？ → エスカレーション
    ├─ 顧客が不満？ → エスカレーション
    ├─ VIP 顧客？ → 優先エスカレーション
    └─ 複雑な質問？ → エスカレーション
    ↓
[2] HandoverManager.CreateHandover()
    ├─ 部門割り当て（販売/サービス/品質）
    ├─ オペレーター選択（負荷考慮）
    └─ キューに追加（RabbitMQ）
    ↓
[3] Slack 通知
    ↓
[4] オペレーターがダッシュボードで対応
    ↓
[5] 解決後、HandoverManager.Resolve()
    ↓
[6] 会話ログに解決メモ保存
```

---

## 4. インフラ設計

### 4.1 構成要素

| 要素 | 技術 | 規模 | 説明 |
|------|------|------|------|
| **Web サーバー** | .NET 10 (Kestrel) | 2 インスタンス | API・WebSocket 処理 |
| **API Gateway** | Kong | 1 インスタンス | 認証・レート制限 |
| **DB** | PostgreSQL 15 | 1 クラスター | データ永続化 |
| **キャッシュ** | Redis 7 | 1 クラスター | セッション・対話キャッシュ |
| **メッセージキュー** | RabbitMQ 3.11 | 1 クラスター | 非同期処理 |
| **LLM** | Qwen API | - | 外部 API |
| **ログ収集** | ELK Stack | 1 クラスター | ログ分析 |
| **監視** | Prometheus + Grafana | 1 インスタンス | メトリクス収集 |

### 4.2 コンテナ構成（Docker Compose）

```yaml
version: '3.8'

services:
  # AI 窗口アプリケーション
  ai-window:
    build:
      context: .
      dockerfile: Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Database__Provider=postgresql
      - ConnectionStrings__DefaultConnection=Host=postgres;Database=ai_window;Username=ai_user;Password=${DB_PASSWORD}
      - Redis__Configuration=redis:6379
      - RabbitMQ__HostName=rabbitmq
      - Llm__Provider=qwen
      - Llm__ApiKey=${LLM_API_KEY}
    ports:
      - "5000:8080"
    depends_on:
      - postgres
      - redis
      - rabbitmq
    restart: unless-stopped

  # PostgreSQL
  postgres:
    image: postgres:15-alpine
    environment:
      - POSTGRES_DB=ai_window
      - POSTGRES_USER=ai_user
      - POSTGRES_PASSWORD=${DB_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./init-db.sql:/docker-entrypoint-initdb.d/init.sql
    ports:
      - "5432:5432"
    restart: unless-stopped

  # Redis
  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data
    restart: unless-stopped

  # RabbitMQ
  rabbitmq:
    image: rabbitmq:3.11-management-alpine
    environment:
      - RABBITMQ_DEFAULT_USER=ai_user
      - RABBITMQ_DEFAULT_PASS=${RABBITMQ_PASSWORD}
    ports:
      - "5672:5672"   # AMQP
      - "15672:15672" # Management UI
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    restart: unless-stopped

  # Prometheus
  prometheus:
    image: prom/prometheus:v2.45.0
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
      - prometheus_data:/prometheus
    ports:
      - "9090:9090"
    restart: unless-stopped

  # Grafana
  grafana:
    image: grafana/grafana:10.0.0
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=${GRAFANA_PASSWORD}
    volumes:
      - grafana_data:/var/lib/grafana
      - ./grafana/dashboards:/etc/grafana/provisioning/dashboards
    ports:
      - "3000:3000"
    depends_on:
      - prometheus
    restart: unless-stopped

  # ELK Stack - Elasticsearch
  elasticsearch:
    image: docker.elastic.co/elasticsearch/elasticsearch:8.8.0
    environment:
      - discovery.type=single-node
      - xpack.security.enabled=false
    volumes:
      - elasticsearch_data:/usr/share/elasticsearch/data
    ports:
      - "9200:9200"
    restart: unless-stopped

  # ELK Stack - Logstash
  logstash:
    image: docker.elastic.co/logstash/logstash:8.8.0
    volumes:
      - ./logstash/pipeline:/usr/share/logstash/pipeline
    ports:
      - "5000:5000"
    depends_on:
      - elasticsearch
    restart: unless-stopped

  # ELK Stack - Kibana
  kibana:
    image: docker.elastic.co/kibana/kibana:8.8.0
    environment:
      - ELASTICSEARCH_HOSTS=http://elasticsearch:9200
    ports:
      - "5601:5601"
    depends_on:
      - elasticsearch
    restart: unless-stopped

volumes:
  postgres_data:
  redis_data:
  rabbitmq_data:
  prometheus_data:
  grafana_data:
  elasticsearch_data:
```

### 4.3 Kubernetes 構成

#### 4.3.1 デプロイメント

```yaml
# k8s/ai-window-deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: ai-window
  labels:
    app: ai-window
spec:
  replicas: 2
  selector:
    matchLabels:
      app: ai-window
  template:
    metadata:
      labels:
        app: ai-window
    spec:
      containers:
      - name: ai-window
        image: your-registry/ai-window:latest
        ports:
        - containerPort: 8080
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: Database__Provider
          value: "postgresql"
        - name: ConnectionStrings__DefaultConnection
          valueFrom:
            secretKeyRef:
              name: ai-window-secrets
              key: database-connection
        - name: Llm__ApiKey
          valueFrom:
            secretKeyRef:
              name: ai-window-secrets
              key: llm-api-key
        resources:
          requests:
            memory: "512Mi"
            cpu: "250m"
          limits:
            memory: "1Gi"
            cpu: "500m"
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 5
```

#### 4.3.2 自動スケーリング（HPA）

```yaml
# k8s/ai-window-hpa.yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: ai-window-hpa
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: ai-window
  minReplicas: 2
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
  behavior:
    scaleUp:
      stabilizationWindowSeconds: 60
      policies:
      - type: Percent
        value: 100
        periodSeconds: 60
    scaleDown:
      stabilizationWindowSeconds: 300
      policies:
      - type: Percent
        value: 10
        periodSeconds: 60
```

#### 4.3.3 サービス

```yaml
# k8s/ai-window-service.yaml
apiVersion: v1
kind: Service
metadata:
  name: ai-window-service
spec:
  selector:
    app: ai-window
  ports:
  - name: http
    port: 80
    targetPort: 8080
  - name: websocket
    port: 443
    targetPort: 8080
  type: LoadBalancer
```

---

## 5. データモデル設計

### 5.1 ER 図

```
┌─────────────────────┐       ┌──────────────────────┐
│   customers         │       │      users           │
│  (既存顧客)          │       │   (オペレーター)      │
└────────┬────────────┘       └──────────┬───────────┘
         │                               │
         │ 1:N                           │ 1:N
         ▼                               ▼
┌─────────────────────────────────────────────────┐
│              ai_conversations                    │
│            (AI 対話セッション)                   │
│─────────────────────────────────────────────────│
│ PK: conversation_id                             │
│ FK: customer_id → customers                     │
│ FK: assigned_to_user_id → users                 │
│    channel, status, started_at, ended_at        │
└─────────────────────┬───────────────────────────┘
                      │ 1:N
                      ▼
         ┌────────────────────────┐
         │    ai_messages         │
         │      (メッセージ)      │
         │────────────────────────│
         │ PK: message_id         │
         │ FK: conversation_id    │
         │ sender, content, type  │
         │ intent, entities_json  │
         │ timestamp              │
         └────────────────────────┘

┌─────────────────┐       ┌──────────────────────┐
│ ai_conversations│──────▶│   ai_handovers       │
│                 │       │   (エスカレーション)  │
└─────────────────┘       └──────────┬───────────┘
                                     │
                                     │ 1:1
                                     ▼
                            ┌──────────────────────┐
                            │  support_tickets     │
                            │  (既存サポートチケット)│
                            └──────────────────────┘

┌─────────────────┐       ┌──────────────────────┐
│ ai_conversations│──────▶│ ai_feedback          │
│                 │       │    (フィードバック)   │
└─────────────────┘       └──────────────────────┘
```

### 5.2 エンティティ定義

#### 5.2.1 `entities/ai-conversations.yml`

```yaml
entity: ai_conversations
displayName: AI 対話セッション
description: AI 顧客対応のセッション管理

columns:
  conversation_id:
    type: string
    length: 64
    required: true
    unique: true
    description: 対話 ID（ユニーク）
    format: "CONV-{yyyyMMdd-HHmmss}-{Guid:N8}"

  customer_id:
    type: string
    length: 50
    required: false
    foreign_key:
      entity: customers
      column: customer_id
    description: 顧客 ID（紐づく場合のみ）

  channel:
    type: string
    length: 20
    required: true
    enum: [web, voice, line, email, sms, tablet]
    description: チャネル種別

  status:
    type: string
    length: 30
    required: true
    default: active
    enum: [active, completed, escalated, abandoned]
    description: セッションステータス

  last_intent:
    type: string
    length: 100
    required: false
    description: 最終インテント

  last_confidence:
    type: decimal
    precision: 5
    scale: 4
    required: false
    description: 最終置信度（0.0000-1.0000）

  sentiment_score:
    type: decimal
    precision: 5
    scale: 4
    required: false
    description: 感情スコア（-1.0: 不満 〜 1.0: 満足）

  context_data:
    type: text
    required: false
    description: コンテキスト情報（JSON）
    example: |
      {
        "vehicle_id": "VH-001",
        "preferred_date": "2026-04-01",
        "service_type": "inspection"
      }

  assigned_to_user_id:
    type: string
    length: 50
    required: false
    foreign_key:
      entity: users
      column: user_id
    description: 割り当てオペレーター

  started_at:
    type: datetime
    required: true
    description: 開始日時

  ended_at:
    type: datetime
    required: false
    description: 終了日時

  created_at:
    type: datetime
    required: true
    default: GETUTCDATE()

  updated_at:
    type: datetime
    required: true
    default: GETUTCDATE()

indexes:
  idx_conversation_id:
    columns: [conversation_id]
    unique: true
  idx_customer_id:
    columns: [customer_id]
  idx_status:
    columns: [status]
  idx_channel:
    columns: [channel]
  idx_started_at:
    columns: [started_at]
  idx_assigned_to_user_id:
    columns: [assigned_to_user_id]

forms:
  conversation_id:
    type: string
    editable: false
    visible: true
  customer_id:
    type: string
    editable: true
    visible: true
  channel:
    type: select
    editable: false
    visible: true
  status:
    type: select
    editable: true
    visible: true
```

#### 5.2.2 `entities/ai-messages.yml`

```yaml
entity: ai_messages
displayName: AI メッセージ
description: AI と顧客のメッセージ履歴

columns:
  message_id:
    type: string
    length: 64
    required: true
    unique: true
    description: メッセージ ID

  conversation_id:
    type: string
    length: 64
    required: true
    foreign_key:
      entity: ai_conversations
      column: conversation_id
    description: 対話 ID

  sender:
    type: string
    length: 20
    required: true
    enum: [customer, ai, agent]
    description: 発話者

  message_type:
    type: string
    length: 20
    required: true
    default: text
    enum: [text, voice_transcript, image, quick_reply, button, carousel]
    description: メッセージ種別

  content:
    type: text
    required: true
    description: メッセージ内容
    encrypt: true  # 個人情報を含むため暗号化

  intent:
    type: string
    length: 100
    required: false
    description: 判定されたインテント

  entities_json:
    type: text
    required: false
    description: 検出されたエンティティ（JSON）
    example: |
      {
        "vehicle_model": "カローラクロス",
        "preferred_date": "2026-04-01",
        "price_range": "200-300 万円"
      }

  confidence_score:
    type: decimal
    precision: 5
    scale: 4
    required: false
    description: 置信度スコア（0.0000-1.0000）

  sentiment_score:
    type: decimal
    precision: 5
    scale: 4
    required: false
    description: 感情スコア

  metadata_json:
    type: text
    required: false
    description: メタデータ（JSON）

  timestamp:
    type: datetime
    required: true
    description: 発話日時

indexes:
  idx_message_id:
    columns: [message_id]
    unique: true
  idx_conversation_id:
    columns: [conversation_id]
  idx_timestamp:
    columns: [timestamp]
  idx_sender:
    columns: [sender]
```

#### 5.2.3 `entities/ai-handovers.yml`

```yaml
entity: ai_handovers
displayName: AI エスカレーション
description: AI から人間オペレーターへの引き継ぎ記録

columns:
  handover_id:
    type: string
    length: 64
    required: true
    unique: true
    description: エスカレーション ID

  conversation_id:
    type: string
    length: 64
    required: true
    foreign_key:
      entity: ai_conversations
      column: conversation_id
    description: 対話 ID

  ticket_id:
    type: string
    length: 64
    required: false
    foreign_key:
      entity: support_tickets
      column: ticket_id
    description: 関連サポートチケット

  reason:
    type: string
    length: 50
    required: true
    enum: 
      - ai_unable
      - customer_request
      - high_value_deal
      - complaint
      - complex_inquiry
      - negative_sentiment
      - vip_customer
    description: エスカレーション理由

  priority:
    type: string
    length: 20
    required: true
    default: medium
    enum: [low, medium, high, urgent]
    description: 優先度

  target_department:
    type: string
    length: 50
    required: false
    enum: [sales, service, quality, finance, general]
    description: 対象部門

  assigned_to_user_id:
    type: string
    length: 50
    required: false
    foreign_key:
      entity: users
      column: user_id
    description: 割り当てオペレーター

  status:
    type: string
    length: 30
    required: true
    default: pending
    enum: [pending, assigned, in_progress, resolved, closed]
    description: ステータス

  handover_notes:
    type: text
    required: false
    description: 引き継ぎメモ（AI 生成）

  resolution_notes:
    type: text
    required: false
    description: 解決メモ（オペレーター入力）

  escalated_at:
    type: datetime
    required: true
    description: エスカレーション日時

  assigned_at:
    type: datetime
    required: false
    description: 割り当て日時

  resolved_at:
    type: datetime
    required: false
    description: 解決日時

  created_at:
    type: datetime
    required: true
    default: GETUTCDATE()

indexes:
  idx_handover_id:
    columns: [handover_id]
    unique: true
  idx_conversation_id:
    columns: [conversation_id]
  idx_assigned_to_user_id:
    columns: [assigned_to_user_id]
  idx_status:
    columns: [status]
  idx_priority:
    columns: [priority]
```

#### 5.2.4 `entities/ai-feedback.yml`

```yaml
entity: ai_feedback
displayName: AI フィードバック
description: 顧客からのフィードバック記録

columns:
  feedback_id:
    type: string
    length: 64
    required: true
    unique: true

  conversation_id:
    type: string
    length: 64
    required: true
    foreign_key:
      entity: ai_conversations
      column: conversation_id

  message_id:
    type: string
    length: 64
    required: false
    foreign_key:
      entity: ai_messages
      column: message_id
    description: 対象メッセージ（特定の回答へのフィードバック）

  rating:
    type: integer
    required: true
    min: 1
    max: 5
    description: 評価（1-5）

  feedback_text:
    type: text
    required: false
    description: フィードバック内容

  category:
    type: string
    length: 50
    required: false
    enum: [helpful, not_helpful, incorrect, rude, slow, other]
    description: フィードバックカテゴリ

  created_at:
    type: datetime
    required: true
    default: GETUTCDATE()

indexes:
  idx_feedback_id:
    columns: [feedback_id]
    unique: true
  idx_conversation_id:
    columns: [conversation_id]
  idx_rating:
    columns: [rating]
```

### 5.3 初期化 SQL

```sql
-- init-db.sql
-- PostgreSQL 初期化スクリプト

-- 拡張機能
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- 顧客マスタ（既存システムと共有）
CREATE TABLE IF NOT EXISTS customers (
    customer_id VARCHAR(50) PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    phone VARCHAR(20),
    email VARCHAR(100),
    tier_level VARCHAR(20) DEFAULT 'regular',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- AI 対話セッション
CREATE TABLE IF NOT EXISTS ai_conversations (
    conversation_id VARCHAR(64) PRIMARY KEY,
    customer_id VARCHAR(50) REFERENCES customers(customer_id),
    channel VARCHAR(20) NOT NULL,
    status VARCHAR(30) NOT NULL DEFAULT 'active',
    last_intent VARCHAR(100),
    last_confidence DECIMAL(5,4),
    sentiment_score DECIMAL(5,4),
    context_data TEXT,
    assigned_to_user_id VARCHAR(50),
    started_at TIMESTAMP NOT NULL,
    ended_at TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_conversations_customer ON ai_conversations(customer_id);
CREATE INDEX idx_conversations_status ON ai_conversations(status);
CREATE INDEX idx_conversations_channel ON ai_conversations(channel);
CREATE INDEX idx_conversations_started ON ai_conversations(started_at);

-- AI メッセージ
CREATE TABLE IF NOT EXISTS ai_messages (
    message_id VARCHAR(64) PRIMARY KEY,
    conversation_id VARCHAR(64) NOT NULL REFERENCES ai_conversations(conversation_id),
    sender VARCHAR(20) NOT NULL,
    message_type VARCHAR(20) NOT NULL DEFAULT 'text',
    content TEXT NOT NULL,
    intent VARCHAR(100),
    entities_json TEXT,
    confidence_score DECIMAL(5,4),
    sentiment_score DECIMAL(5,4),
    metadata_json TEXT,
    timestamp TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_messages_conversation ON ai_messages(conversation_id);
CREATE INDEX idx_messages_timestamp ON ai_messages(timestamp);
CREATE INDEX idx_messages_sender ON ai_messages(sender);

-- エスカレーション
CREATE TABLE IF NOT EXISTS ai_handovers (
    handover_id VARCHAR(64) PRIMARY KEY,
    conversation_id VARCHAR(64) NOT NULL REFERENCES ai_conversations(conversation_id),
    ticket_id VARCHAR(64),
    reason VARCHAR(50) NOT NULL,
    priority VARCHAR(20) NOT NULL DEFAULT 'medium',
    target_department VARCHAR(50),
    assigned_to_user_id VARCHAR(50),
    status VARCHAR(30) NOT NULL DEFAULT 'pending',
    handover_notes TEXT,
    resolution_notes TEXT,
    escalated_at TIMESTAMP NOT NULL,
    assigned_at TIMESTAMP,
    resolved_at TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_handovers_conversation ON ai_handovers(conversation_id);
CREATE INDEX idx_handovers_status ON ai_handovers(status);
CREATE INDEX idx_handovers_priority ON ai_handovers(priority);

-- フィードバック
CREATE TABLE IF NOT EXISTS ai_feedback (
    feedback_id VARCHAR(64) PRIMARY KEY,
    conversation_id VARCHAR(64) NOT NULL REFERENCES ai_conversations(conversation_id),
    message_id VARCHAR(64) REFERENCES ai_messages(message_id),
    rating INTEGER NOT NULL CHECK (rating >= 1 AND rating <= 5),
    feedback_text TEXT,
    category VARCHAR(50),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_feedback_conversation ON ai_feedback(conversation_id);
CREATE INDEX idx_feedback_rating ON ai_feedback(rating);

-- 初期データ
INSERT INTO customers (customer_id, name, phone, email, tier_level) VALUES
('CUST-001', '山田太郎', '090-1234-5678', 'yamada@example.com', 'regular'),
('CUST-002', '鈴木花子', '090-8765-4321', 'suzuki@example.com', 'vip'),
('CUST-003', '佐藤次郎', '090-1111-2222', 'sato@example.com', 'regular');
```

---

## 6. API 設計

### 6.1 REST API エンドポイント

#### 6.1.1 対話管理

| メソッド | エンドポイント | 説明 | 認証 |
|---------|---------------|------|------|
| `POST` | `/api/ai/conversations` | 対話セッション開始 | 不要 |
| `GET` | `/api/ai/conversations/{id}` | 対話セッション取得 | 顧客認証 |
| `POST` | `/api/ai/conversations/{id}/close` | 対話セッション終了 | 顧客認証 |

#### 6.1.2 メッセージ送受信

| メソッド | エンドポイント | 説明 | 認証 |
|---------|---------------|------|------|
| `POST` | `/api/ai/conversations/{id}/messages` | メッセージ送信・応答取得 | 顧客認証 |
| `GET` | `/api/ai/conversations/{id}/messages` | メッセージ履歴取得 | 顧客認証 |

#### 6.1.3 顧客統合

| メソッド | エンドポイント | 説明 | 認証 |
|---------|---------------|------|------|
| `GET` | `/api/ai/customers/{id}` | 顧客情報取得 | 顧客認証 |
| `POST` | `/api/ai/customers/verify` | 顧客認証（電話番号） | 不要 |

#### 6.1.4 予約管理

| メソッド | エンドポイント | 説明 | 認証 |
|---------|---------------|------|------|
| `GET` | `/api/ai/appointments/available` | 空き枠検索 | 顧客認証 |
| `POST` | `/api/ai/appointments` | 予約作成 | 顧客認証 |
| `PUT` | `/api/ai/appointments/{id}` | 予約変更 | 顧客認証 |
| `DELETE` | `/api/ai/appointments/{id}` | 予約キャンセル | 顧客認証 |

#### 6.1.5 エスカレーション

| メソッド | エンドポイント | 説明 | 認証 |
|---------|---------------|------|------|
| `POST` | `/api/ai/handovers` | エスカレーション作成 | 内部 |
| `GET` | `/api/ai/handovers/queue` | 待機キュー取得 | オペレーター認証 |
| `PUT` | `/api/ai/handovers/{id}/assign` | オペレーター割り当て | オペレーター認証 |
| `PUT` | `/api/ai/handovers/{id}/resolve` | 解決マーク | オペレーター認証 |

#### 6.1.6 フィードバック

| メソッド | エンドポイント | 説明 | 認証 |
|---------|---------------|------|------|
| `POST` | `/api/ai/feedback` | フィードバック送信 | 顧客認証 |
| `GET` | `/api/ai/feedback/summary` | フィードバック集計 | オペレーター認証 |

### 6.2 リクエスト・レスポンスモデル

#### 6.2.1 対話開始

```csharp
// POST /api/ai/conversations
public class StartConversationRequest
{
    /// <summary>
    /// チャネル種別
    /// </summary>
    public string Channel { get; set; } = "web";

    /// <summary>
    /// 顧客 ID（任意、後から紐付け可能）
    /// </summary>
    public string? CustomerId { get; set; }

    /// <summary>
    /// 初期メッセージ（任意）
    /// </summary>
    public string? InitialMessage { get; set; }

    /// <summary>
    /// メタデータ（LINE ユーザー ID など）
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

public class StartConversationResponse
{
    /// <summary>
    /// 対話 ID
    /// </summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// 初期応答メッセージ
    /// </summary>
    public string? WelcomeMessage { get; set; }

    /// <summary>
    /// セッション有効期限（分）
    /// </summary>
    public int SessionTimeoutMinutes { get; set; } = 30;
}
```

#### 6.2.2 メッセージ送受信

```csharp
// POST /api/ai/conversations/{id}/messages
public class SendMessageRequest
{
    /// <summary>
    /// 顧客メッセージ
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// メッセージタイプ
    /// </summary>
    public string MessageType { get; set; } = "text";

    /// <summary>
    /// 添付ファイル URL（画像など）
    /// </summary>
    public string? AttachmentUrl { get; set; }
}

public class SendMessageResponse
{
    /// <summary>
    /// 対話 ID
    /// </summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// AI 応答メッセージ
    /// </summary>
    public string ResponseText { get; set; } = string.Empty;

    /// <summary>
    /// 判定されたインテント
    /// </summary>
    public string? Intent { get; set; }

    /// <summary>
    /// 置信度
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 抽出されたエンティティ
    /// </summary>
    public Dictionary<string, string>? Entities { get; set; }

    /// <summary>
    /// クイック返信ボタン
    /// </summary>
    public List<QuickReplyButton>? QuickReplies { get; set; }

    /// <summary>
    /// エスカレーション推奨フラグ
    /// </summary>
    public bool SuggestHandover { get; set; }
}

public class QuickReplyButton
{
    public string Label { get; set; } = string.Empty;
    public string ActionType { get; set; } = "postback"; // postback, url, phone
    public string ActionValue { get; set; } = string.Empty;
}
```

#### 6.2.3 顧客認証

```csharp
// POST /api/ai/customers/verify
public class VerifyCustomerRequest
{
    /// <summary>
    /// 電話番号またはメールアドレス
    /// </summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    /// 認証コード（SMS/メールで送信）
    /// </summary>
    public string VerificationCode { get; set; } = string.Empty;
}

public class VerifyCustomerResponse
{
    /// <summary>
    /// 認証成功フラグ
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 顧客 ID（認証成功時のみ）
    /// </summary>
    public string? CustomerId { get; set; }

    /// <summary>
    /// 顧客名（認証成功時のみ）
    /// </summary>
    public string? CustomerName { get; set; }

    /// <summary>
    /// 顧客ランク
    /// </summary>
    public string? TierLevel { get; set; }

    /// <summary>
    /// エラーメッセージ（認証失敗時のみ）
    /// </summary>
    public string? ErrorMessage { get; set; }
}
```

#### 6.2.4 予約作成

```csharp
// POST /api/ai/appointments
public class CreateAppointmentRequest
{
    /// <summary>
    /// 顧客 ID
    /// </summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// サービス種別
    /// </summary>
    public string ServiceType { get; set; } = string.Empty; // inspection, repair, test_drive

    /// <summary>
    /// 車両 ID（任意）
    /// </summary>
    public string? VehicleId { get; set; }

    /// <summary>
    /// 希望日時
    /// </summary>
    public DateTime PreferredDateTime { get; set; }

    /// <summary>
    /// 詳細情報
    /// </summary>
    public Dictionary<string, string>? Details { get; set; }
}

public class CreateAppointmentResponse
{
    /// <summary>
    /// 予約 ID
    /// </summary>
    public string AppointmentId { get; set; } = string.Empty;

    /// <summary>
    /// 確認番号
    /// </summary>
    public string ConfirmationNumber { get; set; } = string.Empty;

    /// <summary>
    /// 予約日時
    /// </summary>
    public DateTime ConfirmedDateTime { get; set; }

    /// <summary>
    /// ステータス
    /// </summary>
    public string Status { get; set; } = string.Empty; // tentative, confirmed
}
```

### 6.3 WebSocket イベント（SignalR）

#### 6.3.1 クライアント→サーバー

```csharp
// クライアントが送信するイベント
public class ClientEvents
{
    // 接続確立
    public const string Connect = "connect";
    
    // メッセージ送信
    public const string SendMessage = "send_message";
    
    // 入力中フラグ
    public const string TypingStart = "typing_start";
    public const string TypingStop = "typing_stop";
    
    // 接続切断
    public const string Disconnect = "disconnect";
}
```

#### 6.3.2 サーバー→クライアント

```csharp
// サーバーが送信するイベント
public class ServerEvents
{
    // 接続承認
    public const string Connected = "connected";
    
    // メッセージ受信
    public const string MessageReceived = "message_received";
    
    // AI 応答
    public const string AiResponse = "ai_response";
    
    // 入力中表示
    public const string TypingIndicator = "typing_indicator";
    
    // エラー
    public const string Error = "error";
    
    // 対話終了
    public const string ConversationEnded = "conversation_ended";
}
```

#### 6.3.3 SignalR ハブ実装

```csharp
using Microsoft.AspNetCore.SignalR;

namespace NetYamlForge.Hubs;

public class AIChatHub : Hub
{
    private readonly IConversationManager _conversationManager;
    private readonly IIntentClassifier _intentClassifier;
    private readonly IResponseGenerator _responseGenerator;
    private readonly ILogger<AIChatHub> _logger;

    public AIChatHub(
        IConversationManager conversationManager,
        IIntentClassifier intentClassifier,
        IResponseGenerator responseGenerator,
        ILogger<AIChatHub> logger)
    {
        _conversationManager = conversationManager;
        _intentClassifier = intentClassifier;
        _responseGenerator = responseGenerator;
        _logger = logger;
    }

    /// <summary>
    /// 接続確立
    /// </summary>
    public async Task Connect(string conversationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, conversationId);
        
        await Clients.Caller.SendAsync(ServerEvents.Connected, new
        {
            ConversationId = conversationId,
            Timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("Client connected: {ConnectionId}, Conversation: {ConversationId}",
            Context.ConnectionId, conversationId);
    }

    /// <summary>
    /// メッセージ受信
    /// </summary>
    public async Task SendMessage(string conversationId, string content)
    {
        try
        {
            // 入力中表示
            await Clients.Group(conversationId)
                .SendAsync(ServerEvents.TypingIndicator, new { IsTyping = true });

            // 顧客メッセージを保存
            await _conversationManager.AddMessageAsync(conversationId, new Message
            {
                Sender = "customer",
                Content = content,
                Timestamp = DateTime.UtcNow
            });

            // 意図分析
            var context = await _conversationManager.GetContextAsync(conversationId);
            var intentResult = await _intentClassifier.ClassifyAsync(content, context);

            // 応答生成
            var response = await _responseGenerator.GenerateAsync(intentResult, context);

            // AI 応答を保存
            await _conversationManager.AddMessageAsync(conversationId, new Message
            {
                Sender = "ai",
                Content = response.ResponseText,
                Timestamp = DateTime.UtcNow
            });

            // クライアントに送信
            await Clients.Group(conversationId)
                .SendAsync(ServerEvents.AiResponse, new
                {
                    ConversationId = conversationId,
                    ResponseText = response.ResponseText,
                    Intent = intentResult.Intent,
                    Confidence = intentResult.Confidence,
                    QuickReplies = response.QuickReplies,
                    Timestamp = DateTime.UtcNow
                });

            _logger.LogInformation("Message processed: {ConversationId}, Intent: {Intent}",
                conversationId, intentResult.Intent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message: {ConversationId}", conversationId);
            
            await Clients.Caller.SendAsync(ServerEvents.Error, new
            {
                Message = "エラーが発生しました。しばらくしてからお試しください。"
            });
        }
        finally
        {
            await Clients.Group(conversationId)
                .SendAsync(ServerEvents.TypingIndicator, new { IsTyping = false });
        }
    }

    /// <summary>
    /// 接続切断
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
```

---

## 7. コアモジュール設計

### 7.1 対話管理モジュール

#### 7.1.1 インターフェース

```csharp
namespace NetYamlForge.Services.AI.CustomerAI;

/// <summary>
/// 対話マネージャーインターフェース
/// </summary>
public interface IConversationManager
{
    /// <summary>
    /// 新規対話セッション開始
    /// </summary>
    Task<ConversationContext> StartConversationAsync(
        string channel,
        string? customerId = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// 対話コンテキスト取得
    /// </summary>
    Task<ConversationContext?> GetContextAsync(
        string conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// メッセージ追加
    /// </summary>
    Task AddMessageAsync(
        string conversationId,
        Message message,
        CancellationToken ct = default);

    /// <summary>
    /// メッセージ履歴取得
    /// </summary>
    Task<List<Message>> GetHistoryAsync(
        string conversationId,
        int limit = 20,
        CancellationToken ct = default);

    /// <summary>
    /// 対話セッション終了
    /// </summary>
    Task EndConversationAsync(
        string conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// 期限切れ対話クリーンアップ
    /// </summary>
    Task CleanExpiredConversationsAsync(CancellationToken ct = default);
}
```

#### 7.1.2 実装

```csharp
namespace NetYamlForge.Services.AI.CustomerAI;

/// <summary>
/// 対話マネージャー実装
/// </summary>
public class ConversationManager : IConversationManager
{
    private readonly IDbConnection _connection;
    private readonly IDistributedCache _cache;
    private readonly ProjectScope _projectScope;
    private readonly ILogger<ConversationManager> _logger;
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(30);

    public ConversationManager(
        IDbConnection connection,
        IDistributedCache cache,
        ProjectScope projectScope,
        ILogger<ConversationManager> logger)
    {
        _connection = connection;
        _cache = cache;
        _projectScope = projectScope;
        _logger = logger;
    }

    public async Task<ConversationContext> StartConversationAsync(
        string channel,
        string? customerId = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var conversationId = $"CONV-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N8}";

        var context = new ConversationContext
        {
            ConversationId = conversationId,
            CustomerId = customerId,
            Channel = channel,
            Status = "active",
            Metadata = metadata ?? new Dictionary<string, string>(),
            MessageHistory = new Queue<Message>(),
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        };

        // DB に保存
        await SaveContextAsync(context, ct);

        // Redis キャッシュに保存（高速アクセス用）
        await CacheContextAsync(context, ct);

        _logger.LogInformation("Conversation started: {ConversationId}, Channel: {Channel}",
            conversationId, channel);

        return context;
    }

    public async Task<ConversationContext?> GetContextAsync(
        string conversationId,
        CancellationToken ct = default)
    {
        // まずキャッシュから試行
        var cached = await _cache.GetStringAsync($"conv:{conversationId}", ct);
        if (!string.IsNullOrEmpty(cached))
        {
            return JsonSerializer.Deserialize<ConversationContext>(cached);
        }

        // DB から取得
        var context = await LoadContextAsync(conversationId, ct);
        if (context != null)
        {
            // キャッシュに再保存
            await CacheContextAsync(context, ct);
        }

        return context;
    }

    public async Task AddMessageAsync(
        string conversationId,
        Message message,
        CancellationToken ct = default)
    {
        // DB に保存
        const string sql = @"
            INSERT INTO ai_messages 
            (message_id, conversation_id, sender, message_type, content, 
             intent, entities_json, confidence_score, timestamp)
            VALUES 
            (@MessageId, @ConversationId, @Sender, @MessageType, @Content,
             @Intent, @EntitiesJson, @ConfidenceScore, @Timestamp)";

        await _connection.ExecuteAsync(sql, new
        {
            MessageId = message.Id,
            ConversationId = conversationId,
            Sender = message.Sender,
            MessageType = message.Type,
            Content = message.Content,
            Intent = message.Intent,
            EntitiesJson = message.Entities != null ? JsonSerializer.Serialize(message.Entities) : null,
            ConfidenceScore = message.Confidence,
            Timestamp = message.Timestamp
        });

        // コンテキスト更新
        var context = await GetContextAsync(conversationId, ct);
        if (context != null)
        {
            context.LastActivity = DateTime.UtcNow;
            context.MessageHistory.Enqueue(message);

            // 最新 20 メッセージのみ保持
            while (context.MessageHistory.Count > 20)
            {
                context.MessageHistory.Dequeue();
            }

            await UpdateContextAsync(context, ct);
        }
    }

    public async Task<List<Message>> GetHistoryAsync(
        string conversationId,
        int limit = 20,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM ai_messages
            WHERE conversation_id = @ConversationId
            ORDER BY timestamp DESC
            LIMIT @Limit";

        var messages = await _connection.QueryAsync<Message>(sql, new
        {
            ConversationId = conversationId,
            Limit = limit
        });

        return messages.Reverse().ToList();
    }

    public async Task EndConversationAsync(
        string conversationId,
        CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE ai_conversations
            SET status = 'completed',
                ended_at = @EndedAt,
                updated_at = @UpdatedAt
            WHERE conversation_id = @ConversationId";

        await _connection.ExecuteAsync(sql, new
        {
            ConversationId = conversationId,
            EndedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        // キャッシュから削除
        await _cache.RemoveAsync($"conv:{conversationId}", ct);

        _logger.LogInformation("Conversation ended: {ConversationId}", conversationId);
    }

    public async Task CleanExpiredConversationsAsync(CancellationToken ct = default)
    {
        var expiryTime = DateTime.UtcNow - _sessionTimeout;

        const string sql = @"
            UPDATE ai_conversations
            SET status = 'abandoned',
                updated_at = @UpdatedAt
            WHERE status = 'active'
              AND last_activity < @ExpiryTime";

        var updated = await _connection.ExecuteAsync(sql, new
        {
            UpdatedAt = DateTime.UtcNow,
            ExpiryTime = expiryTime
        });

        _logger.LogInformation("Cleaned up {Count} expired conversations", updated);
    }

    private async Task SaveContextAsync(ConversationContext context, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO ai_conversations 
            (conversation_id, channel, status, started_at, created_at, updated_at)
            VALUES 
            (@ConversationId, @Channel, @Status, @StartedAt, @CreatedAt, @UpdatedAt)";

        await _connection.ExecuteAsync(sql, new
        {
            context.ConversationId,
            context.Channel,
            context.Status,
            context.CreatedAt,
            context.UpdatedAt = DateTime.UtcNow
        });
    }

    private async Task<ConversationContext?> LoadContextAsync(
        string conversationId,
        CancellationToken ct)
    {
        const string sql = @"
            SELECT * FROM ai_conversations
            WHERE conversation_id = @ConversationId";

        return await _connection.QueryFirstOrDefaultAsync<ConversationContext>(sql, new
        {
            ConversationId = conversationId
        });
    }

    private async Task CacheContextAsync(ConversationContext context, CancellationToken ct)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _sessionTimeout
        };

        var json = JsonSerializer.Serialize(context);
        await _cache.SetStringAsync($"conv:{context.ConversationId}", json, options, ct);
    }

    private async Task UpdateContextAsync(ConversationContext context, CancellationToken ct)
    {
        const string sql = @"
            UPDATE ai_conversations
            SET last_activity = @LastActivity,
                updated_at = @UpdatedAt
            WHERE conversation_id = @ConversationId";

        await _connection.ExecuteAsync(sql, new
        {
            context.ConversationId,
            context.LastActivity,
            UpdatedAt = DateTime.UtcNow
        });

        // キャッシュ更新
        await CacheContextAsync(context, ct);
    }
}
```

### 7.2 意図分類モジュール

#### 7.2.1 ハイブリッド分類器

```csharp
namespace NetYamlForge.Services.AI.CustomerAI.IntentEngine;

/// <summary>
/// ハイブリッド意図分類器（ルール + LLM）
/// </summary>
public class HybridIntentClassifier : IIntentClassifier
{
    private readonly RuleIntentClassifier _ruleClassifier;
    private readonly LlmIntentClassifier _llmClassifier;
    private readonly ILogger<HybridIntentClassifier> _logger;
    private readonly double _llmThreshold = 0.7;

    public HybridIntentClassifier(
        RuleIntentClassifier ruleClassifier,
        LlmIntentClassifier llmClassifier,
        ILogger<HybridIntentClassifier> logger)
    {
        _ruleClassifier = ruleClassifier;
        _llmClassifier = llmClassifier;
        _logger = logger;
    }

    public async Task<IntentResult> ClassifyAsync(
        string message,
        ConversationContext context,
        CancellationToken ct = default)
    {
        // ステップ 1: ルールベース分類（高速）
        var ruleResult = _ruleClassifier.Classify(message, context);
        
        if (ruleResult.Confidence >= 0.8)
        {
            _logger.LogInformation("Rule match: {Intent} (confidence: {Confidence})",
                ruleResult.Intent, ruleResult.Confidence);
            
            return ruleResult;
        }

        // ステップ 2: LLM 分類（高精度）
        try
        {
            var llmResult = await _llmClassifier.ClassifyAsync(message, context, ct);
            
            _logger.LogInformation("LLM classification: {Intent} (confidence: {Confidence})",
                llmResult.Intent, llmResult.Confidence);
            
            return llmResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM classification failed, falling back to rule result");
            
            // フォールバック：ルール結果（置信度が低くても使用）
            return ruleResult;
        }
    }
}
```

#### 7.2.2 ルールベース分類器

```csharp
namespace NetYamlForge.Services.AI.CustomerAI.IntentEngine;

/// <summary>
/// ルールベース意図分類器
/// </summary>
public class RuleIntentClassifier
{
    private readonly List<IntentPattern> _patterns;

    public RuleIntentClassifier(IOptions<IntentPatternsConfig> config)
    {
        _patterns = config.Value.Patterns;
    }

    public IntentResult Classify(string message, ConversationContext context)
    {
        var bestMatch = _patterns
            .Select(p => new
            {
                Pattern = p,
                Match = TryMatch(p, message, context)
            })
            .Where(x => x.Match != null)
            .OrderByDescending(x => x.Match!.Confidence)
            .FirstOrDefault();

        if (bestMatch != null && bestMatch.Match != null)
        {
            return new IntentResult
            {
                Intent = bestMatch.Pattern.Intent,
                Confidence = bestMatch.Match.Confidence,
                Entities = bestMatch.Match.Entities,
                Source = "rule"
            };
        }

        return new IntentResult
        {
            Intent = "unclear",
            Confidence = 0.5,
            Entities = new Dictionary<string, string>(),
            Source = "default"
        };
    }

    private PatternMatch? TryMatch(IntentPattern pattern, string message, ConversationContext context)
    {
        // キーワードマッチ
        if (pattern.Keywords != null && pattern.Keywords.Any())
        {
            var matchedKeywords = pattern.Keywords
                .Where(k => message.Contains(k, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchedKeywords.Any())
            {
                var confidence = Math.Min(1.0, (double)matchedKeywords.Count / pattern.Keywords.Count);
                
                return new PatternMatch
                {
                    Confidence = confidence,
                    Entities = ExtractEntities(message, pattern)
                };
            }
        }

        // 正規表現マッチ
        if (pattern.RegexPatterns != null && pattern.RegexPatterns.Any())
        {
            foreach (var regex in pattern.RegexPatterns)
            {
                var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return new PatternMatch
                    {
                        Confidence = 0.9,
                        Entities = ExtractEntitiesFromRegex(match, pattern)
                    };
                }
            }
        }

        return null;
    }

    private Dictionary<string, string> ExtractEntities(string message, IntentPattern pattern)
    {
        var entities = new Dictionary<string, string>();

        // 日付抽出
        var dateMatch = Regex.Match(message, @"(\d{4}年\d{1,2}月\d{1,2}日|\d{4}-\d{2}-\d{2})");
        if (dateMatch.Success)
        {
            entities["preferred_date"] = dateMatch.Value;
        }

        // 電話番号抽出
        var phoneMatch = Regex.Match(message, @"(\d{2,4}-\d{3,4}-\d{4})");
        if (phoneMatch.Success)
        {
            entities["phone"] = phoneMatch.Value;
        }

        // 車種抽出（簡易）
        if (pattern.Intent.Contains("vehicle"))
        {
            var carModels = new[] { "カローラ", "プリウス", "クラウン", "RAV4" };
            foreach (var model in carModels)
            {
                if (message.Contains(model))
                {
                    entities["vehicle_model"] = model;
                    break;
                }
            }
        }

        return entities;
    }

    private Dictionary<string, string> ExtractEntitiesFromRegex(Match match, IntentPattern pattern)
    {
        var entities = new Dictionary<string, string>();
        
        // 名前付きキャプチャグループからエンティティ抽出
        foreach (string groupName in match.Groups.Cast<Group>().Select(g => g.Name).Where(n => n != "0"))
        {
            entities[groupName] = match.Groups[groupName].Value;
        }

        return entities;
    }
}

public class IntentPattern
{
    public string Intent { get; set; } = string.Empty;
    public List<string>? Keywords { get; set; }
    public List<string>? RegexPatterns { get; set; }
    public double BaseConfidence { get; set; } = 0.8;
}

public class PatternMatch
{
    public double Confidence { get; set; }
    public Dictionary<string, string> Entities { get; set; } = new();
}
```

### 7.3 応答生成モジュール

#### 7.3.1 LLM 応答生成器

```csharp
namespace NetYamlForge.Services.AI.CustomerAI.ResponseGeneration;

/// <summary>
/// LLM 応答生成器
/// </summary>
public class LlmResponseGenerator : IResponseGenerator
{
    private readonly ILlmService _llmService;
    private readonly IKnowledgeBaseService _knowledgeBase;
    private readonly ILogger<LlmResponseGenerator> _logger;

    public LlmResponseGenerator(
        ILlmService llmService,
        IKnowledgeBaseService knowledgeBase,
        ILogger<LlmResponseGenerator> logger)
    {
        _llmService = llmService;
        _knowledgeBase = knowledgeBase;
        _logger = logger;
    }

    public async Task<AiResponse> GenerateAsync(
        IntentResult intent,
        ConversationContext context,
        CancellationToken ct = default)
    {
        // ステップ 1: ナレッジベース検索
        var kbArticle = await _knowledgeBase.SearchAsync(intent.Intent, context);
        
        if (kbArticle != null && kbArticle.RelevanceScore > 0.8)
        {
            _logger.LogInformation("Knowledge base match: {ArticleId}", kbArticle.ArticleId);
            
            return new AiResponse
            {
                ResponseText = kbArticle.AnswerTemplate,
                QuickReplies = kbArticle.SuggestedReplies,
                Confidence = kbArticle.RelevanceScore,
                Source = "knowledge_base"
            };
        }

        // ステップ 2: LLM 生成
        try
        {
            var prompt = BuildPrompt(intent, context);
            
            var llmResult = await _llmService.GenerateResponseAsync(prompt, ct);
            
            _logger.LogInformation("LLM response generated");
            
            return new AiResponse
            {
                ResponseText = llmResult.Content,
                QuickReplies = llmResult.SuggestedReplies,
                Confidence = llmResult.Confidence,
                Source = "llm"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM response generation failed");
            
            // フォールバック：汎用応答
            return new AiResponse
            {
                ResponseText = "申し訳ございません。理解できませんでした。もう少し詳しく教えていただけますか？",
                Confidence = 0.5,
                Source = "fallback"
            };
        }
    }

    private string BuildPrompt(IntentResult intent, ConversationContext context)
    {
        var history = string.Join("\n", context.MessageHistory.TakeLast(5).Select(m =>
            $"{m.Sender}: {m.Content}"));

        return $@"あなたは自動車ディーラーの AI カスタマーサポートです。
丁寧で親切な口調で応答してください。

会話履歴:
{history}

顧客の意図: {intent.Intent}
置信度: {intent.Confidence:P0}
抽出された情報: {string.Join(", ", intent.Entities.Select(e => $"{e.Key}={e.Value}"))}

上記に基づき、適切な応答を生成してください。
応答は簡潔に、必要な情報のみを含めてください。";
    }
}
```

### 7.4 エスカレーション管理モジュール

#### 7.4.1 エスカレーション判定

```csharp
namespace NetYamlForge.Services.AI.CustomerAI.Handover;

/// <summary>
/// エスカレーションマネージャー
/// </summary>
public class HandoverManager : IHandoverManager
{
    private readonly IDbConnection _connection;
    private readonly IMessageQueueService _queueService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<HandoverManager> _logger;

    public HandoverManager(
        IDbConnection connection,
        IMessageQueueService queueService,
        INotificationService notificationService,
        ILogger<HandoverManager> logger)
    {
        _connection = connection;
        _queueService = queueService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<HandoverDecision> EvaluateAsync(
        ConversationContext context,
        IntentResult? lastIntent,
        CancellationToken ct = default)
    {
        var decision = new HandoverDecision { ShouldHandover = false };

        // 判定ルール 1: 置信度が低い
        if (lastIntent?.Confidence < 0.6)
        {
            decision.ShouldHandover = true;
            decision.Reason = "low_confidence";
            decision.Message = "AI の回答に自信がありません";
            return decision;
        }

        // 判定ルール 2: 顧客が不満を示している
        if (context.SentimentScore < -0.5)
        {
            decision.ShouldHandover = true;
            decision.Reason = "negative_sentiment";
            decision.Message = "顧客が不満を示しています";
            return decision;
        }

        // 判定ルール 3: VIP 顧客
        if (context.CustomerTier == "vip")
        {
            decision.ShouldHandover = true;
            decision.Reason = "vip_customer";
            decision.Priority = "high";
            decision.Message = "VIP 顧客のため優先的に対応";
            return decision;
        }

        // 判定ルール 4: 顧客が明示的に要求
        var lastMessage = context.MessageHistory.LastOrDefault(m => m.Sender == "customer");
        if (lastMessage != null && 
            (lastMessage.Content.Contains("担当者") || lastMessage.Content.Contains("人間")))
        {
            decision.ShouldHandover = true;
            decision.Reason = "customer_request";
            decision.Message = "顧客が担当者への接続を要求";
            return decision;
        }

        return decision;
    }

    public async Task<Handover> CreateHandoverAsync(
        ConversationContext context,
        HandoverDecision decision,
        CancellationToken ct = default)
    {
        var handoverId = $"HO-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N8}";

        // オペレーター自動割り当て
        var assignedOperator = await FindBestOperatorAsync(decision.TargetDepartment, ct);

        const string sql = @"
            INSERT INTO ai_handovers
            (handover_id, conversation_id, reason, priority, target_department,
             assigned_to_user_id, status, handover_notes, escalated_at)
            VALUES
            (@HandoverId, @ConversationId, @Reason, @Priority, @TargetDepartment,
             @AssignedToUserId, @Status, @HandoverNotes, @EscalatedAt)";

        await _connection.ExecuteAsync(sql, new
        {
            HandoverId = handoverId,
            ConversationId = context.ConversationId,
            Reason = decision.Reason,
            Priority = decision.Priority ?? "medium",
            TargetDepartment = decision.TargetDepartment,
            AssignedToUserId = assignedOperator?.UserId,
            Status = "pending",
            HandoverNotes = decision.Message,
            EscalatedAt = DateTime.UtcNow
        });

        // キューに追加
        await _queueService.EnqueueAsync("handovers", new
        {
            HandoverId = handoverId,
            ConversationId = context.ConversationId,
            Priority = decision.Priority
        }, ct);

        // オペレーターに通知
        if (assignedOperator != null)
        {
            await _notificationService.NotifyOperatorAsync(
                assignedOperator.UserId,
                $"新しいエスカレーション: {handoverId}",
                decision.Message);
        }

        _logger.LogInformation("Handover created: {HandoverId}, Reason: {Reason}",
            handoverId, decision.Reason);

        return new Handover
        {
            HandoverId = handoverId,
            ConversationId = context.ConversationId,
            Reason = decision.Reason,
            AssignedToUserId = assignedOperator?.UserId,
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<Operator?> FindBestOperatorAsync(
        string? department,
        CancellationToken ct = default)
    {
        // 部門内で最も負荷の低いオペレーターを選択
        const string sql = @"
            SELECT u.user_id, u.name, u.department,
                   COUNT(h.handover_id) as pending_count
            FROM users u
            LEFT JOIN ai_handovers h ON u.user_id = h.assigned_to_user_id AND h.status = 'pending'
            WHERE u.role = 'operator'
              AND (@Department IS NULL OR u.department = @Department)
              AND u.status = 'active'
            GROUP BY u.user_id, u.name, u.department
            ORDER BY pending_count ASC
            LIMIT 1";

        return await _connection.QueryFirstOrDefaultAsync<Operator>(sql, new
        {
            Department = department
        });
    }
}
```

---

## 8. UI/UX 設計

### 8.1 Web チャットボット

#### 8.1.1 画面レイアウト

```
┌─────────────────────────────────────────────────────────────┐
│  [会社ロゴ]                              [閉じる ×]          │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌───────────────────────────────────────────────────────┐ │
│  │ 🤖 AI サポート                                         │ │
│  │  営業時間：9:00-18:00（土日祝除く）                    │ │
│  └───────────────────────────────────────────────────────┘ │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐ │
│  │ 👤 顧客                                                │ │
│  │                                                       │ │
│  │  明日のサービス予約をしたいのですが                   │ │
│  │                                                       │ │
│  │                                          10:30 AM     │ │
│  └───────────────────────────────────────────────────────┘ │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐ │
│  │ 🤖 AI サポート                                         │ │
│  │                                                       │ │
│  │  承知いたしました。明日のサービス予約ですね。         │ │
│  │  どちらの店舗をご希望でしょうか？                     │ │
│  │                                                       │ │
│  │  [ 東京店 ]  [ 大阪店 ]  [ 名古屋店 ]                 │ │
│  │                                                       │ │
│  │                                          10:30 AM     │ │
│  └───────────────────────────────────────────────────────┘ │
│                                                             │
│  [▼ 自動スクロール]                                         │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│  ┌───────────────────────────────────────────────────────┐ │
│  │  メッセージを入力...                                 │  │
│  └───────────────────────────────────────────────────────┘ │
│  [ 📎 添付 ]                           [ 送信 ▶ ]           │
└─────────────────────────────────────────────────────────────┘
```

#### 8.1.2 コンポーネント構成

```typescript
// React 実装例
interface ChatWidgetProps {
  projectId: string;
  theme?: 'light' | 'dark';
  position?: 'bottom-right' | 'bottom-left';
}

const ChatWidget: React.FC<ChatWidgetProps> = ({ 
  projectId, 
  theme = 'light',
  position = 'bottom-right'
}) => {
  const [isOpen, setIsOpen] = useState(false);
  const [messages, setMessages] = useState<Message[]>([]);
  const [inputValue, setInputValue] = useState('');
  const [isTyping, setIsTyping] = useState(false);

  // SignalR 接続
  const connection = useMemo(() => {
    return new HubConnectionBuilder()
      .withUrl(`/ai-chat-hub`)
      .withAutomaticReconnect()
      .build();
  }, []);

  // メッセージ送信
  const sendMessage = async (content: string) => {
    const message: Message = {
      id: generateId(),
      sender: 'customer',
      content,
      timestamp: new Date()
    };

    setMessages(prev => [...prev, message]);
    setInputValue('');
    setIsTyping(true);

    await connection.invoke('SendMessage', conversationId, content);
  };

  return (
    <div className={`chat-widget ${position} ${theme}`}>
      {/* 起動ボタン */}
      {!isOpen && (
        <button 
          className="chat-launcher"
          onClick={() => setIsOpen(true)}
        >
          💬 チャット
        </button>
      )}

      {/* チャットウィンドウ */}
      {isOpen && (
        <div className="chat-window">
          <ChatHeader onClose={() => setIsOpen(false)} />
          <MessageList messages={messages} isTyping={isTyping} />
          <QuickReplies onReply={sendMessage} />
          <MessageInput 
            value={inputValue}
            onChange={setInputValue}
            onSend={sendMessage}
          />
        </div>
      )}
    </div>
  );
};
```

### 8.2 客服オペレーターダッシュボード

#### 8.2.1 画面レイアウト

```
┌───────────────────────────────────────────────────────────────────────┐
│  AI 窗口 オペレーターダッシュボード                    [ユーザー] [設定] │
├───────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  ┌─ キュー状況 ─────────────────────────────────────────────────┐    │
│  │  待機中：3 件  |  対応中：5 件  |  本日完了：28 件              │    │
│  └───────────────────────────────────────────────────────────────┘    │
│                                                                       │
│  ┌─ 待機キュー ──────────────┐  ┌─ 対話ウィンド─ ──────────────────┐  │
│  │                           │  │                                   │  │
│  │ 🔴 [緊急] 山田様           │  │  対話 ID: CONV-20260328-001      │  │
│  │    クレーム対応            │  │  顧客：山田太郎 (VIP)            │  │
│  │    10 分経過               │  │  チャネル：Web チャット           │  │
│  │                           │  │  意図：complaint                  │  │
│  │ 🟡 鈴木様                  │  │  置信度：45%                     │  │
│  │    複雑な問い合わせ        │  │                                   │  │
│  │    5 分経過                │  │  ┌─ 会話履歴 ──────────────────┐ │  │
│  │                           │  │  │ AI: 本日はどのようなご用件   │ │  │
│  │ 🟢 佐藤様                  │  │  │     でしょうか？            │ │  │
│  │    価格照会                │  │  │                             │ │  │
│  │    新規                    │  │  │ 顧客：昨日納車した車の      │ │  │
│  │                           │  │  │     調子が悪いのですが…     │ │  │
│  │ [更新] [フィルタ▼]         │  │  │                             │ │  │
│  │                           │  │  │ AI: 申し訳ございません。    │ │  │
│  │                           │  │  │     具体的な症状を           │ │  │
│  │                           │  │  │     お聞かせください。      │ │  │
│  │                           │  │  └─────────────────────────────┘ │  │
│  │                           │  │                                   │  │
│  │                           │  │  [ 引き継ぐ ] [ 一時保留 ]        │  │
│  │                           │  └───────────────────────────────────┘  │
│  └─────────────────────────────┘                                       │
│                                                                       │
│  ┌─ 統計情報 ──────────────────────────────────────────────────┐    │
│  │  平均対応時間：8 分  |  顧客満足度：4.2/5.0  |  転接率：15%   │    │
│  └───────────────────────────────────────────────────────────────┘    │
│                                                                       │
└───────────────────────────────────────────────────────────────────────┘
```

---

## 9. セキュリティ設計

### 9.1 認証・認可

#### 9.1.1 顧客認証フロー

```
顧客（電話番号入力）
    ↓
[1] 認証コード送信（SMS/メール）
    ↓
[2] 顧客が認証コード入力
    ↓
[3] VerifyCustomerRequest 検証
    ├─ 電話番号一致確認
    └─ 認証コード有効期限チェック（5 分）
    ↓
[4] JWT トークン発行（有効期限 30 分）
    ↓
[5] 以降のリクエストにトークン付与
```

#### 9.1.2 JWT トークン構成

```csharp
public class CustomerToken
{
    public string CustomerId { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string TierLevel { get; set; } = "regular";
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

// トークン生成
var token = new JwtSecurityToken(
    issuer: "ai-window-system",
    audience: "ai-window-client",
    claims: new[]
    {
        new Claim("customer_id", customer.CustomerId),
        new Claim("phone", customer.PhoneNumber),
        new Claim("tier", customer.TierLevel)
    },
    expires: DateTime.UtcNow.AddMinutes(30),
    signingCredentials: signingCredentials
);
```

### 9.2 データ暗号化

#### 9.2.1 保存時暗号化

```csharp
public class EncryptionService : IEncryptionService
{
    private readonly string _key;
    private readonly string _iv;

    public EncryptionService(IOptions<SecurityConfig> config)
    {
        _key = config.Value.EncryptionKey;
        _iv = config.Value.EncryptionIv;
    }

    public string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(_key);
        aes.IV = Encoding.UTF8.GetBytes(_iv);

        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(plainText);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    public string Decrypt(string cipherText)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(_key);
        aes.IV = Encoding.UTF8.GetBytes(_iv);

        var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        
        using var ms = new MemoryStream(Convert.FromBase64String(cipherText));
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);

        return sr.ReadToEnd();
    }
}
```

#### 9.2.2 個人情報マスキング

```csharp
public class MaskingService : IMaskingService
{
    public string MaskPhoneNumber(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 4)
        {
            return phone;
        }

        return phone.Substring(0, phone.Length - 4).PadRight(phone.Length, '*');
    }

    public string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
        {
            return email;
        }

        var parts = email.Split('@');
        var name = parts[0];
        var domain = parts[1];

        if (name.Length <= 2)
        {
            return $"**@{domain}";
        }

        return $"{name[0]}{new string('*', name.Length - 1)}@{domain}";
    }
}
```

### 9.3 監査ログ

```csharp
public class AuditLogService : IAuditLogService
{
    private readonly IDbConnection _connection;
    private readonly IHttpContextAccessor _httpContext;

    public async Task LogAsync(string action, string entityType, string entityId, object? changes = null)
    {
        var userId = _httpContext.HttpContext?.User?.FindFirst("customer_id")?.Value ?? "anonymous";
        var ipAddress = _httpContext.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";

        const string sql = @"
            INSERT INTO audit_logs
            (log_id, user_id, action, entity_type, entity_id, changes_json, ip_address, timestamp)
            VALUES
            (@LogId, @UserId, @Action, @EntityType, @EntityId, @ChangesJson, @IpAddress, @Timestamp)";

        await _connection.ExecuteAsync(sql, new
        {
            LogId = Guid.NewGuid().ToString(),
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ChangesJson = changes != null ? JsonSerializer.Serialize(changes) : null,
            IpAddress = ipAddress,
            Timestamp = DateTime.UtcNow
        });
    }
}
```

---

## 10. 実装フェーズ

### 10.1 全体ロードマップ

```
Week 1-2: Phase 1 - 基礎フレームワーク
Week 3-4: Phase 2 - 業務統合
Week 5-6: Phase 3 - マルチチャネル
Week 7-8: Phase 4 - 智能最適化
```

### 10.2 Phase 1: 基礎フレームワーク（Week 1-2）

#### 目標
- 対話管理エンジンの実装
- 基本意図分類（ルールベース）
- LLM 統合（Qwen）
- データベースモデル
- REST API エンドポイント

#### 実装タスク

| ID | タスク | 工数 | 担当 |
|----|--------|------|------|
| T1-1 | データベース初期化 | 4h | Backend |
| T1-2 | ConversationManager 実装 | 8h | Backend |
| T1-3 | RuleIntentClassifier 実装 | 8h | Backend |
| T1-4 | LlmService 統合 | 8h | Backend |
| T1-5 | AIWindowController 実装 | 8h | Backend |
| T1-6 | 単体テスト | 8h | QA |

#### 成果物
- ✅ 対話開始・メッセージ送受信 API
- ✅ 意図分類（ルールベース）
- ✅ LLM 応答生成
- ✅ データベース永続化

### 10.3 Phase 2: 業務統合（Week 3-4）

#### 目標
- 顧客情報照会
- サービス予約統合
- エスカレーション機能
- 完全な E2E フロー

#### 実装タスク

| ID | タスク | 工数 | 担当 |
|----|--------|------|------|
| T2-1 | CustomerDataService 実装 | 8h | Backend |
| T2-2 | AppointmentService 実装 | 8h | Backend |
| T2-3 | HandoverManager 実装 | 8h | Backend |
| T2-4 | フック実装（予約作成時） | 4h | Backend |
| T2-5 | 統合テスト | 12h | QA |

#### 成果物
- ✅ 顧客認証・情報照会
- ✅ 予約作成・変更・キャンセル
- ✅ エスカレーション自動判定
- ✅ E2E テスト完了

### 10.4 Phase 3: マルチチャネル（Week 5-6）

#### 目標
- Web チャットボット（React）
- LINE 統合
- 客服ダッシュボード
- SignalR リアルタイム通信

#### 実装タスク

| ID | タスク | 工数 | 担当 |
|----|--------|------|------|
| T3-1 | React チャットボット | 16h | Frontend |
| T3-2 | SignalR ハブ実装 | 8h | Backend |
| T3-3 | LINE Messaging API 統合 | 12h | Backend |
| T3-4 | オペレーターダッシュボード | 16h | Frontend |
| T3-5 | E2E テスト（全チャネル） | 12h | QA |

#### 成果物
- ✅ Web チャットボット（埋め込み可能）
- ✅ LINE 公式アカウント連携
- ✅ 客服ダッシュボード（リアルタイム）
- ✅ 全チャネル統合テスト完了

### 10.5 Phase 4: 智能最適化（Week 7-8）

#### 目標
- 感情分析モジュール
- ユーザーフィードバック学習
- 分析ダッシュボード
- パフォーマンス最適化

#### 実装タスク

| ID | タスク | 工数 | 担当 |
|----|--------|------|------|
| T4-1 | SentimentAnalyzer 実装 | 8h | Backend |
| T4-2 | FeedbackService 実装 | 8h | Backend |
| T4-3 | 分析ダッシュボード | 12h | Frontend |
| T4-4 | パフォーマンステスト | 8h | QA |
| T4-5 | チューニング | 8h | All |

#### 成果物
- ✅ 感情分析（不満検出）
- ✅ フィードバック収集・分析
- ✅ 分析ダッシュボード（Grafana）
- ✅ 性能目標達成（P95 < 2 秒）

---

## 11. テスト戦略

### 11.1 テストレベル

```
┌─────────────────────────────────────┐
│        E2E テスト（統合）            │
│  顧客シナリオベースの完全フロー      │
└─────────────────────────────────────┘
              ↑
┌─────────────────────────────────────┐
│        統合テスト                    │
│  複数モジュールの連携検証            │
└─────────────────────────────────────┘
              ↑
┌─────────────────────────────────────┐
│        単体テスト                    │
│  各モジュールの機能検証              │
└─────────────────────────────────────┘
```

### 11.2 単体テスト例

```csharp
namespace NetYamlForge.Tests.AI;

public class RuleIntentClassifierTests
{
    private readonly RuleIntentClassifier _classifier;

    public RuleIntentClassifierTests()
    {
        var options = Options.Create(new IntentPatternsConfig
        {
            Patterns = new List<IntentPattern>
            {
                new IntentPattern
                {
                    Intent = "price_inquiry",
                    Keywords = new[] { "価格", "値段", "いくら" },
                    BaseConfidence = 0.8
                },
                new IntentPattern
                {
                    Intent = "service_booking",
                    Keywords = new[] { "予約", "申し込む" },
                    RegexPatterns = new[] { @"(\d{4}年\d{1,2}月\d{1,2}日)" },
                    BaseConfidence = 0.8
                }
            }
        });

        _classifier = new RuleIntentClassifier(options);
    }

    [Fact]
    public void Classify_PriceInquiry_ReturnsCorrectIntent()
    {
        // Arrange
        var message = "新車の価格を教えてください";
        var context = new ConversationContext();

        // Act
        var result = _classifier.Classify(message, context);

        // Assert
        Assert.Equal("price_inquiry", result.Intent);
        Assert.True(result.Confidence > 0.7);
    }

    [Fact]
    public void Classify_ServiceBookingWithDate_ExtractsEntity()
    {
        // Arrange
        var message = "来週の 4 月 1 日にサービス予約をしたい";
        var context = new ConversationContext();

        // Act
        var result = _classifier.Classify(message, context);

        // Assert
        Assert.Equal("service_booking", result.Intent);
        Assert.Contains("preferred_date", result.Entities.Keys);
        Assert.Equal("4 月 1 日", result.Entities["preferred_date"]);
    }

    [Fact]
    public void Classify_UnclearMessage_ReturnsDefaultIntent()
    {
        // Arrange
        var message = "あのですね";
        var context = new ConversationContext();

        // Act
        var result = _classifier.Classify(message, context);

        // Assert
        Assert.Equal("unclear", result.Intent);
        Assert.Equal(0.5, result.Confidence);
    }
}
```

### 11.3 統合テスト例

```csharp
namespace NetYamlForge.Tests.AI.Integration;

public class ConversationFlowTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;

    public ConversationFlowTests(IntegrationTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task FullConversationFlow_ServiceBooking_Success()
    {
        // Step 1: 対話開始
        var startResponse = await _client.PostAsJsonAsync("/api/ai/conversations", new
        {
            Channel = "web"
        });

        startResponse.EnsureSuccessStatusCode();
        var startData = await startResponse.Content.ReadFromJsonAsync<StartConversationResponse>();
        var conversationId = startData.ConversationId;

        // Step 2: メッセージ送信（予約依頼）
        var messageResponse = await _client.PostAsJsonAsync(
            $"/api/ai/conversations/{conversationId}/messages",
            new { Content = "明日のサービス予約をしたい" });

        messageResponse.EnsureSuccessStatusCode();
        var messageData = await messageResponse.Content.ReadFromJsonAsync<SendMessageResponse>();

        // Step 3: 応答検証
        Assert.Equal("service_booking", messageData.Intent);
        Assert.NotNull(messageData.ResponseText);
        Assert.NotEmpty(messageData.QuickReplies);
    }
}
```

### 11.4 パフォーマンステスト

```csharp
namespace NetYamlForge.Tests.AI.Performance;

public class PerformanceTests
{
    [Fact]
    public async Task ResponseTime_Under2Seconds_P95()
    {
        var client = new HttpClient();
        var responseTimes = new List<double>();

        // 100 回リクエスト送信
        for (int i = 0; i < 100; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            
            var response = await client.PostAsJsonAsync("/api/ai/conversations", new
            {
                Channel = "web",
                InitialMessage = "こんにちは"
            });

            stopwatch.Stop();
            responseTimes.Add(stopwatch.ElapsedMilliseconds);
        }

        // P95 計算
        var sorted = responseTimes.OrderBy(x => x).ToList();
        var p95Index = (int)(sorted.Count * 0.95);
        var p95 = sorted[p95Index];

        Assert.True(p95 < 2000, $"P95 レイテンシー {p95}ms が目標 2000ms を超過");
    }
}
```

---

## 12. 運用設計

### 12.1 監視指標

| 指標 | 目標値 | アラート閾値 | 測定間隔 |
|------|--------|-------------|----------|
| **API レイテンシー（P95）** | < 2 秒 | > 3 秒 | 1 分 |
| **エラーレート** | < 1% | > 5% | 1 分 |
| **同時接続数** | < 500 | > 400 | 1 分 |
| **CPU 使用率** | < 70% | > 80% | 1 分 |
| **メモリ使用率** | < 80% | > 90% | 1 分 |
| **LLM API エラー** | < 2% | > 10% | 1 分 |
| **対話成功率** | > 70% | < 50% | 5 分 |

### 12.2 アラート通知

```yaml
# prometheus/alerts.yml
groups:
- name: ai-window-alerts
  rules:
  - alert: HighApiLatency
    expr: histogram_quantile(0.95, rate(api_request_duration_seconds_bucket[5m])) > 3
    for: 5m
    labels:
      severity: warning
    annotations:
      summary: "API レイテンシーが高い（P95 > 3 秒）"
      description: "API の P95 レイテンシーが {{ $value }}秒です"

  - alert: HighErrorRate
    expr: rate(http_requests_total{status=~"5.."}[5m]) / rate(http_requests_total[5m]) > 0.05
    for: 2m
    labels:
      severity: critical
    annotations:
      summary: "エラーレートが高い（> 5%）"
      description: "HTTP 5xx エラーレートが {{ $value | humanizePercentage }}です"

  - alert: LlmApiFailing
    expr: rate(llm_api_errors_total[5m]) / rate(llm_api_calls_total[5m]) > 0.1
    for: 5m
    labels:
      severity: critical
    annotations:
      summary: "LLM API が失敗しています"
      description: "LLM API エラーレートが {{ $value | humanizePercentage }}です"
```

### 12.3 バックアップ戦略

| バックアップ対象 | 頻度 | 保持期間 | 保存先 |
|-----------------|------|---------|--------|
| **PostgreSQL** | 日次（午前 2 時） | 30 日間 | S3 |
| **Redis** | 不要（永続化なし） | - | - |
| **対話ログ** | 週次エクスポート | 7 年間 | S3 Glacier |
| **設定ファイル** | 変更時 Git コミット | 無制限 | GitHub |

### 12.4 障害復旧計画

#### 障害レベル分類

| レベル | 説明 | 対応 | RTO | RPO |
|--------|------|------|-----|-----|
| **レベル 1** | 一部機能低下（応答遅延） | 自動スケーリング | - | - |
| **レベル 2** | 一部機能停止（LLM API 障害） | フェイルオーバー（ルールベース） | 5 分 | 0 |
| **レベル 3** | システム停止（DB 障害） | バックアップから復旧 | 4 時間 | 1 時間 |
| **レベル 4** | 完全停止（インフラ障害） | リージョン切り替え | 8 時間 | 4 時間 |

---

## 13. 付録

### 13.1 設定ファイル例

```yaml
# projects/auto-dealer-demo/config/ai-window.yml
version: "1.0"

llm:
  provider: qwen
  api_key: ${LLM_API_KEY}
  base_url: https://dashscope.aliyuncs.com/api/v1
  model: qwen-max
  timeout_ms: 30000
  temperature: 0.3
  max_tokens: 500

channels:
  web-chat:
    enabled: true
    theme: light
    position: bottom-right
    welcome_message: "こんにちは！AI サポートです。どのようなご用件でしょうか？"
  
  line:
    enabled: true
    channel_secret: ${LINE_CHANNEL_SECRET}
    channel_access_token: ${LINE_CHANNEL_TOKEN}
  
  email:
    enabled: false
    pop3_server: pop.example.com
    smtp_server: smtp.example.com

knowledge_base:
  articles:
  - id: kb-001
    intent: business_hours
    keywords: ["営業時間", "何時まで", "何時から"]
    answer: "営業時間は 9:00-18:00（土日祝除く）となります。"
  
  - id: kb-002
    intent: price_inquiry
    keywords: ["価格", "値段", "いくら"]
    answer: "車両の価格はモデルにより異なります。具体的な車種をお知らせいただけますか？"

escalation_rules:
- condition:
    confidence_below: 0.6
  action:
    handover: true
    priority: medium
    department: general

- condition:
    sentiment_below: -0.5
  action:
    handover: true
    priority: high
    department: quality

monitoring:
  prometheus:
    enabled: true
    port: 9090
    scrape_interval: 15s
  
  grafana:
    enabled: true
    port: 3000
    dashboards:
    - overview
    - conversation_metrics
    - performance

performance:
  cache_ttl_minutes: 30
  max_concurrent_conversations: 500
  message_history_limit: 20
  session_timeout_minutes: 30
```

### 13.2 主要な成功指標（KPI）

| KPI | 計算式 | 目標値 |
|-----|--------|--------|
| **首次回答率** | AI が直接回答した割合 | > 70% |
| **顧客満足度** | フィードバック平均評価 | > 4.0/5.0 |
| **平均応答時間** | メッセージ送信から応答までの時間 | < 2 秒 |
| **エスカレーション率** | 人間に転接した割合 | < 30% |
| **システム可用性** | 稼働時間 / 総時間 | > 99.5% |
| **コスト削減効果** | 削減された人工対応時間 | 40% 削減 |

### 13.3 用語集

| 用語 | 説明 |
|------|------|
| **Intent** | 顧客の意図（例：価格照会、予約依頼） |
| **Entity** | 会話から抽出された情報（例：日付、車種） |
| **Confidence** | 意図判定の置信度（0.0-1.0） |
| **Handover** | AI から人間オペレーターへの引き継ぎ |
| **Sentiment Score** | 顧客の感情スコア（-1.0: 不満 〜 1.0: 満足） |
| **LLM** | Large Language Model（大規模言語モデル） |

---

**文書終了**
