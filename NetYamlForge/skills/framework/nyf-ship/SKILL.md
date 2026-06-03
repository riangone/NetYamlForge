---
name: nyf-ship
tier: 1
version: 1.0.0
description: |
  NetYamlForge 框架发布技能
  自动化测试、审查、构建、发布流程
allowed-tools:
  - Bash
  - Read
  - Edit
  - Glob
  - Grep
---

## Preamble (run first)

```bash
# 环境检查
cd /home/ubuntu/ws/NetYamlForge

# 检查 .NET SDK
dotnet --version || {
    echo "❌ .NET SDK 未安装"
    exit 1
}

# 检查当前分支
CURRENT_BRANCH=$(git branch --show-current)
echo "📋 当前分支：$CURRENT_BRANCH"

# 检查变更
CHANGED_FILES=$(git diff --name-only HEAD)
if [ -z "$CHANGED_FILES" ]; then
    echo "ℹ️  无变更文件"
fi
```

## Voice

**Tone:** 专业、可靠、注重细节、自动化优先
**Writing rules:**
- 使用日语（框架标准语言）
- 报告具体数字和状态
- 失败时提供明确的修复步骤

## Completion Status Protocol

- **DONE** — 发布成功完成
- **DONE_WITH_CONCERNS** — 完成但有警告
- **BLOCKED** — 阻塞（测试失败、审查未通过）
- **NEEDS_CONTEXT** — 需要用户决策

## 工作流程

### Step 1: 预检查

```bash
# 1.1 分支检查
if [ "$CURRENT_BRANCH" = "main" ]; then
    echo "⚠️  警告：在 main 分支上发布需要特别确认"
fi

# 1.2 获取变更统计
ADDED=$(git diff --stat HEAD | tail -1 | awk '{print $4}')
DELETED=$(git diff --stat HEAD | tail -1 | awk '{print $6}')
echo "📊 变更统计：+$ADDED -$DELETED"

# 1.3 检查审查状态
if [ -f ".gstack/review-status.txt" ]; then
    REVIEW_STATUS=$(cat .gstack/review-status.txt)
    if [ "$REVIEW_STATUS" != "CLEARED" ]; then
        echo "⚠️  代码审查未通过：$REVIEW_STATUS"
    fi
fi
```

### Step 2: 测试执行

```bash
# 2.1 运行所有测试
echo "🧪 运行测试..."
dotnet test --logger "console;verbosity=detailed" \
    --results-directory /tmp/test-results \
    --collect:"XPlat Code Coverage" \
    2>&1 | tee /tmp/test-output.log

# 2.2 测试结果分析
TEST_RESULT=$?
if [ $TEST_RESULT -ne 0 ]; then
    echo "❌ 测试失败"
    # 分类失败
    grep -A5 "Failed!" /tmp/test-output.log
fi

# 2.3 测试覆盖率
COVERAGE=$(grep -o '"lineRate":"[0-9.]*"' /tmp/test-results/*/coverage.cobertura.xml | \
    head -1 | cut -d'"' -f4)
echo "📊 测试覆盖率：$(echo "$COVERAGE * 100" | bc)%"
```

### Step 3: 构建验证

```bash
# 3.1 Release 构建
echo "🔨 构建 Release..."
dotnet build -c Release --no-restore 2>&1 | tee /tmp/build-output.log

# 3.2 检查构建错误
if [ ${PIPESTATUS[0]} -ne 0 ]; then
    echo "❌ 构建失败"
    grep "error" /tmp/build-output.log
    exit 1
fi

# 3.3 检查警告
WARNING_COUNT=$(grep -c "warning" /tmp/build-output.log || echo "0")
echo "⚠️  构建警告：$WARNING_COUNT 件"
```

### Step 4: 代码质量检查

```bash
# 4.1 运行分析器
echo "🔍 代码分析..."
dotnet build /p:RunAnalyzers=true 2>&1 | tee /tmp/analyzer-output.log

# 4.2 检查分析器错误
ANALYZER_ERRORS=$(grep -c "error DCS" /tmp/analyzer-output.log || echo "0")
if [ "$ANALYZER_ERRORS" -gt 0 ]; then
    echo "❌ 分析器错误：$ANALYZER_ERRORS 件"
    grep "error DCS" /tmp/analyzer-output.log
fi
```

### Step 5: 版本管理

```bash
# 5.1 读取当前版本
CURRENT_VERSION=$(grep -o '<Version>[^<]*</Version>' NetYamlForge/NetYamlForge.csproj | \
    cut -d'>' -f2 | cut -d'<' -f1)
echo "📋 当前版本：$CURRENT_VERSION"

# 5.2 决定版本升级
# MICRO:  bug 修复
# MINOR:  新功能（向后兼容）
# MAJOR:  破坏性变更

# 检查变更类型
HAS_BREAKING=$(git log --oneline origin/main..HEAD | grep -c "BREAKING\|!" || echo "0")
HAS_FEATURE=$(git log --oneline origin/main..HEAD | grep -c "feat:" || echo "0")

if [ "$HAS_BREAKING" -gt 0 ]; then
    NEW_VERSION=$(echo $CURRENT_VERSION | awk -F. '{print ($1+1)".0.0"}')
    echo "📈 破坏性变更：版本升级到 $NEW_VERSION"
elif [ "$HAS_FEATURE" -gt 0 ]; then
    NEW_VERSION=$(echo $CURRENT_VERSION | awk -F. '{print $1"."($2+1)".0"}')
    echo "📈 新功能：版本升级到 $NEW_VERSION"
else
    NEW_VERSION=$(echo $CURRENT_VERSION | awk -F. '{print $1"."$2"."($3+1)}')
    echo "📈 Bug 修复：版本升级到 $NEW_VERSION"
fi
```

### Step 6: 生成发布说明

```markdown
## NetYamlForge リリース {version}

### リリース情報
- バージョン：{version}
- リリース日：{date}
- 変更ファイル：{count} 件
- テスト結果：{pass}/ {total}

### 変更内容

#### 新機能
{feat: 变更列表}

#### バグ修正
{fix: 变更列表}

#### 改善
{refactor/improve: 变更列表}

#### ドキュメント
{docs: 变更列表}

### テスト結果
- 合計：{total} 件
- 合格：{pass} 件
- カバレッジ：{coverage}%

### ビルド結果
- 警告：{warnings} 件
- 分析器エラー：{analyzer_errors} 件

### 確認チェックリスト
- [ ] テストがすべて合格
- [ ] ビルドが成功
- [ ] 分析器エラーなし
- [ ] ドキュメント更新済み
- [ ] 下位互換性確認済み
```

### Step 7: 提交和标签

```bash
# 7.1 提交变更
git add .
git commit -m "chore: release v$NEW_VERSION

$(cat /tmp/release-notes.md)"

# 7.2 创建标签
git tag -a "v$NEW_VERSION" -m "Release v$NEW_VERSION"

echo "✅ リリース準備完了：v$NEW_VERSION"
echo "次のステップ：git push origin $CURRENT_BRANCH && git push origin v$NEW_VERSION"
```

## 输出格式

### 发布就绪仪表板

```markdown
+====================================================================+
| RELEASE READINESS DASHBOARD                                        |
+====================================================================+
| Check          | Status  | Details                                |
|----------------|---------|----------------------------------------|
| Tests          | PASS    | 382 passed, 0 failed                   |
| Build          | PASS    | 0 errors, 12 warnings                  |
| Analyzers      | PASS    | 0 errors                               |
| Review         | CLEAR   | Eng review passed                      |
| Coverage       | 85%     | Target: 80% ✓                          |
+--------------------------------------------------------------------+
| VERDICT: READY TO SHIP — v1.2.3                                    |
+====================================================================+
```

### 测试覆盖率 ASCII 图

```markdown
TEST COVERAGE BY MODULE
===========================
[+] NetYamlForge.Services
│   ├── QueryExecutionService.cs — 92% ★★★
│   ├── SqlSafetyGuard.cs — 100% ★★★
│   └── DynamicCrudRepository.cs — 78% ★★
│
[+] NetYamlForge.Controllers
│   ├── AIController.cs — 85% ★★★
│   └── DynamicEntityController.cs — 71% ★★
│
[+] NetYamlForge.Tests
│   └── Coverage — 100% ★★★

OVERALL: 85% (Target: 80%) ✓
```

## 与其他技能的协作

| 技能 | 协作方式 |
|------|---------|
| `/nyf-review` | 发布前必须通过审查 |
| `/nyf-test` | 测试执行委托 |
| `/nyf-doc` | 发布后自动更新文档 |
| `/nyf-changelog` | 生成 CHANGELOG |

## Command Reference

| Command | Description |
|---------|-------------|
| `/nyf-ship` | 标准发布流程 |
| `/nyf-ship --dry-run` | 预演（不实际提交） |
| `/nyf-ship --skip-tests` | 跳过测试（不推荐） |
| `/nyf-ship --hotfix` | 热修复模式（跳过版本升级） |

## Tips

1. **小步发布**：建议每日发布，避免大变更累积
2. **测试先行**：确保新功能的测试覆盖率 >80%
3. **语义化版本**：严格遵守 SemVer 规范
4. **发布检查表**：每次发布前确认所有检查项
