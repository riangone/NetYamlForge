# 更新サマリー（2026-03-05 15:38 JST）

## 概要
Page 系処理の責務分離を進め、`PageController` から以下をサービスへ抽出しました。
- 第1段階: 行更新/削除ロジック（Mutation）
- 第2段階: セクションデータ読込・フィルタ組み立て（Query）

狙いは、Controller の肥大化を抑え、ユニットテストで回帰を固定しやすい構造にすることです。

## 変更内容
### 1) Mutation 抽出
- 追加: `Services/PageRowMutationService.cs`
- 対応範囲:
  - `UpdateRow` の業務更新ロジック
  - `DeleteRow` の業務削除ロジック
  - CRM プロジェクト向け更新/削除バリデーション

### 2) Query 抽出
- 追加: `Services/PageDataQueryService.cs`
- 対応範囲:
  - `LoadPageDataAsync`
  - `GetSectionDataAsync`
  - セクションフィルタ組み立て
  - 日付境界正規化（`00:00:00` / `23:59:59`）

### 3) Controller 薄化
- 変更: `Controllers/PageController.cs`
- 結果:
  - `UpdateRow` / `DeleteRow` はサービス呼び出し中心に変更
  - データ読込ロジックを `PageDataQueryService` に委譲
  - Controller は権限・HTTP 応答・監査トリガ中心の責務へ整理

### 4) DI 登録
- 変更: `Program.cs`
- 追加登録:
  - `PageRowMutationService`
  - `PageDataQueryService`

## テスト追加
- `NetYamlForge.Tests/PageRowMutationServiceTests.cs`
  - shipped 更新時の `ShippedDate` 補完
  - 不正状態遷移拒否
  - CRM の削除制約
- `NetYamlForge.Tests/PageDataQueryServiceTests.cs`
  - table source の like フィルタ
  - 日付範囲 + 外部キー条件

## 検証
- `dotnet test` を通過（追加テスト含む）

## 次の作業候補
- `DynamicEntityController` の Hook 実行/トランザクション処理を `EntityHookExecutionService` へ抽出
- `PageController` の保存ビュー処理（SaveView/DeleteView）を `PageViewPreferenceService` へ抽出

---

## 追記（2026-03-05 15:5x JST）
上記候補のうち、保存ビュー処理の抽出を実施しました。

### 5) 保存ビュー処理のサービス化
- 追加: `Services/PageViewPreferenceService.cs`
- 抽出対象:
  - 保存ビュー一覧取得
  - 保存ビュー登録（default 切替 + 監査 + Tx）
  - 保存ビュー削除

### 6) Controller の追加薄化
- 変更: `Controllers/PageController.cs`
- 内容:
  - 保存ビューの DB 操作/JSON 処理をサービスへ委譲
  - コントローラーは認可・HTTP 応答・監査トリガ中心へ整理

### 7) 追加テスト
- `NetYamlForge.Tests/PageViewPreferenceServiceTests.cs`
  - default ビューの排他更新
  - FiltersJson の復元
  - 対象ビューのみ削除

---

## 追記（2026-03-05 16:xx JST）
`DynamicEntityController` のフック実行/トランザクション責務をサービスへ抽出しました。

### 8) Hook + Tx 実行基盤のサービス化
- 追加: `Services/EntityCrudExecutionService.cs`
- 抽出対象:
  - Before/After Hook 実行
  - Hook reject 監査ログ
  - CRUD トランザクションラッパー
  - CRUD 監査書き込み（Tx 内）

### 9) DynamicEntityController の薄化
- 変更: `Controllers/DynamicEntityController.cs`
- 内容:
  - Controller 内の `RunBeforeHookAsync` / `RunAfterHookAsync` / `TryWriteHookRejectAuditAsync` / `ExecuteCrudTransactionAsync` を削除
  - 既存 Create/Edit/Delete フローは維持しつつ、上記を `EntityCrudExecutionService` に委譲

### 10) DI 登録
- 変更: `Program.cs`
- 追加:
  - `EntityCrudExecutionService`

### 11) 追加テスト
- `NetYamlForge.Tests/EntityCrudExecutionServiceTests.cs`
  - project hook 優先動作（framework hook より優先）
  - Tx 失敗時 rollback
  - hook reject 監査の書き込み

---

## 追記（2026-03-05 16:xx JST, Command分離）
`DynamicEntityController` の Create/Edit/Delete コマンド実行を専用サービスへ抽出しました。

### 12) DynamicEntity コマンド実行サービス
- 追加: `Services/DynamicEntityCommandService.cs`
- 抽出対象:
  - Create（before/after hooks + tx + audit）
  - Update（before/after hooks + tx + audit）
  - Delete（before/after hooks + tx + audit）
- 目的:
  - Controller の業務分岐を削減
  - Hook/Tx フローの再利用性向上

### 13) DynamicEntityController の更なる薄化
- 変更: `Controllers/DynamicEntityController.cs`
- 内容:
  - Create/Edit/Delete から hook/tx/audit 実行コードを削除
  - `DynamicEntityCommandService` 呼び出しに統一
  - 未使用依存（Hooks/Audit実行関連）の整理

### 14) DI 登録
- 変更: `Program.cs`
- 追加:
  - `DynamicEntityCommandService`

### 15) 追加テスト
- `NetYamlForge.Tests/DynamicEntityCommandServiceTests.cs`
  - before hook abort 時のキャンセル
  - update 実行時の repository 呼び出し確認

---

## 追記（2026-03-05 17:xx JST, Key解決分離）
`DynamicEntityController` の主キー値解決ロジックをサービス化し、重複コードと未使用コードを削減しました。

### 16) 主キー解決サービス
- 追加: `Services/DynamicEntityKeyResolverService.cs`
- 機能:
  - `id` パラメータ優先で主キー値を解決
  - 未指定時は query の主キー列から解決
  - 複合主キーの key-value 解決（JSON/csv フォールバック）

### 17) Controller 整理
- 変更: `Controllers/DynamicEntityController.cs`
- 内容:
  - `EditForm/EditPage/Edit/Delete` の主キー解決を `DynamicEntityKeyResolverService` へ委譲
  - 使われていない複合主キー補助メソッドを削除

### 18) DI 登録
- 変更: `Program.cs`
- 追加:
  - `DynamicEntityKeyResolverService`

### 19) 追加テスト
- `NetYamlForge.Tests/DynamicEntityKeyResolverServiceTests.cs`
  - id 優先
  - query フォールバック
  - 複合主キー JSON 解決

---

## 追記（2026-03-05 17:xx JST, 一覧レスポンス共通化）
`Create/Edit/Delete` 後の一覧再読込ロジックをサービス化し、Controller 側の重複を削減しました。

### 20) 一覧再読込サービス
- 追加: `Services/DynamicEntityListResponseService.cs`
- 機能:
  - 変更後の先頭一覧取得
  - 件数取得有無を引数で切替（`includeCount`）

### 21) Controller 適用
- 変更: `Controllers/DynamicEntityController.cs`
- 内容:
  - `Create/Edit/Delete` で行っていた `GetAllAsync + CountAsync` を `DynamicEntityListResponseService` に委譲
  - 一覧レスポンス構築前のデータ取得コードを統一

### 22) DI 登録
- 変更: `Program.cs`
- 追加:
  - `DynamicEntityListResponseService`

### 23) 追加テスト
- `NetYamlForge.Tests/DynamicEntityListResponseServiceTests.cs`
  - 件数取得ありの通常経路
  - 件数取得スキップ経路

---

## 追記（2026-03-05 17:xx JST, 外部キー候補データ分離）
`DynamicEntityController` の外部キー候補読み込みを専用サービスに抽出しました。

### 24) 外部キー候補サービス
- 追加: `Services/DynamicEntityForeignKeyDataService.cs`
- 機能:
  - フォーム用候補 (`Forms.*.ForeignKey`) の一括ロード
  - フィルタ用候補 (`Filters.*.ForeignKey`) の一括ロード

### 25) Controller 適用
- 変更: `Controllers/DynamicEntityController.cs`
- 内容:
  - `LoadForeignKeyDataForm/Filter` を削除
  - `DynamicEntityForeignKeyDataService` 呼び出しへ置換

### 26) DI 登録
- 変更: `Program.cs`
- 追加:
  - `DynamicEntityForeignKeyDataService`

### 27) 追加テスト
- `NetYamlForge.Tests/DynamicEntityForeignKeyDataServiceTests.cs`
  - Form 対象の FK 項目のみロード
  - Filter 対象の FK 項目のみロード

---

## 追記（2026-03-05 16:29 JST, ナビゲーション責務分離）
`DynamicEntityController` に残っていた returnUrl 解析とパンくず生成を `DynamicEntityNavigationService` に抽出しました。

### 28) ナビゲーションサービス
- 追加: `Services/DynamicEntityNavigationService.cs`
- 機能:
  - returnUrl から entity を抽出
  - returnUrl チェーンを辿ったパンくずリスト構築（最大深度あり）

### 29) Controller 適用
- 変更: `Controllers/DynamicEntityController.cs`
- 内容:
  - `CreatePage` / `EditPage` / 一覧 ViewModel 組み立てからサービス呼び出しへ統一
  - Controller 内の重複 private メソッドを削除

### 30) DI 登録
- 変更: `Program.cs`
- 追加:
  - `DynamicEntityNavigationService`

### 31) 追加テスト
- `NetYamlForge.Tests/DynamicEntityNavigationServiceTests.cs`

---

## 追記（2026-03-05 16:32 JST, 診断Diff責務分離）
`DynamicEntityController` の `ConfigDiagnostics` 内にあった JSON 差分計算をサービスに分離しました。

### 32) Config Diff サービス
- 追加: `Services/DynamicEntityConfigDiffService.cs`
- 機能:
  - EntityDefinition の base/effective 差分行を生成
  - includeUnchanged フラグによる同一値出力制御

### 33) Controller 適用
- 変更: `Controllers/DynamicEntityController.cs`
- 内容:
  - `ConfigDiagnostics` は差分サービス呼び出しのみを担当
  - 差分再帰ロジックを Controller から削除

### 34) DI 登録
- 変更: `Program.cs`
- 追加:
  - `DynamicEntityConfigDiffService`

### 35) 追加テスト
- `NetYamlForge.Tests/DynamicEntityConfigDiffServiceTests.cs`

---

## 追記（2026-03-05 16:34 JST, Form検証責務分離）
`DynamicEntityController` 内のフォーム値変換/検証を `DynamicEntityFormValidationService` に分離しました。

### 36) Form Validation サービス
- 追加: `Services/DynamicEntityFormValidationService.cs`
- 機能:
  - YAML 列定義に基づく型変換
  - bool 欠落時の `false` 補完
  - 変換エラー収集

### 37) Controller 適用
- 変更: `Controllers/DynamicEntityController.cs`
- 内容:
  - `Create` / `Edit` の検証処理をサービス呼び出しへ置換
  - Controller 内 `ConvertAndValidate` を削除
  - `IValueConverter` 直接依存を除去

### 38) DI 登録
- 変更: `Program.cs`
- 追加:
  - `DynamicEntityFormValidationService`

### 39) 追加テスト
- `NetYamlForge.Tests/DynamicEntityFormValidationServiceTests.cs`

---

## 追記（2026-03-05 16:43 JST, ConfigDiagnostics整理 + Controllerテスト追加）
`DynamicEntityController` の `ConfigDiagnostics` 残責務（base metadata 読み込み/JSON 生成）をサービスへ移し、`Create` の controller 挙動テストを追加しました。

### 40) ConfigDiagnostics サービス化
- 追加: `Services/BaseEntityMetadataProvider.cs`
- 追加: `Services/DynamicEntityConfigDiagnosticsService.cs`
- 機能:
  - entity 選択
  - base/effective JSON 生成
  - diff 行生成の統合呼び出し

### 41) Controller 適用
- 変更: `Controllers/DynamicEntityController.cs`
- 内容:
  - `ConfigDiagnostics` はサービス結果を ViewModel に詰めるだけの構成へ整理

### 42) DI 登録
- 変更: `Program.cs`
- 追加:
  - `IBaseEntityMetadataProvider`
  - `DynamicEntityConfigDiagnosticsService`

### 43) 追加テスト
- `NetYamlForge.Tests/DynamicEntityConfigDiagnosticsServiceTests.cs`
- `NetYamlForge.Tests/DynamicEntityControllerTests.cs`

---

## 追記（2026-03-05 16:45 JST, EditフローのControllerテスト拡張）
`DynamicEntityControllerTests` に `Edit` の page/modal 分岐テストを追加しました。

### 44) 追加テスト（Edit）
- `NetYamlForge.Tests/DynamicEntityControllerTests.cs`
- 内容:
  - validation fail + modal の `_Form` 応答
  - page 成功時の `returnUrl` redirect

---

## 追記（2026-03-05 16:48 JST, Delete hook拒否分岐テスト追加）
`DynamicEntityControllerTests` に `Delete` の before hook abort 分岐を追加しました。

### 45) 追加テスト（Delete）
- `NetYamlForge.Tests/DynamicEntityControllerTests.cs`
- 内容:
  - beforeDelete hook が abort した場合の `BadRequest` 応答
  - hook reject 時に repository delete が呼ばれないこと

---

## 追記（2026-03-05 16:51 JST, Delete成功分岐 + ConfigDiagnostics actionテスト追加）
`DynamicEntityControllerTests` に `Delete` 成功分岐と `ConfigDiagnostics` の action レベル検証を追加しました。

### 46) 追加テスト（Delete + ConfigDiagnostics）
- `NetYamlForge.Tests/DynamicEntityControllerTests.cs`
- 内容:
  - delete 成功時の `_List` partial 応答
  - `ConfigDiagnostics` で未存在 entity 指定時のフォールバック
  - `ConfigDiagnosticsViewModel` の主要フィールド検証

---

## 追記（2026-03-05 16:54 JST, 一覧系ヘッダー/オプション分岐テスト追加）
`DynamicEntityControllerTests` に `ListPartial` ヘッダー設定および `count/clear` 分岐のテストを追加しました。

### 47) 追加テスト（ListPartial + Index）
- `NetYamlForge.Tests/DynamicEntityControllerTests.cs`
- 内容:
  - `ListPartial` の `HX-Push-Url` ヘッダー設定
  - `Index` の `count=0` 時の件数取得スキップ
  - `ListPartial` の `clear=1` 時の検索語クリア

---

## 追記（2026-03-05 16:55 JST, Create/Edit modal成功分岐テスト追加）
`DynamicEntityControllerTests` に `Create/Edit` の modal 成功時挙動テストを追加しました。

### 48) 追加テスト（Create/Edit modal success）
- `NetYamlForge.Tests/DynamicEntityControllerTests.cs`
- 内容:
  - `_List` partial 応答
  - `HX-Retarget` / `HX-Trigger` ヘッダー設定
  - 成功後の件数再取得呼び出し

---

## 追記（2026-03-05 17:01 JST, 一覧取得ロジックのサービス分離）
`DynamicEntityController` の `Index/ListPartial` 重複ロジックを `DynamicEntityListQueryService` に分離しました。

### 49) List Query サービス
- 追加: `Services/DynamicEntityListQueryService.cs`
- 機能:
  - 一覧取得クエリ組み立て
  - `count/clear` オプション解決
  - keyset/numbered の `fetchOneExtra` 判定
  - form/filter FK データ取得切替

### 50) Controller 適用
- 変更: `Controllers/DynamicEntityController.cs`
- 内容:
  - `Index/ListPartial` の重複クエリ処理をサービス呼び出しへ置換

### 51) DI 登録
- 変更: `Program.cs`
- 追加:
  - `DynamicEntityListQueryService`

### 52) 追加テスト
- `NetYamlForge.Tests/DynamicEntityListQueryServiceTests.cs`

---

## 追記（2026-03-05 17:12 JST, Hook可観測性テレメトリ追加）
Hook 実行の可観測性を上げるため、統一テレメトリを導入しました。

### 53) Hook Telemetry
- 追加: `Services/HookExecutionTelemetry.cs`
- 機能:
  - `phase/source/entity/operation/hook/result/durationMs` の記録
  - 既定ロガー実装で構造化ログ出力

### 54) CRUD実行基盤適用
- 変更: `Services/EntityCrudExecutionService.cs`
- 内容:
  - before/after hook の成功/拒否/未登録/例外を計測・記録

### 55) DI 登録
- 変更: `Program.cs`
- 追加:
  - `IHookExecutionTelemetry` (`HookExecutionTelemetryLogger`)

### 56) テスト更新
- `NetYamlForge.Tests/EntityCrudExecutionServiceTests.cs`
- `NetYamlForge.Tests/DynamicEntityCommandServiceTests.cs`
- `NetYamlForge.Tests/DynamicEntityControllerTests.cs`

---

## 追記（2026-03-05 17:14 JST, Command結果のエラーモデル統一）
`DynamicEntityCommandService` の tuple 戻り値を `CommandResult` 系に統一しました。

### 57) CommandResult モデル
- 追加: `Services/CommandResult.cs`
- 内容:
  - `CommandError`（`Code` + `Message`）
  - `CommandResult` / `CommandResult<T>`

### 58) CommandService 適用
- 変更: `Services/DynamicEntityCommandService.cs`
- 内容:
  - create/update/delete の戻り値を統一モデル化
  - hook reject 時に error code を付与

### 59) Controller/Tests 追随
- `Controllers/DynamicEntityController.cs`
- `NetYamlForge.Tests/DynamicEntityCommandServiceTests.cs`

---

## 追記（2026-03-05 17:20 JST, 並行更新ガードの追加）
`Update/Delete` の affected rows が 0 件のとき、競合として扱うガードを追加しました。

### 60) CommandService 競合判定
- 変更: `Services/DynamicEntityCommandService.cs`
- 内容:
  - 0件更新/削除時に `concurrency_conflict_or_not_found` を返却

### 61) Controller 応答強化
- 変更: `Controllers/DynamicEntityController.cs`
- 内容:
  - delete 競合時は `409 Conflict` 応答

### 62) 追加テスト
- `NetYamlForge.Tests/DynamicEntityCommandServiceTests.cs`
- `NetYamlForge.Tests/DynamicEntityControllerTests.cs`

---

## 追記（2026-03-05 17:23 JST, エラーコード集中化 + HTTPマッパー分離）
エラーコード判定の散在を減らすため、定数化と HTTP 応答マッパーを導入しました。

### 63) エラーコード定数
- 追加: `Services/CommandErrorCodes.cs`

### 64) HTTP判定マッパー
- 追加: `Services/CommandErrorHttpMapper.cs`
- 機能:
  - `CommandError` から conflict 判定

### 65) 適用
- `Services/DynamicEntityCommandService.cs`
- `Controllers/DynamicEntityController.cs`
- `Program.cs`（DI 登録）

### 66) 追加テスト
- `NetYamlForge.Tests/CommandErrorHttpMapperTests.cs`

---

## 追記（2026-03-05 17:26 JST, 文書分層インデックス整備）
ドキュメントを `architecture / runbook / refactor-log` の入口で辿れるように索引を追加しました。

### 67) 追加ドキュメント
- `docs/architecture-map-ja.md`
- `docs/runbook-index-ja.md`
- `docs/refactor-log-index-ja.md`

### 68) README 再編
- `docs/README-ja.md`
- 内容:
  - 「まず読む」順序を分層入口ベースに更新
  - リファクタログ索引への導線追加

---

## 追記（2026-03-05 17:32 JST, 役割別読み進めガイド追加）
ドキュメント参照導線を役割ベースに整理しました。

### 69) 追加ドキュメント
- `docs/role-reading-paths-ja.md`
- 内容:
  - 開発者/運用担当/障害対応/設定担当の読み順

### 70) 入口更新
- `docs/README-ja.md`
- `docs/runbook-index-ja.md`

---

## 追記（2026-03-05 17:xx JST, Form ViewModel 組み立て分離）
フォーム再表示時の ViewModel 組み立て責務をサービスへ抽出しました。

### 28) Form ViewModel Factory
- 追加: `Services/DynamicEntityFormViewModelFactory.cs`
- 機能:
  - `DynamicFormViewModel` の生成を共通化
  - エラー/送信値/パンくず/表示モードの注入を統一

### 29) Controller 適用
- 変更: `Controllers/DynamicEntityController.cs`
- 内容:
  - `new DynamicFormViewModel(...)` の重複呼び出しを factory に置換
  - `RenderFormByMode` ヘルパー追加で page/modal 分岐を統一

### 30) DI 登録
- 変更: `Program.cs`
- 追加:
  - `DynamicEntityFormViewModelFactory`

### 31) 追加テスト
- `NetYamlForge.Tests/DynamicEntityFormViewModelFactoryTests.cs`
  - Create エラー再描画ケース
  - Page モード + Breadcrumb 設定ケース

---

## 追記（2026-03-05 17:xx JST, 一覧HTTP応答分離）
一覧更新時の HTMX ヘッダー設定をサービス化し、Controller から UI 応答詳細を分離しました。

### 32) List HTTP Response Service
- 追加: `Services/DynamicEntityListHttpResponseService.cs`
- 機能:
  - `HX-Push-Url` 設定（一覧状態URL）
  - `HX-Retarget/HX-Trigger` 設定（フォーム保存後の一覧更新）

### 33) Controller 適用
- 変更: `Controllers/DynamicEntityController.cs`
- 内容:
  - `ListPartial` の push URL 設定をサービスへ委譲
  - `Create/Edit` のレスポンスヘッダー設定をサービスへ委譲

### 34) DI 登録
- 変更: `Program.cs`
- 追加:
  - `DynamicEntityListHttpResponseService`

### 35) 追加テスト
- `NetYamlForge.Tests/DynamicEntityListHttpResponseServiceTests.cs`
  - Retarget/Trigger ヘッダー設定
  - Push URL ヘッダー設定
