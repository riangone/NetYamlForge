---
name: フック生成
icon: 🔗
description: カスタムフッククラスを生成
needsInput: true
inputPlaceholder: 例: MyValidationHook --project=myapp
order: 3
---

カスタムフック（BeforeAsync / AfterAsync）を生成してください。

```bash
dotnet run -- --scaffold-hook --name=<HookName> --project=<name> [--with-tests]
```

フック名とプロジェクト名を指定してください（例: `OrderValidationHook --project=todo-app`）:
