# CLAUDE.md

This file provides guidance to Claude Code when working with the auto-dealer-demo project.

## 🚗 auto-dealer-demo プロジェクト概要

**NetYamlForge** フレームワーク上で動作する自動車販売ディーラー管理システムです。

### 核心定位

- **顧客向け AI**: 車両案内、試乗予約、カスタマーサポート
- **スタッフ向け AI**: 在庫管理、営業リード管理、顧客分析

---

## Skill Routing

**auto-dealer-demo 専用スキル**

利用可能なスキルがマッチする場合は、直接答えずにまずスキルを呼び出してください。

| ユーザーリクエスト | 呼び出すスキル | 説明 |
|------------------|---------------|------|
| 在庫照会、車両一覧 | `/dealer-inventory` | 在庫車両の検索・分析 |
| 在庫分析、長期在庫 | `/dealer-inventory --analysis` | 詳細分析レポート |
| 営業リード管理 | `/dealer-sales` | リードの優先度分類 |
| 今日の営業計画 | `/dealer-sales --today` | アクションプラン |
| 成約率分析 | `/dealer-sales --analysis` | KPI 分析 |
| 顧客情報照会 | `/dealer-customer` | 顧客マスタ検索 |
| 顧客履歴、購入記録 | `/dealer-customer {name}` | 特定顧客情報 |
| VIP 顧客リスト | `/dealer-customer --vip` | 重要顧客一覧 |
| 試乗予約管理 | `/dealer-appointment` | 予約確認・作成 |

---

## Commands

```bash
# プロジェクトルート
cd /home/ubuntu/ws/NetYamlForge

# 開発サーバー起動
dotnet run --project NetYamlForge

# auto-dealer-demo 専用コマンド
dotnet run -- --scaffold-entities --project=auto-dealer-demo
dotnet run -- --scaffold-hook --name=VehicleInspection --project=auto-dealer-demo
dotnet run -- --scaffold-batch-job --project=auto-dealer-demo --name=stale_lead_alert
```

---

## Architecture

### データベース構造

```
NetYamlForge/projects/auto-dealer-demo/database/auto-dealer-demo.db
```

#### 主要テーブル

| テーブル | 説明 |
|---------|------|
| `vehicles` | 車両在庫マスタ |
| `customers` | 顧客マスタ |
| `sales_leads` | 営業リード |
| `service_appointments` | サービス予約 |
| `ai_conversations` | AI 会話履歴 |
| `ai_messages` | AI メッセージ |
| `ai_handovers` | エスカレーション |

### 実体定義

- `entities/vehicles.yml` - 車両在庫マスタ
- `entities/customers.yml` - 顧客マスタ
- `entities/sales_leads.yml` - 営業リード
- `entities/service_appointments.yml` - サービス予約

### AI 統合

`AutoDealerChatService` がグローバル AI と共通の CLI サービスを使用：
- **顧客向け**: `_system-prompt-customer.md`
- **スタッフ向け**: `_system-prompt-staff.md`

---

## Skills 詳細

### `/dealer-inventory` (在庫管理)

**用途**: 車両在庫の照会、分析、推奨アクション生成

**出力例**:
```markdown
# 🚗 在庫車両レポート

## 該当件数：**8 件**

### 統計情報
- 在庫総数：8 台
- 在庫総額：4,237 万円
- 平均価格：529.6 万円

## 💡 洞察
1. SUV 偏重の在庫構成（62.5%）
2. 高価格帯車両が 50% を占める

## 📋 推奨アクション
1. 高価格帯 4 件の優先販売促進
2. SUV 比較試乗会の開催
```

### `/dealer-sales` (営業管理)

**用途**: 販売リードの優先度分類、フォローアップ計画

**優先度分類**:
- 🔴 **高**: 新規リード（24 時間以内）、高スコア未連絡（3 日以上）
- 🟡 **中**: 見積もり送付済み、試乗予約済み
- 🟢 **低**: 情報収集中

### `/dealer-customer` (顧客管理)

**用途**: 顧客情報の照会、購入履歴、フォローアップ計画

**セグメント**:
- VIP/プラチナ顧客（30 日以上未連絡）
- 誕生月顧客
- 車検時期の顧客（3 ヶ月以内）

---

## Data Query Patterns

### 構造化クエリ（推奨）

```json
{
  "entity": "vehicles",
  "action": "list",
  "filters": [
    { "field": "status", "op": "eq", "value": "available" }
  ],
  "orderBy": { "field": "price", "dir": "desc" },
  "top": 20
}
```

### 聚合クエリ

```json
{
  "entity": "vehicles",
  "action": "aggregate",
  "groupBy": ["brand", "vehicle_type"],
  "aggregations": [
    { "function": "count", "field": "vehicle_id", "alias": "count" },
    { "function": "avg", "field": "price", "alias": "avg_price" }
  ]
}
```

### テンプレートクエリ

```json
{
  "mode": "template",
  "template": "inventory_analysis",
  "templateParams": {
    "status": "available"
  }
}
```

---

## Known Pitfalls

### 業務データへのアクセス制限

- ✅ **照会のみ許可**: 顧客情報・車両在庫・販売リードの読み取り
- ❌ **変更禁止**: コードの変更・削除・システム設定
- ❌ **書き込み禁止**: データベースの直接更新

### SQL 安全ガイド

**❌ 禁止**:
```csharp
// 文字列挿入は絶対禁止
var sql = $"SELECT * FROM customers WHERE id = '{id}'";
```

**✅ 推奨**:
```csharp
// パラメータ化クエリ
var sql = "SELECT * FROM customers WHERE id = @Id";
var result = await _db.QueryAsync<Customer>(sql, new { Id = id });
```

---

## Testing

```bash
# 全テスト実行
dotnet test

# auto-dealer 関連テスト
dotnet test --filter "FullyQualifiedName~AutoDealer"
dotnet test --filter "FullyQualifiedName~QueryExecution"
```

---

## Tips

1. **朝礼で確認**: 毎朝の在庫・リード確認にスキルを使用
2. **顧客対応前に**: 顧客の履歴を事前確認
3. **終業前に計画**: 翌日のアクションプラン作成
4. **週次分析**: 成約率、在庫回転率の分析

---

*最終更新：2026 年 4 月 1 日*
