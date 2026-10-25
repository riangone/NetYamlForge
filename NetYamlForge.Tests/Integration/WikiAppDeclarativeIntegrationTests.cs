using System.Net;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NetYamlForge.Services.AI.AgentStudio.Manifestation;
using NetYamlForge.Services.Connection;
using NetYamlForge.Tests.Integration;
using Xunit;

namespace NetYamlForge.Tests.Integration;

public class WikiAppDeclarativeIntegrationTests : IClassFixture<AgentStudioWebApplicationFactory>
{
    private readonly AgentStudioWebApplicationFactory _factory;

    public WikiAppDeclarativeIntegrationTests(AgentStudioWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ManifestWikiApp_And_AccessDeclarativeHomePage_Succeeds()
    {
        // 1. 在 agent-studio.db 中录入 Task 7
        using (var scope = _factory.Services.CreateScope())
        {
            var connMgr = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
            using var db = await connMgr.GetConnectionAsync("agent-studio");

            var taskTitle = "声明式 UI 积木生态扩展与全自动 Wiki 知识库交付 (Declarative UI Ecosystem & Wiki Knowledge Base Manifestation)";
            var developerOutput = @"
### File: `projects/wiki-app/project.yaml`
```yaml
name: wiki-app
displayName: '知识库协作中心 (Wiki Hub)'
description: '全声明式多区块知识库与技术文档门户，开箱即用 Markdown 渲染'
icon: '📚'
defaultRoute: '/wiki-app/Page/HomePage'
database:
  type: sqlite
  path: database/wiki-app.db
features:
  pages: true
  dynamicEntity: true
layout:
  landingPageByRole:
    admin: '/wiki-app/Page/HomePage'
    user: '/wiki-app/Page/HomePage'
    guest: '/wiki-app/Page/HomePage'
```

### File: `projects/wiki-app/entities/wiki_doc.yml`
```yaml
entities:
  wiki_doc:
    table: wiki_doc
    key: id
    displayName: '知识库文档'
    columns:
      id:           { type: int, identity: true, label: ID, sortable: true }
      title:        { type: string, required: true, label: 标题, searchable: true, sortable: true }
      category:     { type: string, required: true, label: 分类, sortable: true }
      author:       { type: string, label: 作者 }
      summary:      { type: textarea, label: 摘要 }
      content:      { type: textarea, required: true, label: Markdown正文 }
      is_published: { type: int, default: 1, label: 是否发布 }
      views_count:  { type: int, default: 0, label: 阅读次数 }
      created_at:   { type: datetime, label: 创建时间 }
```

### File: `projects/wiki-app/pages/HomePage.yaml`
```yaml
title: '知识库与技术文档协作中心'
description: '基于 NetYamlForge 声明式积木构建的现代文档中心，原生支持 Markdown 与指标聚合'
sections:
  - id: wiki_metrics
    title: '知识库全局指标'
    sourceType: custom
    source: >-
      SELECT '全部文档' as label, COUNT(*) as value, '📚' as icon FROM wiki_doc
      UNION ALL
      SELECT '已发布' as label, SUM(CASE WHEN is_published = 1 THEN 1 ELSE 0 END) as value, '✅' as icon FROM wiki_doc
      UNION ALL
      SELECT '技术架构' as label, SUM(CASE WHEN category = '技术架构' THEN 1 ELSE 0 END) as value, '🏗️' as icon FROM wiki_doc
      UNION ALL
      SELECT '总浏览量' as label, COALESCE(SUM(views_count), 0) as value, '🔥' as icon FROM wiki_doc
    component: stat_cards
    labelField: label
    valueField: value
    iconField: icon

  - id: doc_feed
    title: '精选文档阅读'
    description: '基于增强型声明式 Markdown 积木渲染的文档正文与指引'
    sourceType: custom
    source: 'SELECT id, title, category, author, content, created_at FROM wiki_doc WHERE is_published = 1 ORDER BY id ASC'
    component: markdown
    titleField: title
    subtitleField: category
    dateField: created_at
    contentField: content

  - id: doc_overview
    title: '文档目录索引'
    sourceType: custom
    source: 'SELECT id, title, category, author, summary, views_count, created_at FROM wiki_doc ORDER BY id DESC'
    component: card_list
    titleField: title
    subtitleField: category
    descriptionField: summary
    pageSize: 6
```

### File: `projects/wiki-app/database/init_schema.sql`
```sql
CREATE TABLE IF NOT EXISTS wiki_doc (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    category TEXT NOT NULL,
    author TEXT,
    summary TEXT,
    content TEXT NOT NULL,
    is_published INTEGER DEFAULT 1,
    views_count INTEGER DEFAULT 0,
    created_at TEXT DEFAULT (datetime('now', 'localtime'))
);

INSERT INTO wiki_doc (title, category, author, summary, content, is_published, views_count, created_at)
VALUES 
(
    'NetYamlForge 架构设计与工程哲学',
    '技术架构',
    'Architect (Claude)',
    '阐明 NetYamlForge 天生安全与声明式自举演进核心理念',
    '# NetYamlForge 核心工程哲学

## 1. 声明式一切 (Declarative Everything)
NetYamlForge 提倡将业务模型、布局与安全规则收敛至 **YAML**，最大程度减少不可控代码面积。

> 软件工程的终极目标不是写更多代码，而是用最少、最清晰的规格驱动可靠系统的自主运转。

- **天生安全**：Roslyn 编译期拦截与会话物理隔离
- **双轨动线**：前台业务门户与管理后台彻底解耦',
    1,
    128,
    datetime('now', '-2 days')
),
(
    '多智能体协同流水线使用指南',
    '最佳实践',
    'DevOps Engineer',
    '指导团队在 Agent Studio 中高效调度 Claude、Antigravity 与 OpenCode',
    '# 多智能体协同流水线最佳实践

## 角色协作黄金三角
1. **Architect (Claude)**：负责高层结构设计与 RFC 决策
2. **Developer (Antigravity)**：负责 YAML 规约与 C# Hook 实现
3. **Tester (OpenCode)**：负责端到端自动化验收

> 验证命令：dotnet run -- --scaffold-missing-hooks --project=wiki-app',
    1,
    64,
    datetime('now', '-1 days')
);
```
";

            var existingId = await db.ExecuteScalarAsync<int?>(
                "SELECT id FROM agent_task WHERE title = @Title",
                new { Title = taskTitle });

            int taskId;
            if (existingId.HasValue && existingId.Value > 0)
            {
                taskId = existingId.Value;
                await db.ExecuteAsync(
                    "UPDATE agent_task SET developer_output = @Dev, status = 'Completed', updated_at = datetime('now') WHERE id = @Id",
                    new { Dev = developerOutput, Id = taskId });
            }
            else
            {
                taskId = await db.ExecuteScalarAsync<int>(
                    "INSERT INTO agent_task (title, description, status, architect_cli, developer_cli, tester_cli, developer_output, created_at, updated_at) " +
                    "VALUES (@Title, @Desc, 'Completed', 'claude', 'antigravity', 'opencode', @Dev, datetime('now'), datetime('now')); " +
                    "SELECT last_insert_rowid();",
                    new { 
                        Title = taskTitle, 
                        Desc = "基于声明式 UI 积木（无需手写 Razor 视图）全自动交付知识库应用", 
                        Dev = developerOutput 
                    });
            }

            // 2. 调用物理交付引擎
            var manifestationService = scope.ServiceProvider.GetRequiredService<IAppManifestationService>();
            var result = await manifestationService.ManifestAppAsync(new ManifestationRequest(
                TaskId: taskId,
                TargetProjectName: "wiki-app",
                ForceOverwrite: true,
                AutoScaffoldHooks: true,
                ReloadInMemory: true
            ));

            Assert.True(result.Success, $"物理交付失败: {result.Error}");
            Assert.Equal("wiki-app", result.ProjectName);
            Assert.True(result.DatabaseInitialized, "数据库初始化应成功");
            Assert.True(result.DynamicReloadSucceeded, "动态内存重载应成功");
        }

        // 3. HTTP 端到端验收
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // 访问业务主页 (纯声明式渲染 HomePage)
        var response = await client.GetAsync("/wiki-app/Page/HomePage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawHtml = await response.Content.ReadAsStringAsync();
        var html = WebUtility.HtmlDecode(rawHtml);

        // 验证声明式页面标题与指标
        Assert.Contains("知识库与技术文档协作中心", html);
        Assert.Contains("NetYamlForge 核心工程哲学", html);
        Assert.Contains("声明式一切", html);
        Assert.Contains("全部文档", html);
        Assert.Contains("多智能体协同流水线最佳实践", html);
        Assert.Contains("NetYamlForge 核心工程哲学", html);
        Assert.Contains("声明式一切", html);
        Assert.Contains("多智能体协同流水线最佳实践", html);
    }
}
