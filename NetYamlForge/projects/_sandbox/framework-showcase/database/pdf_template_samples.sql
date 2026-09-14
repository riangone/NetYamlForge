-- ============================================================
-- framework-showcase PDF Template Sample Data
-- ============================================================
-- This script creates sample data for demonstrating global PDF templates.
-- Run after init_seed.sql to add PDF-ready sample transactions.
-- ============================================================

-- ============================================================
-- Additional Customer data for PDF templates (if Customer table exists)
-- ============================================================
-- Note: Customer table is in biz-docs project. This is for reference only.
-- Use biz-docs/database/pdf_template_samples.sql for Customer data.

-- ============================================================
-- Sample data references for framework-showcase entities
-- ============================================================

-- The following entities in framework-showcase can be exported to PDF:
-- - form_component: CSV export only (no PDF template)
-- - filter_demo: CSV export only (no PDF template)
-- - layout_demo: Has PDF export defined in layout_demo.yml
-- - batch_job_demo: CSV export only (no PDF template)
-- - hook_demo: CSV export only (no PDF template)
-- - export_demo: Has PDF export defined in export_demo.yml

-- ============================================================
-- Layout Demo PDF Export Sample
-- ============================================================
-- The layout_demo entity has PDF export configured.
-- Sample data is already seeded in init_seed.sql (20 rows).

-- ============================================================
-- Export Demo PDF Export Sample
-- ============================================================
-- The export_demo entity has PDF export configured.
-- Sample data is already seeded in init_seed.sql (20 rows).

-- ============================================================
-- Usage Instructions
-- ============================================================
-- 1. To test PDF templates, use the biz-docs project which has:
--    - JpInvoice (請求書)
--    - JpEstimate (見積書)
--    - JpDelivery (納品書)
--    - JpContract (契約書)
--
-- 2. Run biz-docs/database/pdf_template_samples.sql to add sample data.
--
-- 3. Access PDF export endpoints:
--    - /biz-docs/JpInvoice/ExportPdf?id={id}
--    - /biz-docs/JpEstimate/ExportPdf?id={id}
--    - /biz-docs/JpDelivery/ExportPdf?id={id}
--    - /biz-docs/JpContract/ExportPdf?id={id}
--
-- 4. For framework-showcase entities with PDF export:
--    - /framework-showcase/DynamicEntity/ExportPdf?entity=layout_demo
--    - /framework-showcase/DynamicEntity/ExportPdf?entity=export_demo
