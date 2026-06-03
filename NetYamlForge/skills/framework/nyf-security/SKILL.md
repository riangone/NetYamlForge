---
name: nyf-security
tier: 1
version: 1.0.0
description: |
  NetYamlForge 框架安全审查技能
  OWASP Top 10 + 框架特定安全检查
allowed-tools:
  - Bash
  - Read
  - Grep
  - Glob
  - AskUserQuestion
---

## Preamble (run first)

```bash
# 环境检查
cd /home/ubuntu/ws/NetYamlForge

# 安全检查日志目录
mkdir -p .gstack/security

echo "🔒 NetYamlForge 安全审查开始"
echo "审查范围：全项目代码库"
```

## Voice

**Tone:** 严谨、专业、安全优先、提供具体修复方案
**Writing rules:**
- 使用日语（框架标准语言）
- 每个问题必须包含风险等级（P0-P3）
- 提供具体的修复代码示例

## Completion Status Protocol

- **DONE** — 安全审查完成，无高危问题
- **DONE_WITH_CONCERNS** — 完成但有需要注意的中低危问题
- **BLOCKED** — 发现 P0 级问题，需要立即修复
- **NEEDS_CONTEXT** — 需要用户确认业务场景

## 安全检查清单

### P0（紧急 - 立即修复）

| 检查项 | 检测命令 | 修复方案 |
|-------|---------|---------|
| **SQL インジェクション** | `grep -rn '\$.*SELECT\|INSERT' --include='*.cs'` | 参数化查询 + SqlSafetyGuard |
| **シークレット漏洩** | `grep -rn 'ApiKey\|Password\|Secret' --include='*.cs' \| grep '='` | 环境变量 + UserSecrets |
| **認証バイパス** | `grep -rn '\[Authorize\]' --include='*.cs' -A5` | 正确的授权属性 |
| **XSS 脆弱性** | `grep -rn 'Html\|InnerHtml' --include='*.cs'` | HtmlEncoder 编码 |

### P1（高危 - 优先修复）

| 检查项 | 检测命令 | 修复方案 |
|-------|---------|---------|
| **CSRF 対策不足** | `grep -rn '\[ValidateAntiForgeryToken\]' --include='*.cs'` | 添加 AntiForgeryToken |
| **不適切なエラー処理** | `grep -rn 'catch.*Exception' --include='*.cs' -A3` | 通用错误页面 |
| **セッション管理** | `grep -rn 'Session\|Cookie' --include='*.cs'` | 安全的 Cookie 配置 |
| **YAML デシリアライゼーション** | `grep -rn 'YamlDotNet' --include='*.cs'` | 限制可反序列化类型 |

### P2（中危 - 计划修复）

| 检查项 | 检测命令 | 修复方案 |
|-------|---------|---------|
| **ログへの機密情報出力** | `grep -rn 'Log.*password\|token\|secret' --include='*.cs'` | 脱日志记录 |
| **不適切な CORS** | `grep -rn 'AddCors\|WithOrigins' --include='*.cs'` | 限制允许的源 |
| **レートリミット不足** | `grep -rn 'RateLimit' --include='*.cs'` | 添加速率限制 |
| **依存関係の脆弱性** | `dotnet list package --vulnerable` | 更新依赖包 |

### P3（低危 - 建议改进）

| 检查项 | 说明 |
|-------|------|
| セキュリティヘッダー | Content-Security-Policy 等 |
| 入力検証の強化 | 更严格的输入验证 |
| 監査ログの充実 | 更详细的审计日志 |

## 工作流程

### Step 1: 自动化扫描

```bash
# 1.1 SQL 注入扫描
echo "🔍 SQL インジェクション スキャン..."
grep -rn '\$.*SELECT\|INSERT\|UPDATE\|DELETE' \
    --include="*.cs" \
    NetYamlForge/Services/ NetYamlForge/Controllers/ | \
    grep -v 'SqlSafetyGuard\|SqlParameter\|@\|Dapper' > \
    .gstack/security/sql-injection.txt || true

SQL_INJECTION_COUNT=$(wc -l < .gstack/security/sql-injection.txt)
echo "  发现：$SQL_INJECTION_COUNT 件"

# 1.2 密钥泄露扫描
echo "🔍 シークレット漏洩 スキャン..."
grep -rn '(?i)(api[_-]?key|secret|password|token|credential)' \
    --include="*.cs" --include="*.json" --include="*.yml" \
    NetYamlForge/ | \
    grep -v '\.gstack\|bin/\|obj/' > \
    .gstack/security/secrets.txt || true

SECRETS_COUNT=$(wc -l < .gstack/security/secrets.txt)
echo "  发现：$SECRETS_COUNT 件"

# 1.3 依赖漏洞扫描
echo "🔍 依存関係脆弱性 スキャン..."
dotnet list package --vulnerable > \
    .gstack/security/vulnerable-packages.txt 2>&1 || true

VULN_PKG_COUNT=$(grep -c "vulnerable" .gstack/security/vulnerable-packages.txt || echo "0")
echo "  发现：$VULN_PKG_COUNT 件"
```

### Step 2: 框架特定检查

```bash
# 2.1 SqlSafetyGuard 使用情况
echo "🔍 SqlSafetyGuard 使用情况..."
TOTAL_SQL=$(grep -rc 'ExecuteAsync\|QueryAsync' \
    NetYamlForge/Services/ NetYamlForge/Controllers/ | \
    awk -F: '{sum+=$2} END {print sum}')

SAFE_SQL=$(grep -rc 'SqlSafetyGuard' \
    NetYamlForge/Services/ NetYamlForge/Controllers/ | \
    awk -F: '{sum+=$2} END {print sum}')

echo "  总 SQL 操作：$TOTAL_SQL"
echo "  安全guarded: $SAFE_SQL"
echo "  覆盖率：$(echo "scale=2; $SAFE_SQL * 100 / $TOTAL_SQL" | bc)%"

# 2.2 YAML 安全验证
echo "🔍 YAML 安全验证..."
find NetYamlForge/projects -name "*.yml" -o -name "*.yaml" | \
    while read file; do
        # 检查是否有危险的 YAML 标签
        if grep -q '!!' "$file"; then
            echo "  ⚠️  危险的 YAML 标签：$file"
        fi
    done

# 2.3 授权检查
echo "🔍 授权属性检查..."
grep -rn '\[Authorize\]' \
    --include="*.cs" \
    NetYamlForge/Controllers/ | \
    while read line; do
        file=$(echo "$line" | cut -d: -f1)
        # 检查是否有跳过授权的属性
        if grep -q '\[AllowAnonymous\]' "$file"; then
            echo "  ⚠️  混合授权：$file"
        fi
    done
```

### Step 3: 手动审查辅助

```bash
# 3.1 生成审查辅助文件
cat << 'EOF' > .gstack/security/review-checklist.md
# セキュリティレビューチェックリスト

## 認証・認可
- [ ] 全ての API エンドポイントに適切な認証設定
- [ ] 管理者機能には [Authorize(Roles="Admin")]
- [ ] 匿名アクセス許可箇所は明示的に [AllowAnonymous]

## 入力検証
- [ ] 全てのユーザー入力のバリデーション
- [ ] SQL パラメータは SqlSafetyGuard で検証
- [ ] YAML 読み込みは安全なデシリアライザを使用

## データ保護
- [ ] 機密情報は暗号化（DB 接続文字列等）
- [ ] シークレットは UserSecrets/環境変数で管理
- [ ] ログに機密情報を含めない

## セッション管理
- [ ] Cookie に Secure/HttpOnly/SameSite 設定
- [ ] セッションタイムアウト設定
- [ ] 適切な CSRF 対策

## エラー処理
- [ ] 詳細なエラー情報をクライアントに返さない
- [ ] 例外は適切にログ記録
- [ ] 汎用エラーページ表示
EOF

echo "📋 审查辅助清单已生成：.gstack/security/review-checklist.md"
```

### Step 4: 生成安全报告

```markdown
# NetYamlForge セキュリティ監査レポート

## 監査概要
- 監査日：{date}
- 監査範囲：全プロジェクト
- 監査ツール：gstack-nyf-security v1.0.0

## 発見された問題

### P0 - 緊急（{n} 件）
{P0 问题列表，包含文件路径和行号}

### P1 - 高危（{n} 件）
{P1 问题列表}

### P2 - 中危（{n} 件）
{P2 问题列表}

### P3 - 低危（{n} 件）
{P3 问题列表}

## 安全スコア

| カテゴリ | スコア | 目標 |
|---------|-------|------|
| SQL 安全 | {score}/100 | 100 |
| シークレット管理 | {score}/100 | 100 |
| 認証・認可 | {score}/100 | 100 |
| 入力検証 | {score}/100 | 100 |
| 依存関係 | {score}/100 | 100 |

**総合スコア：{total}/100**

## 修復計画

### 即時対応（1 日以内）
{P0 问题修复计划}

### 短期対応（1 週間以内）
{P1 问题修复计划}

### 中期対応（1 ヶ月以内）
{P2 问题修复计划}

### 長期改善（四半期以内）
{P3 问题改善计划}
```

## 自动修复脚本

### SQL 注入自动修复

```bash
# 检测并提示修复 SQL 注入
cat .gstack/security/sql-injection.txt | while read line; do
    file=$(echo "$line" | cut -d: -f1)
    lineno=$(echo "$line" | cut -d: -f2)
    
    echo "🔧 修复候选：$file:$lineno"
    echo "  原始代码：$(sed -n "${lineno}p" "$file")"
    echo "  修复建议：使用参数化查询"
done
```

### 密钥脱敏

```bash
# 检测 appsettings.json 中的密钥
for file in $(find . -name "appsettings*.json"); do
    if grep -q '"ApiKey".*"[^"]' "$file"; then
        echo "⚠️  $file 包含硬编码的 API Key"
        echo "  建议：使用 UserSecrets 或环境变量"
    fi
done
```

## 与其他技能的协作

| 技能 | 协作方式 |
|------|---------|
| `/nyf-review` | 审查结果共享 |
| `/nyf-ship` | 发布前安全检查 |
| `/nyf-test` | 安全测试用例 |

## Command Reference

| Command | Description |
|---------|-------------|
| `/nyf-security` | 完整安全审查 |
| `/nyf-security --quick` | 快速扫描（仅 P0/P1） |
| `/nyf-security --fix` | 扫描并自动修复 |
| `/nyf-security --report` | 仅生成报告 |

## Tips

1. **定期审查**：建议每周运行一次
2. **CI 集成**：在 CI/CD 中添加安全检查步骤
3. **依赖更新**：定期运行 `dotnet list package --vulnerable`
4. **安全培训**：团队成员了解常见安全问题
