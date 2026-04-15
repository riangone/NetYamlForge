# Docker 環境構築ガイド

NetYamlForge は Docker Compose のプロファイル機能でデータベースを選択できます。

## クイックスタート（SQLite — デフォルト）

```bash
cp .env.example .env
docker compose up -d
```

ブラウザで http://localhost:8080 にアクセス。
ログイン: `admin` / `Admin@123`

---

## データベースの選択

### SQLite（デフォルト・追加設定不要）

```bash
cp .env.example .env
docker compose up -d
```

- todo-app・ui-showcase のデモデータがイメージに含まれています
- データは Docker ボリューム `todo_app_sqlite` / `ui_showcase_sqlite` に永続化されます

### PostgreSQL

```bash
cp .env.example .env          # デフォルト設定をコピー
# 必要に応じて .env のパスワード等を編集

docker compose --profile postgres up -d
```

アクセス: http://localhost:8080

PostgreSQL の接続情報:
```
Host:     localhost
Port:     5432
Database: todo_app
User:     nyforge
Password: nyforge_pass  (または .env の POSTGRES_PASSWORD)
```

### MySQL

```bash
cp .env.example .env

docker compose --profile mysql up -d
```

MySQL の接続情報:
```
Host:     localhost
Port:     3306
Database: todo_app
User:     nyforge
Password: nyforge_pass  (または .env の MYSQL_PASSWORD)
```

### SQL Server 2022

```bash
cp .env.example .env

docker compose --profile sqlserver up -d
```

> SQL Server Express は無償ライセンスです。本番用途では Standard/Enterprise への変更を検討してください（`MSSQL_PID` 環境変数）。

SQL Server の接続情報:
```
Server:   localhost,1433
Database: todo_app
User:     nyforge
Password: Nyforge@123  (または .env の MSSQL_PASSWORD)
```

---

## 環境変数

`.env` ファイルで全設定をカスタマイズできます。

| 変数 | デフォルト | 説明 |
|------|-----------|------|
| `APP_PORT` | `8080` | アプリのホストポート |
| `ASPNETCORE_ENVIRONMENT` | `Production` | ASP.NET Core 環境 |
| `TZ` | `Asia/Tokyo` | タイムゾーン |
| `NYFORGE_TODO_APP_DB_TYPE` | `sqlite` | todo-app のDB種別 |
| `NYFORGE_TODO_APP_CONNECTION_STRING` | *(空=SQLite自動)* | todo-app の接続文字列 |
| `POSTGRES_PASSWORD` | `nyforge_pass` | PostgreSQL パスワード |
| `MYSQL_PASSWORD` | `nyforge_pass` | MySQL パスワード |
| `MSSQL_PASSWORD` | `Nyforge@123` | SQL Server ユーザーパスワード |
| `MSSQL_SA_PASSWORD` | `Sa@Nyforge123` | SQL Server SA パスワード |

### プロジェクト別のDB設定（環境変数）

新しいプロジェクトを外部DBで使用する場合：

```bash
# 形式: NYFORGE_{プロジェクト名（大文字・ハイフン→アンダースコア）}_DB_TYPE
# 形式: NYFORGE_{プロジェクト名}_CONNECTION_STRING

NYFORGE_MY_PROJECT_DB_TYPE=postgresql
NYFORGE_MY_PROJECT_CONNECTION_STRING=Host=mydb;Database=myproject;Username=user;Password=pass
```

---

## データの初期化

### SQLite
- イメージに含まれるシードDB（`/app/data/seeds/*.db`）が、初回起動時にボリュームにコピーされます
- ボリュームを削除すると次回起動時に再シードされます

### PostgreSQL / MySQL
- `docker/postgres/init/` または `docker/mysql/init/` の SQL が、DBコンテナ初回起動時に自動実行されます
- 含まれるデータ：Category (8件)、Project (6件)、Task (38件)、Comment (20件)

### SQL Server
- `docker/sqlserver/init/01_schema.sql` がコンテナ起動時に実行されます
- 同上のデータが投入されます

---

## ボリューム管理

```bash
# 全データを削除してリセット
docker compose down -v

# SQLite データのみリセット
docker volume rm netyamlforge_todo_app_sqlite

# バックアップ（SQLite）
docker run --rm \
  -v netyamlforge_todo_app_sqlite:/data \
  -v $(pwd)/backup:/backup \
  alpine cp /data/todo-app.db /backup/todo-app-$(date +%Y%m%d).db
```

---

## イメージのビルド

```bash
# 通常ビルド
docker compose build

# キャッシュ無効でリビルド
docker compose build --no-cache

# イメージのみビルド（起動しない）
docker build -t netyamlforge:latest .
```

---

## ログの確認

```bash
# アプリログ（リアルタイム）
docker compose logs -f app

# 全サービス
docker compose logs -f

# ボリューム内のログファイル
docker run --rm -v netyamlforge_app_logs:/logs alpine ls -la /logs
```

---

## トラブルシューティング

### アプリが起動しない
```bash
docker compose logs app
```

### DBに接続できない
```bash
# PostgreSQL ヘルスチェック確認
docker compose ps postgres
docker compose logs postgres

# MySQL
docker compose ps mysql
docker compose logs mysql
```

### ポート競合
`.env` の `APP_PORT` を変更してください（例: `APP_PORT=9090`）

### SQL Server がタイムアウト
SQL Server は起動に時間がかかります（20-30秒）。`depends_on: condition: service_healthy` が自動で待機します。

---

## ディレクトリ構造

```
/
├── Dockerfile                      # マルチステージビルド
├── docker-compose.yml              # 全サービス定義（プロファイル切替）
├── .env.example                    # 設定テンプレート（コピーして .env を作成）
├── DOCKER.md                       # このファイル
└── docker/
    ├── entrypoint.sh               # コンテナ起動スクリプト（DBシード初期化）
    ├── postgres/init/
    │   └── 01_schema.sql           # PostgreSQL テーブル作成 + シードデータ
    ├── mysql/init/
    │   └── 01_schema.sql           # MySQL テーブル作成 + シードデータ
    └── sqlserver/init/
        └── 01_schema.sql           # SQL Server テーブル作成 + シードデータ
```
