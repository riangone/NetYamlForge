-- ============================================================
-- NetYamlForge — todo-app schema for PostgreSQL
-- Executed automatically by the postgres container on first start.
-- Framework tables (AppUser, AuditLog, etc.) are created by the
-- application on startup via DbInitializer.
-- ============================================================

-- ---- Project-specific tables ----

CREATE TABLE IF NOT EXISTS "Category" (
    "Id"          SERIAL       PRIMARY KEY,
    "Name"        TEXT         NOT NULL,
    "Color"       TEXT,
    "Description" TEXT
);

CREATE TABLE IF NOT EXISTS "Project" (
    "Id"          SERIAL       PRIMARY KEY,
    "Name"        TEXT         NOT NULL,
    "Status"      TEXT         NOT NULL DEFAULT 'planning',
    "Priority"    TEXT         NOT NULL DEFAULT 'medium',
    "StartDate"   TEXT,
    "EndDate"     TEXT,
    "Description" TEXT
);

CREATE TABLE IF NOT EXISTS "Task" (
    "Id"             SERIAL   PRIMARY KEY,
    "Title"          TEXT     NOT NULL,
    "Status"         TEXT     NOT NULL DEFAULT 'pending',
    "Priority"       TEXT     NOT NULL DEFAULT 'medium',
    "ProjectId"      INTEGER,
    "CategoryId"     INTEGER,
    "DueDate"        TEXT,
    "Description"    TEXT,
    "AssignedTo"     TEXT,
    "Tags"           TEXT,
    "EstimatedHours" REAL,
    "ActualHours"    REAL,
    "CompletedAt"    TEXT
);

CREATE TABLE IF NOT EXISTS "Comment" (
    "Id"         SERIAL  PRIMARY KEY,
    "EntityType" TEXT    NOT NULL,
    "EntityId"   INTEGER NOT NULL,
    "Author"     TEXT    NOT NULL,
    "Body"       TEXT    NOT NULL,
    "CreatedAt"  TEXT    NOT NULL DEFAULT (to_char(NOW() AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS'))
);

-- ---- Seed data ----

INSERT INTO "Category" ("Name", "Color", "Description") VALUES
  ('Frontend',   '#3B82F6', 'UI/UX and browser-side implementation'),
  ('Backend',    '#10B981', 'Server-side APIs and business logic'),
  ('DevOps',     '#F59E0B', 'CI/CD, infrastructure, and deployment'),
  ('Design',     '#EC4899', 'Wireframes, mockups, and visual design'),
  ('Testing',    '#8B5CF6', 'Unit, integration, and E2E tests'),
  ('Database',   '#EF4444', 'Schema design, migrations, and queries'),
  ('Security',   '#F97316', 'Auth, access control, and vulnerability fixes'),
  ('Docs',       '#6B7280', 'Technical writing and documentation')
ON CONFLICT DO NOTHING;

INSERT INTO "Project" ("Name", "Status", "Priority", "StartDate", "EndDate", "Description") VALUES
  ('Web Redesign',     'active',    'high',   '2026-01-15', '2026-04-30', 'Full redesign of the corporate website with modern UI framework'),
  ('API v2 Migration', 'active',    'high',   '2026-02-01', '2026-05-31', 'Migrate REST API from v1 to v2 with OpenAPI 3.1 specification'),
  ('Mobile App MVP',   'planning',  'medium', '2026-04-01', '2026-09-30', 'First release of the iOS/Android companion app'),
  ('Data Warehouse',   'on_hold',   'medium', '2026-01-01', '2026-06-30', 'Build centralised analytics DW with dbt and Redshift'),
  ('Auth Overhaul',    'active',    'high',   '2026-03-01', '2026-04-15', 'Replace legacy session auth with JWT + OAuth2'),
  ('Docs Portal',      'completed', 'low',    '2025-10-01', '2026-02-28', 'Developer documentation portal built with Docusaurus')
ON CONFLICT DO NOTHING;

INSERT INTO "Task" ("Title","Status","Priority","ProjectId","CategoryId","DueDate","Description","AssignedTo","Tags","EstimatedHours","ActualHours","CompletedAt") VALUES
  ('Create homepage wireframes',      'done',        'high',   1, 4, '2026-02-10', 'Figma wireframes for hero, features, and CTA sections',       'Alice', 'design,ux',      8,  9,  '2026-02-09'),
  ('Implement design system tokens',  'done',        'high',   1, 1, '2026-02-20', 'Tailwind config for colors, spacing, and typography',         'Bob',   'css,tailwind',   6,  7,  '2026-02-18'),
  ('Build navigation component',      'done',        'medium', 1, 1, '2026-02-28', 'Responsive top-nav with mobile hamburger menu',               'Bob',   'react,nav',      4,  4,  '2026-02-26'),
  ('Landing page animation',          'in_progress', 'medium', 1, 1, '2026-03-15', 'Scroll-triggered fade-in animations with Framer Motion',      'Alice', 'animation',      6,  3,  NULL),
  ('SEO metadata & OG tags',          'pending',     'low',    1, 1, '2026-03-20', 'Add structured data and Open Graph tags to all pages',        'Carol', 'seo',            3,  NULL, NULL),
  ('Cross-browser testing',           'pending',     'medium', 1, 5, '2026-04-10', 'Cypress tests on Chrome, Firefox, Safari, Edge',             'Dave',  'testing,e2e',    8,  NULL, NULL),
  ('Accessibility audit (WCAG 2.1)',  'review',      'high',   1, 5, '2026-03-25', 'Automated + manual a11y audit; fix P0 issues',               'Carol', 'a11y',           6,  5,  NULL),
  ('Performance optimisation',        'pending',     'high',   1, 1, '2026-04-20', 'Achieve LCP < 2.5s and CLS < 0.1 on Lighthouse',            'Bob',   'perf',           8,  NULL, NULL),
  ('Define OpenAPI 3.1 spec',         'done',        'urgent', 2, 2, '2026-02-15', 'Write full spec for all 42 endpoints in YAML',               'Eve',   'api,openapi',    10, 11, '2026-02-14'),
  ('Versioned routing middleware',    'done',        'high',   2, 2, '2026-02-28', 'Express middleware to route /v1 and /v2 independently',      'Frank', 'backend,node',   6,  6,  '2026-02-27'),
  ('Migrate /auth endpoints',         'in_progress', 'urgent', 2, 7, '2026-03-20', 'Rewrite login/logout/refresh under v2 contracts',            'Eve',   'auth,jwt',       8,  4,  NULL),
  ('Migrate /users endpoints',        'in_progress', 'high',   2, 2, '2026-03-25', 'CRUD for user resource with pagination and filtering',       'Frank', 'crud',           8,  2,  NULL),
  ('Migrate /orders endpoints',       'pending',     'high',   2, 2, '2026-04-05', 'Orders resource with state machine transitions',             'Grace', 'orders',         12, NULL, NULL),
  ('SDK code-gen from spec',          'pending',     'medium', 2, 2, '2026-04-15', 'Auto-generate TypeScript and Python SDKs via openapi-generator','Eve','sdk,codegen',    5,  NULL, NULL),
  ('Integration test suite',          'review',      'high',   2, 5, '2026-04-01', 'Supertest suite covering all migrated v2 endpoints',         'Dave',  'testing',        10, 9,  NULL),
  ('API changelog & migration guide', 'pending',     'low',    2, 8, '2026-05-01', 'Document breaking changes and provide migration examples',   'Carol', 'docs',           4,  NULL, NULL),
  ('App architecture design',         'done',        'high',   3, 4, '2026-04-10', 'Decide on React Native vs Flutter; define folder structure', 'Heidi', 'mobile,arch',    6,  5,  '2026-04-09'),
  ('Auth screens (login/signup)',      'pending',     'high',   3, 1, '2026-05-01', 'Login, signup, and forgot-password screens',                 'Ivan',  'mobile,auth',    8,  NULL, NULL),
  ('Task list screen',                'pending',     'medium', 3, 1, '2026-05-15', 'Main task list with swipe actions and pull-to-refresh',      'Ivan',  'mobile,list',    6,  NULL, NULL),
  ('Push notification setup',         'pending',     'medium', 3, 3, '2026-06-01', 'FCM / APNs integration for due-date reminders',              'Heidi', 'push,notifications',8,NULL,NULL),
  ('Source system analysis',          'done',        'high',   4, 6, '2026-02-01', 'Catalogue all upstream tables and identify CDC sources',     'Judy',  'data,elt',       10, 12, '2026-01-30'),
  ('dbt project scaffold',            'done',        'medium', 4, 6, '2026-02-20', 'Init dbt project with staging / marts layer structure',      'Judy',  'dbt',            4,  4,  '2026-02-19'),
  ('Staging models – orders',         'in_progress', 'medium', 4, 6, '2026-03-10', 'Clean and type-cast raw orders from Postgres CDC',           'Karl',  'dbt,staging',    6,  2,  NULL),
  ('Redshift cluster sizing',         'pending',     'low',    4, 3, '2026-04-01', 'Cost/performance analysis for ra3 vs dc2 node types',        'Judy',  'infra,redshift', 4,  NULL, NULL),
  ('Threat model review',             'done',        'urgent', 5, 7, '2026-03-05', 'Review STRIDE model for new JWT flow',                       'Leo',   'security',       4,  4,  '2026-03-04'),
  ('JWT issuance service',            'in_progress', 'urgent', 5, 2, '2026-03-25', 'Stateless JWT with RS256, short-lived access + refresh tokens','Leo', 'jwt,auth',       8,  5,  NULL),
  ('OAuth2 provider integration',     'in_progress', 'high',   5, 2, '2026-03-28', 'Google and GitHub OAuth2 sign-in with PKCE flow',            'Mia',   'oauth2',         8,  3,  NULL),
  ('Session migration script',        'pending',     'urgent', 5, 6, '2026-04-05', 'One-time migration of existing sessions to new tokens',      'Leo',   'migration,db',   6,  NULL, NULL),
  ('Pen-test (auth scope)',           'pending',     'urgent', 5, 7, '2026-04-10', 'External pen-test focused on new auth endpoints',            'Dave',  'security,pentest',8, NULL, NULL),
  ('Docusaurus setup',                'done',        'medium', 6, 3, '2025-10-20', 'Bootstrap Docusaurus 3 with custom theme',                   'Nina',  'docs,docusaurus',4,  3,  '2025-10-18'),
  ('Content migration (API)',         'done',        'high',   6, 8, '2025-11-30', 'Migrate all API reference pages from Confluence',            'Nina',  'docs,migration', 16, 18, '2025-11-28'),
  ('Search integration (Algolia)',    'done',        'medium', 6, 1, '2025-12-15', 'Algolia DocSearch for full-text search across portal',       'Oscar', 'search,algolia', 6,  5,  '2025-12-12'),
  ('Versioned docs for API v1/v2',    'done',        'high',   6, 8, '2026-01-31', 'Side-by-side versioned docs using Docusaurus versions',      'Nina',  'docs,versioning',8,  9,  '2026-01-29'),
  ('Feedback widget',                 'done',        'low',    6, 1, '2026-02-15', 'Thumbs-up/down widget with comment capture on each page',    'Oscar', 'feedback,ux',    4,  4,  '2026-02-14'),
  ('Launch announcement blog post',   'done',        'low',    6, 8, '2026-02-28', 'Internal blog post announcing Docs Portal go-live',          'Nina',  'docs,marketing', 2,  2,  '2026-02-27'),
  ('Upgrade Node.js to v22 LTS',      'pending',     'medium', NULL,3, '2026-03-31', 'Update all services and CI images to Node 22',              'Frank', 'devops,node',    3,  NULL, NULL),
  ('Renew wildcard TLS certificate',  'pending',     'urgent', NULL,7, '2026-03-28', 'Let''s Encrypt wildcard cert expires 2026-04-01',           'Leo',   'security,tls',   1,  NULL, NULL),
  ('Update dependency audit report',  'done',        'low',    NULL,7, '2026-03-01', 'Run npm audit and Snyk; close or accept all findings',      'Mia',   'security,deps',  2,  2,  '2026-02-28')
ON CONFLICT DO NOTHING;

INSERT INTO "Comment" ("EntityType","EntityId","Author","Body","CreatedAt") VALUES
  ('task',    1,  'Bob',   'Wireframes look great! One suggestion: add a sticky CTA button on mobile.',                  '2026-02-05 09:10:00'),
  ('task',    1,  'Alice', 'Good point – I''ll add a floating button variant to the next iteration.',                    '2026-02-05 11:23:00'),
  ('task',    4,  'Carol', 'Have we decided on Framer Motion or GSAP? GSAP might give more control.',                    '2026-03-01 14:00:00'),
  ('task',    4,  'Alice', 'Going with Framer Motion – better React integration and smaller bundle.',                    '2026-03-01 15:45:00'),
  ('task',    7,  'Dave',  'axe-core found 3 P0 issues: missing aria-label on icon buttons and low contrast footer text.','2026-03-22 10:00:00'),
  ('task',    7,  'Carol', 'Fixing contrast now. Icon button labels will be done by tomorrow.',                          '2026-03-22 13:30:00'),
  ('task',    9,  'Frank', 'Spec draft is in Notion – please review the pagination contract for list endpoints.',         '2026-02-10 09:00:00'),
  ('task',    9,  'Eve',   'Left comments in Notion. Cursor-based pagination is preferred over offset for large datasets.','2026-02-10 11:15:00'),
  ('task',    11, 'Eve',   'Ran into an issue: refresh token rotation must be atomic. Using Redis for token family.',     '2026-03-18 16:00:00'),
  ('task',    11, 'Leo',   'Agreed – Redis sorted-set approach is solid. Let''s align on key TTL values tomorrow.',      '2026-03-18 17:30:00'),
  ('task',    27, 'Mia',   'PKCE state parameter validation is done. Redirect URI whitelist in place.',                  '2026-03-20 10:00:00'),
  ('task',    27, 'Leo',   'Looks good! Don''t forget to log failed attempts to AuditLog.',                              '2026-03-20 10:45:00'),
  ('project', 1,  'Alice', 'Kickoff meeting notes posted in Confluence. Design sprint starts Monday.',                   '2026-01-16 09:00:00'),
  ('project', 2,  'Grace', 'v2 spec review session scheduled for 2026-02-12 at 14:00 JST.',                             '2026-02-08 08:30:00'),
  ('project', 2,  'Eve',   'Reminder: breaking changes must be listed in BREAKING_CHANGES.md before merge.',             '2026-02-08 09:00:00'),
  ('project', 5,  'Leo',   'External auditor confirmed availability for April 8-10. Scope doc sent.',                   '2026-03-10 11:00:00'),
  ('task',    21, 'Heidi', 'Compared RN vs Flutter – going with Flutter for single codebase and better perf on Android.','2026-04-07 14:00:00'),
  ('task',    21, 'Ivan',  'Agreed. I''ll set up the mono-repo structure using Melos.',                                  '2026-04-07 15:30:00'),
  ('task',    38, 'Mia',   'npm audit shows 2 high-severity issues in lodash. PRs ready for review.',                   '2026-02-25 10:00:00'),
  ('task',    14, 'Judy',  'Staging orders model passes 100% of dbt tests locally. Pushing to dev branch.',              '2026-03-08 16:00:00')
ON CONFLICT DO NOTHING;
