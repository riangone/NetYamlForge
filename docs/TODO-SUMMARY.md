# TODO/FIXME 标记总结

本文档总结了代码库中所有 `TODO`、`FIXME`、`HACK`、`XXX` 标记，按优先级分类。

## 🔴 高优先级（建议尽快处理）

### 1. 密码哈希改进（`TenantUserService.cs:288`）
**当前状态**：已使用 ASP.NET Core `PasswordHasher` 替代原来的 SHA256

**原 TODO**：`// TODO: 使用 BCrypt.Net.HashPassword 或 ASP.NET Core PasswordHasher`

**状态**：✅ 已修复（2026-05-07）

---

### 2. Stateless 状态机可视化（`AppointmentStateMachine.cs`）
**位置**：
- `NetYamlForge/Services/AI/AppointmentStateMachine.cs:195`
- `NetYamlForge/NetYamlForge.AI/Services/AppointmentStateMachine.cs:195`

**TODO**：`// TODO: 需要安装 Stateless.Graph 包`

**建议**：安装 `Stateless.Graph` NuGet 包，生成状态机可视化图，用于文档和调试。

**预计工作量**：1 小时

---

### 3. 工具执行逻辑集成（`AiToolOrchestrator.cs`）
**位置**：
- `NetYamlForge/Services/AI/AiToolOrchestrator.cs:150`
- `NetYamlForge/NetYamlForge.AI/Services/AiToolOrchestrator.cs`

**TODO**：
- `// [4] 执行 Tool (TODO: 这里需要集成到实际的 Tool 执行逻辑)`
- `result.Data = null; // TODO: 实际的 Tool 执行结果`
- `LowConfidenceCount = 0 // TODO: 从 FSM 获取`

**建议**：完善 `AiToolOrchestrator` 的工具执行逻辑，从有限状态机（FSM）获取实际数据。

**预计工作量**：2-3 天

---

## 🟡 中优先级（可延后处理）

### 4. 邮件通知实现（`BatchJobHostedService.cs:150`）
**位置**：`NetYamlForge/Services/BatchJob/BatchJobHostedService.cs:150`

**TODO**：`// TODO: メール通知などの実装`

**建议**：实现批处理作业失败时的邮件通知功能。

**预计工作量**：1 天

---

### 5. 历史消息加载（`LMStudioCLIService.cs`, `OllamaCLIService.cs`）
**位置**：
- `NetYamlForge/Services/AI/Providers/LMStudioCLIService.cs`
- `NetYamlForge/Services/AI/Providers/OllamaCLIService.cs`

**TODO**：`// TODO: 从 ChatHistoryService 加载历史消息`

**建议**：集成聊天历史服务，使本地模型（LM Studio、Ollama）能够访问历史对话。

**预计工作量**：1-2 天

---

### 6. 未回答的问题表创建（`KnowledgeBaseService.cs`）
**位置**：`NetYamlForge/Services/AI/KnowledgeBaseService.cs`

**TODO**：`// TODO: ai_unanswered_questions テーブル作成`

**建议**：创建存储未回答问题（用于后续训练或人工回复）的数据表。

**预计工作量**：半天

---

### 7. Hook 脚手架验证（`HookScaffolder.cs`）
**位置**：`NetYamlForge/Services/Cli/HookScaffolder.cs`

**TODO**：
- `// TODO: バリデーション・変換ロジックを実装する`
- `// TODO: 書き込み成功後の後処理（通知・連携等）を実装する（任意）`
- `// TODO: 不正値を入力してフックが Abort を返すことを確認`
- `// TODO: 実装に合わせてアサートを修正`

**建议**：完善 Hook 脚手架的验证、转换逻辑和测试断言。

**预计工作量**：2-3 天

---

## 🟢 低优先级（可忽略或长期规划）

### 8. 文档字符串中的 TODO（`JpiereChatService.cs`）
**位置**：`NetYamlForge/Services/AI/JpiereChatService.cs`

**TODO**：文档字符串中包含 "TODO" 文本（非代码 TODO）

**建议**：更新文档字符串，移除或实现提到的功能。

**预计工作量**：1 小时

---

## 📊 统计

| 优先级 | 数量 | 状态 |
|--------|------|------|
| 🔴 高 | 3 | 1 已修复 |
| 🟡 中 | 4 | 未处理 |
| 🟢 低 | 1 | 未处理 |
| **总计** | **8** | **1 已修复** |

---

## 📝 处理建议

### 立即处理（本周）
1. ✅ 密码哈希改进 - **已完成**
2. 安装 `Stateless.Graph` 并生成状态机图
3. 完善 `AiToolOrchestrator` 工具执行逻辑

### 短期规划（本月）
1. 实现批处理作业邮件通知
2. 集成 LM Studio/Ollama 历史消息加载
3. 创建 `ai_unanswered_questions` 表

### 长期规划（未来）
1. 完善 Hook 脚手架验证和测试
2. 清理文档字符串中的 TODO

---

*最后更新：2026-05-07*
