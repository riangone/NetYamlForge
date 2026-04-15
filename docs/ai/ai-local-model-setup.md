# 本地模型配置指南

本文档介绍如何在 NetYamlForge 中配置和使用本地 AI 模型。

## 支持的本地模型平台

| 平台 | 描述 | 推荐模型 |
|------|------|----------|
| **Ollama** | 轻量级本地模型运行工具 | qwen2.5-coder, llama3.2, deepseek-coder |
| **LM Studio** | 带 GUI 的本地模型工具 | 各种 GGUF 格式模型 |

---

## Ollama 配置

### 1. 安装 Ollama

**Linux:**
```bash
curl -fsSL https://ollama.com/install.sh | sh
```

**macOS:**
```bash
brew install ollama
```

**Windows:**
从 [ollama.com](https://ollama.com) 下载安装程序

### 2. 下载模型

```bash
# 推荐用于编程的模型
ollama pull qwen2.5-coder:7b
ollama pull qwen2.5-coder:14b
ollama pull deepseek-coder:6.7b

# 通用模型
ollama pull llama3.2:3b
ollama pull llama3.2:7b
ollama pull mistral:7b
```

### 3. 启动服务

```bash
# 后台启动 Ollama 服务
ollama serve

# 验证服务运行
curl http://localhost:11434/api/tags
```

### 4. 配置 NetYamlForge

编辑 `appsettings.json`：

```json
{
  "AICli": {
    "DefaultTool": "ollama",
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

### 配置项说明

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `BaseUrl` | Ollama API 地址 | `http://localhost:11434` |
| `Model` | 使用的模型名称 | `qwen2.5-coder` |
| `UseApi` | 使用 API 模式（true）或 CLI 模式（false） | `true` |
| `ContextSize` | 上下文窗口大小 | `4096` |
| `Temperature` | 温度参数（0.1-1.0） | `0.7` |
| `Path` | ollama 命令路径（CLI 模式使用） | 自动检测 |

---

## LM Studio 配置

### 1. 安装 LM Studio

1. 访问 [lmstudio.ai](https://lmstudio.ai/)
2. 下载并安装对应系统的版本
3. 启动 LM Studio

### 2. 下载模型

1. 点击左侧 🔍 搜索图标
2. 搜索想要的模型（如 `Qwen2.5-Coder`, `Llama-3.2`）
3. 选择合适的量化版本（推荐 Q4_K_M 或 Q5_K_M）
4. 点击下载

### 3. 启动本地服务器

1. 点击左侧 ↔️ 图标（Local Server）
2. 选择已下载的模型
3. 点击 **Start Server**
4. 默认端口：`1234`

### 4. 验证服务

```bash
curl http://localhost:1234/v1/models
```

### 5. 配置 NetYamlForge

编辑 `appsettings.json`：

```json
{
  "AICli": {
    "DefaultTool": "lmstudio",
    "LmStudio": {
      "BaseUrl": "http://localhost:1234",
      "Model": "qwen2.5-coder-7b-instruct",
      "ContextSize": 4096,
      "Temperature": 0.7
    }
  }
}
```

### 配置项说明

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `BaseUrl` | LM Studio API 地址 | `http://localhost:1234` |
| `Model` | 模型名称（需与 LM Studio 中一致） | 空 |
| `ContextSize` | 上下文窗口大小 | `4096` |
| `Temperature` | 温度参数（0.1-1.0） | `0.7` |

---

## 在 AI 聊天窗口中使用

1. 启动 NetYamlForge 应用
2. 点击右上角 AI 助手按钮 💬
3. 在下拉菜单中选择：
   - **Ollama (本地模型)** - 使用 Ollama
   - **LM Studio (本地)** - 使用 LM Studio
4. 输入指令并发送

---

## 推荐模型配置

### 编程辅助（推荐）

| 模型 | 显存需求 | 速度 | 质量 |
|------|----------|------|------|
| `qwen2.5-coder:7b` | 8GB | 快 | 优秀 |
| `qwen2.5-coder:14b` | 16GB | 中 | 最佳 |
| `deepseek-coder:6.7b` | 8GB | 快 | 良好 |

### 通用任务

| 模型 | 显存需求 | 特点 |
|------|----------|------|
| `llama3.2:3b` | 4GB | 超快，适合简单任务 |
| `llama3.2:7b` | 8GB | 平衡速度与质量 |
| `mistral:7b` | 8GB | 优秀的通用模型 |

---

## 故障排查

### Ollama 无法连接

```bash
# 检查服务状态
systemctl status ollama

# 重启服务
ollama serve

# 检查端口
netstat -tlnp | grep 11434
```

### LM Studio 连接失败

1. 确认 Local Server 已启动
2. 检查端口是否为 1234
3. 确认模型已加载
4. 检查防火墙设置

### 模型响应慢

- 减小 `ContextSize`（如 2048）
- 使用更小的模型（3b-7b）
- 降低量化等级（Q4_K_S）

### 模型质量不佳

- 增加 `Temperature` 到 0.8-0.9（更有创意）
- 降低 `Temperature` 到 0.3-0.5（更准确）
- 尝试更大的模型（14b-70b）

---

## 环境变量配置（可选）

也可以使用环境变量配置：

```bash
# Ollama
export NYFORGE_OLLAMA_BASE_URL="http://localhost:11434"
export NYFORGE_OLLAMA_MODEL="qwen2.5-coder:7b"

# LM Studio
export NYFORGE_LMSTUDIO_BASE_URL="http://localhost:1234"
export NYFORGE_LMSTUDIO_MODEL="qwen2.5-coder-7b"
```

---

## 性能优化建议

### 显存优化

```json
{
  "Ollama": {
    "ContextSize": 2048,
    "Model": "qwen2.5-coder:7b"
  }
}
```

### 速度优化

- 使用 `llama3.2:3b` 等小模型
- 减小 `ContextSize`
- 使用 GPU 加速

### 质量优化

```json
{
  "Ollama": {
    "ContextSize": 8192,
    "Model": "qwen2.5-coder:14b",
    "Temperature": 0.5
  }
}
```

---

## 参考资料

- [Ollama 官方文档](https://ollama.com/docs)
- [LM Studio 文档](https://lmstudio.ai/docs)
- [Qwen2.5-Coder 模型](https://huggingface.co/Qwen)
- [Llama 3.2 模型](https://huggingface.co/meta-llama)

---

*最后更新：2026 年 3 月*
