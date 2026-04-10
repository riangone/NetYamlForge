# AI プロセスプール最適化ガイド

## 問題点

従来の実装では、**AI を呼び出すたびに新しい CLI プロセスを起動**していました：

```
ユーザーリクエスト
  → BaseCLIService.ExecuteAsync()
    → ProcessExecutor.ExecuteAsync()
      → CreateProcess() → 新 Process 实例
        → process.Start() → qwen/claude 起動（1-3秒）
          → リクエスト送信
          → 応答受信
          → プロセス終了
```

### パフォーマンス影響

- ❌ 毎回 1-3 秒の起動オーバーヘッド
- ❌ CLI の初期化コスト（モデルロード、セッション確立）
- ❌ 高コンカレンシー時に大量のプロセス起動
- ❌ キャッシュ/コンテキストの再利用不可

---

## 解決策：AI プロセスプール

データベース接続池のように、**CLI プロセスを再利用**する仕組みを実装しました。

### アーキテクチャ

```
┌─────────────────────────────────────────────────┐
│              PooledCLIService                   │
│          （デコレーターパターン）                │
├─────────────────────────────────────────────────┤
│  1. プロセスプールから取得                       │
│  2. リクエスト送信                               │
│  3. 応答受信                                     │
│  4. プロセスをプールに返却                       │
└─────────────────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────────┐
│           AIProcessPoolManager                  │
├─────────────────────────────────────────────────┤
│  - ConcurrentQueue<PersistentAIProcess>         │
│  - MaxPoolSize: 3                               │
│  - IdleTimeout: 10分                            │
│  - ヘルスチェック: 30秒ごと                     │
│  - 定期クリーンアップ                            │
└─────────────────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────────┐
│          PersistentAIProcess                    │
├─────────────────────────────────────────────────┤
│  - Process: qwen --daemon mode                  │
│  - Stdin/Stdout: 永続通信パイプ                │
│  - SessionId: コンテキスト保持                  │
│  - HealthCheck: 生存確認                        │
│  - RequestCount: 使用統計                       │
└─────────────────────────────────────────────────┘
```

---

## 設定方法

### appsettings.json

```json
{
  "AICli": {
    "DefaultTool": "qwen",
    "TaskTimeoutSeconds": 1800,
    "MaxConcurrentTasks": 2,
    
    "ProcessPool": {
      "EnableDaemonMode": true,
      "MaxPoolSize": 3,
      "IdleTimeoutMinutes": 10,
      "HealthCheckIntervalSeconds": 30,
      "MaxStartRetries": 3
    },
    
    "QwenCode": {
      "ApiKey": "your-api-key",
      "Model": "qwen-coder-plus"
    }
  }
}
```

### 設定項目説明

| 設定 | 説明 | デフォルト | 推奨値 |
|------|------|-----------|--------|
| `EnableDaemonMode` | デーモンモード有効/無効 | `true` | `true` |
| `MaxPoolSize` | プロバイダーごとの最大プールサイズ | `3` | `2-5` |
| `IdleTimeoutMinutes` | アイドルタイムアウト（分） | `10` | `5-15` |
| `HealthCheckIntervalSeconds` | ヘルスチェック間隔（秒） | `30` | `30-60` |
| `MaxStartRetries` | プロセス起動リトライ回数 | `3` | `3-5` |

---

## 性能比較

### 従来方式（毎回起動）

```
リクエスト 1: [起動 2s] [実行 3s] [終了 0.5s] = 5.5s
リクエスト 2: [起動 2s] [実行 3s] [終了 0.5s] = 5.5s
リクエスト 3: [起動 2s] [実行 3s] [終了 0.5s] = 5.5s
---------------------------------------------------
合計: 16.5s（平均 5.5s/リクエスト）
```

### プロセスプール方式

```
リクエスト 1: [起動 2s] [実行 3s] = 5s    ← 初回のみ起動
リクエスト 2: [取得 0.01s] [実行 3s] = 3.01s  ← プールから再利用
リクエスト 3: [取得 0.01s] [実行 3s] = 3.01s  ← プールから再利用
---------------------------------------------------
合計: 11.02s（平均 3.67s/リクエスト）
改善率: 33% 高速化
```

### 高コンカレンシー時（10 並列）

```
従来: 5.5s × 10 = 55s（プロセス起動×10）
プール: 5s + 3s × 9 = 32s（起動×1 + 再利用×9）
改善率: 42% 高速化
```

---

## 使い方

### 基本的な使い方

プロセスプールは**透過的**に動作します。既存のコードを変更する必要はありません：

```csharp
// 既存のコードそのまま
var cliService = serviceProvider.GetRequiredService<ICLIService>();
var response = await cliService.ExecuteAsync("こんにちは");
```

内部的に `PooledCLIService` がプロセスプールからプロセスを取得・再利用します。

### プール統計情報の取得

```csharp
var poolManager = serviceProvider.GetRequiredService<AIProcessPoolManager>();
var stats = poolManager.GetPoolStats();

// 出力例:
// {
//   "qwen": {
//     "PoolSize": 2,
//     "HealthyCount": 2,
//     "TotalRequests": 15
//   }
// }
```

### プールのクリア

```csharp
// 特定プロバイダーのプールをクリア
poolManager.ClearPool("qwen");
```

---

## チューニングガイド

### シナリオ別設定

#### 1. 開発環境（低頻度利用）

```json
{
  "ProcessPool": {
    "EnableDaemonMode": true,
    "MaxPoolSize": 1,
    "IdleTimeoutMinutes": 5
  }
}
```

- 最小限のリソース使用
- 初回起動後のみ効果あり

#### 2. 本番環境（中頻度利用）

```json
{
  "ProcessPool": {
    "EnableDaemonMode": true,
    "MaxPoolSize": 3,
    "IdleTimeoutMinutes": 10
  }
}
```

- バランスの取れた設定
- ほとんどのユースケースに最適

#### 3. 高負荷環境（高頻度利用）

```json
{
  "ProcessPool": {
    "EnableDaemonMode": true,
    "MaxPoolSize": 5,
    "IdleTimeoutMinutes": 15,
    "HealthCheckIntervalSeconds": 20
  }
}
```

- 最大限の再利用率
- メモリ使用量は増加

#### 4. 無効化（従来方式）

```json
{
  "ProcessPool": {
    "EnableDaemonMode": false
  }
}
```

- 毎回新規起動
- デバッグ時に有用

---

## 制限事項

### 1. ストリーミング実行

ストリーミング実行（`ExecuteStreamingAsync`）は**従来方式**を使用します。

理由:
- ストリーミングは長時間実行される
- プロセスを長時間占有するとプールの効率が低下
- 実装の複雑さが増す

### 2. デーモンモードの対応

現在の実装では、CLI ツールが**デーモンモード**（`--daemon` フラグ）に対応している必要があります。

**対応ツール:**
- ✅ Qwen Code（`--daemon` フラグあり）
- ✅ Claude Code（`--daemon` フラグあり）
- ❌ Ollama（API モード使用）
- ❌ LM Studio（API モード使用）

**未対応ツール**は従来通り毎回起動します。

### 3. セッション管理

永続プロセスはセッション ID を保持します。異なるセッションで再利用すると、コンテキストが混在する可能性があります。

**対策:**
- セッション終了時に `ClearPool()` を呼び出す
- または `IdleTimeoutMinutes` を短く設定

---

## トラブルシューティング

### Q: プロセスプールが効いていない

**確認事項:**

1. `EnableDaemonMode` が `true` か確認
```json
"ProcessPool": { "EnableDaemonMode": true }
```

2. ログを確認
```
[INFO] AI プロセスプール初期化: MaxPoolSize=3, IdleTimeout=10分
[DEBUG] プールからプロセス再利用: qwen (PID=12345, 使用回数=5)
```

3. 統計情報を確認
```csharp
var stats = poolManager.GetPoolStats();
Console.WriteLine($"PoolSize: {stats["qwen"].PoolSize}");
```

### Q: プロセスがハングする

**解決策:**

1. ヘルスチェック間隔を短く
```json
"HealthCheckIntervalSeconds": 15
```

2. アイドルタイムアウトを短く
```json
"IdleTimeoutMinutes": 5
```

3. プールをクリア
```csharp
poolManager.ClearPool("qwen");
```

### Q: メモリ使用量が増加

**解決策:**

1. `MaxPoolSize` を減らす
```json
"MaxPoolSize": 1
```

2. `IdleTimeoutMinutes` を減らす
```json
"IdleTimeoutMinutes": 3
```

---

## パフォーマンスモニタリング

### Application Insights 統合

```csharp
// プール統計を Application Insights に送信
var metrics = new Dictionary<string, double>();
foreach (var kvp in poolManager.GetPoolStats())
{
    metrics[$"ai.pool.{kvp.Key}.size"] = kvp.Value.PoolSize;
    metrics[$"ai.pool.{kvp.Key}.healthy"] = kvp.Value.HealthyCount;
    metrics[$"ai.pool.{kvp.Key}.requests"] = kvp.Value.TotalRequests;
}

telemetryClient.TrackMetrics(metrics);
```

### Prometheus メトリクス

```csharp
// カスタムメトリクス
var poolSizeGauge = Metrics.CreateGauge(
    "ai_process_pool_size",
    "Number of processes in the pool",
    new[] { "provider" });

var requestCounter = Metrics.CreateCounter(
    "ai_process_pool_requests_total",
    "Total number of requests via pooled processes",
    new[] { "provider" });
```

---

## 今後の改善案

### 1. 真のデーモンモード実装

現在の実装はスケルトンです。CLI ツールの実際のデーモンモードに対応するには：

```csharp
// Qwen Code の場合
protected override string GetDaemonArguments()
{
    return "--daemon --output-format json --interactive";
}
```

### 2. gRPC 通信

stdin/stdout ではなく gRPC を使用：

```
┌─────────────┐      gRPC       ┌─────────────┐
│   Web App   │ ←─────────────→ │  AI Daemon  │
└─────────────┘                 └─────────────┘
```

利点:
- バイナリプロトコル（高速）
- ストリーミング対応
- エラーハンドリング強化

### 3. 分散プロセスプール

複数サーバーでプールを共有：

```
┌─────────┐    ┌─────────┐    ┌─────────┐
│ Web 1   │    │ Web 2   │    │ Web 3   │
└────┬────┘    └────┬────┘    └────┬────┘
     │              │              │
     └──────────────┼──────────────┘
                    ↓
          ┌─────────────────┐
          │  AI Process     │
          │  Pool Server    │
          └─────────────────┘
```

### 4. 予測的プロセス起動

リクエストを予測して事前に起動：

```csharp
// 前回のアクセスパターンから学習
if (IsPeakHour(DateTime.Now))
{
    // プールを事前に拡張
    await PreWarmPoolAsync("qwen", targetSize: 5);
}
```

---

## まとめ

| 項目 | 従来 | プロセスプール |
|------|------|---------------|
| 初回応答 | 5-6秒 | 5-6秒 |
| 2回目以降 | 5-6秒 | 3-4秒 |
| メモリ使用 | 最小 | 中（プロセス保持） |
| 高負荷耐性 | 弱い | 強い |
| 実装複雑さ | 低 | 中 |

**推奨:**
- 開発環境: 有効（`MaxPoolSize: 1`）
- 本番環境: 有効（`MaxPoolSize: 3`）
- 高負荷環境: 有効（`MaxPoolSize: 5`）

---

*最終更新: 2026年4月10日*
