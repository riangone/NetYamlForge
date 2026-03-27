# AI 窗口系统（CustomerAI）- 完整文档导航

*自动车经销商智能客服系统构建指南 - 基于 NetYamlForge 多租户框架*

---

## 📚 文档结构

本项目包含四份主要文档，逐层深入：

### 1️⃣ **AUTO-DEALER-SYSTEM-ORGANIZED.md**
- 📌 **用途**: 自动车经销商系统整体概览
- 📌 **内容**: 功能模块、业务流程、数据项、屏幕设计
- 📌 **读者**: 业务分析师、项目经理、需求确认
- ⏱️ **阅读时间**: 30 分钟
- ✅ **关键收获**: 理解业务需求，识别 AI 窗口的应用场景

### 2️⃣ **AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md**
- 📌 **用途**: AI 窗口系统的完整设计方案
- 📌 **内容**: 架构、技术栈、模块设计、集成点、4 周实现路线图、API 设计、数据模型
- 📌 **读者**: 技术负责人、架构师、高级开发人员
- ⏱️ **阅读时间**: 60-90 分钟
- ✅ **关键收获**: 掌握完整的系统架构和实现思路

### 3️⃣ **AI-WINDOW-CONFIG-EXAMPLE.yaml**
- 📌 **用途**: 完整的配置示例（可直接使用）
- 📌 **内容**: LLM 配置、渠道集成、知识库、转接规则、监控告警
- 📌 **读者**: 运维、配置管理人员、开发人员
- ⏱️ **阅读时间**: 20 分钟（查阅型）
- ✅ **关键收获**: 了解所有可配置选项，学会自定义系统行为

### 4️⃣ **AI-WINDOW-QUICK-START.md**
- 📌 **用途**: 开发人员快速上手指南
- 📌 **内容**: 环境准备、项目结构、Hello World 示例、本地开发、常见任务
- 📌 **读者**: 开发工程师、QA 工程师
- ⏱️ **阅读时间**: 45 分钟（实践型）
- ✅ **关键收获**: 能够在本地运行完整示例，理解代码结构

---

## 🎯 快速导航

### 我是...

#### 🧑‍💼 **业务/产品经理**
**你需要的路径:**
1. 读 `AUTO-DEALER-SYSTEM-ORGANIZED.md` 中的 "[AI窗口系统设计](#ai窗口系统设计将来像)"
2. 读 `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md` 中的 "[需求分析](#需求分析)" 和 "[核心价值主张](#核心价值主张)"
3. 结论: 3-4 周交付 MVP，成本降低 40%，客户满意度提升 25%

#### 🏗️ **架构师/技术负责人**
**你需要的路径:**
1. 读 `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md` 完整文档（重点关注 "[架构设计](#架构设计)" 和 "[技术栈选择](#技术栈选择)")
2. 查阅 `AI-WINDOW-CONFIG-EXAMPLE.yaml` 了解配置复杂度
3. 评审 `AI-WINDOW-QUICK-START.md` 中的项目结构，确保与现有框架兼容
4. 结论: 合理利用现有 NetYamlForge 框架，新增模块化设计，无需大幅改造

#### 👨‍💻 **开发工程师（.NET/C#）**
**你需要的路径:**
1. 从 `AI-WINDOW-QUICK-START.md` 的 "[第一个 Hello World](#第一个-hello-world)" 开始
2. 运行完整的 Hello World 示例（15-30 分钟）
3. 研读 `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md` 中的 "[核心模块设计](#核心模块设计)"
4. 按照 "[实现路线图](#实现路线图)" 制定 Sprint 计划
5. 参考 `AI-WINDOW-CONFIG-EXAMPLE.yaml` 理解配置需求

#### 🌐 **前端开发工程师（React/Vue）**
**你需要的路径:**
1. 阅读 `AI-WINDOW-QUICK-START.md` 中的 "[Web 聊天框](#web-聊天框)" 部分
2. 查看 `AI-WINDOW-CONFIG-EXAMPLE.yaml` 中的 "[多渠道集成](#多渠道集成)" → "web-chat"
3. 参考 `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md` 中的 "[API 接口设计](#api-接口设计)" 和 "[WebSocket 事件](#websocket-事件)"
4. 开始开发 React 聊天框组件（基于 SignalR）

#### 🔧 **运维/DevOps**
**你需要的路径:**
1. 查阅 `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md` 中的 "[部署架构](#部署架构)"
2. 学习 `AI-WINDOW-CONFIG-EXAMPLE.yaml` 中的 "[监控和日志](#监控和日志)" 部分
3. 查看 K8s 部署清单和 Docker 配置
4. 设置告警规则和 SLA 监控

#### 📊 **QA/测试工程师**
**你需要的路径:**
1. 读 `AUTO-DEALER-SYSTEM-ORGANIZED.md` 理解业务场景
2. 读 `AI-WINDOW-QUICK-START.md` 中的 "[单元测试](#单元测试)" 部分
3. 编写测试用例
4. 基于 `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md` 中的 "[成功指标](#成功指标)" 进行验收

---

## 🚀 实现路线图概览

### **Phase 1: 基础框架（Weeks 1-2）**

✅ **可交付物:**
- 对话管理引擎
- 基础意图分类（规则引擎）
- LLM 集成（Qwen）
- 数据库模型
- REST API 端点

📝 **相关文件:**
- `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md` § Phase 1
- `AI-WINDOW-QUICK-START.md` § Hello World
- `AI-WINDOW-CONFIG-EXAMPLE.yaml` § LLM 配置

---

### **Phase 2: 业务集成（Weeks 3-4）**

✅ **可交付物:**
- 客户信息查询（与 DynamicCrudRepository 集成）
- 预约系统集成
- 自动转接逻辑
- 完整 E2E 流程测试

📝 **相关文件:**
- `AUTO-DEALER-SYSTEM-ORGANIZED.md` § 销售管理 / 服务管理
- `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md` § Phase 2 / 集成点规划

---

### **Phase 3: 多渠道集成（Weeks 5-6）**

✅ **可交付物:**
- Web 聊天框（React 组件）
- LINE 消息网关
- Email 网关
- 客服仪表板
- 实时消息推送（SignalR）

📝 **相关文件:**
- `AI-WINDOW-CONFIG-EXAMPLE.yaml` § channels
- `AI-WINDOW-QUICK-START.md` § Web 聊天框

---

### **Phase 4: 智能优化（Weeks 7-8）**

✅ **可交付物:**
- 情感分析模块
- 用户反馈学习系统
- 分析仪表板
- 性能优化

📝 **相关文件:**
- `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md` § Phase 4 / 监控指标

---

## 📊 关键数字

| 指标 | 目标 | 说明 |
|------|------|------|
| 实现时间 | 8 周 | Phase 1-4 完整周期 |
| 首次应答率 | > 70% | AI 直接解答的比例 |
| 用户满意度 | > 7/10 | 用户满意度评分 |
| 系统可用性 | 99.5% | 正常运行时间 |
| 平均响应时间 | < 2 秒 | API 延迟 |
| 成本节省 | 40% | 减少人工处理 |

---

## 🔗 主要资源

### 配置和示例

- **完整 YAML 配置**: `AI-WINDOW-CONFIG-EXAMPLE.yaml` (15KB)
- **数据库 SQL**: `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md` § 数据库初始化
- **Dockerfile**: `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md` § 部署架构
- **Kubernetes 清单**: `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md` § Kubernetes 部署

### 代码示例

所有代码示例都包含在 `AI-WINDOW-QUICK-START.md` 中：

- SimpleIntentClassifier.cs - 意图分类
- SimpleResponseGenerator.cs - 回复生成
- InMemoryConversationManager.cs - 对话管理
- AIWindowController.cs - REST API 控制器
- 单元测试示例

### 数据模型

参考 `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md`:

```
┌─────────────────────────┐
│    AIConversation       │ ◄── 主表
├─────────────────────────┤
│ PK: conversation_id     │
│    user_id              │
│    channel              │
└──────────┬──────────────┘
           │ 1:N
           ▼
┌──────────────────────────┐
│      AIMessage           │ ◄── 消息历史
├──────────────────────────┤
│ PK: message_id           │
│    sender                │
│    content               │
└──────────────────────────┘
```

详见: `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md` § 数据模型设计

---

## 💡 设计亮点

### 1. 充分利用现有框架
- ✅ ProjectManager (多租户)
- ✅ DynamicCrudRepository (数据访问)
- ✅ HookExecutionService (业务逻辑)
- ✅ DocumentPdfService (文档生成)

### 2. 模块化架构
- ✅ IConversationManager - 对话管理
- ✅ IIntentClassifier - 意图识别
- ✅ IResponseGenerator - 回复生成
- ✅ ICustomerDataService - 数据集成
- ✅ IAppointmentService - 预约管理
- ✅ IHandoverManager - 智能转接

### 3. 易于扩展
- 规则引擎 + LLM 混合分类
- 可插拔的 LLM 提供商 (Qwen/Claude/GPT-4)
- 支持多渠道 (Web/LINE/Email)

### 4. 生产就绪
- 容器化 (Docker)
- 可伸缩部署 (Kubernetes HPA)
- 完整的监控告警
- 99.5% 高可用性

---

## ❓ 常见问题

### Q: 需要多长时间实现？
**A:** MVP (核心功能) 需要 2-3 周，完整功能需要 8 周。

### Q: 需要多少人？
**A:** 推荐 3-4 名开发人员（后端 2 名、前端 1 名、QA 1 名）。

### Q: 成本如何估算？
**A:** 
- **开发成本**: 8 周 × 4 人 × ¥1,000/小时 ≈ ¥256,000
- **基础设施**: 年 ¥120,000（服务器、LLM API）
- **ROI**: 3 个月内通过人工成本节省收回

### Q: 需要 GPU 吗？
**A:** 不需要。使用 Qwen/Claude 等云 LLM 服务，无需本地部署。

### Q: 能否离线使用？
**A:** 规则引擎部分可离线，LLM 部分需要 API 访问。

### Q: 数据安全怎么保证？
**A:** 
- 加密传输 (HTTPS/TLS)
- 加密存储 (AES-256)
- 定期备份 (日备份)
- 审计日志 (所有操作记录)

---

## 📞 技术支持

### 问题排查

| 问题 | 解决方案 |
|------|--------|
| 数据库连接失败 | 查看 `AI-WINDOW-QUICK-START.md` § 故障排除 |
| LLM 超时 | 增加超时时间或检查网络 |
| 消息推送延迟 | 检查 SignalR 连接和消息队列 |

### 进一步阅读

- **NetYamlForge 文档**: `CLAUDE.md`
- **项目配置**: `projects/auto-dealer-demo/`
- **架构决策**: `AI-ASSISTANT-DESIGN.md`

---

## 🎓 学习路径

### 初级 (1-2 天)
1. 阅读 `AUTO-DEALER-SYSTEM-ORGANIZED.md`
2. 查看 `AI-WINDOW-CONFIG-EXAMPLE.yaml` 的知识库部分
3. 理解基本概念

### 中级 (3-5 天)
1. 完成 `AI-WINDOW-QUICK-START.md` 中的 Hello World 示例
2. 运行单元测试
3. 修改意图分类规则

### 高级 (1-2 周)
1. 阅读完整的 `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md`
2. 设计数据库模型
3. 实现 Phase 1

### 专家 (2+ 周)
1. 贡献新功能 (多渠道、LLM 优化)
2. 性能调优
3. 部署和运维

---

## 📝 许可证和归属

- 基于 NetYamlForge 框架
- 遵循相同许可证
- 欢迎社区贡献

---

## 🙏 致谢

感谢 NetYamlForge 团队提供的优秀多租户框架，使得 AI 窗口系统的构建成为可能。

---

**最后更新**: 2026 年 3 月 27 日
**版本**: 1.0
**维护者**: NetYamlForge AI Team

---

## 下一步

🚀 **准备好开始了？**

1. **立即开始**: 按照 `AI-WINDOW-QUICK-START.md` 运行 Hello World
2. **理解架构**: 深入阅读 `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md`
3. **制定计划**: 基于 "[实现路线图](#-实现路线图概览)" 制定 Sprint
4. **组建团队**: 按照 "[需要多少人](#q-需要多少人)" 组织开发小组

**祝你成功！** 🎉
