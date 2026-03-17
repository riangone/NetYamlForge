// ファイル概要: ホームページ（/ ルート）の表示設定を定義するモデルクラス群です。
// config/home-page.yml または projects/<name>/config/home-page.yml から読み込まれます。
// HomePageConfigProvider が YAML をデシリアライズしてこのクラスに変換します。

namespace NetYamlForge.Models;

/// <summary>
/// ホームページ全体の表示設定。
/// ヒーローセクション・メトリクス・機能紹介・ソリューション・クイックアクション・
/// プロジェクトプロファイルを統合したルート設定クラスです。
/// </summary>
public class HomePageConfig
{
    public HomeHeroConfig Hero { get; set; } = new();
    public List<HomeMetricConfig> Metrics { get; set; } = new();
    public List<HomeCapabilityConfig> Capabilities { get; set; } = new();
    public List<HomeSolutionConfig> Solutions { get; set; } = new();
    public List<HomeQuickActionConfig> QuickActions { get; set; } = new();
    public Dictionary<string, HomeProjectProfileConfig> ProjectProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HomeProjectProfileConfig DefaultProjectProfile { get; set; } = new();
    public string ProjectsSectionTitle { get; set; } = "Active Product Suites";
    public string? ProjectsSectionTitleKey { get; set; }
    public string ProjectsSectionLead { get; set; } = "Choose a workspace to continue.";
    public string? ProjectsSectionLeadKey { get; set; }
    public string CapabilitiesSectionTitle { get; set; } = "Core Capabilities";
    public string? CapabilitiesSectionTitleKey { get; set; }
    public string SolutionsSectionTitle { get; set; } = "Solution Matrix";
    public string? SolutionsSectionTitleKey { get; set; }
    public string QuickActionsSectionTitle { get; set; } = "Quick Actions";
    public string? QuickActionsSectionTitleKey { get; set; }
    public string OpenWorkspaceLabel { get; set; } = "Open Workspace";
    public string? OpenWorkspaceLabelKey { get; set; }
    public string EmptyProjectsMessage { get; set; } = "プロジェクトが見つかりません。`projects/` 配下を確認してください。";
    public string? EmptyProjectsMessageKey { get; set; }
}

/// <summary>ホームページ上部のヒーローセクション設定。キャッチコピー・説明文・CTA ボタンを含む。</summary>
public class HomeHeroConfig
{
    public string Eyebrow { get; set; } = "Enterprise Operations Cloud";
    public string? EyebrowKey { get; set; }
    public string Title { get; set; } = "Metadata-driven enterprise products, delivered faster.";
    public string? TitleKey { get; set; }
    public string Description { get; set; } = "Design once in YAML. Ship dashboards, CRUD, and operational pages consistently.";
    public string? DescriptionKey { get; set; }
    public string PrimaryActionLabel { get; set; } = "Open Control Center";
    public string? PrimaryActionLabelKey { get; set; }
    public string PrimaryActionUrl { get; set; } = "#products";
    public string SecondaryActionLabel { get; set; } = "View Architecture";
    public string? SecondaryActionLabelKey { get; set; }
    public string SecondaryActionUrl { get; set; } = "/Home/Privacy";
    public List<string> Highlights { get; set; } = new();
    public List<string>? HighlightKeys { get; set; }
}

/// <summary>ホームページに表示する KPI メトリクスカードの設定。ラベル・値・トレンド・トーン（色）を保持する。</summary>
public class HomeMetricConfig
{
    public string Label { get; set; } = "";
    public string? LabelKey { get; set; }
    public string Value { get; set; } = "";
    public string? Trend { get; set; }
    public string? TrendKey { get; set; }
    public string? Tone { get; set; }
}

/// <summary>フレームワークのコア機能紹介カード設定。アイコン・タイトル・説明・バッジを持つ。</summary>
public class HomeCapabilityConfig
{
    public string Icon { get; set; } = "🧩";
    public string Title { get; set; } = "";
    public string? TitleKey { get; set; }
    public string Description { get; set; } = "";
    public string? DescriptionKey { get; set; }
    public string? Badge { get; set; }
    public string? BadgeKey { get; set; }
}

/// <summary>ソリューションマトリクス行の設定。対象ユーザー・サマリー・ステータスを表す。</summary>
public class HomeSolutionConfig
{
    public string Name { get; set; } = "";
    public string? NameKey { get; set; }
    public string Audience { get; set; } = "";
    public string? AudienceKey { get; set; }
    public string Summary { get; set; } = "";
    public string? SummaryKey { get; set; }
    public string Status { get; set; } = "";
    public string? StatusKey { get; set; }
}

/// <summary>
/// ホームページのクイックアクションボタン設定。
/// AdminOnly=true なら管理者にのみ表示、UserOnly=true なら非管理者ユーザーのみに表示。
/// </summary>
public class HomeQuickActionConfig
{
    public string Label { get; set; } = "";
    public string? LabelKey { get; set; }
    public string Url { get; set; } = "";
    public string Style { get; set; } = "btn-outline";
    public string? Icon { get; set; }
    public bool AdminOnly { get; set; }
    public bool UserOnly { get; set; }
}

/// <summary>
/// プロジェクトカードのプロファイル設定（projects/&lt;name&gt;/config/home-page.yml で定義）。
/// アイコン・タグライン・タグを持ち、ホームページのプロジェクト一覧カードに表示される。
/// </summary>
public class HomeProjectProfileConfig
{
    public string Icon { get; set; } = "📦";
    public string Tagline { get; set; } = "Metadata-driven product workspace";
    public string? TaglineKey { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string>? TagKeys { get; set; }
}

/// <summary>
/// HomeController.Index アクションが Views/Home/Index.cshtml に渡すビューモデル。
/// HomePageConfig（YAML設定）と ProjectInfo 一覧（実行時プロジェクト情報）を保持する。
/// </summary>
public class HomeIndexViewModel
{
    /// <summary>home-page.yml から読み込んだホームページ表示設定。</summary>
    public HomePageConfig Config { get; set; } = new();
    /// <summary>ProjectManager が検出した全プロジェクトの情報リスト。</summary>
    public IReadOnlyCollection<Services.ProjectInfo> Projects { get; set; } = Array.Empty<Services.ProjectInfo>();
}
