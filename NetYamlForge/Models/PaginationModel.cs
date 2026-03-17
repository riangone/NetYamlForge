namespace NetYamlForge.Models;

/// <summary>ページネーション各ページリンクの情報。</summary>
public record PaginationPageLink(int Page, string HxUrl, bool IsCurrent);

/// <summary>
/// _Pagination.cshtml に渡すページネーション表示モデル。
/// URL構築は呼び出し元が行い、このモデルに格納する。
/// </summary>
public record PaginationModel
{
    /// <summary>"numbered" | "keyset"</summary>
    public string Mode { get; init; } = "numbered";
    public int CurrentPage { get; init; }
    public int TotalPages { get; init; }
    public bool HasMore { get; init; }
    public bool CountEnabled { get; init; } = true;
    public string HxTarget { get; init; } = "";
    /// <summary>hx-include セレクター（フィルターフォームなど）。null の場合は付与しない。</summary>
    public string? HxInclude { get; init; }
    public string HxSwap { get; init; } = "innerHTML";
    public string? FirstHxUrl { get; init; }
    public string? PrevHxUrl { get; init; }
    public IReadOnlyList<PaginationPageLink> PageLinks { get; init; } = Array.Empty<PaginationPageLink>();
    public string? NextHxUrl { get; init; }
    public string? LastHxUrl { get; init; }
}
