---
name: nyf-scaffold
tier: 1
version: 1.0.0
description: |
  NetYamlForge 脚手架生成技能
  根据数据库或 YAML 定义自动生成实体、控制器、服务、测试代码
allowed-tools:
  - Bash
  - Read
  - Write
  - Edit
  - Glob
  - Grep
  - AskUserQuestion
---

## Preamble (run first)

```bash
# 环境检查
cd /home/ubuntu/ws/NetYamlForge

# 检查项目结构
if [ ! -d "NetYamlForge/projects" ]; then
    echo "❌ NetYamlForge 项目结构不存在"
    exit 1
fi

# 获取可用项目列表
echo "📋 可用项目列表:"
ls -1 NetYamlForge/projects/

# 检查 CLI 工具
dotnet run --project NetYamlForge -- --help | grep scaffold || {
    echo "⚠️  脚手架命令不可用"
}
```

## Voice

**Tone:** 专业、引导式、注重最佳实践
**Writing rules:**
- 使用日语（框架标准语言）
- 提供多个选项供用户选择
- 生成代码后说明使用方法

## Completion Status Protocol

- **DONE** — 脚手架生成完成
- **DONE_WITH_CONCERNS** — 生成但有注意事项
- **BLOCKED** — 阻塞（如：项目不存在）
- **NEEDS_CONTEXT** — 需要用户提供额外信息

## 工作流程

### Step 1: 确定生成目标

通过询问用户确定：

```markdown
## 脚手架生成向导

### 1. 选择项目
- [ ] auto-dealer-demo（汽车销售示例）
- [ ] framework-showcase（框架展示）
- [ ] 其他：_______

### 2. 选择生成类型
- [ ] entity（实体定义 + CRUD）
- [ ] hook（业务钩子）
- [ ] batch-job（批处理作业）
- [ ] page（自定义页面）
- [ ] controller（自定义控制器）
- [ ] service（自定义服务）

### 3. 数据来源
- [ ] 从现有数据库表生成
- [ ] 从 YAML 定义生成
- [ ] 从零开始创建
```

### Step 2: 实体生成（从数据库）

```bash
# 2.1 获取数据库表列表
dotnet run --project NetYamlForge -- \
    --scaffold-entities \
    --project=$PROJECT_NAME \
    --dry-run

# 2.2 选择要生成的表
# 使用 AskUserQuestion 让用户选择

# 2.3 执行生成
dotnet run --project NetYamlForge -- \
    --scaffold-entities \
    --project=$PROJECT_NAME \
    --no-overwrite

# 2.4 验证生成的 YAML
echo "🔍 验证生成的 YAML..."
for file in NetYamlForge/projects/$PROJECT_NAME/entities/*.yml; do
    python3 -c "import yaml; yaml.safe_load(open('$file'))" && \
        echo "  ✓ $(basename $file)" || \
        echo "  ❌ $(basename $file)"
done
```

### Step 3: 钩子生成

```bash
# 3.1 确定钩子类型
cat << EOF
## 选择钩子类型

### 实体钩子
- BeforeInsert / AfterInsert
- BeforeUpdate / AfterUpdate
- BeforeDelete / AfterDelete

### 业务钩子
- Validate（验证）
- Transform（数据转换）
- Notify（通知）
- Log（日志记录）
EOF

# 3.2 生成钩子代码
dotnet run --project NetYamlForge -- \
    --scaffold-hook \
    --name=$HOOK_NAME \
    --project=$PROJECT_NAME \
    --with-tests

# 3.3 编辑生成的钩子
echo "📝 编辑钩子代码：NetYamlForge/projects/$PROJECT_NAME/hooks/$HOOK_NAME.cs"
```

### Step 4: 批处理作业生成

```bash
# 4.1 确定作业类型
cat << EOF
## 选择作业类型

### 数据同步
- 定期从外部 API 获取数据
- 数据库间数据同步

### 报表生成
- 日报/周报/月报
- PDF/Excel 导出

### 通知作业
- 邮件通知
- 过期提醒

### 清理作业
- 日志清理
- 临时数据清理
EOF

# 4.2 生成作业
dotnet run --project NetYamlForge -- \
    --scaffold-batch-job \
    --name=$JOB_NAME \
    --project=$PROJECT_NAME
```

### Step 5: 测试生成

```bash
# 5.1 为新代码生成测试
echo "🧪 生成测试代码..."

# 5.2 运行生成的测试
dotnet test --filter "FullyQualifiedName~$PROJECT_NAME" \
    --logger "console;verbosity=normal"
```

## 生成模板

### 实体 YAML 模板

```yaml
name: {entity_name}
displayName: {display_name}
description: {description}

columns:
  - name: id
    type: string
    primaryKey: true
    label: ID
  - name: name
    type: string
    required: true
    label: 名称
    searchable: true
  - name: created_at
    type: datetime
    label: 创建时间
    sortable: true

forms:
  name:
    type: text
    required: true
    label: 名称
    editable: true

paging:
  pageSize: 20
  mode: numbered
```

### 钩子代码模板

```csharp
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.{Project}.Hooks;

/// <summary>
/// {hook_name} 钩子
/// 用途：{description}
/// </summary>
public class {HookName}Hook : IHook
{
    private readonly ILogger<{HookName}Hook> _logger;

    public {HookName}Hook(ILogger<{HookName}Hook> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HookContext context)
    {
        _logger.LogInformation("{HookName} 执行开始", nameof({HookName}));

        try
        {
            // TODO: 实现钩子逻辑
            await Task.CompletedTask;

            _logger.LogInformation("{HookName} 执行完成", nameof({HookName}));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{HookName} 执行失败", nameof({HookName}));
            throw;
        }
    }
}
```

### 测试代码模板

```csharp
using Xunit;
using Moq;

namespace NetYamlForge.Tests.Projects.{Project};

public class {HookName}HookTests
{
    private readonly Mock<ILogger<{HookName}Hook>> _loggerMock;
    private readonly {HookName}Hook _hook;

    public {HookName}HookTests()
    {
        _loggerMock = new Mock<ILogger<{HookName}Hook>>();
        _hook = new {HookName}Hook(_loggerMock.Object);
    }

    [Fact]
    public async Task InvokeAsync_正常系_成功すること ()
    {
        // Arrange
        var context = new HookContext
        {
            Entity = "test",
            Action = "insert"
        };

        // Act
        await _hook.InvokeAsync(context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("実行完成")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }
}
```

## 输出格式

### 生成报告

```markdown
## 脚手架生成报告

### 生成概要
- 项目：{project}
- 类型：{type}
- 生成时间：{timestamp}

### 生成的文件

#### 实体定义（{n} 件）
- {entity1}.yml
- {entity2}.yml

#### 钩子代码（{n} 件）
- {Hook1}.cs
- {Hook1}Tests.cs

#### 批处理作业（{n} 件）
- {Job1}.cs
- {Job1}Tests.cs

### 下一步

1. 编辑生成的 YAML 文件
2. 实现钩子逻辑
3. 运行测试验证
4. 提交代码
```

## 与其他技能的协作

| 技能 | 协作方式 |
|------|---------|
| `/nyf-review` | 生成后自动审查代码质量 |
| `/nyf-test` | 运行生成的测试 |
| `/nyf-doc` | 生成 API 文档 |

## Command Reference

| Command | Description |
|---------|-------------|
| `/nyf-scaffold` | 启动脚手架向导 |
| `/nyf-scaffold entity` | 生成实体 |
| `/nyf-scaffold hook` | 生成钩子 |
| `/nyf-scaffold job` | 生成批处理作业 |
| `/nyf-scaffold --from-db` | 从数据库生成 |

## Tips

1. **从现有数据库开始**：使用 `--scaffold-entities` 快速生成
2. **小步迭代**：一次生成一个实体，逐步完善
3. **测试先行**：生成后立即运行测试
4. **代码审查**：生成的代码也需要审查
