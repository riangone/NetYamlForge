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
