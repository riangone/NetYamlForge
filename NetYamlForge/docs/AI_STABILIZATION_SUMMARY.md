# AI生成コード安定化 — 改善サマリー

> 目的: AI がこのフレームワーク上でコードを生成する際に「ランダムで読みにくいコード」を避け、
> 「安定・可読・保守可能なコード」を一貫して生成できるようにする。

---

## 改善の全体像

| カテゴリ | 施策 | 効果 |
|--------|------|------|
| 制約 | Roslyn アナライザー (DCS001–DCS004) | 禁止パターンをビルドエラーで即時検出 |
| 制約 | YAML JSON Schema (`entity-schema.json`) | entities.yml の構造誤りを保存時に検出 |
| ガイド | CLAUDE.md 決定木・禁止パターン | AI が迷わず正しい手順を選べる |
| ガイド | `docs/examples/` 5本の正解例 | コピーペーストできる完成形コードの提示 |
| ガイド | `project.yaml` の `aiHints` セクション | プロジェクト固有制約をコンテキストに含める |
| ガイド | サービスファイル責務サマリー | 各ファイルの役割を1行で把握 |
| 自動化 | `--scaffold-hook --with-tests` CLI | フック+テストを正しいテンプレートで生成 |
| 回帰防止 | `YamlSchemaValidationTests` | 全プロジェクトYAMLの自動検証（CI保証） |
| 回帰防止 | `SqlGenerationSnapshotTests` | SQL生成ロジックの回帰テスト12件 |
| 回帰防止 | `HookScaffolderTests` | スキャフォールダー名前変換の回帰テスト |

---

## 1. Roslyn アナライザー（DCS001–DCS004）

**ファイル**: `NetYamlForge.Analyzers/ForbiddenPatternAnalyzer.cs`

| ルールID | 重大度 | 検出パターン | 対策 |
|---------|--------|------------|------|
| DCS001 | Error | `sql = $"...{variable}..."` — SQL文字列補間 | DynamicCrudRepository / パラメータ化クエリを使用 |
| DCS002 | Error | `.Result` / `.Wait()` — ブロッキング呼び出し | `await` に変更 |
| DCS003 | Error | `new SqliteConnection()` 等の直接インスタンス化 | DIから `IDbConnection` を注入 |
| DCS004 | Warning | `role == "Admin"` — ロール名ハードコード | `UserRoles.Admin` 定数を使用 |

### 抑制の原則
合法的な使用箇所（DI ファクトリ、DBインフラ）には `#pragma warning disable DCSxxx // 理由` を付与済み。
新規コードで抑制が必要な場合は**理由を必ずコメントに記載**すること。

```csharp
// ❌ 新規コードでこれをそのまま書くとビルドエラー
var sql = $"SELECT * FROM {tableName}";

// ✅ 識別子を検証してから抑制（既存の合法パターン）
if (!IdentifierRegex.IsMatch(tableName)) return;
#pragma warning disable DCS001 // Safe: tableName validated by IdentifierRegex above
var sql = $"SELECT * FROM {tableName}";
#pragma warning restore DCS001

// ✅ または DynamicCrudRepository を使う（推奨）
var results = await _repository.GetAllAsync(entityDef);
```

---

## 2. YAML JSON Schema 強化

**ファイル**: `NetYamlForge/Schemas/entity-schema.json`

### 追加した情報
- 全プロパティに `description` フィールド（AIがスキーマを理解しやすくなる）
- `hooks` セクション: camelCase キー (`beforeCreate`, `afterCreate` 等)、`presets` サポート
- `string | array` ユニオン型（フック値は単一文字列でも配列でも可）
- `displayNameI18n` を allowedとして追加
- `joins.on` を任意に変更（YAML 1.1 の `True` キー対策）
- 型制限を `string` に緩和（`money`, `like`, `checkbox` 等を許容）

---

## 3. CLAUDE.md — AI向けガイド強化

**ファイル**: `/CLAUDE.md`（リポジトリルート）

### 追加セクション
1. **変更タイプ別 決定木（Q1–Q6）** — コードを書く前に「何を変えるべきか」を決定する6段階フロー
2. **禁止パターン一覧** — 具体的なコード例付き（SQL注入、`.Result`、DynamicCrudRepository継承等）
3. **Roslyn アナライザー表** — DCS001–DCS004のルール一覧
4. **正解例カタログリンク** — `docs/examples/` の5本ドキュメントへの参照
5. **CLIコマンド** — `--scaffold-hook --with-tests` 追加

---

## 4. 正解例カタログ (`docs/examples/`)

| ファイル | 内容 | 主なポイント |
|--------|------|-----------|
| `01-add-simple-column.md` | 列追加（YAMLのみ） | 4パターン比較、よくある誤り一覧 |
| `02-add-validation-hook.md` | バリデーション追加 | 既存フック使用 vs カスタムフック実装の判断基準 |
| `03-add-new-entity.md` | 新エンティティ追加 | DB→YAML→ナビゲーションの全手順 |
| `04-add-dashboard-stat.md` | ダッシュボード統計 | COUNT/SUM/AVG、チャート、安全なwhere句 |
| `05-add-custom-hook.md` | カスタムフック実装 | 完全テンプレート、登録手順、命名規則表 |

---

## 5. `project.yaml` の `aiHints` セクション

6プロジェクトに追加済み:

```yaml
aiHints:
  primaryLanguage: ja-JP          # UIの主要言語
  authRequired: true              # 認証が必要か
  customHooks: [hook1, hook2]     # 実装済みカスタムフック名
  protectedEntities: [order]      # 直接SQL編集禁止エンティティ
  approvalWorkflow:               # 承認ワークフロー状態遷移
    leave_request:
      statusField: status
      transitions:
        pending: [approved, rejected]
  notes:
    - "重要な制約事項をここに記載"
```

対象プロジェクト: `blog`, `chinook`, `salesforce-crm`, `todo`, `northwind-sqlite3-ops`, `attendance-ops`

---

## 6. フックスキャフォールダー (`--scaffold-hook --with-tests`)

**ファイル**: `NetYamlForge/Services/HookScaffolder.cs`

```bash
dotnet run -- --scaffold-hook --name=ValidateInventory --project=myproject --with-tests
```

生成されるもの:
- `projects/myproject/Hooks/ValidateInventoryHook.cs` — `IEntityHook` 完全実装テンプレート
- `NetYamlForge.Tests/Hooks/ValidateInventoryHookTests.cs` — 4テンプレート付きテストファイル
- 次のステップ（DI登録、YAML追加）をコンソールに表示

---

## 7. テストカバレッジ強化

| テストファイル | 件数 | テスト内容 |
|-------------|------|----------|
| `YamlSchemaValidationTests.cs` | 全プロジェクトの自動検出 | 全entities.yml / project.yaml の形式検証 |
| `SqlGenerationSnapshotTests.cs` | 12件 | SELECT/INSERT/UPDATE/DELETE/ソフトデリート/ページネーション |
| `HookScaffolderTests.cs` | 12件 | ToPascalCase / ToSnakeCase 名前変換 |

テスト総数: **218件**

---

## 8. セキュリティ修正（CommonHooks.cs）

`validate_unique` / `update_count` / `update_related` の3フックに `IdentifierRegex` 検証を追加:

```csharp
// 修正前（脆弱性あり）
var sql = $"UPDATE {sourceEntity} SET {countColumn} = ...";

// 修正後
if (!HookConstants.HookIdentifierRegex.IsMatch(sourceEntity)) {
    _logger.LogWarning("無効な識別子: {Entity}", sourceEntity);
    return;
}
#pragma warning disable DCS001 // Safe: validated above
var sql = $"UPDATE {sourceEntity} SET {countColumn} = ...";
#pragma warning restore DCS001
```

---

## ファイル変更一覧

### 新規作成
| ファイル | 説明 |
|--------|------|
| `CLAUDE.md` | AI向けフレームワーク使用ガイド（ルート） |
| `NetYamlForge.Analyzers/` | Roslyn アナライザープロジェクト |
| `NetYamlForge/Properties/AssemblyInfo.cs` | InternalsVisibleTo テスト設定 |
| `NetYamlForge/Services/HookScaffolder.cs` | フックスキャフォールダーサービス |
| `NetYamlForge.Tests/YamlSchemaValidationTests.cs` | YAML自動検証テスト |
| `NetYamlForge.Tests/SqlGenerationSnapshotTests.cs` | SQL生成回帰テスト |
| `NetYamlForge.Tests/HookScaffolderTests.cs` | スキャフォールダーユニットテスト |
| `NetYamlForge/docs/examples/01〜05.md` | 正解例カタログ5本 |
| `.claude/commands/` | Claude Code スキルファイル |

### 修正
| ファイル | 変更内容 |
|--------|--------|
| `Schemas/entity-schema.json` | description追加、hooks camelCase、型緩和 |
| `projects/*/project.yaml` | aiHints セクション追加（6プロジェクト） |
| `Services/EntityCrudExecutionService.cs` | 責務サマリーコメント追加 |
| `Services/PageRowMutationService.cs` | 責務サマリー + DCS001抑制コメント |
| `Services/PageDataQueryService.cs` | 責務サマリー + DCS001抑制コメント |
| `Services/DynamicCrudRepository.cs` | DCS001抑制コメント |
| `Services/Hooks/CommonHooks.cs` | IdentifierRegex検証追加 + HookConstants |
| `Controllers/DashboardController.cs` | DCS001抑制コメント |
| `Data/DbInitializer.cs` | DCS001/DCS003抑制コメント |
| `Services/Auth/UserAuthService.cs` | DCS003抑制コメント |
| `Services/Auth/AuditLogService.cs` | DCS003抑制コメント |
| `Services/CrmAutomationHostedService.cs` | DCS003抑制コメント |
| `Services/EntityYamlScaffolder.cs` | DCS003抑制コメント |
| `Services/EntityDbSchemaConsistencyValidator.cs` | DCS003抑制コメント |
| `Program.cs` | --scaffold-hook CLI + DCS003抑制 |
| `ccc.sln` | アナライザープロジェクト追加 |
