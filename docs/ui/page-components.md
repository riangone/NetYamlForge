# Page Components 拡張ガイド

## 概要

本ドキュメントは NetYamlForge の Page Components（`Views/Page/Components/`）に追加可能なコンポーネントの一覧と実装ガイドです。

---

## 既存コンポーネント一覧

| コンポーネント | ファイル | 説明 |
|---------------|---------|------|
| StatsCard | `_SectionStatCards.cshtml` | 統計カード |
| BarChart | `_SectionBarChart.cshtml` | 棒グラフ |
| LineChart | `_SectionLineChart.cshtml` | 折れ線グラフ |
| PieChart | `_SectionPieChart.cshtml` | 円グラフ |
| RadarChart | `_SectionRadarChart.cshtml` | レーダーチャート |
| HeatMap | `_SectionHeatMap.cshtml` | ヒートマップ |
| Kanban | `_SectionKanban.cshtml` | カンバン |
| Timeline | `_SectionTimeline.cshtml` | タイムライン |
| Table | `_SectionTable.cshtml` | データテーブル（フィルター・ページネーション・CRUD フォーム対応） |
| DetailCard | `_SectionDetailCard.cshtml` | 詳細カード |
| ProgressBars | `_SectionProgressBars.cshtml` | プログレスバー |
| BadgeList | `_SectionBadgeList.cshtml` | バッジリスト |

---

## Table コンポーネント機能詳細

`_SectionTable.cshtml` は `sections` 内の `source_type: table` / `source_type: custom` で使用されるデフォルトコンポーネントです。
`entities` の一覧と同等の **フィルター・ページネーション・ソート・CRUD フォーム・フック** をサポートします。

### columns 定義（ラベル・型・表示制御・ソート）

`columns` はシンプルなリスト形式と辞書形式の両方を受け付けます。

```yaml
# シンプル形式（後方互換）
columns: [id, name, category, price]

# 辞書形式（推奨）
columns:
  id:
    label: "ID"
    type: int
    hidden: true        # 一覧に表示しない（主キー隠し等に使用）
  name:
    label: 商品名
    type: string
    sortable: true      # クリックでソート可能
  category:
    label: カテゴリ
    type: select        # 表示時に options のラベルに変換
    sortable: true
    options:
      electronics: 電子機器
      clothing: 衣料品
  price:
    label: 価格
    type: decimal
    sortable: true
```

**列タイプ一覧:** `string` | `int` | `decimal` | `bool` | `date` | `datetime` | `select`

### ソート

`sortable: true` の列ヘッダーがクリック可能なリンクになり、昇順/降順を切り替えられます。

- クエリパラメータ: `{sectionId}__sort=列名&{sectionId}__dir=asc|desc`
- ソート変更時はページが 1 にリセットされる

### フィルター

`filters` を定義するとテーブル上部にフィルターフォームが表示されます。

```yaml
filters:
  name:
    label: 商品名
    type: like                 # 部分一致
  status:
    label: ステータス
    type: toggle_group         # ラジオボタングループ
    options:
      active: 有効
      inactive: 無効
  category:
    label: カテゴリ
    type: select               # ドロップダウン
    options:
      electronics: 電子機器
      clothing: 衣料品
  created_at:
    label: 作成日
    type: date_range           # 日付範囲（from〜to）
  price:
    label: 価格
    type: range                # 数値範囲（min〜max）
  is_active:
    label: 有効
    type: bool_toggle          # All / Yes / No 3択
```

**フィルタータイプ:** `like` | `eq` | `select` | `toggle_group` | `bool_toggle` | `date_range` | `range` | `gte` | `lte`

クエリパラメータ命名規則: `{sectionId}_{filterKey}` （例: `products_category=electronics`）

### ページネーション

`paging` セクションまたは `page_size` で設定します。

```yaml
paging:
  page_size: 20        # 1 ページあたりの件数
  mode: numbered       # numbered のみ対応（将来: keyset）
  enable_count: true   # 総件数を表示する
```

クエリパラメータ: `{sectionId}__page=N`（例: `products__page=2`）

### CRUD フォーム（新規/編集/削除）

`editable: true`、`target_table`、`target_primary_key` を設定すると CRUD ボタンが有効になります。

```yaml
sections:
  - id: products
    source_type: table
    source: Product
    editable: true
    target_table: Product
    target_primary_key: id
    forms:
      create:
        title: "新規商品を登録"
        fields: [name, category, price, supplier]  # 新規作成時のフィールド
      update:                                       # "edit" でも "update" でも動作（相互エイリアス）
        title: "商品を編集"
        fields: [category, price]                  # 編集時は一部のみ変更可
    field_defs:
      name:
        label: 商品名
        type: string
        required: true
        placeholder: "例: MacBook Pro"
      category:
        label: カテゴリ
        type: select
        required: true
        options:
          electronics: 電子機器
          clothing: 衣料品
      price:
        label: 価格
        type: decimal
        placeholder: "例: 128000"
```

- `forms.create` と `forms.update`（または `forms.edit`）でフィールドを個別定義可能
- `field_defs` でフォームフィールドのラベル・型・必須・選択肢・プレースホルダーを定義
- `updatable_fields` で簡易ホワイトリスト指定も可能（`forms` 未設定時のフォールバック）

### フック（hooks）

`entities` と同じ `IEntityHook` を section の CRUD でも利用できます。

```yaml
hooks:
  before_create: [trim, validate_required]   # 新規挿入前に実行
  after_create:  [audit_log]                 # 新規挿入後に実行
  before_update: [validate_required]         # 更新前に実行
  after_update:  [audit_log]                 # 更新後に実行
  before_delete: [check_references]          # 削除前に実行（Abort で中断可能）
  after_delete:  []
```

- フック名は `EntityHookRegistry` に登録された `IEntityHook.Name` と一致する必要があります
- `BeforeAsync` で `HookResult.Abort("エラーメッセージ")` を返すと CRUD がキャンセルされます
- 利用可能なフック一覧は `docs/COMMON_HOOKS.md` を参照

### 関連エンドポイント

| メソッド | パス | 説明 |
|---------|------|------|
| GET | `/{project}/Page/{page}/section/{id}/row-form` | 新規/編集フォームを返す |
| POST | `/{project}/Page/{page}/section/{id}/insert-row` | 新規行挿入 |
| POST | `/{project}/Page/{page}/section/{id}/update-all-fields` | 行の全フィールド更新 |
| POST | `/{project}/Page/{page}/section/{id}/update-row` | 単一フィールドインライン更新 |
| POST | `/{project}/Page/{page}/section/{id}/delete-row` | 行削除 |

---

## 追加可能なコンポーネント提案

### 📊 图表类（Chart Components）

| コンポーネント | 説明 | 用途 |
|---------------|------|------|
| `AreaChart` | 面積グラフ | 時系列データ傾向 |
| `DonutChart` | ドーナツグラフ | 割合構成比 |
| `FunnelChart` | ファネルグラフ | 販売ファネル、転換率 |
| `GaugeChart` | ゲージグラフ | KPI 達成率 |
| `ScatterChart` | 散布図 | 相関分析 |
| `CandlestickChart` | ローソク足 | 株価/価格変動 |

### 📋 列表/卡片类（List/Card Components）

| コンポーネント | 説明 | 用途 |
|---------------|------|------|
| `CardList` | カードリスト | 製品/記事リスト |
| `Accordion` | アコーディオン | FAQ、詳細展開 |
| `TabPanel` | タブパネル | カテゴリ切り替え |
| `Carousel` | カルーセル | 画像/コンテンツ回転 |
| `Masonry` | メイソンリー | 画像壁、Pinterest 風 |
| `GridList` | グリッドリスト | 商品/アルバムグリッド |

### 📈 统计/指标类（Stats/Metrics Components）

| コンポーネント | 説明 | 用途 |
|---------------|------|------|
| `MetricCards` | 指標カード組 | 多指標比較 |
| `Sparkline` | スパークライン | カード内蔵傾向 |
| `ComparisonTable` | 比較テーブル | 前後/目標比較 |
| `Leaderboard` | リーダーボード | 販売/成績ランキング |
| `HeatMatrix` | 熱力行列 | 二次元データヒートマップ |

### 📅 日历/时间类（Calendar/Time Components）

| コンポーネント | 説明 | 用途 |
|---------------|------|------|
| `Calendar` | カレンダー表示 | 予定/イベント管理 |
| `GanttChart` | ガントチャート | プロジェクト進捗管理 |
| `TimeGrid` | 時間グリッド | 時間帯スケジュール |

### 🗂️ 组织/结构类（Organization/Structure Components）

| コンポーネント | 説明 | 用途 |
|---------------|------|------|
| `OrgChart` | 組織図 | 会社/チーム構造 |
| `Tree` | ツリー表示 | カテゴリ/階層構造 |
| `MindMap` | マインドマップ | ブレインストーミング |
| `Flowchart` | フローチャート | ワークフロー可視化 |

### 📍 地图/位置类（Map/Location Components）

| コンポーネント | 説明 | 用途 |
|---------------|------|------|
| `MapMarkers` | 地図マーカー | 店舗/拠点位置 |
| `Choropleth` | 区分統計図 | 地域別データ |
| `BubbleMap` | 気泡地図 | 規模 + 位置表示 |

### 💬 沟通/协作业类（Communication/Collaboration Components）

| コンポーネント | 説明 | 用途 |
|---------------|------|------|
| `CommentThread` | コメントスレッド | 討論/フィードバック |
| `Chat` | チャット表示 | リアルタイムメッセージ |
| `ActivityFeed` | アクティビティフィード | 活動ログ |
| `NotificationList` | 通知リスト | 未読通知管理 |

### 📝 表单/输入类（Form/Input Components）

| コンポーネント | 説明 | 用途 |
|---------------|------|------|
| `Wizard` | ウィザードステップ | 多ステップフォーム |
| `BulkEdit` | 一括編集 | 複数行同時編集 |
| `FileUpload` | ファイルアップロード | ドロップ＆アップロード |
| `RichTextEditor` | リッチテキストエディタ | コンテンツ作成 |

### 🎯 目标/进度类（Goal/Progress Components）

| コンポーネント | 説明 | 用途 |
|---------------|------|------|
| `GoalTracker` | 目標追跡 | OKR/KPI 管理 |
| `Milestone` | マイルストーン | プロジェクトノード |
| `Countdown` | カウントダウン | イベント/締切日 |

### 📱 媒体类（Media Components）

| コンポーネント | 説明 | 用途 |
|---------------|------|------|
| `VideoGallery` | 動画ギャラリー | 動画リスト |
| `AudioPlayer` | オーディオプレイヤー | 音楽/ポッドキャスト |
| `DocumentPreview` | 文書プレビュー | PDF/Office プレビュー |

---

## 実装優先度 Top 10

業務アプリで頻繁に使用される 10 個のコンポーネントを優先的に実装します。

| 優先度 | コンポーネント | ファイル | 推定工数 |
|--------|---------------|---------|----------|
| 1 | 面積グラフ | `_SectionAreaChart.cshtml` | 2h |
| 2 | ドーナツグラフ | `_SectionDonutChart.cshtml` | 2h |
| 3 | カードリスト | `_SectionCardList.cshtml` | 2h |
| 4 | アコーディオン | `_SectionAccordion.cshtml` | 2h |
| 5 | ランキング | `_SectionLeaderboard.cshtml` | 2h |
| 6 | カレンダー | `_SectionCalendar.cshtml` | 4h |
| 7 | アクティビティフィード | `_SectionActivityFeed.cshtml` | 2h |
| 8 | 組織図 | `_SectionOrgChart.cshtml` | 3h |
| 9 | 目標追跡 | `_SectionGoalTracker.cshtml` | 3h |
| 10 | ファイルアップロード | `_SectionFileUpload.cshtml` | 3h |

---

## 実装ガイドライン

### 1. コンポーネント作成手順

1. `Views/Page/Components/_Section[ComponentName].cshtml` を作成
2. `SectionDefinition` に必要なプロパティを追加（必要に応じて）
3. Page Controller でデータを取得し、ビューに渡す
4. YAML で新しい component タイプを定義

### 2. YAML 定義例

```yaml
sections:
  - id: sales_trend
    title: "販売傾向"
    source_type: table
    source: orders
    columns: [date, total, count]
    ui:
      component: AreaChart
      xAxis: date
      yAxis: total
```

### 3. 共通プロパティ

各コンポーネントで使用する可能性のあるプロパティ：

```csharp
public class SectionDefinition
{
    public string Id { get; set; }
    public string? Title { get; set; }
    public List<string> Columns { get; set; }
    public string? LabelField { get; set; }
    public string? ValueField { get; set; }
    // コンポーネント固有プロパティ...
}
```

---

## 関連ドキュメント

- [UI 設計ガイド](ui-design-system-ja.md)
- [フォームコンポーネント一覧](form-components.md)
- [Dashboard 設定](../dashboard.md)
