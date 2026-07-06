# Northwind 小売管理システム 設計書

## 1. プロジェクト概要

- **プロジェクト名**: northwind-retail
- **表示名**: Northwind 小売管理システム
- **基盤DB**: [jpwhite3/northwind-SQLite3](https://github.com/jpwhite3/northwind-SQLite3)
- **フレームワーク**: NetYamlForge（YAML駆動CRUD・ページ・ダッシュボード）
- **対象ユーザー**: 中小企業の販売管理・在庫管理・購買管理・配送管理業務
- **目的**: Northwind サンプルデータベースをベースに、実際業務で稼働可能な小売ERPシステムを構築

### 1.1 主要業務領域

| 領域 | 説明 | 対象テーブル |
|------|------|-------------|
| **受注管理** | 顧客からの注文受付・出荷・請求 | Orders, Order Details, Customers |
| **在庫管理** | 商品在庫の管理・補充発注 | Products, Categories, Suppliers |
| **購買管理** | 仕入先からの商品調達 | Suppliers, Products |
| **配送管理** | 出荷配送業者管理・運送追跡 | Shippers, Orders |
| **顧客管理** | 顧客情報・顧客区分管理 | Customers, CustomerDemographics, CustomerCustomerDemo |
| **社員管理** | 営業担当・社員情報管理 | Employees, EmployeeTerritories |
| **地域管理** | 販売地域・テリトリー管理 | Regions, Territories |
| **レポート** | 売上分析・販売実績・カテゴリ別分析 | ビュー各種 |

---

## 2. データベーススキーマ（Northwind）

### 2.1 テーブル一覧

| # | テーブル名 | PK | 外部キー | 説明 |
|---|------------|----|---------|------|
| 1 | Categories | CategoryID | - | 商品カテゴリ |
| 2 | Customers | CustomerID | - | 顧客マスタ |
| 3 | Employees | EmployeeID | ReportsTo → Employees | 社員マスタ |
| 4 | Order Details | OrderID + ProductID | OrderID → Orders, ProductID → Products | 注文明細 |
| 5 | Orders | OrderID | CustomerID → Customers, EmployeeID → Employees, ShipVia → Shippers | 注文ヘッダ |
| 6 | Products | ProductID | SupplierID → Suppliers, CategoryID → Categories | 商品マスタ |
| 7 | Regions | RegionID | - | 販売地域 |
| 8 | Shippers | ShipperID | - | 配送業者 |
| 9 | Suppliers | SupplierID | - | 仕入先 |
| 10 | Territories | TerritoryID | RegionID → Regions | 販売テリトリー |
| 11 | EmployeeTerritories | (EmployeeID, TerritoryID) | EmployeeID → Employees, TerritoryID → Territories | 社員-テリトリー紐付け |
| 12 | CustomerDemographics | CustomerTypeID | - | 顧客区分 |
| 13 | CustomerCustomerDemo | (CustomerID, CustomerTypeID) | CustomerID → Customers, CustomerTypeID → CustomerDemographics | 顧客-区分紐付け |

### 2.2 リレーション図

```
【顧客管理】
  Customers ──→ CustomerCustomerDemo ──→ CustomerDemographics
  
【商品管理】
  Categories ──→ Products ←── Suppliers
  
【受注管理】
  Customers ──→ Orders ──→ Order Details ──→ Products
             ↑         ↑
           Employees  Shippers

【社員地域管理】
  Regions ──→ Territories ←── EmployeeTerritories ──→ Employees
    
  Employees ──→ Employees (ReportsTo: 自己参照)

【Views】（レポート用）
  - Invoices
  - Order Subtotals
  - Product Sales for 1997
  - Sales by Category
  - Category Sales for 1997
  - Alphabetical list of products
  - Current Product List
  - Products Above Average Price
  - Products by Category
  - etc.
```

### 2.3 テーブル詳細定義

#### Categories
| カラム | 型 | 制約 | 説明 |
|--------|-----|------|------|
| CategoryID | INTEGER | PK, AUTOINCREMENT | カテゴリID |
| CategoryName | TEXT | NOT NULL | カテゴリ名 |
| Description | TEXT | - | 説明 |
| Picture | BLOB | - | 画像 |

#### Customers
| カラム | 型 | 制約 | 説明 |
|--------|-----|------|------|
| CustomerID | TEXT | PK | 顧客ID (5文字コード) |
| CompanyName | TEXT | NOT NULL | 会社名 |
| ContactName | TEXT | - | 担当者名 |
| ContactTitle | TEXT | - | 役職 |
| Address | TEXT | - | 住所 |
| City | TEXT | - | 市区町村 |
| Region | TEXT | - | 地域/都道府県 |
| PostalCode | TEXT | - | 郵便番号 |
| Country | TEXT | - | 国 |
| Phone | TEXT | - | 電話番号 |
| Fax | TEXT | - | FAX |

#### Employees
| カラム | 型 | 制約 | 説明 |
|--------|-----|------|------|
| EmployeeID | INTEGER | PK, AUTOINCREMENT | 社員ID |
| LastName | TEXT | NOT NULL | 姓 |
| FirstName | TEXT | NOT NULL | 名 |
| Title | TEXT | - | 役職名 |
| TitleOfCourtesy | TEXT | - | 敬称 |
| BirthDate | TEXT (date) | - | 生年月日 |
| HireDate | TEXT (date) | - | 入社日 |
| Address | TEXT | - | 住所 |
| City | TEXT | - | 市区町村 |
| Region | TEXT | - | 地域 |
| PostalCode | TEXT | - | 郵便番号 |
| Country | TEXT | - | 国 |
| HomePhone | TEXT | - | 電話番号 |
| Extension | TEXT | - | 内線番号 |
| Photo | BLOB | - | 写真 |
| Notes | TEXT | - | 備考 |
| ReportsTo | INTEGER | FK → Employees | 上司ID |
| PhotoPath | TEXT | - | 写真パス（未使用）|

#### Orders
| カラム | 型 | 制約 | 説明 |
|--------|-----|------|------|
| OrderID | INTEGER | PK, AUTOINCREMENT | 注文ID |
| CustomerID | TEXT | FK → Customers | 顧客ID |
| EmployeeID | INTEGER | FK → Employees | 担当社員ID |
| OrderDate | TEXT (datetime) | - | 注文日 |
| RequiredDate | TEXT (datetime) | - | 必要日 |
| ShippedDate | TEXT (datetime) | - | 出荷日 |
| ShipVia | INTEGER | FK → Shippers | 配送業者ID |
| Freight | NUMERIC | - | 運送料 |
| ShipName | TEXT | - | 配送先名 |
| ShipAddress | TEXT | - | 配送先住所 |
| ShipCity | TEXT | - | 配送先市区町村 |
| ShipRegion | TEXT | - | 配送先地域 |
| ShipPostalCode | TEXT | - | 配送先郵便番号 |
| ShipCountry | TEXT | - | 配送先国 |

#### Order Details
| カラム | 型 | 制約 | 説明 |
|--------|-----|------|------|
| OrderID | INTEGER | PK, FK → Orders | 注文ID |
| ProductID | INTEGER | PK, FK → Products | 商品ID |
| UnitPrice | REAL | - | 単価 |
| Quantity | INTEGER | - | 数量 |
| Discount | REAL | - | 割引率 |

#### Products
| カラム | 型 | 制約 | 説明 |
|--------|-----|------|------|
| ProductID | INTEGER | PK, AUTOINCREMENT | 商品ID |
| ProductName | TEXT | NOT NULL | 商品名 |
| SupplierID | INTEGER | FK → Suppliers | 仕入先ID |
| CategoryID | INTEGER | FK → Categories | カテゴリID |
| QuantityPerUnit | TEXT | - | 単位あたり数量 |
| UnitPrice | REAL | - | 単価 |
| UnitsInStock | INTEGER | - | 在庫数 |
| UnitsOnOrder | INTEGER | - | 注文数 |
| ReorderLevel | INTEGER | - | 発注点 |
| Discontinued | TEXT | - | 販売中止フラグ |

#### Suppliers
| カラム | 型 | 制約 | 説明 |
|--------|-----|------|------|
| SupplierID | INTEGER | PK, AUTOINCREMENT | 仕入先ID |
| CompanyName | TEXT | NOT NULL | 会社名 |
| ContactName | TEXT | - | 担当者名 |
| ContactTitle | TEXT | - | 役職 |
| Address | TEXT | - | 住所 |
| City | TEXT | - | 市区町村 |
| Region | TEXT | - | 地域 |
| PostalCode | TEXT | - | 郵便番号 |
| Country | TEXT | - | 国 |
| Phone | TEXT | - | 電話番号 |
| Fax | TEXT | - | FAX |
| HomePage | TEXT | - | Webサイト |

#### Shippers
| カラム | 型 | 制約 | 説明 |
|--------|-----|------|------|
| ShipperID | INTEGER | PK, AUTOINCREMENT | 配送業者ID |
| CompanyName | TEXT | NOT NULL | 会社名 |
| Phone | TEXT | - | 電話番号 |

#### Regions
| カラム | 型 | 制約 | 説明 |
|--------|-----|------|------|
| RegionID | INTEGER | PK, AUTOINCREMENT | 地域ID |
| RegionDescription | TEXT | NOT NULL | 地域名 |

#### Territories
| カラム | 型 | 制約 | 説明 |
|--------|-----|------|------|
| TerritoryID | TEXT | PK | テリトリーID |
| TerritoryDescription | TEXT | NOT NULL | テリトリー名 |
| RegionID | INTEGER | FK → Regions | 地域ID |

---

## 3. ユーザーロールと権限設計

| ロール | 説明 | アクセス権限 |
|--------|------|-------------|
| admin | システム管理者 | 全機能アクセス |
| sales_rep | 営業担当 | 顧客管理・受注管理 |
| sales_manager | 営業管理者 | sales_rep + レポート・売上分析 |
| customer | 顧客(取引先) | 自社注文照会のみ(制限ビュー) |
| warehouse | 倉庫担当 | 在庫管理・出荷処理 |

### ロール別ナビゲーション

- **admin**: 全メニュー表示
- **sales_rep**: 顧客管理、受注登録、商品照会
- **sales_manager**: sales_rep + ダッシュボード、売上レポート
- **customer**: 自社注文履歴照会
- **warehouse**: 在庫照会、出荷処理

---

## 4. エンティティ定義（YAML）

NetYamlForge の `--scaffold-entities` で自動生成後、以下のようにカスタマイズ。

### 4.1 顧客 (Customers)

```yaml
entities:
  customer:
    table: Customers
    key: CustomerID
    displayName: 顧客
    isPublic: true
    columns:
      CustomerID: { type: string, label: 顧客ID, sortable: true, searchable: true }
      CompanyName: { type: string, label: 会社名, sortable: true, searchable: true }
      ContactName: { type: string, label: 担当者, searchable: true }
      ContactTitle: { type: string, label: 役職 }
      Country: { type: string, label: 国, sortable: true }
      City: { type: string, label: 市区町村, sortable: true }
      Phone: { type: string, label: 電話番号 }
    forms:
      CustomerID: { type: string, label: 顧客ID, editable: false }
      CompanyName: { type: string, label: 会社名, required: true }
      ContactName: { type: string, label: 担当者名 }
      ContactTitle: { type: string, label: 役職 }
      Address: { type: text, label: 住所 }
      City: { type: string, label: 市区町村 }
      Region: { type: string, label: 地域 }
      PostalCode: { type: string, label: 郵便番号 }
      Country: { type: string, label: 国 }
      Phone: { type: string, label: 電話番号 }
      Fax: { type: string, label: FAX }
    links:
      orders: { label: 注文履歴, targetEntity: order, filter: { CustomerID: CustomerID } }
    actions:
      view_orders: { label: 注文一覧, scope: row, url: "DynamicEntity/Index?entity=order&CustomerID={CustomerID}" }
```

### 4.2 注文 (Orders)

```yaml
entities:
  order:
    table: Orders
    key: OrderID
    displayName: 注文
    isPublic: true
    columns:
      OrderID: { type: int, label: 注文番号, sortable: true }
      CustomerID: { type: string, label: 顧客 }
      EmployeeID: { type: int, label: 担当者 }
      OrderDate: { type: datetime, label: 注文日, sortable: true }
      RequiredDate: { type: datetime, label: 必要日 }
      ShippedDate: { type: datetime, label: 出荷日 }
      ShipName: { type: string, label: 配送先 }
      Freight: { type: decimal, label: 運送料 }
    forms:
      CustomerID: { type: string, label: 顧客, required: true, foreignKey: { entity: customer, displayColumn: CompanyName } }
      EmployeeID: { type: int, label: 担当者, foreignKey: { entity: employee, displayColumn: "LastName || ' ' || FirstName" } }
      OrderDate: { type: datetime, label: 注文日 }
      RequiredDate: { type: datetime, label: 必要日 }
      ShippedDate: { type: datetime, label: 出荷日 }
      ShipVia: { type: int, label: 配送業者, foreignKey: { entity: shipper, displayColumn: CompanyName } }
      Freight: { type: decimal, label: 運送料 }
      ShipName: { type: string, label: 配送先名 }
      ShipAddress: { type: text, label: 配送先住所 }
      ShipCity: { type: string, label: 配送先市区町村 }
      ShipCountry: { type: string, label: 配送先国 }
    links:
      details: { label: 注文明細, targetEntity: order_detail, filter: { OrderID: OrderID } }
      customer: { label: 顧客情報, targetEntity: customer, filter: { CustomerID: CustomerID } }
    exports:
      order_csv: { label: "CSV出力", format: csv, columns: [OrderID, CustomerID, OrderDate, ShipName, Freight] }
```

### 4.3 注文明細 (Order Details)

```yaml
entities:
  order_detail:
    table: Order Details
    key: [OrderID, ProductID]
    displayName: 注文明細
    isPublic: true
    columns:
      OrderID: { type: int, label: 注文番号, sortable: true }
      ProductID: { type: int, label: 商品 }
      UnitPrice: { type: decimal, label: 単価 }
      Quantity: { type: int, label: 数量 }
      Discount: { type: decimal, label: 割引率 }
    forms:
      OrderID: { type: int, label: 注文番号, required: true, foreignKey: { entity: order, displayColumn: OrderID } }
      ProductID: { type: int, label: 商品, required: true, foreignKey: { entity: product, displayColumn: ProductName } }
      UnitPrice: { type: decimal, label: 単価, required: true }
      Quantity: { type: int, label: 数量, required: true }
      Discount: { type: decimal, label: 割引率 }
```

### 4.4 商品 (Products)

```yaml
entities:
  product:
    table: Products
    key: ProductID
    displayName: 商品
    isPublic: true
    columns:
      ProductID: { type: int, label: 商品ID }
      ProductName: { type: string, label: 商品名, searchable: true, sortable: true }
      SupplierID: { type: int, label: 仕入先 }
      CategoryID: { type: int, label: カテゴリ }
      QuantityPerUnit: { type: string, label: 単位当数量 }
      UnitPrice: { type: decimal, label: 単価, sortable: true }
      UnitsInStock: { type: int, label: 在庫数, sortable: true }
      UnitsOnOrder: { type: int, label: 注文数 }
      ReorderLevel: { type: int, label: 発注点 }
      Discontinued: { type: string, label: 販売中止 }
    forms:
      ProductName: { type: string, label: 商品名, required: true }
      SupplierID: { type: int, label: 仕入先, foreignKey: { entity: supplier, displayColumn: CompanyName } }
      CategoryID: { type: int, label: カテゴリ, foreignKey: { entity: category, displayColumn: CategoryName } }
      QuantityPerUnit: { type: string, label: 単位当数量 }
      UnitPrice: { type: decimal, label: 単価 }
      UnitsInStock: { type: int, label: 在庫数 }
      UnitsOnOrder: { type: int, label: 注文数 }
      ReorderLevel: { type: int, label: 発注点 }
      Discontinued: { type: string, label: 販売中止, options: [0, 1] }
    actions:
      reorder: { label: 発注, scope: row, url: "DynamicEntity/Create?entity=product&SupplierID={SupplierID}" }
```

### 4.5 仕入先 (Suppliers)

```yaml
entities:
  supplier:
    table: Suppliers
    key: SupplierID
    displayName: 仕入先
    isPublic: true
    columns:
      SupplierID: { type: int, label: ID }
      CompanyName: { type: string, label: 会社名, searchable: true, sortable: true }
      ContactName: { type: string, label: 担当者 }
      Country: { type: string, label: 国 }
      Phone: { type: string, label: 電話番号 }
    links:
      products: { label: 取扱商品, targetEntity: product, filter: { SupplierID: SupplierID } }
```

### 4.6 カテゴリ (Categories)

```yaml
entities:
  category:
    table: Categories
    key: CategoryID
    displayName: カテゴリ
    isPublic: true
    columns:
      CategoryID: { type: int, label: ID }
      CategoryName: { type: string, label: カテゴリ名, searchable: true, sortable: true }
      Description: { type: text, label: 説明 }
```

### 4.7 配送業者 (Shippers)

```yaml
entities:
  shipper:
    table: Shippers
    key: ShipperID
    displayName: 配送業者
    isPublic: false
    columns:
      ShipperID: { type: int, label: ID }
      CompanyName: { type: string, label: 会社名, sortable: true }
      Phone: { type: string, label: 電話番号 }
```

### 4.8 社員 (Employees)

```yaml
entities:
  employee:
    table: Employees
    key: EmployeeID
    displayName: 社員
    isPublic: true
    columns:
      EmployeeID: { type: int, label: ID }
      LastName: { type: string, label: 姓 }
      FirstName: { type: string, label: 名 }
      Title: { type: string, label: 役職, sortable: true }
      HireDate: { type: datetime, label: 入社日, sortable: true }
      Country: { type: string, label: 国 }
    forms:
      LastName: { type: string, label: 姓, required: true }
      FirstName: { type: string, label: 名, required: true }
      Title: { type: string, label: 役職 }
      BirthDate: { type: datetime, label: 生年月日 }
      HireDate: { type: datetime, label: 入社日 }
      Address: { type: text, label: 住所 }
      City: { type: string, label: 市区町村 }
      Country: { type: string, label: 国 }
      HomePhone: { type: string, label: 電話番号 }
      Notes: { type: text, label: 備考 }
      ReportsTo: { type: int, label: 上司, foreignKey: { entity: employee, displayColumn: "LastName || ' ' || FirstName" } }
    links:
      orders: { label: 担当注文, targetEntity: order, filter: { EmployeeID: EmployeeID } }
```

### 4.9 地域 (Regions) / テリトリー (Territories)

```yaml
entities:
  region:
    table: Regions
    key: RegionID
    displayName: 地域
    isPublic: false
    columns:
      RegionID: { type: int, label: ID }
      RegionDescription: { type: string, label: 地域名 }

  territory:
    table: Territories
    key: TerritoryID
    displayName: テリトリー
    isPublic: false
    columns:
      TerritoryID: { type: string, label: ID }
      TerritoryDescription: { type: string, label: テリトリー名 }
      RegionID: { type: int, label: 地域 }
```

---

## 5. ページ定義

### 5.1 ダッシュボード (Dashboard)

```yaml
stats:
  - label: 総受注数
    entity: order
    aggregate: count
    icon: 📋
    color: badge-primary
  - label: 未出荷注文
    entity: order
    aggregate: count
    filter: "ShippedDate IS NULL"
    icon: ⏳
    color: badge-warning
  - label: 在庫商品数
    entity: product
    aggregate: count
    filter: "UnitsInStock > 0"
    icon: 📦
    color: badge-success
  - label: 発注点以下
    entity: product
    aggregate: count
    filter: "UnitsInStock <= ReorderLevel AND Discontinued = 0"
    icon: ⚠️
    color: badge-danger
  - label: 取引先数
    entity: customer
    aggregate: count
    icon: 🏢
    color: badge-info
  - label: 今月の売上
    entity: order
    aggregate: count
    filter: "strftime('%Y-%m', OrderDate) = strftime('%Y-%m', 'now')"
    icon: 💰
    color: badge-success

charts:
  - title: 月別売上推移
    type: line
    entity: order
    valueAggregate: count
    groupExpression: "strftime('%Y-%m', OrderDate)"
    orderBy: label
    orderDir: asc
    limit: 12
  - title: カテゴリ別商品数
    type: doughnut
    entity: product
    valueAggregate: count
    groupExpression: CategoryID
    orderBy: value
    orderDir: desc
    limit: 8
  - title: 国別顧客分布
    type: bar
    entity: customer
    valueAggregate: count
    groupExpression: Country
    orderBy: value
    orderDir: desc
    limit: 10
```

### 5.2 カスタムページ一覧（計画）

| ページ名 | 説明 | コンポーネント | ロール |
|----------|------|---------------|--------|
| SalesDashboard | 営業ダッシュボード | stat_cards, charts | sales_manager, admin |
| OrderKanban | 受注カンバン（未処理順） | kanban | sales_rep, sales_manager |
| InventoryOverview | 在庫概要 | stat_cards, table | warehouse, admin |
| CustomerOrders | 顧客別注文履歴 | table | customer（自社のみ）|
| ProductCatalog | 商品カタログ | card_list, table | 全ロール |
| ExecutiveReport | 経営レポート | stat_cards, charts | sales_manager, admin |

### 5.3 SalesDashboard ページ詳細案

```yaml
title: 営業ダッシュボード
sections:
  - id: kpi
    component: stat_cards
    sourceType: custom
    source: |
      SELECT '今月受注' AS metric_name,
             CAST(COUNT(*) AS TEXT) AS metric_value,
             '📋' AS metric_icon,
             NULL AS metric_delta
      FROM Orders
      WHERE strftime('%Y-%m', OrderDate) = strftime('%Y-%m', 'now')
      UNION ALL
      SELECT '今月売上合計',
             '$' || CAST(ROUND(SUM(od.UnitPrice * od.Quantity * (1 - od.Discount)), 2) AS TEXT),
             '💰',
             NULL
      FROM Orders o
      INNER JOIN "Order Details" od ON o.OrderID = od.OrderID
      WHERE strftime('%Y-%m', o.OrderDate) = strftime('%Y-%m', 'now')
      UNION ALL
      SELECT '未出荷件数',
             CAST(COUNT(*) AS TEXT),
             '⏳',
             NULL
      FROM Orders
      WHERE ShippedDate IS NULL
      UNION ALL
      SELECT '取引先数',
             CAST(COUNT(*) AS TEXT),
             '🏢',
             NULL
      FROM Customers
    columns: [metric_name, metric_value, metric_icon, metric_delta]

  - id: monthly_sales_chart
    component: bar_chart
    sourceType: custom
    source: |
      SELECT strftime('%Y-%m', o.OrderDate) AS label,
             ROUND(SUM(od.UnitPrice * od.Quantity * (1 - od.Discount)), 2) AS value
      FROM Orders o
      INNER JOIN "Order Details" od ON o.OrderID = od.OrderID
      WHERE o.OrderDate >= date('now', '-12 months')
      GROUP BY strftime('%Y-%m', o.OrderDate)
      ORDER BY label
    columns: [label, value]
    xField: label
    yField: value

  - id: top_customers
    component: table
    sourceType: custom
    source: |
      SELECT c.CompanyName AS 顧客名,
             c.Country AS 国,
             COUNT(o.OrderID) AS 注文件数,
             ROUND(SUM(od.UnitPrice * od.Quantity * (1 - od.Discount)), 2) AS 購入金額合計
      FROM Customers c
      INNER JOIN Orders o ON c.CustomerID = o.CustomerID
      INNER JOIN "Order Details" od ON o.OrderID = od.OrderID
      GROUP BY c.CustomerID
      ORDER BY 購入金額合計 DESC
      LIMIT 10
    columns: [顧客名, 国, 注文件数, 購入金額合計]
```

### 5.4 InventoryOverview ページ詳細案

```yaml
title: 在庫概要
sections:
  - id: inventory_kpi
    component: stat_cards
    sourceType: custom
    source: |
      SELECT '在庫あり商品' AS metric_name,
             CAST(COUNT(*) AS TEXT) AS metric_value,
             '📦' AS metric_icon, NULL
      FROM Products WHERE UnitsInStock > 0 AND Discontinued = 0
      UNION ALL
      SELECT '在庫切れ商品', CAST(COUNT(*) AS TEXT), '❌', NULL
      FROM Products WHERE (UnitsInStock = 0 OR UnitsInStock IS NULL) AND Discontinued = 0
      UNION ALL
      SELECT '発注点以下', CAST(COUNT(*) AS TEXT), '⚠️', NULL
      FROM Products WHERE UnitsInStock <= ReorderLevel AND Discontinued = 0
      UNION ALL
      SELECT '全商品数', CAST(COUNT(*) AS TEXT), '📋', NULL
      FROM Products WHERE Discontinued = 0
    columns: [metric_name, metric_value, metric_icon, metric_delta]

  - id: low_stock
    component: table
    sourceType: custom
    source: |
      SELECT p.ProductName AS 商品名,
             s.CompanyName AS 仕入先,
             p.UnitsInStock AS 在庫数,
             p.UnitsOnOrder AS 発注中,
             p.ReorderLevel AS 発注点,
             CASE WHEN p.UnitsInStock <= p.ReorderLevel THEN '⚠️ 要発注' ELSE '✅ 正常' END AS ステータス
      FROM Products p
      INNER JOIN Suppliers s ON p.SupplierID = s.SupplierID
      WHERE p.Discontinued = 0
      ORDER BY p.UnitsInStock ASC
      LIMIT 20
    columns: [商品名, 仕入先, 在庫数, 発注中, 発注点, ステータス]

  - id: category_distribution
    component: pie_chart
    sourceType: custom
    source: |
      SELECT c.CategoryName AS label, COUNT(p.ProductID) AS value
      FROM Products p
      INNER JOIN Categories c ON p.CategoryID = c.CategoryID
      WHERE p.Discontinued = 0
      GROUP BY c.CategoryName
    columns: [label, value]
    xField: label
    yField: value
```

---

## 6. プロジェクト設定 (project.yaml)

```yaml
name: northwind-retail
displayName: "Northwind 小売管理システム"
version: "1.0.0"
description: "Northwind データベースをベースにした小売管理システム（受注管理・在庫管理・顧客管理）"

database:
  type: sqlite
  path: database/northwind.db

features:
  multiLanguage: true
  userAuthentication: true
  dashboard: true
  pages: true
  api: true

layout:
  dashboardTheme: workspace
  navigation:
    showDashboard: true
    entities:
      - order
      - order_detail
      - customer
      - product
      - category
      - supplier
      - shipper
      - employee
      - region
      - territory
    items:
      - label: ダッシュボード
        controller: Dashboard
        action: Index
        icon: 📊
        section: Overview

      - label: 営業ダッシュボード
        url: /northwind-retail/Page/SalesDashboard
        icon: 📈
        section: 営業管理
      - label: 受注管理
        url: /northwind-retail/DynamicEntity/Index?entity=order
        icon: 📋
        section: 営業管理
      - label: 注文明細
        url: /northwind-retail/DynamicEntity/Index?entity=order_detail
        icon: 📝
        section: 営業管理
      - label: 顧客管理
        url: /northwind-retail/DynamicEntity/Index?entity=customer
        icon: 👥
        section: 営業管理

      - label: 商品管理
        url: /northwind-retail/DynamicEntity/Index?entity=product
        icon: 📦
        section: 在庫管理
      - label: カテゴリ
        url: /northwind-retail/DynamicEntity/Index?entity=category
        icon: 🏷️
        section: 在庫管理
      - label: 在庫概要
        url: /northwind-retail/Page/InventoryOverview
        icon: 📊
        section: 在庫管理

      - label: 仕入先管理
        url: /northwind-retail/DynamicEntity/Index?entity=supplier
        icon: 🏭
        section: 購買管理

      - label: 配送業者
        url: /northwind-retail/DynamicEntity/Index?entity=shipper
        icon: 🚚
        section: 配送管理

      - label: 社員管理
        url: /northwind-retail/DynamicEntity/Index?entity=employee
        icon: 👨‍💼
        section: 組織管理
      - label: 地域管理
        url: /northwind-retail/DynamicEntity/Index?entity=region
        icon: 🌍
        section: 組織管理
      - label: テリトリー
        url: /northwind-retail/DynamicEntity/Index?entity=territory
        icon: 🗺️
        section: 組織管理

      - label: 経営レポート
        url: /northwind-retail/Page/ExecutiveReport
        icon: 📊
        section: レポート
        roles: [admin, sales_manager]

settings:
  locale: ja-JP
  timezone: Asia/Tokyo
```

---

## 7. 実装手順

### Phase 1: プロジェクト作成とDBセットアップ
1. `dotnet run -- --init-project --project=northwind-retail --display-name="Northwind 小売管理システム" --db-type=sqlite`
2. 作成された `database/northwind.db` に Northwind のスキーマとデータを投入
3. `dotnet run -- --scaffold-entities --project=northwind-retail` で全エンティティを自動生成

### Phase 2: エンティティ設定のカスタマイズ
1. 生成されたエンティティYAMLを上記設計に合わせて編集（ラベル日本語化、FK設定、リンク設定等）
2. ダッシュボード設定の記述
3. View系エンティティの手動追加（Invoices, Sales by Category等）

### Phase 3: カスタムページ作成
1. SalesDashboard ページの作成（カスタムSQL）
2. InventoryOverview ページの作成
3. ExecutiveReport ページの作成（クロス期間売上比較等）
4. ProductCatalog ページの作成（card_list表示）

### Phase 4: ロール権限設定
1. プロジェクト設定にロールベースナビゲーションを追加
2. 各エンティティにロールフィルター設定
3. テストユーザーの作成

### Phase 5: テスト・調整
1. CRUD 操作の正常性確認
2. ダッシュボード・チャートのデータ確認
3. ページ表示確認
4. パフォーマンスチューニング

---

## 8. 機能一覧（MVP）

### 必須機能 (P0)
- [x] 顧客管理（一覧・作成・編集・削除）
- [x] 受注管理（一覧・作成・編集・削除）
- [x] 注文明細（一覧・作成・編集・削除）
- [x] 商品管理（一覧・作成・編集・削除）
- [x] 仕入先管理（一覧・作成・編集・削除）
- [x] カテゴリ管理（一覧・作成・編集・削除）
- [x] 社員管理（一覧・作成・編集・削除）
- [x] ダッシュボード（KPI・グラフ表示）
- [x] 検索・フィルター機能

### 重要な機能 (P1)
- [x] 営業ダッシュボード（売上推移・TOP顧客）
- [x] 在庫概要ページ（発注点アラート）
- [x] CSVエクスポート
- [x] テリトリー・地域管理
- [x] 配送業者管理
- [x] 経営レポートページ

### 追加機能 (P2)
- [ ] カスタムフック（受注作成時の在庫自動更新）
- [ ] PDFエクスポート（請求書・納品書）
- [ ] バッチジョブ（月末売上集計・在庫補充レポート）
- [ ] AIチャット統合（商品レコメンド）

---

## 9. NetYamlForge 機能活用箇所

| 機能 | 活用内容 |
|------|---------|
| YAML駆動CRUD | 全13テーブルのCRUD操作 |
| 動的フィルター | ステータスフィルター（出荷済/未出荷）、日付範囲フィルター |
| FKドロップダウン | 顧客、商品、社員、配送業者の選択UI |
| ページシステム | ダッシュボード、在庫概要、営業レポート |
| ダッシュボード | 売上KPI、グラフ各種 |
| エクスポート | CSVエクスポート（受注一覧） |
| 多言語対応 | 日本語/英語UI切替 |
| 認証・ロール | ロール別メニュー制御 |
| HTMX | モーダルCRUD・インライン編集 |
