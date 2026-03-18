# Entity と Page Section の通用化候補

Entity（`entities/*.yml` + 関連サービス）と Page Section（`pages/*.yaml` + 関連サービス）の間で重複・不統一になっているコードの整理候補。優先度順に記載。

---

## 優先度 高 / 工数 小

### 1. `IdentifierRegex` の重複定義

`SqlSafetyGuard.cs` に正規表現の権威定義が既にあるにもかかわらず、2 箇所に同一のコピーが存在する。

| ファイル | 行 |
|---|---|
| `Services/DynamicEntity/DynamicCrudRepository.cs` | 75 |
| `Services/Page/PageDataQueryService.cs` | 15 |

**対応:** 両者の private 定義を削除し、`SqlSafetyGuard.IdentifierRegex` を参照する。

---

## 優先度 高 / 工数 中

### 2. Hook 定義の基底クラス化

`EntityHooksDefinition`（EntityMetadata.cs:417–630）と `SectionHooksDefinition`（PageDefinition.cs:62–100）はほぼ同じ構造を持つ。

**共通点:**
- BeforeCreate / AfterCreate / BeforeUpdate / AfterUpdate / BeforeDelete / AfterDelete
- `@presetName` 展開ロジック（循環参照検出を含む）

**差異:** `EntityHooksDefinition` はリフレクション経由でプロパティにアクセスするが、`SectionHooksDefinition` は直接 `List<string>` プロパティを参照する。

**対応:** `HooksDefinitionBase` を抽出し、プリセット展開の共通ロジック（約 150 行）を一元化。両クラスは基底クラスを継承する形に変更。

---

### 3. 小モデルの統合（Paging / Confirmation / Filter）

下表の 3 ペアはプロパティが完全に同一。

| Entity 側 | Section 側 | プロパティ |
|---|---|---|
| `PagingDefinition` (EntityMetadata.cs:266) | `SectionPagingDef` (PageDefinition.cs:171) | PageSize / Mode / EnableCount |
| `ConfirmationDefinition` (EntityMetadata.cs:402) | `SectionConfirmationDef` (PageDefinition.cs:48) | Create / Update / Delete |
| `FilterDefinition` (EntityMetadata.cs:273) | `PageFilterDefinition` (PageDefinition.cs:187) | Label / LabelKey / LabelI18n / Type / Options / GetLabel() |

**対応:** 各ペアを 1 クラスに統合し、Entity・Section の両側から参照する。

---

### 4. Section 側フォームバリデーションの欠落

Entity 側には `DynamicEntityFormValidationService`（型変換 + 必須チェック）が存在するが、Section 側は `PageRowMutationService` 内でアドホックに処理しており、ロジックが不統一。

**対応:** `SectionRowValidationService` を新規作成し、`IValueConverter` を活用して Entity 側と対称的な実装にする。

---

## 優先度 高 / 工数 大

### 5. Row Mutation SQL 生成の統合

INSERT / UPDATE / DELETE の SQL 構築ロジックがほぼ同一。

| Entity 側 | Section 側 |
|---|---|
| `DynamicCrudRepository.cs` InsertAsync (210–226) | `PageRowMutationService.cs` InsertRowAsync (114–158) |
| `DynamicCrudRepository.cs` UpdateAsync (228–284) | `PageRowMutationService.cs` UpdateRowAsync (80–112) |
| `DynamicCrudRepository.cs` DeleteAsync (286–332) | `PageRowMutationService.cs` DeleteRowAsync (209–252) |

差異はメタデータの出所（`EntityDefinition` vs `SectionDefinition`）のみ。

**対応:** `IRowMutationRepository` インターフェースを定義し、共通実装を抽出。メタデータのみ差し替えられるアダプター構造にする。

---

### 6. Hook 実行パターンの統合

`DynamicEntityCommandService` と `PageRowMutationService` の両者が `RunBeforeHooksAsync` / `RunAfterHooksAsync` を独自実装しており、フック解決（project 固有レジストリ → 共通レジストリの 2 段階）も重複している。

**対応:** `HookExecutionService` を抽出し、両側から利用する共有サービスにする。

---

### 7. Section 側フォーム ViewModel Factory の欠落

Entity 側には `DynamicEntityFormViewModelFactory` があるが、Section 側は `PageController` 内でアドホックにモデルを組み立てている。

**対応:** `SectionRowFormViewModelFactory` を新規作成し、Entity 側と対称的な構造にする。

---

## 優先度 中

### 8. リスト取得クエリのページング・ソートロジック

`DynamicCrudRepository.GetAllAsync`（90–161 行）と `PageDataQueryService.GetSectionDataAsync`（77–170 行）がフィルター→ソート→ページングの約 70 行を並行実装している。Section 側は `ISqlDialect` を使用しておらず、LIMIT/OFFSET がハードコードされている点で不統一。

**対応:** 共通のクエリ構築ユーティリティを抽出し、Section 側も `ISqlDialect` に対応させる。

---

### 9. View 層（Razor）の共通コンポーネント化

`_List.cshtml` と `_SectionTable.cshtml` がページング URL 構築・ソート方向トグルなどの HTML/Razor ロジックを約 150 行重複している。

**対応:** `_TablePagination.cshtml` や `_SortHeader.cshtml` などの共有 Partial View を抽出し、両方から参照する。

---

## 実施推奨順序

```
Phase 1（小さな改善）
  1. IdentifierRegex 重複削除           ← 5 分
  2. 小モデル統合 (Paging/Confirmation/Filter)
  3. HooksDefinitionBase 抽出

Phase 2（サービス層）
  4. HookExecutionService 抽出
  5. SectionRowValidationService 新規作成
  6. SectionRowFormViewModelFactory 新規作成

Phase 3（リポジトリ層）
  7. IRowMutationRepository + 共通実装抽出

Phase 4（View 層）
  8. ページング/ソート Partial View 共通化
```

---

## 影響ファイル一覧

**変更:**
- `Models/EntityMetadata.cs`
- `Models/PageDefinition.cs`
- `Services/DynamicEntity/DynamicCrudRepository.cs`
- `Services/DynamicEntity/DynamicEntityCommandService.cs`
- `Services/DynamicEntity/DynamicEntityFormValidationService.cs`
- `Services/DynamicEntity/DynamicEntityFormViewModelFactory.cs`
- `Services/Page/PageDataQueryService.cs`
- `Services/Page/PageRowMutationService.cs`
- `Views/DynamicEntity/_List.cshtml`
- `Views/DynamicEntity/_Form.cshtml`
- `Views/Page/Components/_SectionTable.cshtml`
- `Views/Page/Components/_SectionRowForm.cshtml`

**新規作成:**
- `Models/HooksDefinitionBase.cs`
- `Services/HookExecutionService.cs`
- `Services/Page/SectionRowValidationService.cs`
- `Services/Page/SectionRowFormViewModelFactory.cs`
- `Services/IRowMutationRepository.cs`
