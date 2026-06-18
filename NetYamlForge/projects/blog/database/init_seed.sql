-- ============================================================
-- Blog System - SQLite Schema & Seed Data
-- ============================================================

-- entity alias: into category → BlogCategory (entity file category.yml maps to BlogCategory table)
-- カテゴリ
CREATE TABLE IF NOT EXISTS BlogCategory (
  Id          INTEGER PRIMARY KEY AUTOINCREMENT,
  Name        TEXT    NOT NULL,
  Slug        TEXT    NOT NULL UNIQUE,
  Description TEXT,
  Color       TEXT    NOT NULL DEFAULT '#6366f1',
  SortOrder   INTEGER NOT NULL DEFAULT 0,
  CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now'))
);

-- タグ
CREATE TABLE IF NOT EXISTS Tag (
  Id        INTEGER PRIMARY KEY AUTOINCREMENT,
  Name      TEXT NOT NULL UNIQUE,
  Slug      TEXT NOT NULL UNIQUE,
  CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
);

-- 記事
CREATE TABLE IF NOT EXISTS Post (
  Id          INTEGER PRIMARY KEY AUTOINCREMENT,
  Title       TEXT    NOT NULL,
  Slug        TEXT    NOT NULL UNIQUE,
  Summary     TEXT,
  Content     TEXT,
  CoverImage  TEXT,
  CategoryId  INTEGER,
  AuthorName  TEXT    NOT NULL DEFAULT 'Admin',
  Status      TEXT    NOT NULL DEFAULT 'draft',
  FeaturedFlag INTEGER NOT NULL DEFAULT 0,
  ViewCount   INTEGER NOT NULL DEFAULT 0,
  PublishedAt TEXT,
  CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now')),
  UpdatedAt   TEXT    NOT NULL DEFAULT (datetime('now')),
  FOREIGN KEY (CategoryId) REFERENCES BlogCategory(Id)
);

-- 記事-タグ中間テーブル
CREATE TABLE IF NOT EXISTS PostTag (
  PostId INTEGER NOT NULL,
  TagId  INTEGER NOT NULL,
  PRIMARY KEY (PostId, TagId),
  FOREIGN KEY (PostId) REFERENCES Post(Id),
  FOREIGN KEY (TagId)  REFERENCES Tag(Id)
);

-- コメント
CREATE TABLE IF NOT EXISTS Comment (
  Id          INTEGER PRIMARY KEY AUTOINCREMENT,
  PostId      INTEGER NOT NULL,
  AuthorName  TEXT    NOT NULL,
  AuthorEmail TEXT,
  Content     TEXT    NOT NULL,
  Status      TEXT    NOT NULL DEFAULT 'pending',
  ParentId    INTEGER,
  CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now')),
  FOREIGN KEY (PostId)   REFERENCES Post(Id),
  FOREIGN KEY (ParentId) REFERENCES Comment(Id)
);

-- ============================================================
-- インデックス
-- ============================================================
CREATE INDEX IF NOT EXISTS idx_post_status      ON Post(Status);
CREATE INDEX IF NOT EXISTS idx_post_category    ON Post(CategoryId);
CREATE INDEX IF NOT EXISTS idx_post_published   ON Post(PublishedAt);
CREATE INDEX IF NOT EXISTS idx_comment_post     ON Comment(PostId);
CREATE INDEX IF NOT EXISTS idx_comment_status   ON Comment(Status);
CREATE INDEX IF NOT EXISTS idx_posttag_post     ON PostTag(PostId);
CREATE INDEX IF NOT EXISTS idx_posttag_tag      ON PostTag(TagId);

-- ============================================================
-- シードデータ
-- ============================================================

INSERT OR IGNORE INTO BlogCategory (Id, Name, Slug, Description, Color, SortOrder) VALUES
  (1, '技術',       'tech',      '技術・プログラミング関連の記事',   '#6366f1', 1),
  (2, 'デザイン',   'design',    'UI/UX・デザイン関連の記事',       '#ec4899', 2),
  (3, 'ビジネス',   'business',  'ビジネス・経営関連の記事',         '#f59e0b', 3),
  (4, 'ライフスタイル', 'lifestyle', '生活・文化・趣味関連の記事',   '#10b981', 4),
  (5, 'ニュース',   'news',      '最新ニュース・お知らせ',           '#3b82f6', 5);

INSERT OR IGNORE INTO Tag (Id, Name, Slug) VALUES
  (1, 'Python',      'python'),
  (2, 'JavaScript',  'javascript'),
  (3, '.NET',        'dotnet'),
  (4, 'AI',          'ai'),
  (5, 'Web開発',     'web-dev'),
  (6, 'データベース','database'),
  (7, 'クラウド',    'cloud'),
  (8, 'セキュリティ','security'),
  (9, 'DevOps',      'devops'),
  (10, 'OSS',        'oss');

INSERT OR IGNORE INTO Post (Id, Title, Slug, Summary, Content, CategoryId, AuthorName, Status, FeaturedFlag, ViewCount, PublishedAt, CreatedAt, UpdatedAt) VALUES
  (1,
   'NetYamlForge 入門：YAML だけで業務アプリを構築する',
   'netyamlforge-getting-started',
   'NetYamlForge を使えば、YAML ファイルを書くだけでフル機能の業務アプリが完成します。本記事ではインストールからデモアプリ起動までを解説します。',
   '## はじめに

NetYamlForge は、YAML 設定ファイルと SQLite を組み合わせて業務アプリケーションを素早く構築するフレームワークです。

## インストール

```bash
dotnet new install NetYamlForge.Templates
dotnet new nyf-app -n MyApp
cd MyApp && dotnet run
```

## 最初の Entity を作る

`entities/product.yml` を作成し、以下を記述します：

```yaml
entities:
  product:
    table: Product
    key: Id
    displayName: 商品
    forms:
      Name:
        type: string
        required: true
      Price:
        type: money
```

これだけで CRUD 画面が自動生成されます。',
   1, 'Admin', 'published', 1, 1240,
   '2026-04-01 09:00:00', '2026-04-01 08:00:00', '2026-04-01 09:00:00'),

  (2,
   'SQLite で始める軽量データベース設計',
   'sqlite-lightweight-db-design',
   'SQLite はファイルベースの RDBMS で、プロトタイプから中規模アプリまで幅広く使えます。テーブル設計のベストプラクティスを紹介します。',
   '## SQLite の特徴

- ファイル 1 つで完結
- サーバー不要
- トランザクション対応

## テーブル設計のポイント

```sql
CREATE TABLE Product (
  Id        INTEGER PRIMARY KEY AUTOINCREMENT,
  Name      TEXT    NOT NULL,
  Price     REAL    NOT NULL DEFAULT 0,
  CreatedAt TEXT    NOT NULL DEFAULT (datetime(''now''))
);
```

PascalCase でテーブル名・カラム名を統一すると可読性が上がります。',
   1, 'Admin', 'published', 0, 870,
   '2026-04-05 10:00:00', '2026-04-05 09:00:00', '2026-04-05 10:00:00'),

  (3,
   'DaisyUI + Tailwind CSS でモダン UI を作る',
   'daisyui-tailwind-modern-ui',
   'DaisyUI のコンポーネントライブラリと Tailwind CSS を組み合わせると、最小限のコードで洗練された UI が構築できます。',
   '## DaisyUI とは

Tailwind CSS のプラグインとして動作するコンポーネントライブラリです。

## 基本的な使い方

```html
<button class="btn btn-primary">送信</button>
<div class="card bg-base-100 shadow-xl">
  <div class="card-body">
    <h2 class="card-title">タイトル</h2>
    <p>コンテンツ</p>
  </div>
</div>
```

カード、モーダル、テーブルなどが簡単に実装できます。',
   2, 'Admin', 'published', 1, 620,
   '2026-04-10 11:00:00', '2026-04-10 10:00:00', '2026-04-10 11:00:00'),

  (4,
   'AI 時代のプロダクト開発戦略',
   'ai-era-product-strategy',
   'ChatGPT や Claude などの大規模言語モデルが普及した現代において、プロダクト開発の戦略はどう変わるべきか考察します。',
   '## AI がもたらす変化

LLM の台頭により、以下の領域で大きな変化が起きています：

1. コード生成の自動化
2. ドキュメント作成の効率化
3. カスタマーサポートの自動化

## 開発チームへの影響

エンジニアは「何を作るか」の設計判断と「AI 出力の品質検証」に集中できるようになります。',
   3, 'Editor', 'published', 0, 430,
   '2026-04-15 09:00:00', '2026-04-15 08:00:00', '2026-04-15 09:00:00'),

  (5,
   'リモートワーク時代の集中力管理術',
   'remote-work-focus-management',
   'ホームオフィスでの集中力を維持するための実践的なテクニックを紹介。ポモドーロテクニックから環境整備まで。',
   '## 集中力を高める 5 つの習慣

1. **ポモドーロテクニック**: 25 分作業・5 分休憩を繰り返す
2. **通知のオフ**: Slack・メールは時間を決めて確認
3. **作業環境の整備**: デスク周りを整理する
4. **BGM の活用**: ローファイヒップホップなど集中しやすい音楽
5. **定期的な休憩**: 1 時間ごとに 5 分のストレッチ

## おすすめツール

- Forest（スマホの使いすぎ防止）
- Notion（タスク管理）
- Obsidian（ノート整理）',
   4, 'Editor', 'published', 0, 280,
   '2026-04-20 10:00:00', '2026-04-20 09:00:00', '2026-04-20 10:00:00'),

  (6,
   'クラウドネイティブ開発入門：Docker と Kubernetes',
   'cloud-native-docker-kubernetes',
   'コンテナ技術の基礎から Kubernetes によるオーケストレーションまでを体系的に解説します。',
   '## Docker の基礎

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MyApp.dll"]
```

## Kubernetes の概念

- **Pod**: コンテナの最小単位
- **Deployment**: Pod の管理・スケーリング
- **Service**: Pod へのネットワーク経路

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: my-app
spec:
  replicas: 3
```',
   1, 'Admin', 'published', 0, 510,
   '2026-04-25 09:00:00', '2026-04-25 08:00:00', '2026-04-25 09:00:00'),

  (7,
   'OSS コントリビューション完全ガイド',
   'oss-contribution-guide',
   '初めての OSS 貢献から PR マージまでの流れを、実際のワークフローに沿って丁寧に解説します。',
   '## なぜ OSS に貢献するのか

- スキルアップ
- ネットワーク構築
- ポートフォリオ強化

## ファーストステップ

1. Issue を探す（`good first issue` ラベル）
2. リポジトリを fork する
3. ブランチを切って修正
4. PR を作成・レビュー対応

## マナーとルール

- CoC（行動規範）を読む
- 小さな PR から始める
- レビューには迅速に対応する',
   1, 'Editor', 'draft', 0, 0,
   NULL, '2026-05-01 09:00:00', '2026-05-01 09:00:00'),

  (8,
   'セキュリティ強化：OWASP Top 10 対策ガイド',
   'owasp-top10-security-guide',
   'Web アプリケーションの代表的な脆弱性である OWASP Top 10 を理解し、具体的な対策を実装する方法を解説します。',
   '## OWASP Top 10（2021）

1. Broken Access Control
2. Cryptographic Failures
3. Injection
4. Insecure Design
5. Security Misconfiguration

## SQL インジェクション対策

```csharp
// Bad
var sql = $"SELECT * FROM User WHERE Name = ''{name}''";

// Good（パラメータ化クエリ）
var sql = "SELECT * FROM User WHERE Name = @name";
cmd.Parameters.AddWithValue("@name", name);
```',
   1, 'Admin', 'archived', 0, 190,
   '2026-03-15 09:00:00', '2026-03-15 08:00:00', '2026-03-15 09:00:00');

INSERT OR IGNORE INTO PostTag (PostId, TagId) VALUES
  (1, 3), (1, 5), (1, 6),
  (2, 6), (2, 3),
  (3, 2), (3, 5),
  (4, 4), (4, 5),
  (5, 5),
  (6, 7), (6, 9),
  (7, 10), (7, 5),
  (8, 8), (8, 5);

INSERT OR IGNORE INTO Comment (Id, PostId, AuthorName, AuthorEmail, Content, Status, ParentId, CreatedAt) VALUES
  (1,  1, '田中 太郎', 'tanaka@example.com',  'とても分かりやすい記事でした。早速試してみます！', 'approved', NULL, '2026-04-02 10:00:00'),
  (2,  1, '鈴木 花子', 'suzuki@example.com',  'YAML だけでここまでできるとは驚きです。', 'approved', NULL, '2026-04-03 11:00:00'),
  (3,  1, 'Admin',     'admin@example.com',   'ご質問があればお気軽にどうぞ！', 'approved', 1,    '2026-04-03 12:00:00'),
  (4,  2, '佐藤 一郎', 'sato@example.com',    'SQLite の使い所がよく理解できました。', 'approved', NULL, '2026-04-06 09:00:00'),
  (5,  3, '山田 美咲', 'yamada@example.com',  'DaisyUI は以前から気になっていました。試してみます。', 'approved', NULL, '2026-04-11 14:00:00'),
  (6,  3, '伊藤 健',   'ito@example.com',     'Tailwind との組み合わせが最高ですね。', 'approved', NULL, '2026-04-12 09:00:00'),
  (7,  4, '中村 由美', 'nakamura@example.com','AI 時代の開発戦略について考えさせられました。', 'approved', NULL, '2026-04-16 10:00:00'),
  (8,  1, 'スパムbot',  'spam@evil.com',       'Click here for free money!!!', 'spam', NULL, '2026-04-04 03:00:00'),
  (9,  6, '高橋 誠',   'takahashi@example.com','Kubernetes の解説がとても分かりやすかったです。', 'pending', NULL, '2026-04-26 15:00:00'),
  (10, 2, '渡辺 律子', 'watanabe@example.com', 'インデックスの設計についても記事にしてほしいです。', 'approved', NULL, '2026-04-07 11:00:00');
