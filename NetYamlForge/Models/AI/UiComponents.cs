using System.Text.Json.Serialization;

namespace NetYamlForge.Models.AI;

/// <summary>AIチャット返答に含めるUIコンポーネントの共通基底</summary>
[JsonDerivedType(typeof(QuickReplyGroup), typeDiscriminator: "quick_reply_group")]
[JsonDerivedType(typeof(SingleSelectGroup), typeDiscriminator: "single_select")]
[JsonDerivedType(typeof(MultiSelectGroup), typeDiscriminator: "multi_select")]
[JsonDerivedType(typeof(DateTimePicker), typeDiscriminator: "datetime_picker")]
[JsonDerivedType(typeof(RangeSlider), typeDiscriminator: "range_slider")]
[JsonDerivedType(typeof(CardCarousel), typeDiscriminator: "card_carousel")]
[JsonDerivedType(typeof(ConfirmPrompt), typeDiscriminator: "confirm")]
[JsonDerivedType(typeof(RatingWidget), typeDiscriminator: "rating")]
[JsonDerivedType(typeof(TextSuggestions), typeDiscriminator: "text_suggestions")]
public abstract record UiComponent(string Type);

// ─────────────────────────────────────────────────────────
// クイックリプライ
// ─────────────────────────────────────────────────────────

/// <summary>クイック返信ボタン群（既存 quickReplies を置き換え）</summary>
public record QuickReplyGroup(
    List<QuickReplyItem> Items,
    bool Dismissible = true
) : UiComponent("quick_reply_group");

public record QuickReplyItem(
    string Label,
    string Value,
    string? Icon = null,
    string? Style = null
);

// ─────────────────────────────────────────────────────────
// 選択系
// ─────────────────────────────────────────────────────────

/// <summary>単一選択（ラジオ相当）</summary>
public record SingleSelectGroup(
    string Title,
    List<SelectOption> Options,
    string SubmitLabel = "選択"
) : UiComponent("single_select");

/// <summary>複数選択（チェックボックス相当）</summary>
public record MultiSelectGroup(
    string Title,
    List<SelectOption> Options,
    string SubmitLabel = "送信",
    int? Min = null,
    int? Max = null
) : UiComponent("multi_select");

public record SelectOption(
    string Label,
    string Value,
    string? Description = null,
    string? Icon = null
);

// ─────────────────────────────────────────────────────────
// 日時ピッカー
// ─────────────────────────────────────────────────────────

/// <summary>日時ピッカー</summary>
public record DateTimePicker(
    string Title,
    string Mode,
    string? MinDate = null,
    string? MaxDate = null,
    string SubmitLabel = "確定"
) : UiComponent("datetime_picker");

// ─────────────────────────────────────────────────────────
// レンジスライダー
// ─────────────────────────────────────────────────────────

/// <summary>数値スライダー（価格帯・距離・年式等）</summary>
public record RangeSlider(
    string Title,
    double Min,
    double Max,
    double? DefaultMin = null,
    double? DefaultMax = null,
    double Step = 1,
    string? Unit = null,
    string SubmitLabel = "適用"
) : UiComponent("range_slider");

// ─────────────────────────────────────────────────────────
// カードカルーセル
// ─────────────────────────────────────────────────────────

/// <summary>カード型カルーセル（車両一覧等）</summary>
public record CardCarousel(
    List<CardItem> Items,
    string? Title = null
) : UiComponent("card_carousel");

public record CardItem(
    string Id,
    string Title,
    string? Subtitle = null,
    string? ImageUrl = null,
    string? BadgeLabel = null,
    string? BadgeStyle = null,
    List<CardAction>? Actions = null
);

public record CardAction(
    string Label,
    string ActionType,
    string Value
);

// ─────────────────────────────────────────────────────────
// 確認ダイアログ
// ─────────────────────────────────────────────────────────

/// <summary>確認ダイアログ（はい/いいえ）</summary>
public record ConfirmPrompt(
    string Question,
    string ConfirmLabel = "はい",
    string CancelLabel = "いいえ",
    string ConfirmValue = "yes",
    string CancelValue = "no",
    string? Style = null
) : UiComponent("confirm");

// ─────────────────────────────────────────────────────────
// 評価ウィジェット
// ─────────────────────────────────────────────────────────

/// <summary>評価ウィジェット（星・絵文字）</summary>
public record RatingWidget(
    string Title,
    int MaxStars = 5,
    string? SubmitLabel = "送信"
) : UiComponent("rating");

// ─────────────────────────────────────────────────────────
// テキスト入力補助
// ─────────────────────────────────────────────────────────

/// <summary>テキスト入力補助（ひな形提示）</summary>
public record TextSuggestions(
    string Placeholder,
    List<string> Suggestions
) : UiComponent("text_suggestions");
