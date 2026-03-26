---
name: エンティティ生成
icon: 📋
description: エンティティ YAML からコードを生成
needsInput: true
inputPlaceholder: プロジェクト名を入力...
order: 2
---

指定プロジェクトのエンティティをスキャフォールドしてください。

```bash
dotnet run -- --scaffold-entities --project=<name>
```

生成後に `dotnet build` でビルドエラーがないか確認してください。

プロジェクト名:
