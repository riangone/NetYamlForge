-- ============================================================
-- NetYamlForge — todo-app schema for SQL Server 2022
-- Run by the sqlserver container's init command.
-- Framework tables (AppUser, AuditLog, etc.) are created by the
-- application on startup via DbInitializer.
-- ============================================================

-- ---- Project-specific tables ----

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Category]') AND type = N'U')
CREATE TABLE [dbo].[Category] (
    [Id]          INT           IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Name]        NVARCHAR(255) NOT NULL,
    [Color]       NVARCHAR(50),
    [Description] NVARCHAR(MAX)
);
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Project]') AND type = N'U')
CREATE TABLE [dbo].[Project] (
    [Id]          INT           IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Name]        NVARCHAR(255) NOT NULL,
    [Status]      NVARCHAR(50)  NOT NULL CONSTRAINT [DF_Project_Status]   DEFAULT 'planning',
    [Priority]    NVARCHAR(50)  NOT NULL CONSTRAINT [DF_Project_Priority] DEFAULT 'medium',
    [StartDate]   NVARCHAR(20),
    [EndDate]     NVARCHAR(20),
    [Description] NVARCHAR(MAX)
);
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Task]') AND type = N'U')
CREATE TABLE [dbo].[Task] (
    [Id]             INT           IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Title]          NVARCHAR(MAX) NOT NULL,
    [Status]         NVARCHAR(50)  NOT NULL CONSTRAINT [DF_Task_Status]   DEFAULT 'pending',
    [Priority]       NVARCHAR(50)  NOT NULL CONSTRAINT [DF_Task_Priority] DEFAULT 'medium',
    [ProjectId]      INT,
    [CategoryId]     INT,
    [DueDate]        NVARCHAR(20),
    [Description]    NVARCHAR(MAX),
    [AssignedTo]     NVARCHAR(100),
    [Tags]           NVARCHAR(MAX),
    [EstimatedHours] FLOAT,
    [ActualHours]    FLOAT,
    [CompletedAt]    NVARCHAR(20)
);
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Comment]') AND type = N'U')
CREATE TABLE [dbo].[Comment] (
    [Id]         INT           IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [EntityType] NVARCHAR(50)  NOT NULL,
    [EntityId]   INT           NOT NULL,
    [Author]     NVARCHAR(100) NOT NULL,
    [Body]       NVARCHAR(MAX) NOT NULL,
    [CreatedAt]  NVARCHAR(30)  NOT NULL CONSTRAINT [DF_Comment_CreatedAt] DEFAULT (FORMAT(GETUTCDATE(), 'yyyy-MM-dd HH:mm:ss'))
);
GO

-- ---- Seed data (idempotent via IF NOT EXISTS check) ----

IF NOT EXISTS (SELECT 1 FROM [Category])
BEGIN
  INSERT INTO [Category] ([Name],[Color],[Description]) VALUES
    ('Frontend',   '#3B82F6', 'UI/UX and browser-side implementation'),
    ('Backend',    '#10B981', 'Server-side APIs and business logic'),
    ('DevOps',     '#F59E0B', 'CI/CD, infrastructure, and deployment'),
    ('Design',     '#EC4899', 'Wireframes, mockups, and visual design'),
    ('Testing',    '#8B5CF6', 'Unit, integration, and E2E tests'),
    ('Database',   '#EF4444', 'Schema design, migrations, and queries'),
    ('Security',   '#F97316', 'Auth, access control, and vulnerability fixes'),
    ('Docs',       '#6B7280', 'Technical writing and documentation');
END
GO

IF NOT EXISTS (SELECT 1 FROM [Project])
BEGIN
  INSERT INTO [Project] ([Name],[Status],[Priority],[StartDate],[EndDate],[Description]) VALUES
    ('Web Redesign',     'active',    'high',   '2026-01-15', '2026-04-30', 'Full redesign of the corporate website with modern UI framework'),
    ('API v2 Migration', 'active',    'high',   '2026-02-01', '2026-05-31', 'Migrate REST API from v1 to v2 with OpenAPI 3.1 specification'),
    ('Mobile App MVP',   'planning',  'medium', '2026-04-01', '2026-09-30', 'First release of the iOS/Android companion app'),
    ('Data Warehouse',   'on_hold',   'medium', '2026-01-01', '2026-06-30', 'Build centralised analytics DW with dbt and Redshift'),
    ('Auth Overhaul',    'active',    'high',   '2026-03-01', '2026-04-15', 'Replace legacy session auth with JWT + OAuth2'),
    ('Docs Portal',      'completed', 'low',    '2025-10-01', '2026-02-28', 'Developer documentation portal built with Docusaurus');
END
GO

IF NOT EXISTS (SELECT 1 FROM [Task])
BEGIN
  SET IDENTITY_INSERT [Task] OFF;
  INSERT INTO [Task] ([Title],[Status],[Priority],[ProjectId],[CategoryId],[DueDate],[Description],[AssignedTo],[Tags],[EstimatedHours],[ActualHours],[CompletedAt]) VALUES
    ('Create homepage wireframes',     'done',        'high',   1, 4, '2026-02-10', 'Figma wireframes for hero, features, and CTA sections',       'Alice', 'design,ux',     8,  9,  '2026-02-09'),
    ('Implement design system tokens', 'done',        'high',   1, 1, '2026-02-20', 'Tailwind config for colors, spacing, and typography',         'Bob',   'css,tailwind',  6,  7,  '2026-02-18'),
    ('Build navigation component',     'done',        'medium', 1, 1, '2026-02-28', 'Responsive top-nav with mobile hamburger menu',               'Bob',   'react,nav',     4,  4,  '2026-02-26'),
    ('Landing page animation',         'in_progress', 'medium', 1, 1, '2026-03-15', 'Scroll-triggered animations with Framer Motion',             'Alice', 'animation',     6,  3,  NULL),
    ('SEO metadata & OG tags',         'pending',     'low',    1, 1, '2026-03-20', 'Add structured data and Open Graph tags to all pages',        'Carol', 'seo',           3,  NULL,NULL),
    ('Cross-browser testing',          'pending',     'medium', 1, 5, '2026-04-10', 'Cypress tests on Chrome, Firefox, Safari, Edge',             'Dave',  'testing,e2e',   8,  NULL,NULL),
    ('Accessibility audit (WCAG 2.1)', 'review',      'high',   1, 5, '2026-03-25', 'Automated + manual a11y audit; fix P0 issues',               'Carol', 'a11y',          6,  5,  NULL),
    ('Performance optimisation',       'pending',     'high',   1, 1, '2026-04-20', 'Achieve LCP < 2.5s and CLS < 0.1 on Lighthouse',            'Bob',   'perf',          8,  NULL,NULL),
    ('Define OpenAPI 3.1 spec',        'done',        'urgent', 2, 2, '2026-02-15', 'Write full spec for all 42 endpoints in YAML',               'Eve',   'api,openapi',   10, 11, '2026-02-14'),
    ('Versioned routing middleware',   'done',        'high',   2, 2, '2026-02-28', 'Express middleware to route /v1 and /v2 independently',      'Frank', 'backend,node',  6,  6,  '2026-02-27'),
    ('Migrate /auth endpoints',        'in_progress', 'urgent', 2, 7, '2026-03-20', 'Rewrite login/logout/refresh under v2 contracts',            'Eve',   'auth,jwt',      8,  4,  NULL),
    ('Migrate /users endpoints',       'in_progress', 'high',   2, 2, '2026-03-25', 'CRUD for user resource with pagination and filtering',       'Frank', 'crud',          8,  2,  NULL),
    ('Migrate /orders endpoints',      'pending',     'high',   2, 2, '2026-04-05', 'Orders resource with state machine transitions',             'Grace', 'orders',        12, NULL,NULL),
    ('Integration test suite',         'review',      'high',   2, 5, '2026-04-01', 'Supertest suite covering all migrated v2 endpoints',         'Dave',  'testing',       10, 9,  NULL),
    ('Threat model review',            'done',        'urgent', 5, 7, '2026-03-05', 'Review STRIDE model for new JWT flow',                       'Leo',   'security',      4,  4,  '2026-03-04'),
    ('JWT issuance service',           'in_progress', 'urgent', 5, 2, '2026-03-25', 'Stateless JWT with RS256',                                   'Leo',   'jwt,auth',      8,  5,  NULL),
    ('OAuth2 provider integration',    'in_progress', 'high',   5, 2, '2026-03-28', 'Google and GitHub OAuth2 sign-in with PKCE flow',            'Mia',   'oauth2',        8,  3,  NULL),
    ('Upgrade Node.js to v22 LTS',     'pending',     'medium', NULL,3, '2026-03-31', 'Update all services and CI images to Node 22',             'Frank', 'devops,node',   3,  NULL,NULL),
    ('Renew wildcard TLS certificate', 'pending',     'urgent', NULL,7, '2026-03-28', 'Let''s Encrypt wildcard cert expires 2026-04-01',          'Leo',   'security,tls',  1,  NULL,NULL),
    ('Update dependency audit report', 'done',        'low',    NULL,7, '2026-03-01', 'Run npm audit and Snyk; close or accept all findings',     'Mia',   'security,deps', 2,  2,  '2026-02-28');
END
GO

IF NOT EXISTS (SELECT 1 FROM [Comment])
BEGIN
  INSERT INTO [Comment] ([EntityType],[EntityId],[Author],[Body],[CreatedAt]) VALUES
    ('task',    1,  'Bob',   'Wireframes look great! One suggestion: add a sticky CTA button on mobile.', '2026-02-05 09:10:00'),
    ('task',    1,  'Alice', 'Good point - I''ll add a floating button variant to the next iteration.',   '2026-02-05 11:23:00'),
    ('project', 1,  'Alice', 'Kickoff meeting notes posted in Confluence. Design sprint starts Monday.',  '2026-01-16 09:00:00'),
    ('project', 5,  'Leo',   'External auditor confirmed availability for April 8-10. Scope doc sent.',  '2026-03-10 11:00:00');
END
GO
