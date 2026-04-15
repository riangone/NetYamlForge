# バッチジョブ機能ガイド

NetYamlForge のバッチジョブ機能では、YAML 設定ファイルで定義した定期実行タスクを実行できます。

## 概要

バッチジョブ機能は以下の用途に適しています：

- 夜間の統計データ集計と CSV 出力
- 定期データ同期
- クリーンアップタスク
- 通知バッチ

## クイックスタート

### 1. バッチジョブの作成

CLI コマンドでテンプレートを生成：

```bash
dotnet run --project NetYamlForge -- --scaffold-batch-job \
  --project=shop \
  --name=nightly_stats
```

### 2. YAML 設定の編集

`projects/shop/jobs/nightly_stats.yml` を編集：

```yaml
jobs:
  nightly_stats:
    displayName: 夜间统计作业
    schedule:
      cron: "0 2 * * *"  # 毎日 2:00
      timezone: "Asia/Tokyo"
    type: sql_to_csv
    settings:
      sqlFile: jobs/sql/nightly_stats.sql
      outputFile: jobs/output/stats_{date:yyyyMMdd}.csv
      includeHeader: true
```

### 3. SQL クエリの作成

`projects/shop/jobs/sql/nightly_stats.sql` に集計クエリを記述：

```sql
SELECT 
    DATE('now') AS stat_date,
    COUNT(*) AS total_count,
    SUM(amount) AS total_amount
FROM orders
WHERE order_date >= DATE('now', '-1 day')
GROUP BY DATE('now');
```

### 4. アプリケーション再起動

```bash
dotnet run --project NetYamlForge
```

## YAML 設定リファレンス

### 基本構造

```yaml
jobs:
  <job_id>:
    displayName: <表示名>
    description: <説明>
    enabled: true|false
    
    schedule:
      cron: "<cron 式>"
      timezone: "<タイムゾーン>"
      # または
      intervalSeconds: <秒数>
    
    type: sql_to_csv|sql_command|stored_procedure
    
    settings:
      # sql_to_csv / sql_command の場合
      sqlFile: <SQL ファイルパス>
      # または
      sqlQuery: <直接 SQL>
      
      # sql_to_csv の場合のみ
      outputFile: <出力ファイルパス>
      includeHeader: true|false
      delimiter: ","
      outputFormat: csv|json|xml
    
    beforeRun:
      - <フック名>
    
    afterRun:
      - <フック名>
    
    onFailure:
      retryCount: <リトライ回数>
      retryInterval: <リトライ間隔（秒）>
      logError: true|false
      notify:
        - <通知先>
```

### スケジュール設定

#### Cron 式

Cron 式は 5 つのフィールドで構成されます：

```
分 時 日 月 曜
```

**例：**

| Cron 式 | 意味 |
|---------|------|
| `0 2 * * *` | 毎日 2:00 |
| `0 0 * * 0` | 毎週日曜日 0:00 |
| `0 */6 * * *` | 6 時間ごと |
| `30 8 * * 1-5` | 平日 8:30 |
| `0 0 1 * *` | 毎月 1 日 0:00 |

**特殊文字：**

- `*` - 全ての値
- `,` - 列挙（例：`1,3,5`）
- `-` - 範囲（例：`1-5`）
- `/` - ステップ（例：`*/15` は 15 分ごと）

#### タイムゾーン

主なタイムゾーン：

- `UTC` - 協定世界時
- `Asia/Tokyo` - 日本標準時
- `America/New_York` - 東部標準時
- `Europe/London` - グリニッジ標準時

### ジョブタイプ

#### sql_to_csv

SQL クエリの実行結果を CSV ファイルに出力します。

```yaml
type: sql_to_csv
settings:
  sqlFile: jobs/sql/report.sql
  outputFile: jobs/output/report_{date:yyyyMMdd}.csv
  includeHeader: true
  delimiter: ","
```

#### sql_command

SQL コマンド（INSERT/UPDATE/DELETE など）を実行します。

```yaml
type: sql_command
settings:
  sqlFile: jobs/sql/cleanup.sql
```

### 出力ファイルパスのプレースホルダー

出力ファイルパスでは以下の変数が使用できます：

- `{date:yyyyMMdd}` - 日付（例：`20250115`）
- `{date:yyyy-MM-dd}` - 日付（例：`2025-01-15`）
- `{datetime:yyyyMMdd_HHmmss}` - 日時（例：`20250115_143022`）

## フック連携

バッチジョブでは実行前後にフックを実行できます：

```yaml
beforeRun:
  - check_data_ready

afterRun:
  - send_notification
```

### フックの例

```csharp
using System.Data;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.Hooks;

/// <summary>
/// バッチジョブ実行前チェック
/// </summary>
public class CheckDataReadyHook : IEntityHook
{
    public Task<HookResult> BeforeAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        // データ準備チェック
        var count = db.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM orders WHERE processed = 0", 
            transaction: tx);
        
        if (count == 0)
        {
            return Task.FromResult(HookResult.Cancel("処理対象データがありません"));
        }
        
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
```

## 失敗時のリトライ

```yaml
onFailure:
  retryCount: 3
  retryInterval: 300  # 5 分
  logError: true
```

## 実行履歴

バッチジョブの実行履歴はインメモリで保存されます（実運用では DB 保存を実装予定）。

## 监控とログ

バッチジョブの実行ログは Serilog によって記録されます：

- コンソール出力
- `logs/app-YYYYMMDD.log` ファイル

**ログ例：**

```
[INF] ジョブ実行開始：nightly_stats (試行 1/3)
[INF] SQL->CSV ジョブ完了：nightly_stats, Rows: 150, File: .../stats_20250115.csv
[INF] ジョブ成功：nightly_stats, Duration: 234ms, Rows: 150
```

## トラブルシューティング

### ジョブが実行されない

1. `enabled: true` を確認
2. Cron 式の形式を確認
3. タイムゾーン設定を確認
4. アプリケーションログを確認

### SQL エラー

1. SQL ファイルのパスを確認
2. SQL 構文を確認
3. 対象テーブルの存在を確認

### CSV が出力されない

1. 出力ディレクトリの存在と権限を確認
2. `outputFile` パスを確認
3. ログファイルでエラーメッセージを確認

## 制限事項

- 現在、インメモリ履歴ストアはアプリ再起動でクリアされます
- メール通知機能は未実装（TODO）
- ストアドプロシージャタイプは未実装（TODO）

## Windows サービスとしての実行

バッチジョブを定期的に実行するには、アプリケーションをバックグラウンドで実行する必要があります。
Windows では**Windows サービス**として実行することを推奨します。

### 事前準備

1.  アプリケーションを公開ディレクトリに配置

```powershell
dotnet publish -c Release -o C:\apps\NetYamlForge
```

2.  `scripts` ディレクトリからインストールスクリプトをコピー

```powershell
# 公開ディレクトリにスクリプトをコピー
Copy-Item scripts\install-windows-service.bat C:\apps\NetYamlForge\
Copy-Item scripts\WindowsServiceInstaller.ps1 C:\apps\NetYamlForge\
```

### 方法 1: PowerShell スクリプトを使用（推奨）

```powershell
# 管理者として PowerShell を実行
cd C:\apps\NetYamlForge
.\WindowsServiceInstaller.ps1

# アンインストールする場合
.\WindowsServiceInstaller.ps1 -Uninstall
```

### 方法 2: バッチファイルを使用

```cmd
REM 管理者としてコマンドプロンプトを実行
cd C:\apps\NetYamlForge
install-windows-service.bat

REM アンインストールする場合
uninstall-windows-service.bat
```

### 方法 3: 手動で設定

```powershell
# サービス作成
sc create NetYamlForge binPath= "C:\apps\NetYamlForge\NetYamlForge.exe --run-as-service" start= auto

# サービス説明設定
sc description NetYamlForge "NetYamlForge Web Application"

# サービス開始
net start NetYamlForge
```

### サービス操作コマンド

```powershell
# サービス一覧確認
Get-Service NetYamlForge

# サービス開始
Start-Service NetYamlForge
# または
net start NetYamlForge

# サービス停止
Stop-Service NetYamlForge
# または
net stop NetYamlForge

# サービス削除
sc delete NetYamlForge
```

### ログの確認

Windows イベントログ:
```powershell
# イベントログを表示
Get-EventLog -LogName Application -Source .NETRuntime | Select-Object -First 20
```

アプリケーションログ:
```
C:\apps\NetYamlForge\logs\app-YYYYMMDD.log
```

### Linux/macOS での実行

Linux/macOS では、systemd サービスまたは supervisor として設定します。

**systemd サービス例** (`/etc/systemd/system/netyamlforge.service`):

```ini
[Unit]
Description=NetYamlForge Web Application
After=network.target

[Service]
Type=notify
User=www-data
WorkingDirectory=/var/www/NetYamlForge
ExecStart=/usr/bin/dotnet /var/www/NetYamlForge/NetYamlForge.dll
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

```bash
# サービス有効化
sudo systemctl enable netyamlforge
sudo systemctl start netyamlforge

# 状態確認
sudo systemctl status netyamlforge
```

## ベストプラクティス

1. **冪等性の確保**: ジョブは複数回実行されても問題ないように設計
2. **適切なエラーハンドリング**: フックで事前チェックを実装
3. **ログの活用**: 重要な処理ではログ出力を実装
4. **リトライポリシー**: 一時的なエラーに備えてリトライを設定
5. **监控**: 重要なジョブは実行結果を监控

## 関連ドキュメント

- [フックシステムガイド](hooks.md)
- [YAML 設定リファレンス](yaml-config.md)
