# 汽车销售 AI 提示词系统设计文档

> **版本**: 3.0（完全独立版）
> **最后更新**: 2026 年 4 月 1 日

---

## 📋 目录

- [概述](#概述)
- [架构设计](#架构设计)
- [文件结构](#文件结构)
- [运作原理](#运作原理)
- [配置说明](#配置说明)
- [使用指南](#使用指南)

---

## 概述

### 设计理念

本系统采用**完全独立**的提示词文件结构，将汽车销售领域的专用提示词与框架通用提示词分离，实现：

1. ✅ **关注点分离**: 框架 AI 与汽车销售 AI 各自独立
2. ✅ **可维护性**: 修改汽车销售提示词不影响框架
3. ✅ **可扩展性**: 可轻松添加其他行业的提示词包
4. ✅ **版本控制**: 每个提示词包可独立版本管理

### 核心定位

| 角色 | 核心定位 | 主要功能 |
|------|---------|---------|
| **框架 AI** | 代码生成・CLI 操作 | `qwen code`、`claude code` 等 |
| **汽车销售 AI** | 数据查询・分析 | `query_data`、销售分析 |

---

## 架构设计

### 系统架构图

```
┌─────────────────────────────────────────────────────────┐
│                    NetYamlForge 框架                      │
│  ┌─────────────────────────────────────────────────────┐ │
│  │  AIController.cs                                    │ │
│  │  - /api/AI/chat (框架 AI)                           │ │
│  │  - /api/AI/skills (技能加载)                        │ │
│  └─────────────────────────────────────────────────────┘ │
│                           │                               │
│  ┌─────────────────────────────────────────────────────┐ │
│  │  SkillLoader.cs                                     │ │
│  │  - 加载 skills/_system-prompt.md (框架通用)          │ │
│  └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│              auto-dealer-demo 项目专用 AI                  │
│  ┌─────────────────────────────────────────────────────┐ │
│  │  AutoDealerChatService.cs                           │ │
│  │  - 专用聊天服务                                      │ │
│  │  - 加载 skills/auto-dealer/ 下的独立提示词           │ │
│  └─────────────────────────────────────────────────────┘ │
│                           │                               │
│  ┌─────────────────────────────────────────────────────┐ │
│  │  提示词文件集 (完全独立)                             │ │
│  │  - _system-prompt-staff.md                          │ │
│  │  - _system-prompt-customer.md                       │ │
│  │  - _tools-definition.md                             │ │
│  │  - _entity-reference.md                             │ │
│  │  - _response-templates.md                           │ │
│  └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

---

## 文件结构

### 完整目录结构

```
NetYamlForge/
├── skills/                          # 框架通用提示词
│   ├── _system-prompt.md           # 框架系统提示词（代码生成等）
│   ├── build-check.md
│   ├── explain-project.md
│   └── ...
│
├── skills/auto-dealer/              # 汽车销售专用提示词（独立）⭐
│   ├── _system-prompt-staff.md     # 员工版系统提示词
│   ├── _system-prompt-customer.md  # 顾客版系统提示词
│   ├── _tools-definition.md        # 工具定义（独立）
│   ├── _entity-reference.md        # 实体字段参考（独立）
│   └── _response-templates.md      # 响应模板（独立）
│
├── NetYamlForge/
│   ├── Controllers/
│   │   ├── AIController.cs         # 框架 AI 控制器
│   │   └── ...
│   │
│   ├── Services/AI/
│   │   ├── AutoDealerChatService.cs  # 汽车销售 AI 服务
│   │   ├── SkillLoader.cs            # 技能加载器
│   │   └── ...
│   │
│   └── projects/
│       └── auto-dealer-demo/
│           ├── project.yaml          # 项目主配置
│           ├── entities/*.yml        # 实体定义
│           └── ai-config.yaml        # AI 专用配置（新增）⭐
│
└── docs/
    └── AI_PROMPT_DESIGN.md          # 本设计文档
```

---

## 运作原理

### 1. 提示词加载流程

```
用户请求
   │
   ▼
AutoDealerChatService
   │
   ├─ 判断用户类型（员工/顾客）
   │
   ▼
加载对应提示词文件
   │
   ├─ skills/auto-dealer/_system-prompt-staff.md   (员工)
   └─ skills/auto-dealer/_system-prompt-customer.md (顾客)
   │
   ▼
引用独立文档
   ├─ _tools-definition.md      (工具定义)
   ├─ _entity-reference.md      (实体参考)
   └─ _response-templates.md    (响应模板)
   │
   ▼
生成最终系统提示词
   │
   ▼
发送给 AI 提供商 (Claude/Qwen/Gemini 等)
```

### 2. 代码示例

#### AutoDealerChatService.cs - 提示词加载

```csharp
private string LoadSystemPromptFromMd(bool isStaff)
{
    var skillsDir = FindSkillsDirectory("auto-dealer");
    
    if (skillsDir == null)
    {
        _logger.LogWarning("skills/auto-dealer ディレクトリが見つかりません");
        return BuildFallbackSystemPrompt(isStaff);
    }

    var fileName = isStaff 
        ? "_system-prompt-staff.md" 
        : "_system-prompt-customer.md";
    
    var filePath = Path.Combine(skillsDir, fileName);

    if (!File.Exists(filePath))
    {
        _logger.LogWarning("プロンプトファイル {File} が見つかりません", filePath);
        return BuildFallbackSystemPrompt(isStaff);
    }

    var content = File.ReadAllText(filePath).Trim();
    
    // frontmatter を除去
    if (content.StartsWith("---"))
    {
        var end = content.IndexOf("---", 3);
        if (end >= 0)
        {
            content = content[(end + 3)..].Trim();
        }
    }

    _logger.LogInformation(
        "【システムプロンプト読込】ファイル：{File}, isStaff={IsStaff}, 文字数={Length}", 
        filePath, isStaff, content.Length);

    return content;
}
```

### 3. 与框架 AI 的关系

```
框架 AI (skills/_system-prompt.md)
   │
   ├─ 用途：代码生成、CLI 操作、项目开发
   ├─ 工具：Read, Write, Edit, Bash, Git
   └─ 场景：开发者使用
   
汽车销售 AI (skills/auto-dealer/*)
   │
   ├─ 用途：数据查询、销售分析、客户支持
   ├─ 工具：query_data, create_appointment_request
   └─ 场景：销售员/顾客使用
```

**两者完全独立，互不影响。**

---

## 配置说明

### appsettings.json 配置

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

### 详细配置说明

请参考：[ai-config.yaml](./NetYamlForge/projects/auto-dealer-demo/ai-config.yaml)

---

## 使用指南

### 1. 员工模式示例

**用户**: 「今天应该联系哪些客户？」

**AI 响应流程**:

1. 加载 `_system-prompt-staff.md`
2. 调用 `query_data` 工具获取销售线索
3. 根据 `_entity-reference.md` 的字段定义分析数据
4. 按照 `_response-templates.md` 的模板生成响应
5. 包含详细链接和分析报告

**响应示例**:

```markdown
## 本日連絡すべき顧客

### 🔴 優先度：高（3 日以上未連絡）

> 該当件数：**3 件**

| 顧客名 | ランク | 状態 | 興味 | 最終連絡 |
|--------|--------|------|------|----------|
| **鈴木一郎** | 一般 | 新規 | 見積依頼 | - |
| **小林大輔** | 一般 | 新規 | 価格問い合わせ | - |
| **中村愛** | シルバー | 連絡済み | 車両問い合わせ | 3/23 |

---

### 📊 統計

- **未連絡顧客**: 4 件
- **フォローアップ必要**: 3 件
- **合計**: 8 件

---

### 📋 推奨アクション

1. **新規リードに初回連絡**
   - 対象：鈴木一郎、小林大輔
   - 話術：「こんにちは！AI 自動車販売の...」

2. **3 日以上未連絡のフォローアップ**
   - 対象：中村愛
   - 話術：「先日お越しいただきました...」

---

### 🔗 詳細ページリンク

- [销售线索一覧](/auto-dealer-demo/DynamicEntity/Index?entity=sales_leads)
- [顧客マスタ一覧](/auto-dealer-demo/DynamicEntity/Index?entity=customers)
```

---

### 2. 顾客模式示例

**用户**: 「想看 SUV 的库存」

**AI 响应流程**:

1. 加载 `_system-prompt-customer.md`
2. 调用 `query_data` 工具获取车辆库存
3. 按照顾客友好的格式生成响应
4. 包含详细链接和预约按钮

**响应示例**:

```markdown
該当件数：**5 台**

- **トヨタ RAV4 2024** — 税込 3,850,000 円 / ハイブリッド — [詳細・お問い合わせ](URL)
- **マツダ CX-5 2024** — 税込 3,699,000 円 / ガソリン — [詳細・お問い合わせ](URL)
- **ホンダ CR-V 2024** — 税込 3,950,000 円 / ハイブリッド — [詳細・お問い合わせ](URL)

💡 **おすすめ**:
- RAV4 は今月最も人気です（閲覧数 28 回）
- 試乗予約がおすすめです

📋 **次のアクション**:
- [試乗予約をする](/auto-dealer-demo/Page/Appointments)
- [お問い合わせ](/auto-dealer-demo/Page/ChatDetail)
```

---

### 3. 添加新的提示词包

如需添加其他行业的提示词包（如房地产、医疗等）：

```
skills/
├── auto-dealer/          # 汽车销售
├── real-estate/          # 房地产（新增）
│   ├── _system-prompt-staff.md
│   ├── _system-prompt-customer.md
│   ├── _tools-definition.md
│   ├── _entity-reference.md
│   └── _response-templates.md
└── healthcare/           # 医疗（新增）
    └── ...
```

**步骤**:

1. 在 `skills/` 下创建新目录
2. 复制提示词文件模板
3. 修改内容适应新行业
4. 在项目中配置使用

---

## 最佳实践

### 1. 提示词文件组织

✅ **推荐**:
- 每个文件职责单一
- 使用 frontmatter 标注元数据
- 包含详细的示例和模板
- 使用 Markdown 表格提高可读性

❌ **避免**:
- 单个文件过大（>1000 行）
- 混合多个主题
- 缺少示例说明

### 2. 工具定义

✅ **推荐**:
- 提供完整的参数说明
- 包含多个使用示例
- 说明字段的所有可能值
- 提供错误处理指南

### 3. 响应模板

✅ **推荐**:
- 使用一致的格式
- 包含占位符说明
- 提供多种场景的模板
- 包含错误处理模板

---

## 测试与验证

### 单元测试

```csharp
[Fact]
public void LoadSystemPrompt_StaffMode_ReturnsCorrectPrompt()
{
    var service = new AutoDealerChatService(...);
    var prompt = service.LoadSystemPromptFromMd(isStaff: true);
    
    Assert.Contains("query_data", prompt);
    Assert.Contains("分析・分類レポート", prompt);
    Assert.Contains("_tools-definition.md", prompt);
}
```

### 集成测试

```bash
# 测试员工模式
curl -X POST http://localhost:5000/api/AI/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "今日連絡すべき顧客は？", "channel": "staff"}'

# 测试顾客模式
curl -X POST http://localhost:5000/api/AI/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "SUV の在庫を見せて", "channel": "customer"}'
```

---

## 故障排查

### 问题：提示词未加载

**症状**: AI 响应不符合预期格式

**解决方案**:
1. 检查 `skills/auto-dealer/` 目录是否存在
2. 确认提示词文件是否存在
3. 查看日志中的加载信息
4. 验证文件权限

### 问题：工具调用失败

**症状**: AI 无法调用 `query_data`

**解决方案**:
1. 检查 `_tools-definition.md` 是否包含正确的工具定义
2. 确认实体字段定义正确
3. 验证数据库连接

---

## 相关文档

- [系统提示词（员工）](./NetYamlForge/skills/auto-dealer/_system-prompt-staff.md)
- [系统提示词（顾客）](./NetYamlForge/skills/auto-dealer/_system-prompt-customer.md)
- [工具定义](./NetYamlForge/skills/auto-dealer/_tools-definition.md)
- [实体参考](./NetYamlForge/skills/auto-dealer/_entity-reference.md)
- [响应模板](./NetYamlForge/skills/auto-dealer/_response-templates.md)
- [AI 配置指南](./NetYamlForge/projects/auto-dealer-demo/ai-config.yaml)

---

*最后更新：2026 年 4 月 1 日*
