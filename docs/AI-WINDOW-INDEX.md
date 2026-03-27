# AI 窗口系统文档索引

*快速查找所有相关文档和资源*

---

## 📑 文档导航

### 核心设计文档

| 文档 | 大小 | 主要内容 | 推荐人群 | 阅读时间 |
|------|------|---------|---------|---------|
| **AI-WINDOW-DELIVERY-SUMMARY.md** | 7 KB | 交付内容总结、质量保证、后续支持 | 所有人 | 10 分钟 |
| **AI-WINDOW-README.md** | 11 KB | 文档导航、快速参考、常见问题 | 所有人 | 15 分钟 |
| **AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md** | 60 KB | 完整系统设计、架构、API、路线图 | 开发/架构师 | 90 分钟 |
| **AI-WINDOW-QUICK-START.md** | 22 KB | 快速上手、Hello World、本地开发 | 开发工程师 | 45 分钟 |
| **AI-WINDOW-CONFIG-EXAMPLE.yaml** | 19 KB | 完整配置示例、即插即用 | 配置/运维 | 30 分钟 |
| **AUTO-DEALER-SYSTEM-ORGANIZED.md** | 21 KB | 业务系统规划、需求整理 | 产品/业务 | 30 分钟 |

---

## 🎯 按角色快速查找

### 产品经理 / 业务分析师

**阅读顺序:**
1. AI-WINDOW-DELIVERY-SUMMARY.md（概览）
2. AUTO-DEALER-SYSTEM-ORGANIZED.md（需求）
3. AI-WINDOW-README.md（FAQ）

**关键问题解答:**
- Q: 系统能做什么? → 见 AUTO-DEALER-SYSTEM-ORGANIZED.md
- Q: 能省多少成本? → 见 AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md § 成功指标
- Q: 需要多长时间? → 见 AI-WINDOW-README.md § 常见问题
- Q: 有哪些风险? → 见 AI-WINDOW-DELIVERY-SUMMARY.md § 质量保证

---

### 技术架构师

**阅读顺序:**
1. AI-WINDOW-README.md（导航）
2. AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md（完整设计）
   - § 架构设计
   - § 技术栈选择
   - § 集成点规划
3. AI-WINDOW-CONFIG-EXAMPLE.yaml（配置复杂度评估）

**关键主题:**
- 架构图 → 实现计划 § 系统全景图
- 技术选型 → 实现计划 § 技术栈选择
- 与框架集成 → 实现计划 § 集成点规划
- 部署方案 → 实现计划 § 部署架构

---

### 后端开发工程师

**阅读顺序:**
1. AI-WINDOW-QUICK-START.md（Hello World）
2. AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md（完整设计）
   - § 核心模块设计
   - § 实现路线图
   - § API 接口设计
   - § 数据模型设计
3. AI-WINDOW-CONFIG-EXAMPLE.yaml（配置需求）

**快速开始:**
```bash
# 第 1 步：准备环境（10 分钟）
# 见: AI-WINDOW-QUICK-START.md § 环境准备

# 第 2 步：运行 Hello World（15 分钟）
# 见: AI-WINDOW-QUICK-START.md § 第一个 Hello World

# 第 3 步：理解架构（60 分钟）
# 见: AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md § 核心模块设计

# 第 4 步：制定计划（30 分钟）
# 见: AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md § 实现路线图
```

**核心模块学习路径:**
1. ConversationManager → 实现计划 § 对话管理模块
2. IntentClassifier → 实现计划 § 意图识别模块
3. ResponseGenerator → 实现计划 § 回复生成模块
4. CustomerDataService → 实现计划 § 客户信息集成模块
5. AppointmentService → 实现计划 § 预约管理模块
6. HandoverManager → 实现计划 § 智能转接模块

**代码示例位置:**
- 完整示例 → 快速开始 § Hello World
- 单元测试 → 快速开始 § 本地开发
- 常见任务 → 快速开始 § 常见任务

---

### 前端开发工程师

**阅读顺序:**
1. AI-WINDOW-README.md（导航）
2. AI-WINDOW-QUICK-START.md（项目结构）
3. AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md
   - § API 接口设计
   - § WebSocket 事件

**关键资源:**
- Web 聊天框规范 → 实现计划 § Web 聊天框或配置 § channels.web-chat
- API 端点 → 实现计划 § REST 端点（8 个）
- WebSocket 事件 → 实现计划 § WebSocket 事件
- 错误处理 → 快速开始 § 常见任务

---

### 运维 / DevOps

**阅读顺序:**
1. AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md
   - § 部署架构
   - § Kubernetes 部署
2. AI-WINDOW-CONFIG-EXAMPLE.yaml
   - § 监控和日志
   - § 性能配置

**部署资源:**
- Dockerfile → 实现计划 § 容器化
- Kubernetes 清单 → 实现计划 § Kubernetes 部署
- docker-compose → 实现计划 § Docker Compose
- 监控栈 → 实现计划 § 监控栈

**配置检查单:**
- [ ] 环境变量设置（LLM_API_KEY 等）
- [ ] 数据库连接字符串
- [ ] Redis 连接配置
- [ ] RabbitMQ 队列配置
- [ ] Prometheus 数据收集
- [ ] Grafana 仪表板
- [ ] ELK Stack 配置

---

### QA / 测试工程师

**阅读顺序:**
1. AUTO-DEALER-SYSTEM-ORGANIZED.md（业务场景）
2. AI-WINDOW-QUICK-START.md（测试框架）
3. AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md
   - § 成功指标
   - § 实现路线图

**测试用例设计:**
- 单元测试 → 快速开始 § 单元测试
- 集成测试 → 实现计划 § Phase 2
- E2E 测试场景 → 自动化系统 § 3 个业务流程
- 性能测试 → 实现计划 § 非功能需求

**验收标准:**
- 首次应答率 > 70% → 实现计划 § 成功指标
- 用户满意度 > 7/10 → 配置 § feedback 部分
- 平均响应时间 < 2 秒 → 配置 § performance
- 系统可用性 > 99.5% → 配置 § sla

---

## 🔍 按主题查找

### 系统架构

| 主题 | 文档位置 | 页码参考 |
|------|---------|---------|
| 系统全景图 | 实现计划 § 架构设计 | N/A |
| 部署拓扑图 | 实现计划 § 部署拓扑 | N/A |
| 模块关系 | 实现计划 § 核心模块设计 | N/A |
| 数据流 | 实现计划 § 数据模型 | N/A |

### 实现细节

| 主题 | 文档位置 | 部分 |
|------|---------|------|
| ConversationManager | 实现计划 | § 对话管理模块 |
| IntentClassifier | 实现计划 | § 意图识别模块 |
| ResponseGenerator | 实现计划 | § 回复生成模块 |
| CustomerDataService | 实现计划 | § 客户信息集成 |
| AppointmentService | 实现计划 | § 预约管理模块 |
| HandoverManager | 实现计划 | § 智能转接模块 |
| SentimentAnalyzer | 配置示例 | § sentiment |
| ChatHistoryService | 实现计划 | § 数据模型设计 |

### 业务流程

| 流程 | 文档位置 | 详情 |
|------|---------|------|
| 销售契约流 | 自动化系统 | § 销售管理 § 7个步骤 |
| 服务受付流 | 自动化系统 | § 服务管理 § 7个步骤 |
| 部品在庫流 | 自动化系统 | § 部品管理 § 7个步骤 |
| AI 对话流 | 实现计划 | § 意图识别 § 回复生成 |
| 智能转接流 | 实现计划 | § 转接管理模块 |

### 配置和参数

| 配置项 | 文档位置 | 默认值 |
|--------|---------|--------|
| LLM Provider | 配置示例 | "qwen" |
| Temperature | 配置示例 | 0.3 |
| 超时时间 | 配置示例 | 30000 ms |
| 知识库 | 配置示例 | 5 个示例 |
| 转接规则 | 配置示例 | 4 条规则 |
| 监控间隔 | 配置示例 | 60 秒 |

### API 和集成

| API | 文档位置 | 方法 | 端点 |
|-----|---------|------|------|
| 启动对话 | 实现计划 | POST | /api/ai/conversations |
| 发送消息 | 实现计划 | POST | /api/ai/messages |
| 查询客户 | 实现计划 | GET | /api/ai/customers/{id} |
| 创建预约 | 实现计划 | POST | /api/ai/appointments |
| 转接人工 | 实现计划 | POST | /api/ai/handover |
| 提交反馈 | 实现计划 | POST | /api/ai/feedback |
| WebSocket | 实现计划 | - | /hubs/ai-chat |

---

## 📊 文件和资源

### 配置文件

- `AI-WINDOW-CONFIG-EXAMPLE.yaml` - 完整配置模板（即插即用）
- `appsettings.Development.json` - 本地开发配置（示例）

### 代码文件

**完整工作示例:**
- SimpleIntentClassifier.cs
- SimpleResponseGenerator.cs
- InMemoryConversationManager.cs
- AIWindowController.cs

**代码骨架:**
- IConversationManager 接口
- IIntentClassifier 接口
- IResponseGenerator 接口
- ICustomerDataService 接口
- IAppointmentService 接口
- IHandoverManager 接口

**单元测试:**
- SimpleIntentClassifierTests.cs

### 部署文件

- Dockerfile（容器镜像）
- docker-compose.yml（开发环境）
- k8s/ai-window-deployment.yaml（生产部署）
- k8s/ai-window-service.yaml（服务暴露）
- k8s/ai-window-hpa.yaml（自动扩容）

### 数据库文件

- SQL 初始化脚本（PostgreSQL）
- 数据表定义（7 个表）
- 索引优化建议

---

## 🎓 学习路径建议

### 入门级（1-2 天）
1. ✅ 阅读 AUTO-DEALER-SYSTEM-ORGANIZED.md
2. ✅ 阅读 AI-WINDOW-README.md
3. ✅ 浏览 AI-WINDOW-CONFIG-EXAMPLE.yaml

**成果:** 了解系统能做什么

### 进阶级（3-5 天）
1. ✅ 完成 AI-WINDOW-QUICK-START.md 中的 Hello World
2. ✅ 运行单元测试
3. ✅ 修改意图分类规则

**成果:** 能够修改和定制系统

### 高级级（1-2 周）
1. ✅ 阅读完整 AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md
2. ✅ 学习每个模块的设计
3. ✅ 开始 Phase 1 开发

**成果:** 能够从头构建完整系统

### 专家级（2+ 周）
1. ✅ 深入研究 LLM 集成
2. ✅ 优化性能和成本
3. ✅ 贡献新功能

**成果:** 成为系统的专家

---

## 🔗 相关资源

### 外部文档

- [NetYamlForge 框架](https://github.com/yourorg/NetYamlForge) - CLAUDE.md
- [Qwen API 文档](https://help.aliyun.com/zh/dashscope) - LLM 集成参考
- [LINE Messaging API](https://developers.line.biz/ja/) - 多渠道集成
- [Kubernetes 官方文档](https://kubernetes.io/docs/) - 部署参考
- [PostgreSQL 官方文档](https://www.postgresql.org/docs/) - 数据库参考

### 相关项目

- NetYamlForge 主项目
- projects/auto-dealer-demo/ - 示例项目

### 联系方式

- 技术问题 → 提交 GitHub Issue
- 文档反馈 → 提交文档改进建议
- 架构咨询 → 联系技术负责人

---

## ✅ 快速检查清单

### 开发前准备

- [ ] 阅读了相关的 AI-WINDOW-*.md 文档
- [ ] 理解了系统架构
- [ ] 准备好了开发环境
- [ ] 安装了所有依赖
- [ ] 能够运行 Hello World 示例
- [ ] 配置了 IDE 和调试工具

### 代码审查

- [ ] 代码符合架构设计
- [ ] 单元测试覆盖率 > 80%
- [ ] 错误处理完整
- [ ] 日志输出充分
- [ ] 性能指标达标
- [ ] 安全性检查通过

### 部署前检查

- [ ] 所有环境变量配置正确
- [ ] 数据库迁移成功
- [ ] 外部 API 可连接
- [ ] 监控告警配置完成
- [ ] 备份和恢复测试通过
- [ ] 性能测试达标

---

## 📞 需要帮助？

1. **快速查找** → 使用本索引
2. **详细说明** → 查阅相应文档
3. **代码示例** → 见快速开始文档
4. **故障排除** → 见 AI-WINDOW-QUICK-START.md § 故障排除
5. **FAQ** → 见 AI-WINDOW-README.md § 常见问题

---

**最后更新**: 2026 年 3 月 27 日
**版本**: 1.0
