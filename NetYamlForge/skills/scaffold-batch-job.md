---
name: バッチジョブ生成
icon: ⏰
description: cron スケジュールで動くバッチジョブを生成
needsInput: true
inputPlaceholder: 例: todo-app --name=daily_report
order: 4
---

バッチジョブの雛形を生成してください。

## コマンド

```bash
dotnet run -- --scaffold-batch-job --project=<name> --name=<job_name>
```

## 生成されるファイル

| ファイル | 説明 |
|---|---|
| `projects/<name>/jobs/<job_name>.yml` | ジョブ定義（cron 式・タイプ・リトライ設定） |
| `projects/<name>/jobs/sql/<job_name>.sql` | SQL テンプレート（`sql_to_csv` タイプ用） |
| `projects/<name>/Hooks/<job_name>_BeforeHook.cs` | 実行前フック（前処理・バリデーション） |

## 生成後の設定例（`jobs/<job_name>.yml`）

```yaml
name: <job_name>
display_name: "ジョブ表示名"
enabled: true
schedule: "0 2 * * *"   # 毎日 02:00
type: sql_to_csv         # または custom_handler
settings:
  sql_file: sql/<job_name>.sql
  output_dir: jobs/output
  filename_pattern: "result_{date}.csv"
retry:
  max_attempts: 3
  delay_seconds: 60
```

ジョブタイプ: `sql_to_csv`（SQL 結果を CSV 出力）または `custom_handler`（C# 実装）

プロジェクト名とジョブ名を指定してください:
