---
name: nyf-review
tier: 1
version: 1.0.0
description: |
  NetYamlForge 框架代码审查技能
  在合并前检测 SQL 注入、YAML 安全、ORM 误用等问题
allowed-tools:
  - Bash
  - Read
  - Edit
  - Grep
  - Glob
  - AskUserQuestion
---

## Preamble (run first)

```bash
# 环境检查
cd /home/ubuntu/ws/NetYamlForge

# 获取当前分支和变更
CURRENT_BRANCH=$(git branch --show-current)
BASE_BRANCH="main"

if [ "$CURRENT_BRANCH" = "$BASE_BRANCH" ]; then
    echo "⚠️  警告：当前在 $BASE_BRANCH 分支上，无法审查"
    exit 0
fi

# 获取变更文件列表
git diff --name-only "origin/$BASE_BRANCH" > /tmp/nyf_review_files.txt
echo "📋 审查范围：$(wc -l < /tmp/nyf_review_files.txt) 个文件"
```

## Voice

**Tone:** 直接、具体、专业、重视安全、关注框架一致性
**Writing rules:**
- 使用日语（框架标准语言）
- 每条发现必须包含文件路径和行号
- 提供具体的修复建议
- 区分 CRITICAL / WARNING / INFO 级别

## Completion Status Protocol

完成状态报告标准：
- **DONE** — 所有审查步骤完成，无问题或已修复
- **DONE_WITH_CONCERNS** — 完成但有需要注意的问题
- **BLOCKED** — 无法继续（如：在 main 分支上）
- **NEEDS_CONTEXT** — 需要用户确认设计决策

## 审查清单

### CRITICAL（必须修复）

| 检查项 | 说明 | 检测方式 |
|-------|------|---------|
| **SQL インジェクション** | 文字列挿入での SQL 生成 | Grep: `\$.*FROM\|WHERE\|SELECT` |
| **YAML スキーマ違反** | 定義されていないフィールド | 对照 Schemas/*.json |
| **SqlSafetyGuard 不使用** | 安全ガードなしの識別子使用 | Grep: `ExecuteAsync.*\$` |
| **シークレット漏洩** | ハードコードされた API キー等 | Grep: `ApiKey.*=.*"[^"]` |
| **未検証ユーザー入力** | バリデーションなしの入力使用 | 检查 Controller 输入参数 |

### WARNING（推奨修正）

| 检查项 | 说明 | 检测方式 |
|-------|------|---------|
| **ログレベル不適切** | 機密情報のログ出力 | Grep: `Log.*password\|token` |
| **例外処理不足** | 一般的な catch 句 | Grep: `catch.*Exception` |
| **パフォーマンス** | N+1 クエリ、未最適化ループ | 检查 Dapper クエリ |
| **テスト不足** | 新機能にテストなし | 对照 *Tests.cs |

### INFO（参考）

| 检查项 | 说明 |
|-------|------|
| コードスタイル違反 | 命名規則、インデント |
| ドキュメント不足 | XML コメント、README |
| 依存関係更新 | NuGet パッケージ更新 |

## 工作流程

### Step 1: 变更文件分析

```bash
# 获取变更文件
cat /tmp/nyf_review_files.txt | while read file; do
    echo "🔍 审查：$file"
    
    # 检查文件类型
    case "$file" in
        *.cs)
            echo "  → C# 代码审查"
            ;;
        *.yml|*.yaml)
            echo "  → YAML 配置审查"
            ;;
        *.json)
            echo "  → JSON Schema 审查"
            ;;
    esac
done
```

### Step 2: SQL 安全检查

```bash
# 检测 SQL 注入风险
grep -rn '\$.*SELECT\|INSERT\|UPDATE\|DELETE' \
    --include="*.cs" \
    NetYamlForge/Services/ NetYamlForge/Controllers/ | \
    grep -v 'SqlSafetyGuard\|SqlParameter\|@'
```

### Step 3: YAML 安全检查

```bash
# 验证 YAML 文件语法
find NetYamlForge/projects -name "*.yml" -o -name "*.yaml" | \
    while read file; do
        # 检查 YAML 语法
        python3 -c "import yaml; yaml.safe_load(open('$file'))" 2>&1 || \
            echo "❌ YAML 语法错误：$file"
    done
```

### Step 4: 框架一致性检查

```bash
# 检查是否使用框架规定的服务
grep -rn 'new.*Repository\|new.*Service' \
    --include="*.cs" | \
    grep -v 'Services/' | \
    echo "⚠️  直接使用 new 创建服务，应使用 DI"
```

### Step 5: 生成审查报告

```markdown
## NetYamlForge 代码审查报告

### 审查概要
- 分支：{branch}
- 变更文件：{count} 个
- 审查时间：{timestamp}

### 发现的问题

#### CRITICAL（{n} 件）
{严重问题列表}

#### WARNING（{n} 件）
{警告问题列表}

#### INFO（{n} 件）
{建议问题列表}

### 修复建议
{具体修复步骤}

### 审查结论
- [ ] 可安全合并
- [ ] 需要修复后重新审查
- [ ] 需要设计讨论
```

## 自动修复

对于以下问题，提供自动修复：

### 1. SQL 字符串插值 → 参数化查询

**Before:**
```csharp
var sql = $"SELECT * FROM users WHERE id = '{userId}'";
```

**After:**
```csharp
var sql = "SELECT * FROM users WHERE id = @UserId";
var user = await _db.QueryFirstOrDefaultAsync<User>(sql, new { UserId = userId });
```

### 2. 缺少 SqlSafetyGuard 验证

**Before:**
```csharp
var column = userProvidedColumn;
var sql = $"SELECT {column} FROM table";
```

**After:**
```csharp
var column = userProvidedColumn;
SqlSafetyGuard.EnsureIdentifier(column, "user input");
var sql = $"SELECT {QuoteColumn(column)} FROM {QuoteTable(table)}";
```

## 与其他技能的协作

| 技能 | 协作方式 |
|------|---------|
| `/nyf-ship` | 审查通过后才能发布 |
| `/nyf-test` | 测试覆盖率检查联动 |
| `/nyf-security` | 深度安全审查委托 |
| `/nyf-doc` | 文档更新检查 |

## Command Reference

| Command | Description |
|---------|-------------|
| `/nyf-review` | 启动代码审查 |
| `/nyf-review --fix` | 审查并自动修复 |
| `/nyf-review --quick` | 仅 CRITICAL 检查 |
| `/nyf-review --full` | 完整审查（含 INFO） |

## Tips

1. **审查前运行测试**：确保现有测试通过
2. **小步审查**：建议 200 行以内的变更
3. **上下文理解**：先阅读相关设计文档
4. **建设性反馈**：问题 + 具体修复方案
