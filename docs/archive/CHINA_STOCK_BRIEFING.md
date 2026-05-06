# 中国股市简报定时任务

## 概述

本系统现已支持中国股市定时简报功能，将在每天的 **09:00** 和 **16:00**（中国时区）自动获取并发送中国股市最新行情及简评。

## 功能特点

- **定时执行**：每天 09:00（开盘前）和 16:00（收盘后）自动执行
- **多指数支持**：涵盖上证指数、深证成指、创业板指、上证 50、沪深 300
- **实时数据**：通过东方财富网 API 获取实时行情数据
- **智能简评**：自动生成市场简评，包括涨跌幅、成交量分析等
- **数据持久化**：将行情数据保存到数据库，支持历史查询
- **CSV 导出**：支持导出 CSV 格式，便于进一步分析

## 配置文件

定时任务配置文件位于：
```
NetYamlForge/projects/todo-app/jobs/china_stock_briefing.yml
```

### 配置说明

```yaml
jobs:
  china_stock_briefing:
    displayName: 中国股市最新简报
    description: "每天 09:00 和 16:00（中国时区）获取并发送中国股市最新行情及简评"
    enabled: true

    schedule:
      cron: "0 9,16 * * *"        # 每天 09:00 和 16:00
      timezone: "Asia/Shanghai"   # 中国时区

    type: china_stock_briefing
    settings:
      outputFile: "jobs/output/china_stock_briefing_{date:yyyyMMdd_HHmm}.csv"
      includeHeader: true
      delimiter: ","

    onFailure:
      retryCount: 2
      retryInterval: 300         # 5 分钟后重试
      logError: true
      notify: true
```

### Cron 表达式说明

```
0 9,16 * * *
│ │  │ │ │
│ │  │ │ └──── 星期几（0-6，0=周日）
│ │  │ └────── 月份（1-12）
│ │  └───────── 日期（1-31）
│ └──────────── 小时（0-23），9,16 表示 9 点和 16 点
└────────────── 分钟（0-59）
```

## 数据库表结构

系统会自动创建 `stock_market_data` 表来存储股市行情数据：

```sql
CREATE TABLE stock_market_data (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    market_code VARCHAR(20) NOT NULL,      -- 市场代码：SSEC, SZSC, CYB
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
```

## 市场代码说明

| 代码 | 名称 | 说明 |
|------|------|------|
| SSEC | 上证指数 | 上海证券交易所综合指数 |
| SZSC | 深证成指 | 深圳证券交易所成份指数 |
| CYB | 创业板指 | 创业板指数 |
| SZ50 | 上证 50 | 上证 50 指数 |
| HS300 | 沪深 300 | 沪深 300 指数 |

## 查看执行结果

### 1. 查看定时任务状态

访问系统的 Batch Job 管理页面：
```
https://your-domain/BatchJob
```

### 2. 查看股市数据

使用 SQL 查询最新的股市数据：
```sql
SELECT 
    market_code AS '市场代码',
    market_name AS '市场名称',
    current_price AS '当前点位',
    change_percent AS '涨跌幅 (%)',
    briefing_note AS '简评',
    created_at AS '数据时间'
FROM stock_market_data
ORDER BY created_at DESC
LIMIT 10;
```

### 3. 查看 CSV 输出文件

CSV 文件保存在：
```
NetYamlForge/projects/todo-app/jobs/output/china_stock_briefing_YYYYMMDD_HHMM.csv
```

## 修改执行时间

如需修改执行时间，编辑 `china_stock_briefing.yml` 文件中的 Cron 表达式：

### 示例：仅在工作日执行

```yaml
schedule:
  cron: "0 9,16 * * 1-5"        # 周一至周五 09:00 和 16:00
  timezone: "Asia/Shanghai"
```

### 示例：增加中午 12:00 的执行

```yaml
schedule:
  cron: "0 9,12,16 * * *"        # 09:00, 12:00, 16:00
  timezone: "Asia/Shanghai"
```

## 禁用定时任务

临时禁用定时任务，将 `enabled` 设置为 `false`：

```yaml
jobs:
  china_stock_briefing:
    enabled: false
    # ... 其他配置
```

## 故障排除

### 1. 任务未执行

检查日志文件，确认：
- 定时任务已正确加载
- Cron 表达式格式正确
- 系统时间与时区设置正确

### 2. 数据获取失败

系统会自动重试 2 次（间隔 5 分钟）。如果仍然失败，将使用模拟数据作为降级方案。

### 3. 查看日志

```bash
# 查看最近的定时任务日志
dotnet run --project NetYamlForge -- --logs | grep "中国股市"
```

## 技术实现

### 服务层

- `IChinaStockService` / `ChinaStockService`：中国股市行情数据服务
- `ChinaStockBriefingExecutor`：中国股市简报任务执行器

### 数据来源

默认使用东方财富网 API 获取实时行情数据：
```
https://push2.eastmoney.com/api/qt/stock/get
```

### 降级方案

当 API 不可用时，系统会生成模拟数据以确保功能可用性（标记为"模拟数据"）。

## 扩展功能

### 添加邮件通知

在配置文件中添加邮件通知：

```yaml
onFailure:
  notify: true
  notifyEmails:
    - user@example.com
```

### 添加自定义简评逻辑

修改 `ChinaStockService.GenerateBriefingNote()` 方法，添加自定义的市场分析逻辑。

## 相关文档

- [定时任务系统文档](./BATCH_JOBS_IMPLEMENTATION.md)
- [Cron 表达式语法](https://developer.alibaba.com/docs/doc.htm?treeId=608&articleId=113142)
- [东方财富网 API 文档](https://akshare.akfamily.xyz/data/index/index.html)

## 更新日志

- 2026-03-27：初始版本，支持每天 09:00 和 16:00 定时获取中国股市行情
