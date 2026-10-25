INSERT INTO blog_category (id, name, slug, description, sort_order, created_at)
VALUES 
(1, '技术架构', 'architecture', '云原生、微服务与高可用架构设计', 1, '2026-09-25 12:00:00'),
(2, 'NetYamlForge 实践', 'netyamlforge', 'YAML 声明式驱动开发与低代码引擎实战', 2, '2026-09-25 12:00:00'),
(3, '多 Agent 协同', 'ai-agents', '探讨多模型、多角色自动化流水线演进', 3, '2026-09-25 12:00:00');

INSERT INTO blog_post (id, title, slug, summary, content, category_id, author, status, view_count, published_at, created_at, updated_at)
VALUES 
(1, 'NetYamlForge 架构与声明式应用设计哲学', 'netyamlforge-architecture-philosophy', '深入探讨如何通过 YAML 声明实体、业务钩子与轻量 SQLite 实现快速业务交付。', '# NetYamlForge 架构设计哲学\n\nNetYamlForge 采用**配置驱动 + C# 钩子扩展**的混合模型，结合微前端/双动线解耦，极大提升了内部工具与 CRUD 系统的交付效率。', 2, 'Architect', 'published', 128, '2026-09-25 12:30:00', '2026-09-25 12:00:00', '2026-09-25 12:30:00'),
(2, '解密多 Agent 协同：Claude、Antigravity 与 OpenCode 的跨模型编排', 'multi-agent-orchestration-patterns', '破除单一 CLI 依赖，实现多模型分工协同与动态故障降级。', '# 多 Agent 协同实践\n\n在现代 AI 辅助研发中，单一 Agent 容易遭遇模型偏见或执行单点故障。通过将 Architect、Developer、Tester 三阶段解耦并分配给不同模型，能够实现交叉校验与鲁棒交付。', 3, 'DevOps', 'published', 256, '2026-09-25 13:00:00', '2026-09-25 12:00:00', '2026-09-25 13:00:00'),
(3, '领域驱动设计与数据模型解耦探索', 'ddd-data-model-decoupling', '论前台业务主页与通用管理后台在企业级低代码应用中的边界。', '# 业务门户与通用管理后台解耦\n\n前台业务主页注重用户心智与关键指标概览，而通用后台则专注于全量数据的高效维护。', 1, 'Architect', 'draft', 12, NULL, '2026-09-25 14:00:00', '2026-09-25 14:00:00');
