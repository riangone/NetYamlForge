# NetYamlForge 設定リファレンス

このドキュメントでは、NetYamlForge のすべての設定項目を解説します。

---

## 目次

1. [データベース設定](#データベース設定)
2. [AI アシスタント設定](#ai アシスタント設定)
3. [ホットリロード設定](#ホットリロード設定)
4. [ログ設定](#ログ設定)
5. [環境変数](#環境変数)

---

## データベース設定

### SQLite（デフォルト）

```json
{
  "DatabaseProvider": "sqlite",
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=chinook.db"
  }
}
```

### PostgreSQL

```json
{
  "DatabaseProvider": "postgresql",
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=netyamlforge;Username=postgres;Password=secret"
  }
}
```

### MySQL

```json
{
  "DatabaseProvider": "mysql",
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=netyamlforge;Uid=root;Pwd=secret;"
  }
}
```

### SQL Server

```json
{
  "DatabaseProvider": "sqlserver",
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=NetYamlForge;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

---

## AI アシスタント設定

### 基本設定

```json
{
  "AICli": {
    "DefaultTool": "ollama",
    "TaskTimeoutSeconds": 1800,
    "MaxConcurrentTasks": 2,
    "DefaultWorkingDirectory": "/path/to/project",
    "DefaultAllowedTools": ["Read", "Write", "Edit", "Bash", "Git"]
  }
}
```

| 設定 | 説明 | デフォルト |
|------|------|-----------|
| `DefaultTool` | デフォルトの AI ツール | `claude` |
| `TaskTimeoutSeconds` | タスクタイムアウト（秒） | `1800` |
| `MaxConcurrentTasks` | 最大同時実行数 | `2` |
| `DefaultWorkingDirectory` | デフォルト作業ディレクトリ | - |
| `DefaultAllowedTools` | 許可されるツールリスト | 上記参照 |

---

### Ollama（ローカル）

```json
{
  "AICli": {
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "Model": "qwen2.5-coder:7b",
      "UseApi": true,
      "ContextSize": 4096,
      "Temperature": 0.7,
      "Path": ""
    }
  }
}
```

| 設定 | 説明 | デフォルト |
|------|------|-----------|
| `BaseUrl` | Ollama API エンドポイント | `http://localhost:11434` |
| `Model` | 使用するモデル名 | `qwen2.5-coder` |
| `UseApi` | API モードを使用 | `true` |
| `ContextSize` | コンテキストサイズ | `4096` |
| `Temperature` | 温度パラメータ | `0.7` |
| `Path` | CLI コマンドパス | 自動検出 |

**詳細:** [ローカルモデル設定ガイド](guides/ai-local-model-setup.md)

---

### LM Studio（ローカル）

```json
{
  "AICli": {
    "LmStudio": {
      "BaseUrl": "http://localhost:1234",
      "Model": "qwen2.5-coder-7b-instruct",
      "ContextSize": 4096,
      "Temperature": 0.7
    }
  }
}
```

| 設定 | 説明 | デフォルト |
|------|------|-----------|
| `BaseUrl` | LM Studio API エンドポイント | `http://localhost:1234` |
| `Model` | モデル名 | - |
| `ContextSize` | コンテキストサイズ | `4096` |
| `Temperature` | 温度パラメータ | `0.7` |

---

### Claude Code

```json
{
  "AICli": {
    "Claude": {
      "ApiKey": "your-api-key",
      "Path": ""
    }
  }
}
```

| 設定 | 説明 |
|------|------|
| `ApiKey` | Anthropic API キー（ANTHROPIC_API_KEY） |
| `Path` | claude コマンドパス |

---

### Qwen Code

```json
{
  "AICli": {
    "QwenCode": {
      "ApiKey": "your-api-key",
      "BaseUrl": "",
      "Model": "qwen-coder-plus",
      "Path": ""
    }
  }
}
```

| 設定 | 説明 |
|------|------|
| `ApiKey` | DashScope API キー |
| `BaseUrl` | API エンドポイント（任意） |
| `Model` | モデル名 |
| `Path` | qwen-code コマンドパス |

---

### OpenAI Codex

```json
{
  "AICli": {
    "Codex": {
      "ApiKey": "your-openai-api-key",
      "BaseUrl": "",
      "Organization": "",
      "Model": "codex-latest",
      "Path": ""
    }
  }
}
```

| 設定 | 説明 |
|------|------|
| `ApiKey` | OpenAI API キー |
| `BaseUrl` | API エンドポイント（任意） |
| `Organization` | Organization ID（任意） |
| `Model` | モデル名 |
| `Path` | codex コマンドパス |

---

### Google Gemini

```json
{
  "AICli": {
    "Gemini": {
      "ApiKey": "your-google-api-key",
      "ProjectId": "",
      "DeveloperMode": false,
      "Model": "gemini-2.5-pro",
      "Path": ""
    }
  }
}
```

| 設定 | 説明 |
|------|------|
| `ApiKey` | Google API キー |
| `ProjectId` | Google Cloud Project ID |
| `DeveloperMode` | 開発者モード |
| `Model` | モデル名 |
| `Path` | gemini コマンドパス |

---

## ホットリロード設定

```json
{
  "HotReload": {
    "Enabled": true,
    "OnlyInDevelopment": true,
    "DebounceMs": 500
  }
}
```

| 設定 | 説明 | デフォルト |
|------|------|-----------|
| `Enabled` | ホットリロードを有効化 | `true` |
| `OnlyInDevelopment` | 開発環境でのみ有効 | `true` |
| `DebounceMs` | 変更検知の遅延（ms） | `500` |

---

## ログ設定（Serilog）

```json
{
  "Serilog": {
    "Using": ["Serilog.Sinks.Console", "Serilog.Sinks.File"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.AspNetCore": "Warning"
      }
    }
  }
}
```

---

## 環境変数

### データベース

```bash
# プロジェクト別データベース設定
export NYFORGE_<PROJECT>_DB_TYPE="postgresql"
export NYFORGE_<PROJECT>_CONNECTION_STRING="Host=...;Database=...;"
```

### AI

```bash
# Ollama
export NYFORGE_OLLAMA_BASE_URL="http://localhost:11434"
export NYFORGE_OLLAMA_MODEL="qwen2.5-coder:7b"

# LM Studio
export NYFORGE_LMSTUDIO_BASE_URL="http://localhost:1234"
export NYFORGE_LMSTUDIO_MODEL="qwen2.5-coder-7b"

# Claude
export ANTHROPIC_API_KEY="your-key"

# Qwen
export DASHSCOPE_API_KEY="your-key"
export DASHSCOPE_BASE_URL="https://dashscope.aliyuncs.com"

# OpenAI
export OPENAI_API_KEY="your-key"
export OPENAI_BASE_URL="https://api.openai.com/v1"
export OPENAI_ORG_ID="org-xxx"

# Google
export GOOGLE_API_KEY="your-key"
export GOOGLE_CLOUD_PROJECT="project-id"
```

---

## プロジェクト別設定上書き

`appsettings.<PROJECT>.json` ファイルを作成すると、プロジェクト固有の設定を適用できます。

例：`appsettings.todo-app.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=projects/todo-app/database.db"
  },
  "AICli": {
    "DefaultTool": "ollama",
    "Ollama": {
      "Model": "qwen2.5-coder:7b"
    }
  }
}
```

---

## 関連ドキュメント

- [ローカルモデル設定ガイド](guides/ai-local-model-setup.md)
- [AI アシスタントガイド](ai-assistant-guide.md)
- [ホットリロード説明](HOTRELOAD.md)
- [SQL Server 設定](sqlserver-setup.md)

---

*最終更新：2026 年 3 月*
