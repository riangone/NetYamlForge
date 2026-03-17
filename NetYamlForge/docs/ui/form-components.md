# フォームコンポーネント一覧

## 概要

本ドキュメントは NetYamlForge で利用可能なフォームコンポーネント（入力フィールド型）の一覧と、今後追加予定のコンポーネントをまとめたものです。

- **実装済み**: 現在 `_FormField.cshtml` でサポートされている型
- **追加予定**: 今後実装を予定している拡張コンポーネント

---

## 実装済みコンポーネント

### 基本入力フィールド

| 型 | 説明 | YAML 例 |
|----|------|---------|
| `string` | テキスト入力（1 行） | `type: string` |
| `int` | 整数入力 | `type: int` |
| `decimal` / `double` | 小数入力 | `type: decimal` |
| `bool` | トグルスイッチ（有効/無効） | `type: bool` |
| `datetime` | 日時選択 | `type: datetime` |
| `date` | 日付選択 | `type: date` |
| `email` | メールアドレス入力 | `type: email` |
| `textarea` | 複数行テキスト | `type: textarea` |

### 選択フィールド

| 型 | 説明 | YAML 例 |
|----|------|---------|
| `radio` | ラジオボタン（単一選択） | `type: radio` + `options: [A, B, C]` |
| `select` | ドロップダウン（単一選択） | `options: [A, B, C]` |
| `color` | カラーピッカー | `type: color` |
| `range` | スライダー（範囲選択） | `type: range` |
| `rating` | 星評価（5 段階） | `type: rating` |

### 関連フィールド

| 型 | 説明 | YAML 例 |
|----|------|---------|
| `ForeignKey` | 外部キー選択（ドロップダウン） | `foreignKey: { entity: Customer, displayColumns: [Name] }` |
| `Picker` | エンティティピッカー（単一選択） | `foreignKey: { entity: Customer, picker: true }` |
| `MultiPicker` | エンティティピッカー（複数選択） | `foreignKey: { entity: Customer, multiPicker: true }` |

### 特殊フィールド

| 型 | 説明 | YAML 例 |
|----|------|---------|
| `readonly` | 読み取り専用表示 | `editable: false` |

---

## 追加予定コンポーネント

### ファイル・メディア系

#### 1. ファイルアップロード (`file`)

ファイルをアップロードするコンポーネント。

```yaml
columns:
  Attachment:
    type: file
    uploadPath: "/uploads"
    allowedExtensions: [".pdf", ".docx", ".xlsx"]
    maxSize: 10485760  # 10MB
    displayName: "添付ファイル"
```

**実装項目:**
- [ ] `_FormField.cshtml` に `file` 型レンダリング追加
- [ ] アップロードハンドラー（Controller/Service）
- [ ] ファイルサイズ・拡張子検証
- [ ] `KnownColumnTypes` に `file` 追加

---

#### 2. 画像アップロード (`image`)

画像ファイルをアップロードし、プレビュー表示するコンポーネント。

```yaml
columns:
  Avatar:
    type: image
    uploadPath: "/images/avatars"
    thumbnailSize: [100, 100]
    allowedExtensions: [".jpg", ".png", ".gif"]
    maxSize: 5242880  # 5MB
    displayName: "アバター画像"
```

**実装項目:**
- [ ] `_FormField.cshtml` に画像プレビュー追加
- [ ] サムネイル生成ロジック
- [ ] 画像リサイズ機能
- [ ] `KnownColumnTypes` に `image` 追加

---

### テキスト編集系

#### 3. リッチテキストエディタ (`richtext`)

WYSIWYG エディタ（TinyMCE / Quill / CKEditor 等）。

```yaml
columns:
  Description:
    type: richtext
    toolbar: [bold, italic, link, image, code]
    height: 300
    displayName: "詳細説明"
```

**実装項目:**
- [ ] エディタライブラリ選定（Quill.js 推奨）
- [ ] `_FormField.cshtml` にエディタ初期化
- [ ] HTML サニタイズ（XSS 対策）
- [ ] `KnownColumnTypes` に `richtext` 追加

---

#### 4. Markdown エディタ (`markdown`)

Markdown 形式で入力し、プレビュー表示可能。

```yaml
columns:
  Content:
    type: markdown
    preview: true
    height: 400
    displayName: "コンテンツ"
```

**実装項目:**
- [ ] Markdown エディタライブラリ（SimpleMDE / EasyMDE）
- [ ] プレビュー表示機能
- [ ] HTML 変換（Markdig 等）
- [ ] `KnownColumnTypes` に `markdown` 追加

---

#### 5. コードエディタ (`code`)

コード入力用エディタ（Syntax Highlight 付き）。

```yaml
columns:
  Script:
    type: code
    language: javascript  # sql, html, css, json, python 等
    lineNumbers: true
    theme: monokai
    displayName: "スクリプト"
```

**実装項目:**
- [ ] CodeMirror / Monaco Editor 統合
- [ ] 言語別シンタックスハイライト
- [ ] `KnownColumnTypes` に `code` 追加

---

### 検索・入力支援系

#### 6. 自動完成・コンボボックス (`autocomplete`)

入力中に候補を表示するコンポーネント。

```yaml
columns:
  Tags:
    type: autocomplete
    source: /api/tags  # API エンド点
    multiple: true
    freeText: true  # 自由入力許可
    minChars: 2  # 最小入力文字数
    displayName: "タグ"
```

**実装項目:**
- [ ] 自動完成 UI 実装（Choices.js / Tom Select）
- [ ] API からの候補取得
- [ ] 複数選択対応
- [ ] `KnownColumnTypes` に `autocomplete` 追加

---

#### 7. タグ入力 (`tags`)

タグをカンマ区切り等で入力するコンポーネント。

```yaml
columns:
  Tags:
    type: tags
    suggestions: [Tag1, Tag2, Tag3]
    delimiter: ","
    maxTags: 10
    displayName: "タグ"
```

**実装項目:**
- [ ] タグ入力 UI（Chips 形式）
- [ ] 候補表示機能
- [ ] 区切り文字設定
- [ ] `KnownColumnTypes` に `tags` 追加

---

### 数値・通貨系

#### 8. 通貨入力 (`money`)

通貨形式の入力（桁区切り、通貨記号表示）。

```yaml
columns:
  Price:
    type: money
    currency: JPY  # USD, EUR, CNY 等
    locale: ja-JP
    thousandSeparator: true
    decimalPlaces: 0
    displayName: "価格"
```

**実装項目:**
- [ ] 通貨フォーマット入力
- [ ] 桁区切り表示（Intl.NumberFormat）
- [ ] 通貨記号表示
- [ ] `KnownColumnTypes` に `money` 追加

---

#### 9. パーセント入力 (`percent`)

パーセント入力（0-100 範囲）。

```yaml
columns:
  Discount:
    type: percent
    min: 0
    max: 100
    decimals: 2
    displayName: "割引率"
```

**実装項目:**
- [ ] パーセント入力 UI
- [ ] 範囲検証
- [ ] `%` 記号表示
- [ ] `KnownColumnTypes` に `percent` 追加

---

### 日付・時間系

#### 10. 日時範囲 (`datetime-range`)

開始日時と終了日時をペアで入力。

```yaml
columns:
  StartDate:
    type: datetime-range
    endField: EndDate  # 関連終了フィールド
    displayName: "開催期間"
```

**実装項目:**
- [ ] 日付範囲ピッカー（Flatpickr /daterangepicker.js）
- [ ] 開始/終了の整合性検証
- [ ] `KnownColumnTypes` に `datetime-range` 追加

---

### 連絡先系

#### 11. 電話番号 (`tel`)

電話番号形式の入力。

```yaml
columns:
  Phone:
    type: tel
    pattern: "^[0-9]{10,11}$"
    placeholder: "03-1234-5678"
    displayName: "電話番号"
```

**実装項目:**
- [ ] 電話番号入力 UI
- [ ] 形式検証（国別対応）
- [ ] `KnownColumnTypes` に `tel` 追加

---

#### 12. URL 入力 (`url`)

URL 形式の入力。

```yaml
columns:
  Website:
    type: url
    placeholder: "https://example.com"
    displayName: "ウェブサイト"
```

**実装項目:**
- [ ] URL 入力 UI
- [ ] 形式検証
- [ ] `KnownColumnTypes` に `url` 追加

---

### セキュリティ系

#### 13. パスワード入力 (`password`)

パスワード入力（強度表示付き）。

```yaml
columns:
  Password:
    type: password
    minLength: 8
    showStrength: true  # パスワード強度表示
    toggleVisibility: true  # 表示/非表示切り替え
    displayName: "パスワード"
```

**実装項目:**
- [ ] パスワード入力 UI（マスキング）
- [ ] 表示/非表示切り替え
- [ ] パスワード強度チェッカー
- [ ] `KnownColumnTypes` に `password` 追加

---

### 選択・設定系

#### 14. チェックボックスグループ (`checkbox-group`)

複数選択可能なチェックボックス。

```yaml
columns:
  Interests:
    type: checkbox-group
    options: [Sports, Music, Art, Technology]
    columns: 2  # 2 列レイアウト
    displayName: "興味関心"
```

**実装項目:**
- [ ] チェックボックスグループ UI
- [ ] 複数値の保存（CSV または JSON）
- [ ] `KnownColumnTypes` に `checkbox-group` 追加

---

#### 15. スイッチグループ (`switch-group`)

複数のスイッチ設定。

```yaml
columns:
  Notifications:
    type: switch-group
    options:
      - { value: email, label: "メール通知" }
      - { value: sms, label: "SMS 通知" }
      - { value: push, label: "プッシュ通知" }
    displayName: "通知設定"
```

**実装項目:**
- [ ] スイッチグループ UI
- [ ] 複数値の保存
- [ ] `KnownColumnTypes` に `switch-group` 追加

---

### 特殊入力系

#### 16. サイン板 (`signature`)

手書きサイン入力。

```yaml
columns:
  Signature:
    type: signature
    width: 400
    height: 200
    penColor: "#000000"
    backgroundColor: "#ffffff"
    displayName: "署名"
```

**実装項目:**
- [ ] サイン入力 UI（Signature Pad）
- [ ] 画像データ保存（Base64 / ファイル）
- [ ] クリア機能
- [ ] `KnownColumnTypes` に `signature` 追加

---

#### 17. 地図選択器 (`map` / `location`)

地図上で位置を選択。

```yaml
columns:
  Location:
    type: map
    provider: openstreetmap  # google, baidu, mapbox
    defaultZoom: 15
    outputFormat: "lat,lng"  # または address
    displayName: "所在地"
```

**実装項目:**
- [ ] 地図統合（Leaflet / Google Maps）
- [ ] 緯度経度取得
- [ ] 住所逆引き（オプション）
- [ ] `KnownColumnTypes` に `map` 追加

---

#### 18. JSON エディタ (`json`)

JSON 形式の設定入力。

```yaml
columns:
  Config:
    type: json
    schema: /schemas/config-schema.json
    validate: true
    displayName: "設定"
```

**実装項目:**
- [ ] JSON エディタ（JSONEditor / Ace）
- [ ] JSON Schema 検証
- [ ] エラー表示
- [ ] `KnownColumnTypes` に `json` 追加

---

#### 19. ソート可能リスト (`sortable-list`)

ドラッグ＆ドロップで順序変更。

```yaml
columns:
  Priority:
    type: sortable-list
    source: /api/items
    valueField: id
    displayField: name
    displayName: "優先順位"
```

**実装項目:**
- [ ] ソート可能リスト UI（SortableJS）
- [ ] 順序保存（CSV または JSON）
- [ ] `KnownColumnTypes` に `sortable-list` 追加

---

## 実装ガイドライン

### 1. 新規コンポーネント追加手順

1. **YAML 型定義追加**
   - `Services/Validation/YamlConfigStartupValidator.cs` の `KnownColumnTypes` に追加

2. **フォームレンダリング追加**
   - `Views/DynamicEntity/_FormField.cshtml` に型別レンダリングロジックを追加

3. **検証ロジック追加**
   - `Services/DynamicEntityFormValidationService.cs` に型別検証を追加

4. **ドキュメント更新**
   - 本ファイル（`docs/ui/form-components.md`）を更新

5. **テスト追加**
   - `NetYamlForge.Tests/DynamicEntityFormValidationServiceTests.cs` にテストケース追加

### 2. 依存関係の追加

新しい UI ライブラリを追加する場合は：

1. `NetYamlForge.csproj` に NuGet パッケージ追加（必要な場合）
2. `wwwroot/` に JS/CSS リソース配置
3. または CDN から読み込み（`_Layout.cshtml` または `_Form.cshtml`）

### 3. 国際化（i18n）

- ラベル、プレースホルダー、エラーメッセージは `labelI18n` で管理
- `Resources/` ディレクトリに翻訳リソースを追加

---

## 優先度マトリクス

| 優先度 | コンポーネント | 状態 | 備考 |
|--------|---------------|------|------|
| 🔴 高 | `file`, `image` | ✅ 完了 | 2026-03-13 実装済み |
| 🔴 高 | `richtext`, `markdown` | ✅ 完了 | 2026-03-13 実装済み（簡易版） |
| 🔴 高 | `autocomplete`, `tags` | ✅ 完了 | 2026-03-13 実装済み |
| 🟡 中 | `money`, `percent` | ✅ 完了 | 2026-03-13 実装済み |
| 🟡 中 | `tel`, `url`, `password` | ✅ 完了 | 2026-03-13 実装済み |
| 🟡 中 | `checkbox-group`, `switch-group` | ✅ 完了 | 複数選択 UX |
| 🟢 低 | `datetime-range` | ✅ 完了 | 日付範囲入力 |
| 🟢 低 | `code`, `json` | ✅ 完了 | 特殊ユースケース |
| 🟢 低 | `signature`, `map` | ✅ 完了 | 特定業務向け |
| 🟢 低 | `sortable-list` | ✅ 完了 | 高度な UX |

---

## 関連ドキュメント

- [UI 設計ガイド](ui-design-system-ja.md)
- [共通フック一覧](../COMMON_HOOKS.md)
- [YAML 実例集](../chinook-yaml-examples.md)
