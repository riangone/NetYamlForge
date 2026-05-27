// ファイル概要：project.yaml の YAML 構造にマッピングするモデルクラス群です。
// YamlDotNet でデシリアライズされ、ProjectManager がプロジェクト情報の構築に使用します。

namespace NetYamlForge.Models;

public class ProjectConfig
{
    public string Name { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string Version { get; set; } = "1.0.0";
    public ProjectDatabaseConfig Database { get; set; } = new();
    public ProjectFeaturesConfig Features { get; set; } = new();
    /// <summary>
    /// レイアウト設定（省略可能）。設定されている場合、プロジェクト固有のレイアウトが使用されます。
    /// </summary>
    public ProjectLayoutConfig? Layout { get; set; }
    /// <summary>
    /// カレンダー関連のプロジェクト共通設定（省略可能）。
    /// </summary>
    public ProjectCalendarConfig? Calendar { get; set; }
    /// <summary>
    /// プロジェクト固有のメール設定（省略時はグローバル設定を使用）。
    /// </summary>
    public ProjectEmailConfig? Email { get; set; }
}

/// <summary>
/// project.yaml の email: セクション。省略された項目はグローバル設定で補完されます。
/// </summary>
public class ProjectEmailConfig
{
    public ProjectSmtpConfig? Smtp { get; set; }
    public ProjectImapConfig? Imap { get; set; }
}

public class ProjectSmtpConfig
{
    public string? Server { get; set; }
    public int? Port { get; set; }
    public bool? UseSsl { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }
    public string? FromName { get; set; }
    public string? FromAddress { get; set; }
}

public class ProjectImapConfig
{
    public string? Server { get; set; }
    public int? Port { get; set; }
    public bool? UseSsl { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }
}

public class ProjectCalendarConfig
{
    public ProjectJapanHolidayConfig? JapanHoliday { get; set; }
}

public class ProjectJapanHolidayConfig
{
    /// <summary>JP祝日ソース: hybrid | api | builtin</summary>
    public string Provider { get; set; } = "hybrid";
    /// <summary>外部JP祝日API URLテンプレート（{year} を含む）</summary>
    public string ApiUrlTemplate { get; set; } = "https://date.nager.at/api/v3/PublicHolidays/{year}/JP";
    public int ApiTimeoutMs { get; set; } = 4000;
}

public class ProjectDatabaseConfig
{
    /// <summary>データベース種別："sqlite" / "sqlserver" / "postgresql" / "mysql"</summary>
    public string Type { get; set; } = "sqlite";
    /// <summary>SQLite DB ファイルのパス（project.yaml からの相対パス）</summary>
    public string? Path { get; set; }
    /// <summary>SQL Server 用接続文字列</summary>
    public string? ConnectionString { get; set; }
}

public class ProjectFeaturesConfig
{
    public bool MultiLanguage { get; set; } = true;
    public bool UserAuthentication { get; set; } = true;
}

/// <summary>
/// プロジェクト固有のレイアウト設定。
/// </summary>
public class ProjectLayoutConfig
{
    /// <summary>
    /// 使用するレイアウトビューのパス（Views/Shared/_Layout.cshtml からの相対パス、または絶対パス）。
    /// 例："views/_Layout.cshtml"（プロジェクト内ビュー）, "_Layout.cshtml"（標準）
    /// </summary>
    public string? View { get; set; }

    /// <summary>
    /// ダッシュボードのビジュアルテーマ。
    /// 使用可能な値: default | minimal | analytics | ops | workspace
    /// </summary>
    public string DashboardTheme { get; set; } = "default";
    
    /// <summary>
    /// サイドバーに表示するナビゲーション項目のカスタマイズ。
    /// </summary>
    public ProjectNavigationConfig? Navigation { get; set; }

    /// <summary>
    /// ロール別ランディングページ（URL）。ログイン後の初回リダイレクト先をロールごとに設定。
    /// Key: AppUserRole.RoleName, Value: リダイレクト先 URL。
    /// </summary>
    public Dictionary<string, string> LandingPageByRole { get; set; } = new();

    /// <summary>
    /// ヘッダーのカスタマイズ。
    /// </summary>
    public ProjectHeaderConfig? Header { get; set; }
    
    /// <summary>
    /// フッターのカスタマイズ。
    /// </summary>
    public ProjectFooterConfig? Footer { get; set; }
}

/// <summary>
/// プロジェクト固有のナビゲーション設定。
/// </summary>
public class ProjectNavigationConfig
{
    /// <summary>
    /// サイドバーに表示するエンティティのリスト（指定がない場合はすべての公開エンティティを表示）。
    /// </summary>
    public List<string> Entities { get; set; } = new();
    
    /// <summary>
    /// カスタムナビゲーション項目。
    /// </summary>
    public List<ProjectNavigationItemConfig> Items { get; set; } = new();
    
    /// <summary>
    /// ダッシュボードリンクを表示するかどうか（デフォルト：true）。
    /// </summary>
    public bool ShowDashboard { get; set; } = true;

    /// <summary>
    /// ページセクション（自動生成）を表示するかどうか（デフォルト：true）。
    /// </summary>
    public bool ShowPages { get; set; } = true;

    /// <summary>
    /// エンティティセクション（自動生成）を表示するかどうか（デフォルト：true）。
    /// </summary>
    public bool ShowEntities { get; set; } = true;
}

/// <summary>
/// ナビゲーション項目の設定。
/// </summary>
public class ProjectNavigationItemConfig
{
    /// <summary>表示ラベル</summary>
    public string? Label { get; set; }
    public string? LabelKey { get; set; }
    public string? Section { get; set; }
    
    /// <summary>多言語ラベル（省略可）</summary>
    public Dictionary<string, string>? LabelI18n { get; set; }
    
    /// <summary>リンク先 URL（asp-controller/asp-action の代わりに使用）</summary>
    public string? Url { get; set; }
    
    /// <summary>コントローラー名（省略可）</summary>
    public string? Controller { get; set; }
    
    /// <summary>アクション名（省略可）</summary>
    public string? Action { get; set; }
    
    /// <summary>アイコン（絵文字または CSS クラス）</summary>
    public string? Icon { get; set; }
    
    /// <summary>Admin ロールのみ表示（デフォルト：false）</summary>
    public bool AdminOnly { get; set; }

    /// <summary>区切り線を表示するかどうか</summary>
    public bool Divider { get; set; }

    /// <summary>子項目（階層メニュー用）</summary>
    public List<ProjectNavigationItemConfig> Children { get; set; } = new();

    /// <summary>
    /// この項目を表示するカスタムロールのリスト（AppUserRole.RoleName と照合）。
    /// 空の場合は全ユーザーに表示。AdminOnly=true の場合は無視。
    /// </summary>
    public List<string> Roles { get; set; } = new();
}

/// <summary>
/// プロジェクト固有のヘッダー設定。
/// </summary>
public class ProjectHeaderConfig
{
    /// <summary>ヘッダータイトル（デフォルト：プロジェクト名）</summary>
    public string? Title { get; set; }
    
    /// <summary>ロゴ画像のパス（省略可）</summary>
    public string? Logo { get; set; }
    
    /// <summary>カスタムヘッダー要素（HTML）</summary>
    public string? CustomHtml { get; set; }
}

/// <summary>
/// プロジェクト固有のフッター設定。
/// </summary>
public class ProjectFooterConfig
{
    /// <summary>フッターテキスト（デフォルト：著作権表示）</summary>
    public string? Text { get; set; }
    
    /// <summary>カスタムフッター要素（HTML）</summary>
    public string? CustomHtml { get; set; }
}
