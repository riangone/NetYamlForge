# AI 顧客数查询问题修复报告

## 问题描述
用户询问"現在の顧客数"（当前顾客数）时，系统返回默认回复：
> ご質問ありがとうございます。詳しい内容については担当者が丁寧にご案内いたします。他にご不明な点はございますか？

而不是返回实际的顾客数量。

## 根本原因分析

### 1. CLI 超时时间太短
- **原设置**: 8 秒
- **问题**: AI 需要时间思考并生成 tool_call JSON，8 秒可能不够
- **影响**: CLI 在 AI 完成思考前就被强制终止

### 2. 系统提示词不够明确
- **原提示词**: 简单说明需要输出 JSON
- **问题**: 没有明确说明：
  - 件数查询需要使用 `action:"count"`
  - JSON 格式的具体示例
  - 强调只输出 JSON，不要其他内容

### 3. 缺少调试日志
- **问题**: 无法知道 CLI 实际返回了什么内容
- **影响**: 难以诊断问题是 AI 没有返回 JSON 还是解析失败

## 修复内容

### 1. 增加 CLI 超时时间 ⏱️
**文件**: `NetYamlForge/Services/AI/AutoDealerChatService.cs`
```csharp
// 修改前
private int CliTimeoutSeconds => int.TryParse(_config["AiWindow:CliTimeoutSeconds"], out var t) ? t : 8;

// 修改后
private int CliTimeoutSeconds => int.TryParse(_config["AiWindow:CliTimeoutSeconds"], out var t) ? t : 30;
```

**文件**: `NetYamlForge/appsettings.json`
```json
"AiWindow": {
  "CliTimeoutSeconds": 30  // 从 8 改为 30
}
```

### 2. 优化系统提示词 📝
**文件**: `NetYamlForge/Services/AI/AutoDealerChatService.cs`

新增详细的工具调用说明：
```
## 🔧 ツール呼び出しルール（最重要）
**ユーザーがデータ・件数・一覧を尋ねた場合は、必ず tool_call JSON だけを出力してください**

### 件数質問の例
- 「顧客数は？」「車両が何台ある？」「予約が何件？」→ action:"count" を使用

### 一覧表示の例
- 「顧客一覧を見せて」「利用可能な車両は？」「今日の予約は？」→ action:"list" を使用

### 出力する JSON 形式（必ずこの形式で）
{"tool_call":"query_data","entity":"customers","action":"count","filters":[],"top":1}

### 重要なポイント
- JSON**だけ**を出力（説明文・前後のテキスト・```json マークは一切不要）
- 件数質問には action:"count" を使用（デフォルトは"list"）
- entity は "customers", "vehicles", "service_appointments", "sales_leads" のいずれか
```

### 3. 添加详细调试日志 📊

#### CLI 响应成功日志
```csharp
_logger.LogInformation("CLI 応答成功 provider={Name}, 結果長さ={Length}, 先頭 300 文字={First300}", 
    name, result.Length, result.Substring(0, Math.Min(300, result.Length)));

if (result.Trim().StartsWith("{") && result.Contains("tool_call"))
{
    _logger.LogInformation("🔧 CLI が tool_call JSON を返した provider={Name}, JSON={Json200}", 
        name, result.Substring(0, Math.Min(200, result.Length)));
}
```

#### tool_call 解析失败日志
```csharp
_logger.LogWarning("CLI が tool_call JSON を返さなかった。応答長さ={Length}, 先頭 400 文字={First400}", 
    round1.Length, round1.Substring(0, Math.Min(400, round1.Length)));
```

#### CLI 响应为空日志
```csharp
_logger.LogWarning("CLI 応答が空または null provider={Name}, 生応答長さ={RawLength}", 
    name, raw?.Length ?? 0);
```

## 测试方法

### 1. 重启应用程序
```bash
cd /home/ubuntu/ws/NetYamlForge
dotnet run --project NetYamlForge
```

### 2. 测试顾客数查询
访问：`http://localhost:5000/auto-dealer-demo/Page/AIDashboard` 或相应的 AI 聊天界面

输入：**"現在の顧客数は？"**

### 3. 查看日志
```bash
tail -f /home/ubuntu/ws/NetYamlForge/NetYamlForge/logs/app-20260331.log | grep -i "tool_call\|CLI 応答"
```

### 4. 预期结果

#### 成功情况 ✅
日志应显示：
```
🔧 CLI が tool_call JSON を返した provider=qwen, JSON={"tool_call":"query_data","entity":"customers","action":"count",...}
CLI 応答成功 provider=qwen, 結果長さ=XXX
```

AI 应回复：
```
現在の顧客数は XX 名です。
```

#### 失败情况 ❌
如果仍然失败，日志会显示：
```
CLI が tool_call JSON を返さなかった。応答長さ=XXX, 先頭 400 文字=...
```

这时可以根据日志内容进一步诊断问题。

## 其他诊断工具

### 检查 Qwen CLI 是否正常工作
```bash
# 测试 CLI
qwen --version

# 手动测试 CLI 响应
echo '{"messages":[{"role":"user","content":"顧客数は？"}]}' | qwen --stdin
```

### 检查 AI 配置
```bash
# 查看当前配置
cat /home/ubuntu/ws/NetYamlForge/NetYamlForge/appsettings.json | grep -A 10 '"AiWindow"'
```

## 后续优化建议

1. **监控日志**: 运行后持续监控日志 1-2 天，收集 AI 响应数据
2. **调整提示词**: 根据日志中的实际响应调整提示词
3. **考虑使用 Claude API**: 如果 Qwen CLI 持续失败，可以启用 Claude API 作为主要提供者
4. **添加性能指标**: 记录 AI 响应时间，优化超时设置

## 修改文件清单

1. ✅ `NetYamlForge/Services/AI/AutoDealerChatService.cs` - 超时时间、系统提示词、日志
2. ✅ `NetYamlForge/appsettings.json` - 默认超时配置

## 编译状态
✅ 编译成功，无错误

---
**修复时间**: 2026-03-31  
**修复者**: AI Assistant  
**下次检查**: 运行后 24 小时查看日志
