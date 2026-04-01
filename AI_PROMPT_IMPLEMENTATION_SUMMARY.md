# 汽车销售 AI 提示词系统 - 实施总结

> **实施日期**: 2026 年 4 月 1 日
> **版本**: 3.0（完全独立版）

---

## ✅ 已完成的工作

### 1. 创建了独立的提示词文件集

在 `skills/auto-dealer/` 目录下创建了以下 5 个独立文件：

| 文件 | 说明 | 大小 |
|------|------|------|
| `_system-prompt-staff.md` | 员工版系统提示词 | ~400 行 |
| `_system-prompt-customer.md` | 顾客版系统提示词 | ~200 行 |
| `_tools-definition.md` | 工具定义（query_data 等） | ~300 行 |
| `_entity-reference.md` | 实体字段完全参考 | ~400 行 |
| `_response-templates.md` | 响应模板集 | ~500 行 |

**总计**: 约 1,800 行的完整提示词文档

---

### 2. 创建了项目配置文件

| 文件 | 说明 |
|------|------|
| `projects/auto-dealer-demo/ai-config.yaml` | AI 专用配置指南 |
| `docs/AI_PROMPT_DESIGN.md` | 完整设计文档 |

---

### 3. 文件结构

```
NetYamlForge/
├── skills/
│   ├── _system-prompt.md              # 框架通用（已存在）
│   └── auto-dealer/                   # 汽车销售专用（新增）⭐
│       ├── _system-prompt-staff.md    # 员工版 ✅
│       ├── _system-prompt-customer.md # 顾客版 ✅
│       ├── _tools-definition.md       # 工具定义 ✅
│       ├── _entity-reference.md       # 实体参考 ✅
│       └── _response-templates.md     # 响应模板 ✅
│
├── NetYamlForge/projects/auto-dealer-demo/
│   ├── project.yaml                   # 项目配置（已存在）
│   └── ai-config.yaml                 # AI 配置 ✅ 新增
│
└── docs/
    └── AI_PROMPT_DESIGN.md            # 设计文档 ✅ 新增
```

---

## 🎯 核心设计特点

### 1. 完全独立

- ✅ **与框架 AI 分离**: 汽车销售提示词完全独立于框架通用提示词
- ✅ **独立引用**: 通过 Markdown 链接相互引用，保持模块化
- ✅ **独立更新**: 修改汽车销售提示词不影响框架

### 2. 模块化设计

```
_system-prompt-staff.md
    ↓ 引用
_tools-definition.md
_entity-reference.md
_response-templates.md
```

每个文件职责单一，易于维护和更新。

### 3. 统一格式

所有文件使用相同的格式规范：

- Frontmatter 元数据
- Markdown 表格
- 代码块示例
- 清晰的层级结构

---

## 📊 提示词内容概览

### 员工版系统提示词

**核心功能**:
- 数据查询（query_data 工具）
- 分析・分类レポート生成
- 優先度分類ガイド
- 推奨アクション生成
- 話術テンプレート

**响应格式**:
```markdown
該当件数：X 件

### 🔴 優先度：高（条件）
| 顧客名 | ランク | 状態 | 興味 | 最終連絡 |
|--------|--------|------|------|----------|
| 鈴木一郎 | 一般 | 新規 | 見積依頼 | - |

### 📊 統計
- 未連絡顧客：4 件
- フォローアップ必要：3 件

### 📋 推奨アクション
1. 新規リードに初回連絡
   - 話術：「こんにちは！...」
```

---

### 顾客版系统提示词

**核心功能**:
- 車両案内（在庫検索）
- 試乗予約受付
- サービス予約
- 購入相談

**响应格式**:
```markdown
該当件数：2 台

- **トヨタ RAV4 2024** — 税込 3,850,000 円 / ハイブリッド 
  [詳細・お問い合わせ](URL)
- **マツダ CX-5 2024** — 税込 3,699,000 円 / ガソリン
  [詳細・お問い合わせ](URL)

💡 おすすめ:
- RAV4 は今月最も人気です

📋 次のアクション:
- [試乗予約をする](URL)
```

---

### 工具定义

**包含内容**:
- `query_data` 工具完整定义
- `create_appointment_request` 工具定义
- 所有实体字段说明
- 过滤器运算符说明
- 日期相对指定说明
- 详细页面 URL 模式

**示例**:
```json
{
  "entity": "sales_leads",
  "action": "list",
  "filters": [
    {"field": "status", "op": "eq", "value": "new"},
    {"field": "created_at", "op": "gte", "value": "this_week"}
  ],
  "select": [
    "customer_id",
    "status",
    "vehicle_interest",
    "last_contact_at",
    "created_at",
    "lead_score"
  ],
  "orderBy": {"field": "last_contact_at", "dir": "asc"},
  "top": 50
}
```

---

### 实体参考

**包含内容**:
- 4 个核心实体的完整字段定义
- 所有枚举值说明
- 分析用字段指南
- 优先级分类指南
- 查询示例

**实体列表**:
1. `vehicles` - 车辆库存
2. `sales_leads` - 销售线索
3. `service_appointments` - 服务预约
4. `customers` - 顾客

---

### 响应模板

**包含内容**:
- 基本响应格式
- 分析レポート格式
- 10+ 种场景模板
- 话术模板集
- 错误处理模板

**场景列表**:
1. 営業リード查询
2. 車両在庫查询
3. 顧客フォローアップ
4. 予約状況查询
5. 顧客数查询
6. 販売実績查询
7. 初回連絡
8. フォローアップ
9. 試乗予約確認
10. 長期在庫促销

---

## 🔧 配置说明

### appsettings.json

```json
{
  "AiWindow": {
    "DealerName": "AI 自動車販売",
    "BusinessHours": "月〜土 9:00-18:00",
    "ProviderPriority": ["claude", "qwen", "gemini", "ollama"],
    "CliFirst": true,
    "CliTimeoutSeconds": 30,
    "EnableProactiveSuggestions": true,
    "MaxRecommendations": 5,
    "ChurnRiskThreshold": 0.7,
    "LeadFollowupDays": 3,
    "InventoryAgingDays": 90
  }
}
```

### 环境变量

```bash
# .env
ANTHROPIC_API_KEY=sk-ant-xxxxx
QWEN_API_KEY=sk-xxxxx
GOOGLE_API_KEY=xxxxx
```

---

## 🚀 运作原理

### 1. 提示词加载流程

```
AutoDealerChatService.SendMessageAsync()
    │
    ├─ 判断用户类型（员工/顾客）
    │
    ▼
LoadSystemPromptFromMd(isStaff)
    │
    ├─ skills/auto-dealer/_system-prompt-staff.md   (员工)
    └─ skills/auto-dealer/_system-prompt-customer.md (顾客)
    │
    ▼
读取文件内容
    │
    ├─ 移除 frontmatter (--- 部分)
    ├─ 验证必要セクション
    └─ 日志输出
    │
    ▼
返回系统提示词
    │
    ▼
发送给 AI 提供商
```

### 2. 代码位置

**核心服务**: `NetYamlForge/Services/AI/AutoDealerChatService.cs`

**关键方法**:
- `LoadSystemPromptFromMd(bool isStaff)` - 加载提示词
- `BuildSystemPrompt(bool isStaff, string? dbContextMarkdown)` - 构建完整提示
- `SendMessageAsync(string conversationId, string message)` - 处理消息

---

## 📈 效果评估指标

| 指标 | 定义 | 目标值 |
|------|------|--------|
| 采纳率 | 推奨アクション的採用率 | >60% |
| 转化率 | リード→成約的转化率 | >25% |
| 响应时间 | AI 平均応答時間 | <3 秒 |
| 満足度 | ユーザー評価（1-5） | >4.5 |
| 試乗予約率 | 相談→試乗的转化率 | >20% |
| フォローアップ率 | 24 時間以内フォローアップ率 | >95% |

---

## 📚 相关文档

### 设计文档

- [AI_PROMPT_DESIGN.md](./docs/AI_PROMPT_DESIGN.md) - 完整设计文档
- [ai-config.yaml](./NetYamlForge/projects/auto-dealer-demo/ai-config.yaml) - 配置指南

### 提示词文件

- [_system-prompt-staff.md](./NetYamlForge/skills/auto-dealer/_system-prompt-staff.md) - 员工版
- [_system-prompt-customer.md](./NetYamlForge/skills/auto-dealer/_system-prompt-customer.md) - 顾客版
- [_tools-definition.md](./NetYamlForge/skills/auto-dealer/_tools-definition.md) - 工具定义
- [_entity-reference.md](./NetYamlForge/skills/auto-dealer/_entity-reference.md) - 实体参考
- [_response-templates.md](./NetYamlForge/skills/auto-dealer/_response-templates.md) - 响应模板

---

## ✅ 验证清单

- [x] 创建独立的提示词目录 `skills/auto-dealer/`
- [x] 创建员工版系统提示词
- [x] 创建顾客版系统提示词
- [x] 创建工具定义文档
- [x] 创建实体参考文档
- [x] 创建响应模板文档
- [x] 创建项目配置文件
- [x] 创建设计文档
- [x] 验证文件结构
- [x] 验证引用关系

---

## 🔄 下一步行动

### 短期（1 周内）

1. **测试提示词加载**
   ```bash
   dotnet run --project NetYamlForge
   # 测试员工模式
   # 测试顾客模式
   ```

2. **验证响应格式**
   - 测试数据查询
   - 测试分析レポート
   - 测试链接生成

3. **收集反馈**
   - 销售员使用反馈
   - 顾客使用反馈

### 中期（1 个月内）

1. **优化提示词**
   - 根据反馈调整模板
   - 添加更多场景模板

2. **添加新功能**
   - 感情分析
   - 竞品分析
   - 趋势预测

3. **性能优化**
   - 减少响应时间
   - 优化查询效率

---

## 🎓 使用指南

### 开发者

1. **阅读设计文档**: [docs/AI_PROMPT_DESIGN.md](./docs/AI_PROMPT_DESIGN.md)
2. **了解配置**: [ai-config.yaml](./NetYamlForge/projects/auto-dealer-demo/ai-config.yaml)
3. **测试功能**: 运行项目并测试聊天功能

### 销售员（员工）

1. **登录系统**: 使用员工账号登录
2. **访问 AI 控制台**: `/auto-dealer-demo/Page/AIDashboard`
3. **查询数据**: 使用自然语言询问
4. **查看分析**: 获取分析レポート和推奨アクション

### 顾客

1. **访问网站**: 打开汽车销售网站
2. **开始聊天**: 点击聊天按钮
3. **咨询车辆**: 询问感兴趣的车型
4. **预约试驾**: 通过聊天完成预约

---

## 📞 技术支持

如有问题，请参考以下资源：

1. **设计文档**: [docs/AI_PROMPT_DESIGN.md](./docs/AI_PROMPT_DESIGN.md)
2. **配置指南**: [ai-config.yaml](./NetYamlForge/projects/auto-dealer-demo/ai-config.yaml)
3. **框架文档**: [QWEN.md](./QWEN.md)
4. **AGENTS.md**: [AGENTS.md](./AGENTS.md)

---

*实施完成日期：2026 年 4 月 1 日*
