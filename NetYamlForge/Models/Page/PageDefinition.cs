

namespace NetYamlForge.Models;

/// <summary>カスタムページ定義</summary>
public class PageDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>メインテーブル（外部キー結合の基準）</summary>
    public string? MainTable { get; set; }
    /// <summary>カスタム Razor テンプレート名（拡張子なし）。未設定時は汎用 PageView.cshtml を使用。</summary>
    public string? Template { get; set; }
    /// <summary>サイドバーに自動表示するかどうか。デフォルト: true</summary>
    public bool ShowInSidebar { get; set; } = true;
    /// <summary>カレンダーUI向けの追加設定（pages/*.yaml の calendar_ui）。</summary>
    public CalendarUiDefinition? CalendarUi { get; set; }
    /// <summary>ページ末尾に挿入する JS ファイル URL リスト（pages/*.yaml の scripts）。</summary>
    public List<string> Scripts { get; set; } = new();
    public bool IsPublic { get; set; }
    public List<SectionDefinition> Sections { get; set; } = new();
}

public class CalendarUiDefinition
{
    public int MobileMonthCount { get; set; } = 1;
    public int DesktopMinMonthCount { get; set; } = 2;
    public int DesktopMaxMonthCount { get; set; } = 6;
    public bool ShowJapanHolidays { get; set; } = true;
    /// <summary>JP祝日ソース: hybrid | api | builtin</summary>
    public string? JapanHolidayProvider { get; set; }
    /// <summary>外部JP祝日API URLテンプレート（{year} を含む）</summary>
    public string? JapanHolidayApiUrlTemplate { get; set; }
    public int? JapanHolidayApiTimeoutMs { get; set; }
    public bool ShowChineseLunar { get; set; } = true;
    public List<CalendarHolidayDefinition> ThirdPartyHolidays { get; set; } = new();
    public List<CalendarHolidayDefinition> CustomHolidays { get; set; } = new();
}

public class CalendarHolidayDefinition
{
    public string Date { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Source { get; set; }
}
