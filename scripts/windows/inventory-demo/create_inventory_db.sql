-- ============================================================================
-- NetYamlForge 在庫管理システム データベーススキーマ
-- ============================================================================
-- 概要:
--   在庫管理システム (Inventory Management System) の SQLite データベーススキーマ
--
-- 使用方法:
--   sqlite3 inventory.db < create_inventory_db.sql
--
-- 著者：NetYamlForge Team
-- 更新日：2026-03-19
-- ============================================================================

-- 既存テーブルの削除（オプション）
-- DROP TABLE IF EXISTS StockMovements;
-- DROP TABLE IF EXISTS Products;
-- DROP TABLE IF EXISTS Categories;

-- ============================================================================
-- カテゴリテーブル
-- ============================================================================
CREATE TABLE IF NOT EXISTS Categories (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Description TEXT,
    CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================================
-- 商品テーブル
-- ============================================================================
CREATE TABLE IF NOT EXISTS Products (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    CategoryId INTEGER,
    Price DECIMAL(10,2) NOT NULL DEFAULT 0,
    Stock INTEGER NOT NULL DEFAULT 0,
    MinStock INTEGER NOT NULL DEFAULT 10,
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
    UpdatedAt TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (CategoryId) REFERENCES Categories(Id)
);

-- ============================================================================
-- 在庫移動テーブル
-- ============================================================================
CREATE TABLE IF NOT EXISTS StockMovements (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId INTEGER NOT NULL,
    Quantity INTEGER NOT NULL,
    MovementType TEXT NOT NULL,  -- 'IN'(入荷), 'OUT'(出荷), 'ADJUST'(調整)
    Reason TEXT,
    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (ProductId) REFERENCES Products(Id)
);

-- ============================================================================
-- インデックス
-- ============================================================================
CREATE INDEX IF NOT EXISTS IX_Products_CategoryId ON Products(CategoryId);
CREATE INDEX IF NOT EXISTS IX_StockMovements_ProductId ON StockMovements(ProductId);
CREATE INDEX IF NOT EXISTS IX_StockMovements_MovementType ON StockMovements(MovementType);
CREATE INDEX IF NOT EXISTS IX_Products_IsActive ON Products(IsActive);
CREATE INDEX IF NOT EXISTS IX_Products_Stock ON Products(Stock);

-- ============================================================================
-- サンプルデータ
-- ============================================================================

-- カテゴリ
INSERT INTO Categories (Name, Description) VALUES 
    ('電子機器', 'スマートフォン、PC、タブレット等'),
    ('オフィス用品', '文具、事務機器、消耗品等'),
    ('家具', 'デスク、チェア、キャビネット等'),
    ('家電', '冷蔵庫、洗濯機、エアコン等'),
    ('スポーツ用品', 'ゴルフ、テニス、フィットネス等');

-- 商品
INSERT INTO Products (Name, CategoryId, Price, Stock, MinStock, IsActive) VALUES 
    -- 電子機器
    ('iPhone 15 Pro', 1, 159800, 50, 10, 1),
    ('iPhone 15', 1, 124800, 80, 15, 1),
    ('MacBook Pro 14"', 1, 248800, 30, 5, 1),
    ('MacBook Air 13"', 1, 164800, 40, 8, 1),
    ('iPad Pro 11"', 1, 140800, 35, 10, 1),
    ('AirPods Pro', 1, 39800, 100, 20, 1),
    
    -- オフィス用品
    ('ボールペン 10 本セット', 2, 580, 200, 50, 1),
    ('ノート A4 100 枚', 2, 320, 300, 100, 1),
    ('コピー用紙 A4 5000 枚', 2, 4980, 150, 30, 1),
    ('デスクオーガナイザー', 2, 1280, 80, 20, 1),
    
    -- 家具
    ('オフィスチェア エルゴノミクス', 3, 45800, 15, 5, 1),
    ('スタンディングデスク', 3, 68000, 10, 3, 1),
    ('書棚 5 段', 3, 25800, 20, 5, 1),
    ('キャビネット 3 段', 3, 18800, 25, 8, 1),
    
    -- 家電
    ('冷蔵庫 400L', 4, 128000, 8, 3, 1),
    ('洗濯機 10kg', 4, 89800, 12, 4, 1),
    ('エアコン 14 畳', 4, 78000, 15, 5, 1),
    
    -- スポーツ用品
    ('ゴルフクラブセット', 5, 89800, 10, 3, 1),
    ('テニスラケット', 5, 25800, 20, 5, 1),
    ('ヨガマット', 5, 3980, 50, 15, 1);

-- 在庫移動（サンプル）
INSERT INTO StockMovements (ProductId, Quantity, MovementType, Reason, CreatedAt) VALUES 
    -- 入荷
    (1, 100, 'IN', '初期在庫', datetime('now', '-30 days')),
    (2, 150, 'IN', '初期在庫', datetime('now', '-30 days')),
    (3, 50, 'IN', '初期在庫', datetime('now', '-30 days')),
    (7, 500, 'IN', '初期在庫', datetime('now', '-30 days')),
    (11, 30, 'IN', '初期在庫', datetime('now', '-30 days')),
    
    -- 出荷
    (1, -20, 'OUT', '注文出荷：#1001', datetime('now', '-25 days')),
    (2, -30, 'OUT', '注文出荷：#1002', datetime('now', '-24 days')),
    (3, -10, 'OUT', '注文出荷：#1003', datetime('now', '-23 days')),
    (7, -150, 'OUT', '注文出荷：#1004', datetime('now', '-22 days')),
    (1, -15, 'OUT', '注文出荷：#1005', datetime('now', '-20 days')),
    
    -- 追加入荷
    (1, 50, 'IN', '追加発注', datetime('now', '-15 days')),
    (3, 20, 'IN', '追加発注', datetime('now', '-15 days')),
    
    -- さらなる出荷
    (1, -25, 'OUT', '注文出荷：#1006', datetime('now', '-10 days')),
    (2, -40, 'OUT', '注文出荷：#1007', datetime('now', '-8 days')),
    (11, -10, 'OUT', '注文出荷：#1008', datetime('now', '-5 days')),
    
    -- 在庫調整
    (7, -50, 'ADJUST', '棚卸しによる調整', datetime('now', '-3 days')),
    (11, -5, 'ADJUST', '破損による廃棄', datetime('now', '-2 days'));

-- ============================================================================
-- 確認クエリ
-- ============================================================================

-- カテゴリ数
SELECT 'Categories: ' || COUNT(*) FROM Categories;

-- 商品数
SELECT 'Products: ' || COUNT(*) FROM Products;

-- 在庫移動数
SELECT 'StockMovements: ' || COUNT(*) FROM StockMovements;

-- 在庫金額合計
SELECT 'Total Stock Value: ¥' || SUM(Stock * Price) FROM Products WHERE IsActive = 1;

-- 最小在庫割れ商品数
SELECT 'Low Stock Alerts: ' || COUNT(*) FROM Products WHERE Stock < MinStock AND IsActive = 1;
