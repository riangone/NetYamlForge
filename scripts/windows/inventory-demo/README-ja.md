# NetYamlForge 在庫管理システム 自動生成スクリプト

在庫管理システム (Inventory Management System) のサブプロジェクトを自動生成する Windows 用スクリプトです。

---

## ファイル構成

```
inventory-demo/
├── New-InventoryProject.ps1      # PowerShell 自動生成スクリプト
├── create-inventory-project.bat   # バッチファイル（管理者権限用）
├── create_inventory_db.sql        # SQLite データベース作成スクリプト
└── README-ja.md                   # このファイル
```

---

## 事前準備

### 1. .NET 10.0 SDK のインストール

[.NET ダウンロードページ](https://dotnet.microsoft.com/download) から .NET 10.0 SDK をインストールしてください。

インストール確認:
```cmd
dotnet --version
```

### 2. SQLite のインストール（オプション）

[SQLite ダウンロードページ](https://www.sqlite.org/download.html) から SQLite をダウンロードし、パスを通してください。

インストール確認:
```cmd
sqlite3 --version
```

### 3. NetYamlForge のビルド

ソリューションルートで以下を実行:
```cmd
dotnet build
```

---

## 使用方法

### 方法 1: PowerShell スクリプトを実行（推奨）

1. **PowerShell を管理者権限で起動**
   - スタートメニューで「PowerShell」を右クリック
   - 「管理者として実行」を選択

2. **実行ポリシーの確認**（初回のみ）:
   ```powershell
   Get-ExecutionPolicy
   ```
   `Restricted` の場合は以下を実行:
   ```powershell
   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
   ```

3. **スクリプトを実行**:
   ```powershell
   cd C:\path\to\scripts\windows\inventory-demo
   .\New-InventoryProject.ps1
   ```

### 方法 2: バッチファイルを実行

1. **`create-inventory-project.bat` を右クリック**
2. **「管理者として実行」を選択**

バッチファイルは自動的に管理者権限で PowerShell スクリプトを実行します。

---

## カスタマイズ

### プロジェクト名の変更

```powershell
.\New-InventoryProject.ps1 -ProjectName "my-inventory" -DisplayName "マイ在庫システム"
```

### パラメータ一覧

| パラメータ | 型 | デフォルト | 説明 |
|-----------|-----|-----------|------|
| `-ProjectName` | string | `inventory` | プロジェクト名（英小文字・数字・ハイフン） |
| `-DisplayName` | string | `在庫管理システム` | 表示名 |
| `-OutputDir` | string | スクリプトディレクトリ | 出力先ディレクトリ |
| `-Verbose` | switch | - | 詳細出力 |
| `-Force` | switch | - | 確認プロンプトをスキップ |

### 使用例

```powershell
# 詳細モードで実行
.\New-InventoryProject.ps1 -Verbose

# 確認をスキップして実行
.\New-InventoryProject.ps1 -Force

# プロジェクト名を指定
.\New-InventoryProject.ps1 -ProjectName "shop-inventory" -DisplayName "店舗在庫管理"

# 出力先を指定
.\New-InventoryProject.ps1 -OutputDir "C:\Projects"
```

---

## 生成されるファイル構造

```
projects/inventory/
├── project.yaml              # プロジェクト定義
├── dashboard.yml             # ダッシュボード設定
├── config/
│   ├── home-page.yml         # ホームページ設定
│   ├── layout.yml            # レイアウト定義
│   └── i18n.yml              # 多言語設定
├── database/
│   └── inventory.db          # SQLite DB
├── entities/
│   ├── product.yml           # 商品エンティティ
│   ├── category.yml          # カテゴリエンティティ
│   └── stockmovement.yml     # 在庫移動エンティティ
├── entities.generated/       # 自動生成エンティティ
├── pages/
│   └── StockStatus.yaml      # 在庫状況ページ
├── views/
│   └── StockStatus.cshtml    # 在庫状況ビュー
└── Hooks/
    ├── ValidateProductPriceHook.cs      # 価格検証フック
    ├── CheckStockThresholdHook.cs       # 在庫閾値チェック
    ├── UpdateProductStockHook.cs        # 在庫更新フック
    └── CheckCategoryUsageHook.cs        # カテゴリ使用チェック
```

---

## 次のステップ

### 1. フックの DI 登録

`NetYamlForge/Program.cs` の `AddNetYamlForge` 呼び出し後に以下を追加:

```csharp
// Inventory プロジェクト用フック登録
builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.ValidateProductPriceHook>();
builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.CheckStockThresholdHook>();
builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.UpdateProductStockHook>();
builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.CheckCategoryUsageHook>();
```

### 2. エンティティ YAML にフックを追加

`projects/inventory/entities/product.yml` の `hooks` セクション:

```yaml
hooks:
  beforeCreate: [validate_product_price, check_stock_threshold]
  beforeUpdate: [validate_product_price, check_stock_threshold, update_timestamp]
  afterCreate: [log_product_change, check_stock_threshold]
  afterUpdate: [log_product_change, check_stock_threshold]
```

`projects/inventory/entities/stockmovement.yml`:

```yaml
hooks:
  beforeCreate: [validate_stock_movement, update_product_stock]
  afterCreate: [check_stock_threshold]
```

`projects/inventory/entities/category.yml`:

```yaml
hooks:
  beforeDelete: [check_category_usage]
```

### 3. アプリケーションの起動

```cmd
dotnet run --project NetYamlForge
```

### 4. ブラウザで確認

- ホームページ：`http://localhost:5000/inventory`
- ダッシュボード：`http://localhost:5000/inventory/Dashboard`
- 商品一覧：`http://localhost:5000/inventory/DynamicEntity/Index?entity=product`
- 在庫状況：`http://localhost:5000/inventory/Page/StockStatus`

---

## トラブルシューティング

### Q1: 「実行ポリシー」エラー

**エラーメッセージ**:
```
スクリプトを実行できません...
```

**解決**:
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### Q2: 管理者権限が必要

**エラーメッセージ**:
```
管理者権限で実行されていません
```

**解決**:
- PowerShell を「管理者として実行」
- またはバッチファイルを「管理者として実行」

### Q3: .NET SDK が見つからない

**解決**:
1. [.NET 10.0 SDK](https://dotnet.microsoft.com/download) をインストール
2. コマンドプロンプトを再起動
3. `dotnet --version` で確認

### Q4: SQLite が見つからない

**解決**:
- オプションです。空の DB ファイルが作成されます。
- 後で手動でテーブルを作成してください:
  ```cmd
  sqlite3 projects/inventory/database/inventory.db < create_inventory_db.sql
  ```

### Q5: フックが実行されない

**確認点**:
1. フッククラスが `IEntityHook` を実装しているか
2. `Name` プロパティが YAML のフック名と一致しているか
3. DI に登録されているか
4. YAML の `hooks` セクションのキー名が camelCase か

---

## 削除方法

生成されたプロジェクトを削除するには:

```cmd
rmdir /s /q projects\inventory
```

---

## 関連ドキュメント

- [CLI 子项目开发完全ガイド](../../../docs/guides/cli-subproject-complete-guide-ja.md)
- [CLI 子项目创建教程](../../../docs/guides/cli-create-subproject-tutorial-ja.md)
- [フック設計ガイド](../../../docs/guides/hooks/project-hooks-guide.md)

---

## サポート

問題が発生した場合は、以下の情報を添えてお問い合わせください:

1. OS バージョン
2. .NET SDK バージョン (`dotnet --version`)
3. PowerShell バージョン (`$PSVersionTable.PSVersion`)
4. エラーメッセージの全文
5. 実行したコマンド

---

## ライセンス

このスクリプトは NetYamlForge プロジェクトの一部として提供されます。
