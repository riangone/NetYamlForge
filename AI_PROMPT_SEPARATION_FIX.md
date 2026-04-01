# AI 提示词分离修复报告

**日期**: 2026 年 4 月 1 日  
**版本**: 1.0  
**状态**: ✅ 已完成

---

## 📋 问题概述

### 问题 1：全局 AI 和汽车销售 AI 提示词混淆

**现象**：汽车销售 AI 助手没有按照提示词要求返回分析报表格式

**根本原因**：
1. `skills/_system-prompt.md`（全局 AI 用）内容是汽车销售的旧版本，而非框架开发内容
2. 汽车销售 AI 有多个冗余提示词文件，导致版本混乱
3. 提示词缺少明确的判断指南，AI 不知道何时需要分析报表

### 问题 2：提示词文件冗余

```
projects/auto-dealer-demo/
├── AI-PROMPT-ENHANCED.md          ⚠️ 冗余（未使用）
├── AI-SYSTEM-PROMPT-ACTIVE.md     ⚠️ 冗余（未使用）
├── AI-ENHANCEMENT-PLAN.md         ⚠️ 冗余（未使用）
├── AI-ENHANCEMENT-README.md       ⚠️ 冗余（未使用）
├── AI-ROLE-UPGRADE-REPORT.md      ⚠️ 冗余（未使用）
└── IMPLEMENTATION-PHASES.md       ⚠️ 冗余（未使用）
```

---

## 🔧 修复方案

### 方案 1：分离全局 AI 和子项目 AI 提示词

**修复前**：
```
skills/
├── _system-prompt.md              ❌ 内容是汽车销售（错误）
└── auto-dealer/
    └── _system-prompt-staff.md    ✅ 汽车销售员工用
```

**修复后**：
```
skills/
├── _system-prompt.md              ✅ 框架开发 AI 用（已修复）
└── auto-dealer/
    ├── _system-prompt-staff.md    ✅ 汽车销售员工用（v3.1 优化）
    ├── _system-prompt-customer.md ✅ 汽车销售客户用
    ├── _tools-definition.md       ✅ 工具定义
    ├── _entity-reference.md       ✅ 实体定义
    └── _response-templates.md     ✅ 响应模板
```

---

## 📝 修改详情

### 1. 删除冗余文件

```bash
rm projects/auto-dealer-demo/AI-PROMPT-ENHANCED.md
rm projects/auto-dealer-demo/AI-SYSTEM-PROMPT-ACTIVE.md
rm projects/auto-dealer-demo/AI-ENHANCEMENT-PLAN.md
rm projects/auto-dealer-demo/AI-ENHANCEMENT-README.md
rm projects/auto-dealer-demo/AI-ROLE-UPGRADE-REPORT.md
rm projects/auto-dealer-demo/IMPLEMENTATION-PHASES.md
```

### 2. 修复全局 AI 提示词

**文件**: `skills/_system-prompt.md`

**新职责定位**:
- ✅ 代码开发：C# 代码创建、修正、重构
- ✅ YAML 设定：Entity YAML、页面设定、项目设定
- ✅ 框架结构：项目初始化、脚手架、Schema 验证

**权限限制**:
- ❌ 禁止访问 auto-dealer-demo 业务数据
- ❌ 禁止 SQL 注入风险代码
- ❌ 禁止违反框架规范

### 3. 优化汽车销售 AI 提示词

**文件**: `skills/auto-dealer/_system-prompt-staff.md` (v3.1)

**新增内容**:

#### 🎯 核心原则
> AI 自ら判断して分析せよ
> 
> 1. **第一段階**: query_data ツールで DB から最新情報を取得
> 2. **第二段階**: 取得したデータを**AI 自ら分析・分類**し、洞察と推奨アクションを追加

#### 🤖 判断指南表格

| ユーザー発話 | 判定理由 | 必要な分析 |
|-------------|----------|-----------|
| 「今日連絡すべき顧客は？」 | 優先度分類が必要 | 未連絡期間× リードスコア |
| 「優先度の高いリードは？」 | 優先度分類が必要 | リードスコア× 経過日数 |
| 「今週の予約を教えて」 | 期間指定＋一覧 | 日付別分類、準備事項 |
| 「今月の販売状況は？」 | 期間指定＋分析 | 目標対比、前月比 |
| 「フォローアップが必要な顧客は？」 | 優先度分類が必要 | 未連絡期間× 顧客ランク |
| 「長期在庫の車両は？」 | リスク分析が必要 | 在庫期間× 資金コスト |
| 「VIP 顧客は何人？」 | 件数＋リスト | ランク別分類、購入履歴 |
| 「状況はどうなってる？」 | 全体分析が必要 | 統計＋傾向＋課題 |

#### 💡 判断のポイント

**AI 自ら考えてください：**

1. **優先度・分類が求められているか？**
   - 「べき」「必要」「優先」「重要」→ 分析レポート必須

2. **期間・状況が求められているか？**
   - 「今日」「今週」「今月」「状況」→ 分析レポート必須

3. **単純な一覧要求か？**
   - 「一覧」「全部」「全て」→ 簡潔な一覧で OK

4. **単一の特定レコードか？**
   - 「〜の情報を」「〜を教えて」（特定）→ 詳細情報＋関連データ

---

## 📁 修改文件清单

| 文件路径 | 操作 | 说明 |
|---------|------|------|
| `skills/_system-prompt.md` | ✏️ 重写 | 从汽车销售改为框架开发 AI |
| `skills/auto-dealer/_system-prompt-staff.md` | ✏️ 优化 | v3.0 → v3.1，新增判断指南 |
| `projects/auto-dealer-demo/AI-PROMPT-ENHANCED.md` | ❌ 删除 | 冗余文件 |
| `projects/auto-dealer-demo/AI-SYSTEM-PROMPT-ACTIVE.md` | ❌ 删除 | 冗余文件 |
| `projects/auto-dealer-demo/AI-ENHANCEMENT-PLAN.md` | ❌ 删除 | 冗余文件 |
| `projects/auto-dealer-demo/AI-ENHANCEMENT-README.md` | ❌ 删除 | 冗余文件 |
| `projects/auto-dealer-demo/AI-ROLE-UPGRADE-REPORT.md` | ❌ 删除 | 冗余文件 |
| `projects/auto-dealer-demo/IMPLEMENTATION-PHASES.md` | ❌ 删除 | 冗余文件 |

---

## 🎯 预期效果

### 全局 AI 助手（框架开发）

**测试查询** | **预期响应**
-------------|-------------
「创建一个新的 Entity」 | 生成 YAML 配置和代码框架
「运行测试」 | 执行 dotnet test 命令
「解释项目结构」 | 说明 NetYamlForge 架构

### 汽车销售 AI 助手（业务查询）

**测试查询** | **预期响应**
-------------|-------------
「今日連絡すべき顧客は？」 | 🔴 分析レポート形式（優先度分類＋統計＋推奨アクション）
「優先度の高いリードは？」 | 🔴 分析レポート形式（重要/普通/低 分類）
「今週の予約を教えて」 | 🔴 分析レポート形式（日付別分類＋準備事項）
「今月の販売状況は？」 | 🔴 分析レポート形式（目標対比＋前月比）
「全顧客の一覧」 | 🟡 簡潔な一覧のみ（件数＋リスト）
「田中さんの情報」 | 🟢 詳細情報＋関連データ
「RAV4 の価格を教えて」 | 🟢 単一レコード詳細

---

## 🧪 验证步骤

### 1. 启动应用

```bash
cd NetYamlForge
dotnet run
```

### 2. 测试全局 AI

访问：`/AI/Index`

**测试查询**:
```
创建一个新的钩子代码
```

**预期**: 生成钩子代码框架，使用 `scaffold-hook` 技能

### 3. 测试汽车销售 AI

访问：`/auto-dealer-demo/Page/AIDashboard`

**测试查询**:
```
今日連絡すべき顧客は？
```

**预期响应格式**:
```markdown
## 本日連絡すべき顧客

### 🔴 優先度：高（3 日以上未連絡）
> 該当件数：**3 件**

| 顧客名 | ランク | 状態 | 興味 | 最終連絡 |
|--------|--------|------|------|----------|
| **鈴木一郎** | 一般 | 新規 | 見積依頼 | - |

### 📊 統計
- **未連絡顧客**: 4 件
- **フォローアップ必要**: 3 件
- **合計**: 8 件

### 📋 推奨アクション
1. **新規リードに初回連絡**
   - 理由：24 時間以内の連絡で成約率が 3 倍
```

---

## 📊 效果评估指标

| 指标 | 修复前 | 修复后目标 |
|------|--------|-----------|
| 分析报表格式遵守率 | <50% | >90% |
| 响应格式一致性 | 混乱 | 统一 |
| 提示词版本数量 | 7 个 | 2 个（全局 + 汽车销售） |
| AI 自主判断准确率 | 低 | 高 |

---

## 🔗 相关文档

- [AI 助手完全指南](docs/ai-assistant-guide.md)
- [汽车销售 AI 配置](NetYamlForge/projects/auto-dealer-demo/ai-config.yaml)
- [响应模板](NetYamlForge/skills/auto-dealer/_response-templates.md)
- [工具定义](NetYamlForge/skills/auto-dealer/_tools-definition.md)

---

## 📌 注意事项

### 部署后

1. **重启应用**：提示词文件在应用启动时加载
2. **清除缓存**：浏览器缓存可能导致旧提示词生效
3. **日志监控**：检查 `LoadSystemPromptFromMd` 方法的日志输出

### 开发模式

热重载功能会在文件修改后 500ms 自动重新加载提示词。

---

*报告完成时间：2026 年 4 月 1 日*
