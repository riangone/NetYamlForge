# GitHub Copilot CLI 配置指南

本文档介绍如何在 NetYamlForge 项目中配置和使用 GitHub Copilot CLI。

---

## 目录

- [概述](#概述)
- [安装 GitHub Copilot CLI](#安装-github-copilot-cli)
- [配置认证](#配置认证)
- [NetYamlForge 配置](#netyamlforge-配置)
- [使用方法](#使用方法)
- [故障排查](#故障排查)

---

## 概述

GitHub Copilot CLI 是 GitHub 官方提供的命令行 AI 助手，可以直接在终端中执行代码生成、文件操作等任务。

### 主要特性

| 特性 | 说明 |
|------|------|
| **代码生成** | 根据自然语言描述生成代码 |
| **文件操作** | 读取、写入、编辑项目文件 |
| **命令执行** | 执行 Shell 命令并解释结果 |
| **Git 集成** | 执行 Git 操作和代码审查 |
| **网络访问** | 搜索文档和最新技术信息 |

### 与其他 AI 工具的对比

| 工具 | 类型 | 优点 | 缺点 |
|------|------|------|------|
| **Copilot CLI** | 云端 | GitHub 官方支持，代码理解能力强 | 需要付费订阅 |
| **Claude Code** | 云端 | 上下文窗口大，推理能力强 | API 调用收费 |
| **Qwen Code** | 云端 | 中文支持好，性价比高 | 需要 API 密钥 |
| **Ollama** | 本地 | 免费，离线可用 | 需要本地 GPU 资源 |

---

## 安装 GitHub Copilot CLI

### 前置要求

1. **GitHub Copilot 订阅**
   - 需要有效的 GitHub Copilot 个人/企业订阅
   - 访问 [github.com/github-copilot](https://github.com/github-copilot) 订阅

2. **GitHub CLI (gh)**
   ```bash
   # macOS
   brew install gh

   # Ubuntu/Debian
   sudo apt-get install gh

   # Windows (使用 winget)
   winget install --id GitHub.cli
   ```

3. **登录 GitHub CLI**
   ```bash
   gh auth login
   ```

### 安装 Copilot CLI

```bash
# 使用 gh 扩展安装
gh extension install github/gh-copilot

# 验证安装
gh copilot --version
```

或者使用 npm 安装独立的 CLI 工具：

```bash
npm install -g @github/copilot

# 验证安装
copilot --version
```

---

## 配置认证

### 方法一：使用 gh copilot（推荐）

如果使用 `gh copilot` 命令：

```bash
# 登录 GitHub（如果尚未登录）
gh auth login

# 验证 Copilot 订阅状态
gh copilot --version
```

### 方法二：使用独立 copilot CLI

如果使用独立的 `copilot` 命令：

1. **获取 Copilot Token**
   ```bash
   # 登录获取 token
   copilot login
   ```

2. **手动获取 Token（可选）**
   - 访问 GitHub 设置页面
   - 生成新的 fine-grained token
   - 复制 token 用于配置

---

## NetYamlForge 配置

### appsettings.json 配置

编辑 `NetYamlForge/appsettings.json`：

```json
{
  "AICli": {
    "DefaultTool": "copilot",
    "TaskTimeoutSeconds": 1800,
    "MaxConcurrentTasks": 2,
    "DefaultAllowedTools": [
      "Read", "Write", "Edit", "Bash", "Git"
    ],
    "Copilot": {
      "Token": "your-github-copilot-token",
      "Path": ""
    }
  }
}
```

### 配置项说明

| 配置项 | 说明 | 默认值 | 必填 |
|--------|------|--------|------|
| `Token` | GitHub Copilot 认证 token | 空 | 否* |
| `Path` | copilot 命令的完整路径 | 从 PATH 查找 | 否 |

\* 如果已使用 `gh auth login` 或 `copilot login` 登录，则不需要配置 Token

### 使用环境变量（推荐）

在生产环境中，建议使用环境变量：

```bash
# .env 文件
GITHUB_COPILOT_TOKEN=your-token-here
```

或者在 Docker 中：

```yaml
# docker-compose.yml
environment:
  - GITHUB_COPILOT_TOKEN=${GITHUB_COPILOT_TOKEN}
```

---

## 使用方法

### 在 AI 助手界面中使用

1. **打开 AI 助手面板**
   - 点击页面右上角的 💬 按钮

2. **选择 Copilot**
   - 在下拉菜单中选择 "GitHub Copilot"

3. **输入指令**
   ```
   为 Task 实体创建 YAML 定义
   ```

4. **查看结果**
   - AI 会生成代码并显示在聊天窗口
   - 可以确认并应用更改

### 常用指令示例

#### 实体定义生成

```
创建一个 Task 实体，包含以下字段：
- Id (主键)
- Title (字符串，必填)
- Description (文本)
- Status (枚举：Pending, InProgress, Completed)
- DueDate (日期)
- ProjectId (外键)
```

#### 页面模板生成

```
为 Task 实体创建一个列表页面，包含：
- 搜索框（按标题搜索）
- 状态筛选器
- 分页功能
- 操作列（编辑/删除）
```

#### 业务逻辑实现

```
为 Task 实体添加以下业务规则：
- 创建时自动设置 CreatedAt 为当前时间
- 状态变更为 Completed 时自动设置 CompletedAt
- 逾期任务在列表中高亮显示
```

#### 代码审查

```
检查当前项目的代码质量问题，并给出改进建议
```

### 命令行直接使用

```bash
# 使用 gh copilot
gh copilot --prompt "为 Task 实体创建 YAML 定义"

# 使用独立 copilot
copilot --prompt "创建一个 REST API 端点用于 Task 管理"
```

---

## 故障排查

### 常见问题

#### 1. 认证失败

**错误信息**: `Authentication failed` 或 `Invalid token`

**解决方案**:
```bash
# 重新登录
gh auth logout
gh auth login

# 或者更新 token
copilot login
```

#### 2. CLI 未找到

**错误信息**: `copilot: command not found`

**解决方案**:
```bash
# 确认安装
which copilot
which gh

# 如果未安装，参考安装章节
```

#### 3. 订阅验证失败

**错误信息**: `No Copilot subscription found`

**解决方案**:
- 确认 GitHub 账户有有效的 Copilot 订阅
- 访问 [github.com/settings/copilot](https://github.com/settings/copilot) 检查

#### 4. 请求超时

**错误信息**: `Request timeout`

**解决方案**:
- 检查网络连接
- 增加 `TaskTimeoutSeconds` 配置值
- 如果是企业防火墙，可能需要配置代理

### 日志位置

查看 AI 助手的日志：

```bash
# 应用程序日志
dotnet run --project NetYamlForge -- --debug

# 或者查看日志文件
tail -f NetYamlForge/logs/*.log
```

### 获取帮助

- [GitHub Copilot 官方文档](https://docs.github.com/en/copilot)
- [gh copilot 扩展文档](https://github.com/github/gh-copilot)
- [NetYamlForge AI 助手指南](../ai-assistant-guide.md)

---

## 最佳实践

### 1. 提示词编写

- **具体明确**: 详细描述需要的功能和字段
- **上下文完整**: 提供相关的实体关系和业务规则
- **分步请求**: 复杂任务拆分为多个小步骤

### 2. 代码审查

- 始终审查 AI 生成的代码
- 运行测试确保功能正常
- 检查 SQL 注入等安全问题

### 3. 性能优化

- 避免过长的上下文（影响响应速度）
- 使用流式输出查看进度
- 合理设置超时时间

---

## 相关文档

- [AI 助手完全指南](../ai-assistant-guide.md)
- [Claude Code 配置指南](ai-claude-setup.md)
- [Qwen Code 配置指南](ai-qwen-setup.md)
- [本地模型配置指南](ai-local-model-setup.md)

---

*最后更新：2026 年 3 月*
