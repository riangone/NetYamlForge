# NetYamlForge ドキュメントハブ（日本語）

> **このファイルが唯一の入口です。** 全ドキュメントへのリンクをここで管理します。

---

## はじめに（必読順）

| # | ドキュメント | 内容 |
|---|------------|------|
| 1 | [5分クイックスタート](quickstart-ja.md) | 起動・動作確認の最短手順 |
| 2 | [アーキテクチャマップ](architecture-map-ja.md) | 責務・処理フロー・主要クラス図 |
| 3 | [フレームワーク概要](framework-overview-tutorial-ja.md) | YAML→UI自動生成の全体像 |
| 4 | [開発者チュートリアル（完全版）](developer-tutorial-ja.md) | ゼロから業務アプリを構築する全手順 |
| 5 | [コアアーキテクチャ詳解](annotated-architecture-ja.md) | コードアノテーション付き内部実装解説 |

---

## 機能別ガイド

### YAML・エンティティ定義

| ドキュメント | 内容 |
|------------|------|
| [スキャフォールド運用](entity-scaffold-workflow-ja.md) | DBからYAMLを自動生成する手順 |
| [YAML実例集（Chinook）](chinook-yaml-examples.md) | JOIN・フィルター・フォームの豊富なサンプル |
| [複合主キー例](composite-key-example.md) | 複合PK対応のYAML記述 |
| [外部キー表示列](foreignkey-displaycolumns-query-ja.md) | FK表示列の解決ロジック |

### フック・ビジネスロジック

| ドキュメント | 内容 |
|------------|------|
| [共通フック一覧](COMMON_HOOKS.md) | 20種以上の既製フック詳細・YAML使用例 |
| [フック設計ガイド](project-hooks-guide.md) | プロジェクト固有フックの設計指針 |
| [複数フック実行ガイド](multiple-hooks-guide.md) | フックチェーンの組み合わせ方 |
| [確認ダイアログ×フック](confirmation-and-hooks.md) | 削除確認とフック連携 |
| [YAMLフックプリセット](yaml-hook-presets-ja.md) | よく使うフック設定のプリセット集 |

### ダッシュボード・UI

| ドキュメント | 内容 |
|------------|------|
| [Dashboard設定](dashboard.md) | 統計カード・チャートのYAML定義方法 |
| [UIコンポーネント一覧](ui/) | フォーム・フィルター部品のスクリーンショット |

### CLI コマンド

```bash
# エンティティYAML生成（DBスキーマから自動生成）
dotnet run -- --scaffold-entities --project=<name> [--no-overwrite] [--json]

# フックスキャフォールド（実装クラス＋テストを同時生成）
dotnet run -- --scaffold-hook --name=<HookName> --project=<name> [--with-tests] [--json]

# プロジェクト初期化（雛形一式を生成）
dotnet run -- --init-project --project=<name> --display-name="<名前>" --db-type=sqlite [--json]

# YAML現代化（古い形式を最新規約に変換）
dotnet run -- --upgrade-entity-yaml --project=<name> [--json]
```

`--json` フラグを付けると、CI連携向けの構造化JSON（`generatedFiles`・`skippedFiles`・`nextSteps`・`errors`）がstdoutに出力されます。

### SQL Server・マルチDB

| ドキュメント | 内容 |
|------------|------|
| [SQL Server設定](sqlserver-setup.md) | `appsettings.json`とダイアレクト切替手順 |

---

## 正解例カタログ（`examples/`）

| ファイル | 内容 |
|---------|------|
| [01-add-simple-column.md](examples/01-add-simple-column.md) | YAMLのみで列追加 |
| [02-add-validation-hook.md](examples/02-add-validation-hook.md) | バリデーションフック追加 |
| [03-add-new-entity.md](examples/03-add-new-entity.md) | 新エンティティの全手順 |
| [04-add-dashboard-stat.md](examples/04-add-dashboard-stat.md) | 統計カード追加 |
| [05-add-custom-hook.md](examples/05-add-custom-hook.md) | 独自フックの実装テンプレート |

---

## 運用・品質管理

| ドキュメント | 内容 |
|------------|------|
| [運用Runbook索引](runbook-index-ja.md) | 本番運用手順の索引 |
| [運用チェックリスト](operations-checklist-ja.md) | デプロイ前後の確認リスト |
| [役割別読み進めガイド](role-reading-paths-ja.md) | 開発者・運用者・レビュアー別の推奨読書順 |
| [CHANGELOG](CHANGELOG.md) | バージョン別の変更履歴 |
| [リファクタログ索引](refactor-log-index-ja.md) | 改善実装の経緯・判断記録 |

### 改善計画

| ドキュメント | 内容 |
|------------|------|
| [ロードマップ](improvement-plan/roadmap.md) | 優先度別の改善項目一覧 |
| [品質ゲート](improvement-plan/quality-gates.md) | マージ基準・テスト合格条件 |
| [回帰チェック](improvement-plan/regression-checklist.md) | 変更時の回帰確認項目 |

---

## 参考・背景

| ドキュメント | 言語 | 内容 |
|------------|------|------|
| [なぜORMを使わないか](why-no-orm-zh.md) | 中国語 | Dapper採用の設計根拠 |
| [YAML駆動設計の思想](yaml-driven-design-zh.md) | 中国語 | YAML-First設計の背景 |
| [vs Retool比較](vs-retool-zh.md) | 中国語 | 類似ツールとの比較分析 |

---

## テスト

```bash
# 全テスト実行（380件）
dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj

# 単一テスト実行
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"
```

主要テストファイル:

| ファイル | テスト対象 |
|---------|-----------|
| `DynamicEntityControllerTests.cs` | コントローラー統合テスト |
| `EntityCrudExecutionServiceTests.cs` | フック実行・トランザクション |
| `YamlSchemaValidationTests.cs` | 全プロジェクトのYAML形式検証 |
| `SqlGenerationSnapshotTests.cs` | SQL生成の回帰テスト |
| `YamlConfigStartupValidatorTests.cs` | 起動時型バリデーション |
| `ListStateUrlBuilderTests.cs` | URL状態ビルダー |
