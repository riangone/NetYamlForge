---
name: バッチジョブ生成
icon: ⏰
description: バッチジョブ（cron スケジュール）を生成
needsInput: true
inputPlaceholder: 例: todo-app --name=daily_report
order: 4
---

バッチジョブを生成してください。

```bash
dotnet run -- --scaffold-batch-job --project=<name> --name=<job_name>
```

ジョブ実装は `projects/<name>/Hooks/` に生成されます。
`jobs/*.yml` でスケジュール（cron式）を設定してください。

プロジェクト名とジョブ名を指定してください:
