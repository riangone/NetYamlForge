# ============================================================================
# NetYamlForge 在庫管理システム 自動生成スクリプト
# ============================================================================
# 概要:
#   CLI コマンドを使用して在庫管理システムのサブプロジェクトを自動生成します。
#
# 使用方法:
#   PowerShell を管理者権限で実行し、以下のコマンドを実行：
#   .\New-InventoryProject.ps1
#
# 前提条件:
#   - .NET 10.0 SDK がインストールされていること
#   - SQLite がインストールされていること (オプション、手動作成も可能)
#   - NetYamlForge ソリューションがビルド済みであること
#
# 著者: NetYamlForge Team
# 更新日: 2026-03-19
# ============================================================================

[CmdletBinding()]
param(
    # プロジェクト名 (デフォルト：inventory)
    [string]$ProjectName = "inventory",
    
    # 表示名 (デフォルト：在庫管理システム)
    [string]$DisplayName = "在庫管理システム",
    
    # 出力先ディレクトリ (デフォルト：スクリプトと同一ディレクトリ)
    [string]$OutputDir = "",
    
    # 詳細モード
    [switch]$Verbose,
    
    # 確認プロンプトをスキップ
    [switch]$Force
)

# ============================================================================
# 初期設定
# ============================================================================
$ErrorActionPreference = "Stop"
$ProgressPreference = "Continue"

# スクリプトディレクトリの取得
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrEmpty($OutputDir)) {
    $OutputDir = $ScriptDir
}

# 色の定義
$ColorInfo = "Cyan"
$ColorSuccess = "Green"
$ColorWarning = "Yellow"
$ColorError = "Red"

# ============================================================================
# ヘルパー関数
# ============================================================================

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor $ColorInfo
}

function Write-Success {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor $ColorSuccess
}

function Write-Warning {
    param([string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor $ColorWarning
}

function Write-Error {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor $ColorError
}

function Test-Command {
    param([string]$Name)
    $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Test-DotNet {
    try {
        $version = dotnet --version 2>$null
        return $null -ne $version
    }
    catch {
        return $false
    }
}

function Test-SQLite {
    try {
        $null = sqlite3 --version 2>$null
        return $true
    }
    catch {
        return $false
    }
}

function Invoke-Retry {
    param(
        [scriptblock]$ScriptBlock,
        [int]$MaxAttempts = 3,
        [int]$DelaySeconds = 2
    )
    
    $attempt = 1
    while ($attempt -le $MaxAttempts) {
        try {
            & $ScriptBlock
            return
        }
        catch {
            if ($attempt -eq $MaxAttempts) {
                throw
            }
            Write-Warning "試行 $attempt 失敗。${DelaySeconds}秒後に再試行します..."
            Start-Sleep -Seconds $DelaySeconds
            $attempt++
        }
    }
}

# ============================================================================
# 事前チェック
# ============================================================================

Write-Host ""
Write-Host "============================================================================" -ForegroundColor $ColorInfo
Write-Host "  NetYamlForge 在庫管理システム 自動生成スクリプト" -ForegroundColor $ColorInfo
Write-Host "============================================================================" -ForegroundColor $ColorInfo
Write-Host ""

Write-Info "プロジェクト名：$ProjectName"
Write-Info "表示名：$DisplayName"
Write-Info "出力先：$OutputDir"
Write-Host ""

# .NET SDK のチェック
Write-Info ".NET SDK を確認中..."
if (-not (Test-DotNet)) {
    Write-Error ".NET 10.0 SDK がインストールされていません。"
    Write-Info "https://dotnet.microsoft.com/download からインストールしてください。"
    exit 1
}
$dotnetVersion = dotnet --version
Write-Success ".NET SDK バージョン：$dotnetVersion"

# SQLite のチェック（オプション）
$hasSqlite = Test-SQLite
if ($hasSqlite) {
    Write-Success "SQLite が利用可能です"
}
else {
    Write-Warning "SQLite が見つかりません。DB ファイルは手動で作成してください。"
}

# 確認プロンプト
if (-not $Force) {
    Write-Host ""
    $confirm = Read-Host "上記の設定でプロジェクトを生成しますか？ (y/n)"
    if ($confirm -ne 'y' -and $confirm -ne 'Y') {
        Write-Info "スクリプトを中止しました。"
        exit 0
    }
}

# ============================================================================
# ステップ 1: プロジェクトの初期化
# ============================================================================

Write-Host ""
Write-Host "----------------------------------------------------------------------------" -ForegroundColor $ColorInfo
Write-Host "  ステップ 1: プロジェクトの初期化" -ForegroundColor $ColorInfo
Write-Host "----------------------------------------------------------------------------" -ForegroundColor $ColorInfo
Write-Host ""

try {
    Write-Info "プロジェクトを初期化中..."
    dotnet run -- --init-project --project=$ProjectName --display-name=$DisplayName --force
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "プロジェクトの初期化が完了しました"
    }
    else {
        throw "dotnet run コマンドが失敗しました"
    }
}
catch {
    Write-Error "プロジェクトの初期化に失敗しました：$_"
    exit 1
}

# プロジェクトディレクトリの設定
$ProjectDir = Join-Path $OutputDir "projects" $ProjectName
if (-not (Test-Path $ProjectDir)) {
    # 相対パスを解決
    $currentDir = Get-Location
    $ProjectDir = Join-Path $currentDir "NetYamlForge" "projects" $ProjectName
    if (-not (Test-Path $ProjectDir)) {
        $ProjectDir = Join-Path $currentDir "projects" $ProjectName
    }
}

Write-Info "プロジェクトディレクトリ：$ProjectDir"

# ============================================================================
# ステップ 2: データベースの作成
# ============================================================================

Write-Host ""
Write-Host "----------------------------------------------------------------------------" -ForegroundColor $ColorInfo
Write-Host "  ステップ 2: データベースの作成" -ForegroundColor $ColorInfo
Write-Host "----------------------------------------------------------------------------" -Write-Host ""

$DatabaseDir = Join-Path $ProjectDir "database"
$DatabasePath = Join-Path $DatabaseDir "inventory.db"

if (-not (Test-Path $DatabaseDir)) {
    New-Item -ItemType Directory -Path $DatabaseDir -Force | Out-Null
    Write-Info "データベースディレクトリを作成しました：$DatabaseDir"
}

if ($hasSqlite) {
    Write-Info "SQLite データベースを作成中..."
    
    # SQL スクリプトの実行
    $SqlScript = @'
-- カテゴリテーブル
CREATE TABLE IF NOT EXISTS Categories (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Description TEXT,
    CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
);

-- 商品テーブル
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

-- 在庫移動テーブル
CREATE TABLE IF NOT EXISTS StockMovements (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId INTEGER NOT NULL,
    Quantity INTEGER NOT NULL,
    MovementType TEXT NOT NULL,
    Reason TEXT,
    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (ProductId) REFERENCES Products(Id)
);

-- インデックス作成
CREATE INDEX IF NOT EXISTS IX_Products_CategoryId ON Products(CategoryId);
CREATE INDEX IF NOT EXISTS IX_StockMovements_ProductId ON StockMovements(ProductId);
CREATE INDEX IF NOT EXISTS IX_StockMovements_MovementType ON StockMovements(MovementType);

-- サンプルデータ
INSERT INTO Categories (Name, Description) VALUES 
    ('電子機器', 'スマートフォン、PC 等'),
    ('オフィス用品', '文具、事務機器等'),
    ('家具', 'デスク、チェア等');

INSERT INTO Products (Name, CategoryId, Price, Stock, MinStock) VALUES 
    ('iPhone 15 Pro', 1, 159800, 50, 10),
    ('MacBook Pro 14"', 1, 248800, 30, 5),
    ('ボールペン 10 本セット', 2, 580, 200, 50),
    ('オフィスチェア', 3, 25800, 20, 5);
'@
    
    $SqlScript | sqlite3 $DatabasePath
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "データベースの作成が完了しました：$DatabasePath"
    }
    else {
        Write-Warning "データベースの作成に失敗しました"
    }
}
else {
    Write-Info "空のデータベースファイルを作成中..."
    New-Item -ItemType File -Path $DatabasePath -Force | Out-Null
    Write-Warning "SQLite が見つからないため、空の DB ファイルを作成しました。"
    Write-Info "後で手動でテーブルを作成してください。"
}

# ============================================================================
# ステップ 3: エンティティ YAML の生成
# ============================================================================

Write-Host ""
Write-Host "----------------------------------------------------------------------------" -ForegroundColor $ColorInfo
Write-Host "  ステップ 3: エンティティ YAML の生成" -ForegroundColor $ColorInfo
Write-Host "----------------------------------------------------------------------------" -ForegroundColor $ColorInfo
Write-Host ""

try {
    Write-Info "エンティティ YAML を自動生成中..."
    dotnet run -- --scaffold-entities --project=$ProjectName --no-overwrite
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "エンティティ YAML の生成が完了しました"
    }
    else {
        Write-Warning "エンティティ YAML の生成に失敗しました（DB が空の可能性があります）"
    }
}
catch {
    Write-Warning "エンティティ YAML の生成中にエラーが発生しました：$_"
}

# ============================================================================
# ステップ 4: 設定ファイルの作成
# ============================================================================

Write-Host ""
Write-Host "----------------------------------------------------------------------------" -ForegroundColor $ColorInfo
Write-Host "  ステップ 4: 設定ファイルの作成" -ForegroundColor $ColorInfo
Write-Host "----------------------------------------------------------------------------" -ForegroundColor $ColorInfo
Write-Host ""

# layout.yml の作成
$LayoutYamlPath = Join-Path $ProjectDir "config" "layout.yml"
$LayoutYaml = @"
# 在庫管理システム用レイアウト設定

header:
  title: $DisplayName

navigation:
  showDashboard: true
  entities:
    - product
    - category
    - stockmovement
  items:
    - label: ダッシュボード
      controller: Dashboard
      action: Index
      icon: 📊
    - label: 商品一覧
      url: /$ProjectName/DynamicEntity/Index?entity=product
      icon: 📦
    - label: カテゴリ
      url: /$ProjectName/DynamicEntity/Index?entity=category
      icon: 🏷️
    - label: 在庫移動
      url: /$ProjectName/DynamicEntity/Index?entity=stockmovement
      icon: 🔄
    - label: 在庫状況
      url: /$ProjectName/Page/StockStatus
      icon: 📈
"@

$LayoutYaml | Out-File -FilePath $LayoutYamlPath -Encoding UTF8 -NoNewline
Write-Success "layout.yml を作成しました"

# home-page.yml の作成
$HomePageYamlPath = Join-Path $ProjectDir "config" "home-page.yml"
$HomePageYaml = @"
hero:
  eyebrow: $DisplayName
  title: 在庫管理ダッシュボード
  description: 商品・在庫・売上を一元的に管理するシステムです。
  primaryActionLabel: 商品一覧へ
  primaryActionUrl: /$ProjectName/DynamicEntity/Index?entity=product
  secondaryActionLabel: ダッシュボード
  secondaryActionUrl: /$ProjectName/Dashboard
  highlights:
    - リアルタイム在庫管理
    - 自動発注アラート
    - 売上分析

projectsSectionTitle: 在庫管理ワークスペース
projectsSectionLead: 商品管理から在庫移動、売上分析までを一元管理。

quickActions:
  - label: 商品一覧
    url: /$ProjectName/DynamicEntity/Index?entity=product
    style: btn-primary
    icon: 📦
  - label: 新規商品登録
    url: /$ProjectName/DynamicEntity/CreatePage?entity=product
    style: btn-accent
    icon: ➕
  - label: 在庫移動
    url: /$ProjectName/DynamicEntity/CreatePage?entity=stockmovement
    style: btn-outline
    icon: 🔄
  - label: 在庫状況
    url: /$ProjectName/Page/StockStatus
    style: btn-outline
    icon: 📈
"@

$HomePageYaml | Out-File -FilePath $HomePageYamlPath -Encoding UTF8 -NoNewline
Write-Success "home-page.yml を作成しました"

# dashboard.yml の作成
$DashboardYamlPath = Join-Path $ProjectDir "dashboard.yml"
$DashboardYaml = @"
stats:
  - label: 総商品数
    entity: product
    aggregate: count
    icon: 📦
    color: badge-primary
  - label: 有効商品
    entity: product
    aggregate: count
    filter:
      IsActive: true
    icon: ✅
    color: badge-success
  - label: カテゴリ数
    entity: category
    aggregate: count
    icon: 🏷️
    color: badge-secondary
  - label: 在庫総数
    entity: product
    aggregate: sum
    column: Stock
    icon: 📊
    color: badge-accent
  - label: 在庫金額
    entity: product
    aggregate: custom
    expression: "SUM(Stock * Price)"
    icon: 💰
    color: badge-warning
  - label: 最小在庫割れ
    entity: product
    aggregate: count
    filter:
      Stock: "< MinStock"
    icon: ⚠️
    color: badge-error

charts:
  - title: 商品カテゴリ別在庫数
    type: doughnut
    entity: product
    valueAggregate: sum
    valueColumn: Stock
    labelJoinEntity: category
    labelJoinKey: CategoryId
    labelJoinDisplay: Name
    orderBy: value
    orderDir: desc
    limit: 10
    colors:
      - rgba(99, 102, 241, 0.85)
      - rgba(16, 185, 129, 0.85)
      - rgba(245, 158, 11, 0.85)
      - rgba(239, 68, 68, 0.85)
      - rgba(59, 130, 246, 0.85)

  - title: 月別在庫移動推移
    type: line
    entity: stockmovement
    valueAggregate: sum
    valueColumn: Quantity
    groupExpression: strftime('%Y-%m', CreatedAt)
    orderBy: label
    orderDir: asc
    limit: 12
    colorBg: rgba(99, 102, 241, 0.15)
    colorBorder: rgba(99, 102, 241, 1)
"@

$DashboardYaml | Out-File -FilePath $DashboardYamlPath -Encoding UTF8 -NoNewline
Write-Success "dashboard.yml を作成しました"

# ============================================================================
# ステップ 5: フックファイルの作成
# ============================================================================

Write-Host ""
Write-Host "----------------------------------------------------------------------------" -ForegroundColor $ColorInfo
Write-Host "  ステップ 5: フックファイルの作成" -ForegroundColor $ColorInfo
Write-Host "----------------------------------------------------------------------------" -ForegroundColor $ColorInfo
Write-Host ""

$HooksDir = Join-Path $ProjectDir "Hooks"
if (-not (Test-Path $HooksDir)) {
    New-Item -ItemType Directory -Path $HooksDir -Force | Out-Null
}

# ValidateProductPriceHook.cs
$ValidatePriceHookPath = Join-Path $HooksDir "ValidateProductPriceHook.cs"
$ValidatePriceHook = @'
// 責務: 商品価格のバリデーションを行うフック

using System.Data;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Inventory.Hooks;

public sealed class ValidateProductPriceHook : IEntityHook
{
    private readonly ILogger<ValidateProductPriceHook> _logger;

    public string Name => "validate_product_price";

    public ValidateProductPriceHook(ILogger<ValidateProductPriceHook> logger)
    {
        _logger = logger;
    }

    public Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create && ctx.Operation != CrudOperation.Update)
            return Task.FromResult(HookResult.Continue());

        if (ctx.Values.TryGetValue("Price", out var priceObj) && priceObj is decimal price)
        {
            if (price < 0)
            {
                _logger.LogWarning("商品価格が負の値です：{Price}", price);
                return Task.FromResult(HookResult.Abort("価格は 0 以上である必要があります。"));
            }

            if (price > 1000000)
            {
                _logger.LogWarning("商品価格が上限を超えています：{Price}", price);
                return Task.FromResult(HookResult.Abort("価格の上限は 1,000,000 円です。"));
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
'@

$ValidatePriceHook | Out-File -FilePath $ValidatePriceHookPath -Encoding UTF8 -NoNewline
Write-Success "ValidateProductPriceHook.cs を作成しました"

# CheckStockThresholdHook.cs
$CheckStockHookPath = Join-Path $HooksDir "CheckStockThresholdHook.cs"
$CheckStockHook = @'
// 責務: 在庫閾値をチェックするフック

using System.Data;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Inventory.Hooks;

public sealed class CheckStockThresholdHook : IEntityHook
{
    private readonly ILogger<CheckStockThresholdHook> _logger;
    private readonly IDbConnection _db;

    public string Name => "check_stock_threshold";

    public CheckStockThresholdHook(ILogger<CheckStockThresholdHook> logger, IDbConnection db)
    {
        _logger = logger;
        _db = db;
    }

    public Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        return Task.FromResult(HookResult.Continue());
    }

    public async Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create && ctx.Operation != CrudOperation.Update)
            return;

        if (ctx.Entity != "product")
            return;

        if (ctx.Id is int productId)
        {
            var sql = @"
                SELECT p.Name, p.Stock, p.MinStock, c.Name as CategoryName
                FROM Products p
                LEFT JOIN Categories c ON p.CategoryId = c.Id
                WHERE p.Id = @ProductId";

            var product = await (tx != null ?
                db.QueryFirstOrDefaultAsync<ProductRow>(sql, new { ProductId = productId }, tx) :
                db.QueryFirstOrDefaultAsync<ProductRow>(sql, new { ProductId = productId }));

            if (product != null && product.Stock < product.MinStock)
            {
                _logger.LogWarning(
                    "[在庫アラート] 商品「{ProductName}」(カテゴリ：{CategoryName}) の在庫が閾値を下回っています。現在：{Stock}, 最小：{MinStock}",
                    product.Name, product.CategoryName ?? "未設定", product.Stock, product.MinStock);
            }
        }
    }

    private class ProductRow
    {
        public string Name { get; set; } = "";
        public int Stock { get; set; }
        public int MinStock { get; set; }
        public string? CategoryName { get; set; }
    }
}
'@

$CheckStockHook | Out-File -FilePath $CheckStockHookPath -Encoding UTF8 -NoNewline
Write-Success "CheckStockThresholdHook.cs を作成しました"

# UpdateProductStockHook.cs
$UpdateStockHookPath = Join-Path $HooksDir "UpdateProductStockHook.cs"
$UpdateStockHook = @'
// 責務: 在庫移動時に商品在庫を更新するフック

using System.Data;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Inventory.Hooks;

public sealed class UpdateProductStockHook : IEntityHook
{
    private readonly ILogger<UpdateProductStockHook> _logger;

    public string Name => "update_product_stock";

    public UpdateProductStockHook(ILogger<UpdateProductStockHook> logger)
    {
        _logger = logger;
    }

    public Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create)
            return Task.FromResult(HookResult.Continue());

        if (!ctx.Values.TryGetValue("ProductId", out var productIdObj) || productIdObj is not int productId)
            return Task.FromResult(HookResult.Abort("商品 ID が指定されていません。"));

        if (!ctx.Values.TryGetValue("Quantity", out var quantityObj) || quantityObj is not int quantity)
            return Task.FromResult(HookResult.Abort("数量が指定されていません。"));

        if (!ctx.Values.TryGetValue("MovementType", out var movementTypeObj) || movementTypeObj is not string movementType)
            return Task.FromResult(HookResult.Abort("移動種別が指定されていません。"));

        int stockChange = movementType switch
        {
            "IN" => quantity,
            "OUT" => -quantity,
            "ADJUST" => quantity,
            _ => 0
        };

        var currentStockSql = "SELECT Stock FROM Products WHERE Id = @ProductId";
        var currentStock = db.ExecuteScalar<int?>(currentStockSql, new { ProductId = productId }, tx) ?? 0;

        var newStock = currentStock + stockChange;

        if (newStock < 0)
        {
            _logger.LogWarning("在庫数が負になります。商品 ID: {ProductId}, 現在：{CurrentStock}, 変更：{StockChange}", 
                productId, currentStock, stockChange);
            return Task.FromResult(HookResult.Abort($"在庫不足です。現在：{currentStock}, 必要：{quantity}"));
        }

        var updateSql = "UPDATE Products SET Stock = @Stock, UpdatedAt = datetime('now') WHERE Id = @ProductId";
        db.Execute(updateSql, new { Stock = newStock, ProductId = productId }, tx);

        _logger.LogInformation(
            "商品 ID {ProductId} の在庫を更新：{Before} → {After}",
            productId, currentStock, newStock);

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
'@

$UpdateStockHook | Out-File -FilePath $UpdateStockHookPath -Encoding UTF8 -NoNewline
Write-Success "UpdateProductStockHook.cs を作成しました"

# CheckCategoryUsageHook.cs
$CheckCategoryHookPath = Join-Path $HooksDir "CheckCategoryUsageHook.cs"
$CheckCategoryHook = @'
// 責務: カテゴリ削除時に使用状況をチェックするフック

using System.Data;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Inventory.Hooks;

public sealed class CheckCategoryUsageHook : IEntityHook
{
    private readonly ILogger<CheckCategoryUsageHook> _logger;

    public string Name => "check_category_usage";

    public CheckCategoryUsageHook(ILogger<CheckCategoryUsageHook> logger)
    {
        _logger = logger;
    }

    public async Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Delete)
            return HookResult.Continue();

        if (ctx.Id is int categoryId)
        {
            var sql = "SELECT COUNT(*) FROM Products WHERE CategoryId = @CategoryId";
            var productCount = await db.ExecuteScalarAsync<int>(sql, new { CategoryId = categoryId }, tx);

            if (productCount > 0)
            {
                _logger.LogWarning(
                    "カテゴリ {CategoryId} には {ProductCount} 件の商品が関連しています。",
                    categoryId, productCount);
                return HookResult.Abort($"このカテゴリには {productCount} 件の商品が関連しているため削除できません。");
            }
        }

        return HookResult.Continue();
    }

    public Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
'@

$CheckCategoryHook | Out-File -FilePath $CheckCategoryHookPath -Encoding UTF8 -NoNewline
Write-Success "CheckCategoryUsageHook.cs を作成しました"

# ============================================================================
# ステップ 6: カスタムページの作成
# ============================================================================

Write-Host ""
Write-Host "----------------------------------------------------------------------------" -ForegroundColor $ColorInfo
Write-Host "  ステップ 6: カスタムページの作成" -ForegroundColor $ColorInfo
Write-Host "----------------------------------------------------------------------------" -ForegroundColor $ColorInfo
Write-Host ""

$PagesDir = Join-Path $ProjectDir "pages"
if (-not (Test-Path $PagesDir)) {
    New-Item -ItemType Directory -Path $PagesDir -Force | Out-Null
}

$ViewsDir = Join-Path $ProjectDir "views"
if (-not (Test-Path $ViewsDir)) {
    New-Item -ItemType Directory -Path $ViewsDir -Force | Out-Null
}

# StockStatus.yaml
$StockStatusYamlPath = Join-Path $PagesDir "StockStatus.yaml"
$StockStatusYaml = @"
title: 在庫状況
description: 商品別の在庫状況とアラートを一覧表示
main_table: Products

ui:
  page:
    layout: single
    density: comfortable

sections:
  - id: stock_alerts
    title: ⚠️ 最小在庫割れアラート
    source_type: custom
    source: |
      SELECT 
        p.Id,
        p.Name as ProductName,
        c.Name as CategoryName,
        p.Stock as CurrentStock,
        p.MinStock as MinStock,
        p.MinStock - p.Stock as Shortage
      FROM Products p
      LEFT JOIN Categories c ON p.CategoryId = c.Id
      WHERE p.IsActive = 1 AND p.Stock < p.MinStock
      ORDER BY Shortage DESC
    columns:
      - ProductName
      - CategoryName
      - CurrentStock
      - MinStock
      - Shortage
    page_size: 10
    editable: false
    read_only: true
    ui:
      component: DataTable
      selectable: none

  - id: product_stock_list
    title: 📦 商品別在庫一覧
    source_type: custom
    source: |
      SELECT 
        p.Id,
        p.Name as ProductName,
        c.Name as CategoryName,
        p.Price as UnitPrice,
        p.Stock as Stock,
        p.Stock * p.Price as StockValue,
        CASE 
          WHEN p.Stock = 0 THEN '在庫切れ'
          WHEN p.Stock < p.MinStock THEN '在庫不足'
          WHEN p.Stock < p.MinStock * 2 THEN '要注意'
          ELSE '正常'
        END as StockStatus
      FROM Products p
      LEFT JOIN Categories c ON p.CategoryId = c.Id
      WHERE p.IsActive = 1
      ORDER BY StockValue DESC
    page_size: 20
    editable: false
    read_only: true
    ui:
      component: DataTable
      selectable: single

  - id: category_summary
    title: 🏷️ カテゴリ別集計
    source_type: custom
    source: |
      SELECT 
        c.Name as CategoryName,
        COUNT(p.Id) as ProductCount,
        SUM(p.Stock) as TotalStock,
        SUM(p.Stock * p.Price) as TotalValue,
        AVG(p.Price) as AvgPrice
      FROM Categories c
      LEFT JOIN Products p ON c.Id = p.CategoryId AND p.IsActive = 1
      GROUP BY c.Id, c.Name
      ORDER BY TotalValue DESC
    columns:
      - CategoryName
      - ProductCount
      - TotalStock
      - TotalValue
      - AvgPrice
    page_size: 10
    editable: false
    read_only: true
    ui:
      component: DataTable
      selectable: none
"@

$StockStatusYaml | Out-File -FilePath $StockStatusYamlPath -Encoding UTF8 -NoNewline
Write-Success "StockStatus.yaml を作成しました"

# StockStatus.cshtml
$StockStatusViewPath = Join-Path $ViewsDir "StockStatus.cshtml"
$StockStatusView = @'
@model Dictionary<string, (IEnumerable<Dictionary<string, object>> Rows, int Total)>
@{
    var title = ViewData["Title"]?.ToString() ?? "在庫状況";
}
<div class="space-y-6">
    <div class="flex justify-between items-center">
        <h1 class="text-2xl font-bold">@title</h1>
        <div class="flex gap-2">
            <a href="/inventory/DynamicEntity/CreatePage?entity=stockmovement" 
               class="btn btn-primary btn-sm">
                🔄 在庫移動
            </a>
            <a href="/inventory/DynamicEntity/CreatePage?entity=product" 
               class="btn btn-accent btn-sm">
                ➕ 新規商品
            </a>
        </div>
    </div>

    @foreach (var section in Model)
    {
        <div class="card bg-base-100 border border-base-300 shadow-sm">
            <div class="card-body">
                <h2 class="card-title text-lg">@section.Key</h2>
                
                @if (section.Value.Rows.Any())
                {
                    <div class="overflow-x-auto">
                        <table class="table table-zebra table-sm w-full">
                            <thead>
                                <tr>
                                    @foreach (var col in section.Value.Rows.First().Keys)
                                    {
                                        <th>@col</th>
                                    }
                                </tr>
                            </thead>
                            <tbody>
                                @foreach (var row in section.Value.Rows)
                                {
                                    <tr>
                                        @foreach (var val in row.Values)
                                        {
                                            <td>@(val?.ToString())</td>
                                        }
                                    </tr>
                                }
                            </tbody>
                        </table>
                    </div>
                }
                else
                {
                    <p class="text-center opacity-70 py-4">データがありません</p>
                }
            </div>
        </div>
    }
</div>
'@

$StockStatusView | Out-File -FilePath $StockStatusViewPath -Encoding UTF8 -NoNewline
Write-Success "StockStatus.cshtml を作成しました"

# ============================================================================
# ステップ 7: i18n 設定の作成
# ============================================================================

Write-Host ""
Write-Host "----------------------------------------------------------------------------" -ForegroundColor $ColorInfo
Write-Host "  ステップ 7: 多言語設定の作成" -ForegroundColor $ColorInfo
Write-Host "----------------------------------------------------------------------------" -ForegroundColor $ColorInfo
Write-Host ""

$I18nYamlPath = Join-Path $ProjectDir "config" "i18n.yml"
$I18nYaml = @"
translations:
  entities.product.displayName:
    en-US: Product
    zh-CN: 商品
    ja-JP: 商品
  entities.product.columns.Name.label:
    en-US: Product Name
    zh-CN: 商品名称
    ja-JP: 商品名
  entities.product.columns.Price.label:
    en-US: Price
    zh-CN: 价格
    ja-JP: 価格
  entities.product.columns.Stock.label:
    en-US: Stock
    zh-CN: 库存
    ja-JP: 在庫数
  entities.category.displayName:
    en-US: Category
    zh-CN: 类别
    ja-JP: カテゴリ
  entities.stockmovement.displayName:
    en-US: Stock Movement
    zh-CN: 库存移动
    ja-JP: 在庫移動
  projects.$ProjectName.home.hero.eyebrow:
    en-US: Inventory Management System
    zh-CN: 库存管理系统
    ja-JP: 在庫管理システム
  projects.$ProjectName.home.hero.title:
    en-US: Inventory Dashboard
    zh-CN: 库存仪表板
    ja-JP: 在庫管理ダッシュボード
"@

$I18nYaml | Out-File -FilePath $I18nYamlPath -Encoding UTF8 -NoNewline
Write-Success "i18n.yml を作成しました"

# ============================================================================
# ステップ 8: 完了メッセージ
# ============================================================================

Write-Host ""
Write-Host "============================================================================" -ForegroundColor $ColorSuccess
Write-Host "  完了！" -ForegroundColor $ColorSuccess
Write-Host "============================================================================" -ForegroundColor $ColorSuccess
Write-Host ""

Write-Success "在庫管理システムの生成が完了しました！"
Write-Host ""
Write-Host "次のステップ:" -ForegroundColor $ColorInfo
Write-Host ""
Write-Host "1. フックを DI に登録する (NetYamlForge/Program.cs):" -ForegroundColor $ColorInfo
Write-Host "   builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.ValidateProductPriceHook>();" -ForegroundColor Gray
Write-Host "   builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.CheckStockThresholdHook>();" -ForegroundColor Gray
Write-Host "   builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.UpdateProductStockHook>();" -ForegroundColor Gray
Write-Host "   builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.CheckCategoryUsageHook>();" -ForegroundColor Gray
Write-Host ""
Write-Host "2. エンティティ YAML にフックを追加する:" -ForegroundColor $ColorInfo
Write-Host "   projects/$ProjectName/entities/product.yml の hooks セクションに追加" -ForegroundColor Gray
Write-Host ""
Write-Host "3. アプリケーションを起動する:" -ForegroundColor $ColorInfo
Write-Host "   dotnet run --project NetYamlForge" -ForegroundColor Gray
Write-Host ""
Write-Host "4. ブラウザで確認する:" -ForegroundColor $ColorInfo
Write-Host "   http://localhost:5000/$ProjectName" -ForegroundColor Gray
Write-Host "   http://localhost:5000/$ProjectName/Dashboard" -ForegroundColor Gray
Write-Host "   http://localhost:5000/$ProjectName/Page/StockStatus" -ForegroundColor Gray
Write-Host ""
Write-Host "詳細は docs/guides/cli-subproject-complete-guide-ja.md を参照してください。" -ForegroundColor $ColorInfo
Write-Host ""
