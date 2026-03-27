-- 中国股市简报数据查询
-- 获取主要指数的最新行情数据
-- 
-- 注意：实际使用时需要通过外部 API 获取实时行情数据
-- 这里提供一个示例结构，您可以根据实际情况调整

-- 创建股市行情记录表（如果不存在）
CREATE TABLE IF NOT EXISTS stock_market_data (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    market_code VARCHAR(20) NOT NULL,      -- 市场代码：SSEC(上证), SZSC(深证), CYB(创业板)
    market_name VARCHAR(50) NOT NULL,      -- 市场名称
    current_price DECIMAL(10,2),           -- 当前点位
    change_amount DECIMAL(10,2),           -- 涨跌额
    change_percent DECIMAL(6,2),           -- 涨跌幅 (%)
    open_price DECIMAL(10,2),              -- 开盘价
    high_price DECIMAL(10,2),              -- 最高价
    low_price DECIMAL(10,2),               -- 最低价
    prev_close DECIMAL(10,2),              -- 昨收
    volume BIGINT,                         -- 成交量 (手)
    amount DECIMAL(18,2),                  -- 成交额 (元)
    market_status VARCHAR(20),             -- 市场状态：TRADING, CLOSED, HALTED
    briefing_note TEXT,                    -- 简评
    data_source VARCHAR(50),               -- 数据来源
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 创建索引
CREATE INDEX IF NOT EXISTS idx_stock_market_code ON stock_market_data(market_code);
CREATE INDEX IF NOT EXISTS idx_stock_market_created ON stock_market_data(created_at);

-- 查询最新的股市行情数据（用于导出 CSV）
SELECT 
    market_code AS '市场代码',
    market_name AS '市场名称',
    current_price AS '当前点位',
    change_amount AS '涨跌额',
    change_percent AS '涨跌幅 (%)',
    open_price AS '开盘',
    high_price AS '最高',
    low_price AS '最低',
    prev_close AS '昨收',
    volume AS '成交量 (手)',
    amount AS '成交额 (元)',
    market_status AS '市场状态',
    briefing_note AS '简评',
    data_source AS '数据来源',
    created_at AS '数据时间'
FROM stock_market_data
WHERE created_at >= datetime('now', '-1 hour')
ORDER BY 
    CASE market_code
        WHEN 'SSEC' THEN 1
        WHEN 'SZSC' THEN 2
        WHEN 'CYB' THEN 3
        ELSE 4
    END,
    created_at DESC;
