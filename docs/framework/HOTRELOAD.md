# YAML ホットリロード機能

NetYamlForge の YAML ホットリロード機能は、開発中に YAML ファイルの変更を自動的に検知し、キャッシュをリロードします。

## 概要

この機能は以下のコンポーネントで構成されます：

- **IYamlFileWatcher**: `FileSystemWatcher` を使用して YAML ファイルの変更を監視
- **ProjectYamlCacheManager**: 各種 YAML キャッシュを管理
- **YamlHotReloadService**: 背景で動作する IHostedService
- **HotReloadController**: キャッシュ状態を管理する API エンドポイント

## 設定

`appsettings.json` に以下の設定を追加：

```json
{
  "HotReload": {
    "Enabled": true,
    "OnlyInDevelopment": true,
    "DebounceMs": 500
  }
}
```

### 設定オプション

| オプション | 型 | デフォルト | 説明 |
|-----------|-----|-----------|------|
| `Enabled` | bool | `true` | ホットリロード機能を有効/無効 |
| `OnlyInDevelopment` | bool | `true` | 開発環境でのみ有効にする |
| `DebounceMs` | int | `500` | ファイル変更検知のディバウンス時間（ms） |

## 監視対象

ホットリロードサービスは以下のディレクトリを監視します：

- `projects/{projectName}/**/*.yml`
- `projects/{projectName}/**/*.yaml`

### 自動リロード対象

ファイルの場所に応じて、以下のキャッシュが自動的にリロードされます：

- **`/entities/`**: エンティティ定義キャッシュ
- **`dashboard.yml`**: ダッシュボード設定キャッシュ
- **`/pages/`**: カスタムページキャッシュ
- **`/config/`**, **`project.yaml`**: プロジェクト全体設定キャッシュ

## API エンドポイント

### キャッシュ状態の取得

```http
GET /api/hotreload/status
```

レスポンス例：
```json
{
  "enabled": true,
  "onlyInDevelopment": true,
  "isDevelopment": true,
  "caches": {
    "test_entities": {
      "key": "test_entities",
      "lastModified": "2026-03-26T12:34:56Z",
      "isValid": true
    }
  },
  "timestamp": "2026-03-26T12:34:56Z"
}
```

### プロジェクトキャッシュのリロード

```http
POST /api/hotreload/reload/{projectName}
```

### 全キャッシュのクリア

```http
POST /api/hotreload/clear-all
```

## 使用例

### 開発中のワークフロー

1. アプリケーションを実行
2. YAML ファイルを編集（例：`entities/customer.yml`）
3. ファイルを保存すると自動的にキャッシュがリロード
4. ブラウザをリフレッシュして変更を確認

### 手動リロード

API エンドポイントを使用して手動でリロード：

```bash
curl -X POST http://localhost:5000/api/hotreload/reload/test \
  -H "Authorization: Bearer {token}"
```

## 制限事項

- **本番環境**: デフォルトでは `ASPNETCORE_ENVIRONMENT=Development` の場合のみ有効
- **パフォーマンス**: 多数の YAML ファイルがある場合、初回リロードに時間がかかる可能性
- **ファイルロック**: 編集中のファイルがロックされている場合、リロードに失敗する可能性

## トラブルシューティング

### キャッシュがリロードされない

1. ログファイルで `YAML ファイル変更を検知` メッセージを確認
2. `HotReload.Enabled` が `true` か確認
3. ファイルパスが `projects/` ディレクトリ下にあるか確認

### エラーが発生した場合

ログに以下のメッセージが出力される：
- `YAML ホットリロード処理中にエラーが発生`

詳細なスタックトレースはアプリケーションログを確認。

## 今後の拡張

- [ ] WebSocket によるリアルタイム変更通知
- [ ] 特定ファイルタイプのフィルタリング
- [ ] リロード履歴の永続化
- [ ] UI でのキャッシュ状態表示
