-- JPiere Contract Service (JPCS) SQLite Initialization
-- 完整数据库已从 ExpDat.jar (PostgreSQL Dump) 转换导入
-- 包含 1148 张表，647,105 行数据（覆盖 749 张表有数据）
-- 源文件: JPiere/JPCS ExpDat.jar -> ExpDat.dmp -> jpcs.db
-- 转换工具: Python pg_dump_to_sqlite.py

-- 如需重新创建数据库:
--   gunzip -k jpcs_dump.sql.gz && sqlite3 jpcs.db < jpcs_dump.sql

-- 表统计:
--   总表数:    1148
--   有数据表:  749
--   总数据行:  647,105
--   数据库大小: ~126 MB

-- 核心业务表数据示例:
--   jp_contract:        34 行 (契约主表)
--   jp_contractline:    241 行 (契约行)
--   jp_contractcontent: 59 行 (契约内容)
--   c_bpartner:         130 行 (业务伙伴)
--   c_invoice:          519 行 (发票)
--   c_order:            407 行 (订单)
--   m_product:          101 行 (产品)
--   ad_user:            119 行 (用户)
--   ad_client:          3 行 (租户)
