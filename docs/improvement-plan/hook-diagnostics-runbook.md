# Hook 診断 Runbook

## 1. 目的

プロジェクト固有 Hook の障害を、`ErrorCode` ベースで迅速に切り分けるための運用手順。

## 2. 対象 ErrorCode 一覧

1. `HOOK_COMPILE_FAILED`
- 意味: Hook ソースのコンパイルが失敗
- 一次確認: 同時に `HOOK_COMPILE_DIAGNOSTICS` が出ているか

2. `HOOK_COMPILE_DIAGNOSTICS`
- 意味: コンパイル診断の詳細（`file(line,col) CSxxxx`）
- 一次確認: CS エラー番号ごとのヒントを確認

3. `HOOK_INIT_FAILED`
- 意味: Hook インスタンス化（DI解決）に失敗
- 一次確認: コンストラクタ引数（Logger, IService 等）を確認

4. `HOOK_LOAD_FAILED`
- 意味: Hook 読み込み処理全体で例外
- 一次確認: `Hooks/` 配下のファイル、権限、ロード順を確認

5. `BIZLOGIC_INIT_FAILED`
- 意味: `IProjectBusinessLogic` 初期化失敗

6. `BIZLOGIC_LOAD_FAILED`
- 意味: ビジネスロジック読込処理全体失敗

7. `VALIDATOR_REGISTER_FAILED`
- 意味: `IProjectValidator` 登録失敗

8. `TRANSFORMER_REGISTER_FAILED`
- 意味: `IProjectDataTransformer` 登録失敗

## 3. 標準切り分け手順

1. ErrorCode でログを抽出
```bash
rg -n "HOOK_|BIZLOGIC_|VALIDATOR_|TRANSFORMER_" logs/ -g"*.log"
```

2. 対象プロジェクト名を特定
- 例: `project=northwind-sqlite3-ops`

3. `HOOK_COMPILE_DIAGNOSTICS` の詳細を確認
- `CS0246`: using/参照不足
- `CS0103`: 変数名/スコープミス
- `CS1061`: メンバー/拡張メソッド不足
- `CS1503`: 引数型不一致

4. Hook ファイルを修正後、再ビルド
```bash
dotnet build
```

5. アプリ再起動または対象プロジェクト再ロードで再評価

## 4. 典型原因と対処

1. using 不足（`CS0246`）
- 対処: `System.Collections.Generic` など不足 using を追加

2. DI 未登録依存
- 対処: Hook のコンストラクタ依存を見直し、DI登録または引数削減

3. 動的コンパイル参照不足
- 対処: `ProjectHookLoader` の metadata references を確認

4. Hook 名不一致（YAML と実装）
- 対処: `Name` プロパティと YAML のフック名を一致させる

## 5. 確認コマンド（最小）

1. Build
```bash
dotnet build
```

2. テスト
```bash
dotnet test /home/ubuntu/ws/ccc/NetYamlForge.Tests/NetYamlForge.Tests.csproj
```

3. Hook 関連ログ抽出
```bash
rg -n "HOOK_|BIZLOGIC_|VALIDATOR_|TRANSFORMER_" /path/to/app.log
```

## 6. エスカレーション条件

以下の場合は即エスカレーション。

1. `HOOK_LOAD_FAILED` が連続発生し、プロジェクト全体がロード不能
2. `HOOK_COMPILE_DIAGNOSTICS` が同一 ErrorCode で再発（2回以上）
3. 本番で Hook 起因の CRUD 失敗が連鎖
