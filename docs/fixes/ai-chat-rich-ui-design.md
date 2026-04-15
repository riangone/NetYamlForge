# AIチャット リッチUI設計書

## 概要

AIチャットアシスタントの回答UIを改善し、ユーザーが文字を打つ量を最小化する。
テキスト回答に加え、ボタン・選択肢・カルーセル等のインタラクティブ要素を追加する。

---

## 現状の問題点

| 問題 | 影響 |
|------|------|
| 全入力がテキスト | 「はい/いいえ」でも文字を打つ必要がある |
| QuickReply は文字列配列のみ | ラベルと実行値が同一、アイコンなし |
| 車両一覧がテキストテーブル | 画像なし、比較しにくい |
| 日時入力がテキスト | フォーマットミスが発生しやすい |
| フィルタ条件がテキスト | 「価格100〜300万」のような範囲指定が難しい |

---

## 設計方針

1. **段階的実装** — 既存の `quickReplies` と `dataRows` を拡張し、新型 `components` 配列を追加
2. **後方互換** — `quickReplies` (string[]) は今後も動作させる
3. **コンポーネント駆動** — バックエンドは JSON でコンポーネント定義を返し、フロントエンドがレンダリング
4. **送信値とラベルを分離** — ユーザーには日本語ラベル、サーバーには機械可読な値を送信

---

## バックエンド変更

### 1. UIComponent モデル（新規）

**ファイル**: `NetYamlForge/Models/AI/UiComponents.cs`

```csharp
/// <summary>AIチャット返答に含めるUIコンポーネントの共通基底</summary>
public abstract record UiComponent(string Type);

/// クイック返信ボタン群（既存 quickReplies を置き換え）
public record QuickReplyGroup(
    List<QuickReplyItem> Items,
    bool Dismissible = true           // クリック後にボタンを非活性化するか
) : UiComponent("quick_reply_group");

public record QuickReplyItem(
    string Label,                     // 表示テキスト
    string Value,                     // サーバーへ送信する値
    string? Icon = null,              // emoji or Material Icon名
    string? Style = null              // primary | danger | success | default
);

/// 単一選択（ラジオ相当）
public record SingleSelectGroup(
    string Title,
    List<SelectOption> Options,
    string SubmitLabel = "選択"
) : UiComponent("single_select");

/// 複数選択（チェックボックス相当）
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

/// 日時ピッカー
public record DateTimePicker(
    string Title,
    string Mode,                       // date | time | datetime
    string? MinDate = null,            // ISO8601
    string? MaxDate = null,
    string SubmitLabel = "確定"
) : UiComponent("datetime_picker");

/// 数値スライダー（価格帯・距離・年式等）
public record RangeSlider(
    string Title,
    double Min,
    double Max,
    double? DefaultMin = null,
    double? DefaultMax = null,
    double Step = 1,
    string? Unit = null,               // "万円" "km" "年"
    string SubmitLabel = "適用"
) : UiComponent("range_slider");

/// カード型カルーセル（車両一覧等）
public record CardCarousel(
    List<CardItem> Items,
    string? Title = null
) : UiComponent("card_carousel");

public record CardItem(
    string Id,
    string Title,
    string? Subtitle = null,
    string? ImageUrl = null,
    string? BadgeLabel = null,         // "在庫あり" "試乗車"
    string? BadgeStyle = null,         // success | warning | danger
    List<CardAction>? Actions = null
);

public record CardAction(
    string Label,
    string ActionType,                 // postback | url
    string Value
);

/// 確認ダイアログ（はい/いいえ）
public record ConfirmPrompt(
    string Question,
    string ConfirmLabel = "はい",
    string CancelLabel = "いいえ",
    string ConfirmValue = "yes",
    string CancelValue = "no",
    string? Style = null               // danger（赤確認）| default
) : UiComponent("confirm");

/// 評価ウィジェット（星・絵文字）
public record RatingWidget(
    string Title,
    int MaxStars = 5,
    string? SubmitLabel = "送信"
) : UiComponent("rating");

/// テキスト入力補助（ひな形提示）
public record TextSuggestions(
    string Placeholder,
    List<string> Suggestions           // クリックで入力欄にセット
) : UiComponent("text_suggestions");
```

### 2. SendMessageResponse 拡張

**ファイル**: `NetYamlForge/Models/AI/AIWindowRequests.cs`（既存クラスを修正）

```csharp
public class SendMessageResponse
{
    // --- 既存フィールド（変更なし）---
    public string ResponseText { get; set; } = "";
    public string? AiModel { get; set; }
    public List<string>? QuickReplies { get; set; }     // 後方互換で残す
    public List<Dictionary<string, string>>? DataRows { get; set; }
    public string? NavigationUrl { get; set; }
    public string? NavigationLabel { get; set; }
    public string? ConversationId { get; set; }

    // --- 新規フィールド ---
    /// <summary>構造化UIコンポーネント。quickReplies より優先して表示する</summary>
    public List<UiComponent>? Components { get; set; }
}
```

### 3. BaseChatService — コンポーネント生成ヘルパー

**ファイル**: `NetYamlForge/Services/AI/BaseChatService.cs`

```csharp
// インテントに応じたコンポーネント生成の例
protected virtual List<UiComponent>? BuildComponents(
    string intent, 
    List<Dictionary<string, string>>? dataRows,
    string? context)
{
    return intent switch
    {
        // 車両検索結果 → カルーセル
        "vehicle_search" when dataRows?.Count > 0 => new List<UiComponent>
        {
            new CardCarousel(
                Title: "検索結果",
                Items: dataRows.Select(r => new CardItem(
                    Id: r.GetValueOrDefault("id", ""),
                    Title: r.GetValueOrDefault("name", ""),
                    Subtitle: $"¥{r.GetValueOrDefault("price", "")}万",
                    BadgeLabel: r.GetValueOrDefault("status", ""),
                    Actions: new List<CardAction>
                    {
                        new("詳細を見る", "postback", $"車両ID {r["id"]} の詳細を教えて"),
                        new("試乗予約", "postback", $"車両ID {r["id"]} を試乗予約したい"),
                    }
                )).ToList()
            )
        },

        // 試乗・来店予約 → 日時ピッカー
        "appointment_booking" => new List<UiComponent>
        {
            new DateTimePicker(
                Title: "ご希望の日時を選択してください",
                Mode: "datetime",
                MinDate: DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"),
                MaxDate: DateTime.Today.AddMonths(2).ToString("yyyy-MM-dd")
            )
        },

        // 価格帯絞り込み → レンジスライダー
        "price_filter" => new List<UiComponent>
        {
            new RangeSlider(
                Title: "ご予算の範囲を選択してください",
                Min: 50, Max: 1000, Step: 10, Unit: "万円",
                SubmitLabel: "この価格帯で探す"
            )
        },

        // YES/NO 確認
        "confirm_booking" => new List<UiComponent>
        {
            new ConfirmPrompt(
                Question: "この内容で予約を確定しますか？",
                ConfirmLabel: "はい、確定します",
                CancelLabel: "いいえ、変更する"
            )
        },

        // 複数選択（メーカー選択等）
        "brand_selection" => new List<UiComponent>
        {
            new MultiSelectGroup(
                Title: "ご希望のメーカーを選択してください（複数可）",
                Options: new List<SelectOption>
                {
                    new("トヨタ",  "toyota",  Icon: "🚗"),
                    new("ホンダ",  "honda",   Icon: "🚗"),
                    new("日産",    "nissan",  Icon: "🚗"),
                    new("スバル",  "subaru",  Icon: "🚗"),
                    new("マツダ",  "mazda",   Icon: "🚗"),
                    new("三菱",    "mitsubishi", Icon: "🚗"),
                },
                SubmitLabel: "このメーカーで探す",
                Min: 1
            )
        },

        _ => null
    };
}
```

### 4. AutoDealerChatService — QuickReply の充実

**ファイル**: `NetYamlForge/Services/AI/AutoDealerChatService.cs`

```csharp
protected override List<string> GetCustomerQuickReplies(string intent) => intent switch
{
    "greeting" => new()
    {
        "在庫車両を探したい",
        "試乗予約をしたい",
        "車の下取り査定",
        "ローン・支払い相談"
    },
    "vehicle_search" => new()
    {
        "価格帯で絞り込む",
        "メーカーで絞り込む",
        "SUVだけ見たい",
        "試乗できる車を見たい"
    },
    "appointment_booking" => new()
    {
        "今週末に予約したい",
        "来週以降で希望を出す",
        "電話で予約する"
    },
    _ => new() { "車両を探す", "試乗予約", "お問い合わせ" }
};
```

---

## フロントエンド変更

### 1. コンポーネントレンダラー

**ファイル**: `NetYamlForge/wwwroot/js/ai-chat-components.js`（新規）

```javascript
/**
 * AIチャット リッチUIコンポーネント レンダラー
 * 
 * バックエンドから返却された components 配列を DOM に変換する。
 * 各コンポーネントはユーザー操作後に自動で非活性化（dismiss）される。
 */

const AiChatComponents = (() => {

  /**
   * コンポーネント配列を DOM 要素に変換して返す
   * @param {Array} components  - UiComponent の配列
   * @param {Function} onSubmit - ユーザー入力確定時コールバック (value: string) => void
   * @returns {HTMLElement}
   */
  function render(components, onSubmit) {
    const wrapper = document.createElement('div');
    wrapper.className = 'aic-components';

    for (const comp of components) {
      const el = renderOne(comp, onSubmit);
      if (el) wrapper.appendChild(el);
    }
    return wrapper;
  }

  function renderOne(comp, onSubmit) {
    switch (comp.type) {
      case 'quick_reply_group': return renderQuickReplyGroup(comp, onSubmit);
      case 'single_select':     return renderSingleSelect(comp, onSubmit);
      case 'multi_select':      return renderMultiSelect(comp, onSubmit);
      case 'datetime_picker':   return renderDateTimePicker(comp, onSubmit);
      case 'range_slider':      return renderRangeSlider(comp, onSubmit);
      case 'card_carousel':     return renderCardCarousel(comp, onSubmit);
      case 'confirm':           return renderConfirm(comp, onSubmit);
      case 'rating':            return renderRating(comp, onSubmit);
      case 'text_suggestions':  return renderTextSuggestions(comp, onSubmit);
      default: return null;
    }
  }

  /** クイック返信ボタン群 */
  function renderQuickReplyGroup(comp, onSubmit) {
    const div = document.createElement('div');
    div.className = 'aic-qr-group';

    for (const item of comp.items) {
      const btn = document.createElement('button');
      btn.className = `aic-qr-btn ${item.style || 'default'}`;
      btn.innerHTML = (item.icon ? `<span class="aic-icon">${item.icon}</span>` : '')
                    + `<span>${escHtml(item.label)}</span>`;
      btn.addEventListener('click', () => {
        if (comp.dismissible !== false) dismissGroup(div);
        onSubmit(item.value);
      });
      div.appendChild(btn);
    }
    return div;
  }

  /** 単一選択 (ラジオ相当) */
  function renderSingleSelect(comp, onSubmit) {
    const div = document.createElement('div');
    div.className = 'aic-select-group';
    div.innerHTML = `<p class="aic-select-title">${escHtml(comp.title)}</p>`;

    let selected = null;
    const optEls = [];

    for (const opt of comp.options) {
      const label = document.createElement('label');
      label.className = 'aic-radio-label';
      const input = document.createElement('input');
      input.type = 'radio';
      input.name = `aic_single_${Date.now()}`;
      input.value = opt.value;
      input.addEventListener('change', () => { selected = opt.value; });
      label.appendChild(input);
      if (opt.icon) label.insertAdjacentHTML('beforeend', `<span class="aic-icon">${opt.icon}</span>`);
      label.insertAdjacentHTML('beforeend', `<span class="aic-opt-label">${escHtml(opt.label)}</span>`);
      if (opt.description) {
        label.insertAdjacentHTML('beforeend', `<small class="aic-opt-desc">${escHtml(opt.description)}</small>`);
      }
      div.appendChild(label);
      optEls.push(label);
    }

    const submitBtn = document.createElement('button');
    submitBtn.className = 'aic-submit-btn';
    submitBtn.textContent = comp.submitLabel || '選択';
    submitBtn.addEventListener('click', () => {
      if (!selected) return;
      const opt = comp.options.find(o => o.value === selected);
      dismissGroup(div);
      onSubmit(opt?.label || selected);
    });
    div.appendChild(submitBtn);
    return div;
  }

  /** 複数選択 (チェックボックス) */
  function renderMultiSelect(comp, onSubmit) {
    const div = document.createElement('div');
    div.className = 'aic-select-group';
    div.innerHTML = `<p class="aic-select-title">${escHtml(comp.title)}</p>`;
    if (comp.min || comp.max) {
      const hint = comp.min && comp.max ? `${comp.min}〜${comp.max}個選択`
                 : comp.min             ? `${comp.min}個以上選択`
                                        : `${comp.max}個まで選択`;
      div.insertAdjacentHTML('beforeend', `<p class="aic-select-hint">${hint}</p>`);
    }

    const selected = new Set();

    for (const opt of comp.options) {
      const label = document.createElement('label');
      label.className = 'aic-check-label';
      const input = document.createElement('input');
      input.type = 'checkbox';
      input.value = opt.value;
      input.addEventListener('change', e => {
        if (e.target.checked) selected.add(opt.value);
        else selected.delete(opt.value);
        // max 制約
        if (comp.max && selected.size >= comp.max) {
          div.querySelectorAll('input[type=checkbox]:not(:checked)').forEach(el => {
            el.disabled = true;
          });
        } else {
          div.querySelectorAll('input[type=checkbox]').forEach(el => { el.disabled = false; });
        }
        submitBtn.disabled = comp.min ? selected.size < comp.min : false;
      });
      label.appendChild(input);
      if (opt.icon) label.insertAdjacentHTML('beforeend', `<span class="aic-icon">${opt.icon}</span>`);
      label.insertAdjacentHTML('beforeend', `<span>${escHtml(opt.label)}</span>`);
      div.appendChild(label);
    }

    const submitBtn = document.createElement('button');
    submitBtn.className = 'aic-submit-btn';
    submitBtn.textContent = comp.submitLabel || '送信';
    submitBtn.disabled = !!(comp.min);
    submitBtn.addEventListener('click', () => {
      if (selected.size === 0) return;
      const labels = comp.options
        .filter(o => selected.has(o.value))
        .map(o => o.label).join('、');
      dismissGroup(div);
      onSubmit(labels);
    });
    div.appendChild(submitBtn);
    return div;
  }

  /** 日時ピッカー */
  function renderDateTimePicker(comp, onSubmit) {
    const div = document.createElement('div');
    div.className = 'aic-datetime-picker';
    div.innerHTML = `<p class="aic-select-title">${escHtml(comp.title)}</p>`;

    const inputType = comp.mode === 'date' ? 'date'
                    : comp.mode === 'time' ? 'time'
                    : 'datetime-local';
    const input = document.createElement('input');
    input.type = inputType;
    if (comp.minDate) input.min = comp.minDate;
    if (comp.maxDate) input.max = comp.maxDate;
    input.className = 'aic-date-input';
    div.appendChild(input);

    const submitBtn = document.createElement('button');
    submitBtn.className = 'aic-submit-btn';
    submitBtn.textContent = comp.submitLabel || '確定';
    submitBtn.addEventListener('click', () => {
      if (!input.value) return;
      const formatted = formatDateTime(input.value, comp.mode);
      dismissGroup(div);
      onSubmit(formatted);
    });
    div.appendChild(submitBtn);
    return div;
  }

  /** レンジスライダー */
  function renderRangeSlider(comp, onSubmit) {
    const div = document.createElement('div');
    div.className = 'aic-range-slider';
    div.innerHTML = `<p class="aic-select-title">${escHtml(comp.title)}</p>`;

    const unit = comp.unit || '';
    let lo = comp.defaultMin ?? comp.min;
    let hi = comp.defaultMax ?? comp.max;

    const display = document.createElement('div');
    display.className = 'aic-range-display';
    const updateDisplay = () => {
      display.textContent = `${lo}${unit} 〜 ${hi}${unit}`;
    };
    updateDisplay();
    div.appendChild(display);

    // 最小値スライダー
    const sliderMin = makeSlider(comp.min, comp.max, lo, comp.step, val => {
      lo = Math.min(val, hi - comp.step);
      sliderMin.value = lo;
      updateDisplay();
    });
    // 最大値スライダー
    const sliderMax = makeSlider(comp.min, comp.max, hi, comp.step, val => {
      hi = Math.max(val, lo + comp.step);
      sliderMax.value = hi;
      updateDisplay();
    });
    div.appendChild(sliderMin);
    div.appendChild(sliderMax);

    const submitBtn = document.createElement('button');
    submitBtn.className = 'aic-submit-btn';
    submitBtn.textContent = comp.submitLabel || '適用';
    submitBtn.addEventListener('click', () => {
      dismissGroup(div);
      onSubmit(`${lo}${unit}から${hi}${unit}の範囲で探して`);
    });
    div.appendChild(submitBtn);
    return div;
  }

  /** カードカルーセル */
  function renderCardCarousel(comp, onSubmit) {
    const div = document.createElement('div');
    div.className = 'aic-carousel';
    if (comp.title) div.innerHTML = `<p class="aic-carousel-title">${escHtml(comp.title)}</p>`;

    const track = document.createElement('div');
    track.className = 'aic-carousel-track';

    for (const item of comp.items) {
      const card = document.createElement('div');
      card.className = 'aic-card';

      if (item.imageUrl) {
        const img = document.createElement('img');
        img.src = item.imageUrl;
        img.alt = item.title;
        img.className = 'aic-card-img';
        card.appendChild(img);
      }

      if (item.badgeLabel) {
        const badge = document.createElement('span');
        badge.className = `aic-card-badge ${item.badgeStyle || 'default'}`;
        badge.textContent = item.badgeLabel;
        card.appendChild(badge);
      }

      card.insertAdjacentHTML('beforeend', `
        <div class="aic-card-body">
          <strong class="aic-card-title">${escHtml(item.title)}</strong>
          ${item.subtitle ? `<span class="aic-card-subtitle">${escHtml(item.subtitle)}</span>` : ''}
        </div>
      `);

      if (item.actions?.length) {
        const actionsDiv = document.createElement('div');
        actionsDiv.className = 'aic-card-actions';
        for (const action of item.actions) {
          if (action.actionType === 'url') {
            const a = document.createElement('a');
            a.href = action.value;
            a.target = '_blank';
            a.className = 'aic-card-action-btn';
            a.textContent = action.label;
            actionsDiv.appendChild(a);
          } else {
            const btn = document.createElement('button');
            btn.className = 'aic-card-action-btn';
            btn.textContent = action.label;
            btn.addEventListener('click', () => {
              onSubmit(action.value);
            });
            actionsDiv.appendChild(btn);
          }
        }
        card.appendChild(actionsDiv);
      }

      track.appendChild(card);
    }

    div.appendChild(track);

    // スクロールナビ（2枚以上の場合）
    if (comp.items.length > 1) {
      const nav = document.createElement('div');
      nav.className = 'aic-carousel-nav';
      const prevBtn = document.createElement('button');
      prevBtn.textContent = '‹';
      prevBtn.className = 'aic-carousel-prev';
      const nextBtn = document.createElement('button');
      nextBtn.textContent = '›';
      nextBtn.className = 'aic-carousel-next';
      prevBtn.addEventListener('click', () => { track.scrollBy({ left: -220, behavior: 'smooth' }); });
      nextBtn.addEventListener('click', () => { track.scrollBy({ left: 220, behavior: 'smooth' }); });
      nav.appendChild(prevBtn);
      nav.appendChild(nextBtn);
      div.appendChild(nav);
    }

    return div;
  }

  /** 確認ダイアログ */
  function renderConfirm(comp, onSubmit) {
    const div = document.createElement('div');
    div.className = 'aic-confirm';
    div.innerHTML = `<p class="aic-confirm-question">${escHtml(comp.question)}</p>`;

    const btnRow = document.createElement('div');
    btnRow.className = 'aic-confirm-btns';

    const confirmBtn = document.createElement('button');
    confirmBtn.className = `aic-confirm-btn confirm ${comp.style || 'default'}`;
    confirmBtn.textContent = comp.confirmLabel || 'はい';
    confirmBtn.addEventListener('click', () => {
      dismissGroup(div);
      onSubmit(comp.confirmValue || 'yes');
    });

    const cancelBtn = document.createElement('button');
    cancelBtn.className = 'aic-confirm-btn cancel';
    cancelBtn.textContent = comp.cancelLabel || 'いいえ';
    cancelBtn.addEventListener('click', () => {
      dismissGroup(div);
      onSubmit(comp.cancelValue || 'no');
    });

    btnRow.appendChild(confirmBtn);
    btnRow.appendChild(cancelBtn);
    div.appendChild(btnRow);
    return div;
  }

  /** 星評価 */
  function renderRating(comp, onSubmit) {
    const div = document.createElement('div');
    div.className = 'aic-rating';
    div.innerHTML = `<p class="aic-select-title">${escHtml(comp.title)}</p>`;

    const stars = document.createElement('div');
    stars.className = 'aic-stars';
    let selected = 0;

    for (let i = 1; i <= (comp.maxStars || 5); i++) {
      const star = document.createElement('span');
      star.className = 'aic-star';
      star.textContent = '★';
      star.dataset.val = i;
      star.addEventListener('mouseover', () => {
        stars.querySelectorAll('.aic-star').forEach(s => {
          s.classList.toggle('hover', Number(s.dataset.val) <= i);
        });
      });
      star.addEventListener('mouseleave', () => {
        stars.querySelectorAll('.aic-star').forEach(s => s.classList.remove('hover'));
      });
      star.addEventListener('click', () => {
        selected = i;
        stars.querySelectorAll('.aic-star').forEach(s => {
          s.classList.toggle('active', Number(s.dataset.val) <= i);
        });
      });
      stars.appendChild(star);
    }
    div.appendChild(stars);

    const submitBtn = document.createElement('button');
    submitBtn.className = 'aic-submit-btn';
    submitBtn.textContent = comp.submitLabel || '送信';
    submitBtn.addEventListener('click', () => {
      if (!selected) return;
      dismissGroup(div);
      onSubmit(`${selected}点（${comp.maxStars || 5}点満点）`);
    });
    div.appendChild(submitBtn);
    return div;
  }

  /** テキスト入力補助 */
  function renderTextSuggestions(comp, onSubmit) {
    // このコンポーネントは入力フォームを置き換えず、サジェストのみ表示
    const div = document.createElement('div');
    div.className = 'aic-text-suggestions';
    for (const s of comp.suggestions) {
      const chip = document.createElement('button');
      chip.className = 'aic-suggestion-chip';
      chip.textContent = s;
      chip.addEventListener('click', () => {
        // チャット入力欄にセット（送信はしない）
        const inputEl = document.getElementById('aw-input')
                      || document.getElementById('ai-query-input');
        if (inputEl) {
          inputEl.value = s;
          inputEl.focus();
        }
      });
      div.appendChild(chip);
    }
    return div;
  }

  // ---- ユーティリティ ----

  function dismissGroup(el) {
    el.classList.add('aic-dismissed');
    el.querySelectorAll('button, input, select').forEach(e => { e.disabled = true; });
  }

  function escHtml(str) {
    return String(str)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;')
      .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function makeSlider(min, max, val, step, onChange) {
    const s = document.createElement('input');
    s.type = 'range';
    s.min = min; s.max = max; s.value = val; s.step = step || 1;
    s.className = 'aic-slider';
    s.addEventListener('input', e => onChange(Number(e.target.value)));
    return s;
  }

  function formatDateTime(value, mode) {
    if (mode === 'date') {
      const d = new Date(value);
      return `${d.getFullYear()}年${d.getMonth()+1}月${d.getDate()}日`;
    }
    if (mode === 'time') return value;
    const d = new Date(value);
    return `${d.getFullYear()}年${d.getMonth()+1}月${d.getDate()}日 ${d.getHours()}時${String(d.getMinutes()).padStart(2,'0')}分`;
  }

  return { render };
})();
```

### 2. ai-chat-widget.js への組み込み

**ファイル**: `NetYamlForge/wwwroot/js/ai-chat-widget.js`（既存ファイルの `addMessageRow` 関数を修正）

```javascript
// 変更箇所のみ抜粋

function addMessageRow(content, role, extra) {
    const rendered = renderMarkdown(content);
    const row = document.createElement('div');
    row.className = `aw-row ${role}`;
    row.innerHTML = `<div class="aw-msg ${role}">${rendered}</div>`;

    // ---- 新規: 構造化コンポーネントを優先表示 ----
    if (role === 'assistant' && extra?.components?.length) {
        const compEl = AiChatComponents.render(extra.components, (value) => {
            // コンポーネント操作をメッセージとして送信
            const inputEl = document.getElementById('aw-input');
            if (inputEl) inputEl.value = value;
            sendMessage();
        });
        row.appendChild(compEl);
    }
    // ---- 後方互換: 旧 quickReplies ----
    else if (extra?.quickReplies?.length) {
        const qr = document.createElement('div');
        qr.className = 'aw-quick-replies';
        extra.quickReplies.forEach(r => {
            const btn = document.createElement('button');
            btn.className = 'aw-quick-btn';
            btn.textContent = r;
            btn.onclick = () => {
                document.getElementById('aw-input').value = r;
                sendMessage();
            };
            qr.appendChild(btn);
        });
        row.appendChild(qr);
    }

    // データテーブル (既存・変更なし)
    if (extra?.dataRows?.length) { /* ... 既存コード ... */ }

    // ナビリンク (既存・変更なし)
    if (extra?.navigationUrl) { /* ... 既存コード ... */ }

    panel.querySelector('.aw-messages').appendChild(row);
    row.scrollIntoView({ behavior: 'smooth' });
}
```

### 3. CSS スタイルシート

**ファイル**: `NetYamlForge/wwwroot/css/ai-chat-components.css`（新規）

```css
/* ============================================
   AIチャット リッチUIコンポーネント スタイル
   ============================================ */

/* ---- 共通 ---- */
.aic-components { display: flex; flex-direction: column; gap: 8px; margin-top: 8px; }
.aic-dismissed { opacity: 0.45; pointer-events: none; }
.aic-select-title { font-size: 0.85rem; font-weight: 600; color: #444; margin: 0 0 6px; }
.aic-select-hint  { font-size: 0.75rem; color: #888; margin: 0 0 8px; }
.aic-submit-btn {
  margin-top: 8px; padding: 8px 18px;
  background: #2563eb; color: #fff;
  border: none; border-radius: 8px; cursor: pointer; font-size: 0.85rem;
  transition: background 0.15s;
}
.aic-submit-btn:hover { background: #1d4ed8; }
.aic-submit-btn:disabled { background: #93c5fd; cursor: not-allowed; }
.aic-icon { margin-right: 4px; }

/* ---- クイックリプライ ---- */
.aic-qr-group { display: flex; flex-wrap: wrap; gap: 6px; }
.aic-qr-btn {
  padding: 6px 14px; border-radius: 20px; border: 1.5px solid #2563eb;
  background: #fff; color: #2563eb; cursor: pointer; font-size: 0.82rem;
  transition: background 0.15s, color 0.15s;
}
.aic-qr-btn:hover { background: #2563eb; color: #fff; }
.aic-qr-btn.primary { background: #2563eb; color: #fff; }
.aic-qr-btn.danger  { border-color: #dc2626; color: #dc2626; }
.aic-qr-btn.danger:hover { background: #dc2626; color: #fff; }
.aic-qr-btn.success { border-color: #16a34a; color: #16a34a; }
.aic-qr-btn.success:hover { background: #16a34a; color: #fff; }

/* ---- 選択系 ---- */
.aic-select-group { background: #f8fafc; border-radius: 10px; padding: 12px; }
.aic-radio-label, .aic-check-label {
  display: flex; align-items: center; gap: 8px;
  padding: 6px 4px; cursor: pointer; font-size: 0.85rem;
  border-radius: 6px; transition: background 0.12s;
}
.aic-radio-label:hover, .aic-check-label:hover { background: #e0f2fe; }
.aic-opt-desc { display: block; font-size: 0.75rem; color: #888; margin-left: auto; }

/* ---- 日時ピッカー ---- */
.aic-datetime-picker { background: #f8fafc; border-radius: 10px; padding: 12px; }
.aic-date-input {
  width: 100%; padding: 8px 10px; border: 1.5px solid #cbd5e1;
  border-radius: 8px; font-size: 0.85rem; color: #222;
}

/* ---- レンジスライダー ---- */
.aic-range-slider { background: #f8fafc; border-radius: 10px; padding: 12px; }
.aic-range-display { font-size: 0.9rem; font-weight: 600; color: #2563eb; text-align: center; margin-bottom: 8px; }
.aic-slider { width: 100%; accent-color: #2563eb; margin: 4px 0; }

/* ---- カルーセル ---- */
.aic-carousel { position: relative; }
.aic-carousel-title { font-size: 0.85rem; font-weight: 600; color: #444; margin-bottom: 8px; }
.aic-carousel-track {
  display: flex; gap: 12px; overflow-x: auto; padding-bottom: 8px;
  scroll-snap-type: x mandatory;
  -ms-overflow-style: none; scrollbar-width: none;
}
.aic-carousel-track::-webkit-scrollbar { display: none; }
.aic-card {
  flex: 0 0 200px; background: #fff; border-radius: 12px;
  border: 1px solid #e2e8f0; overflow: hidden;
  box-shadow: 0 1px 4px rgba(0,0,0,.06); scroll-snap-align: start;
  transition: box-shadow 0.15s;
}
.aic-card:hover { box-shadow: 0 4px 12px rgba(37,99,235,.15); }
.aic-card-img { width: 100%; height: 120px; object-fit: cover; }
.aic-card-badge {
  position: absolute; top: 8px; right: 8px;
  padding: 2px 8px; border-radius: 10px; font-size: 0.7rem; font-weight: 600;
}
.aic-card-badge.success { background: #dcfce7; color: #15803d; }
.aic-card-badge.warning { background: #fef9c3; color: #a16207; }
.aic-card-badge.danger  { background: #fee2e2; color: #b91c1c; }
.aic-card-body { padding: 8px 10px; }
.aic-card-title { font-size: 0.82rem; font-weight: 600; display: block; }
.aic-card-subtitle { font-size: 0.78rem; color: #2563eb; font-weight: 600; margin-top: 2px; display: block; }
.aic-card-actions { display: flex; flex-direction: column; gap: 4px; padding: 0 8px 8px; }
.aic-card-action-btn {
  padding: 5px 0; border: 1.5px solid #2563eb; border-radius: 6px;
  background: #fff; color: #2563eb; cursor: pointer; font-size: 0.75rem; text-align: center;
  text-decoration: none; transition: background 0.12s;
}
.aic-card-action-btn:hover { background: #2563eb; color: #fff; }
.aic-carousel-nav { display: flex; justify-content: flex-end; gap: 4px; margin-top: 4px; }
.aic-carousel-prev, .aic-carousel-next {
  width: 28px; height: 28px; border-radius: 50%;
  border: 1.5px solid #cbd5e1; background: #fff; cursor: pointer; font-size: 1rem;
  display: flex; align-items: center; justify-content: center;
  transition: background 0.12s;
}
.aic-carousel-prev:hover, .aic-carousel-next:hover { background: #f1f5f9; }

/* ---- 確認ダイアログ ---- */
.aic-confirm { background: #f8fafc; border-radius: 10px; padding: 12px; }
.aic-confirm-question { font-size: 0.87rem; font-weight: 500; margin: 0 0 10px; }
.aic-confirm-btns { display: flex; gap: 8px; }
.aic-confirm-btn {
  flex: 1; padding: 8px; border-radius: 8px; cursor: pointer;
  font-size: 0.83rem; border: none; transition: opacity 0.12s;
}
.aic-confirm-btn.confirm.default { background: #2563eb; color: #fff; }
.aic-confirm-btn.confirm.danger  { background: #dc2626; color: #fff; }
.aic-confirm-btn.cancel { background: #e2e8f0; color: #444; }
.aic-confirm-btn:hover { opacity: 0.85; }

/* ---- 星評価 ---- */
.aic-rating { background: #f8fafc; border-radius: 10px; padding: 12px; }
.aic-stars { display: flex; gap: 4px; font-size: 1.8rem; margin: 4px 0; }
.aic-star { cursor: pointer; color: #d1d5db; transition: color 0.1s; }
.aic-star.hover, .aic-star.active { color: #f59e0b; }

/* ---- テキストサジェスト ---- */
.aic-text-suggestions { display: flex; flex-wrap: wrap; gap: 6px; }
.aic-suggestion-chip {
  padding: 4px 12px; border-radius: 14px;
  border: 1px dashed #94a3b8; background: #f1f5f9;
  color: #475569; cursor: pointer; font-size: 0.78rem;
  transition: background 0.12s;
}
.aic-suggestion-chip:hover { background: #e0f2fe; border-color: #38bdf8; color: #0369a1; }
```

---

## HTML テンプレート変更

**ファイル**: `NetYamlForge/Views/Shared/_ChatWidgetBase.cshtml`（または各チャットLayoutファイル）

```html
<!-- CSS を追加 -->
<link rel="stylesheet" href="/css/ai-chat-components.css" />

<!-- JS を追加（ai-chat-widget.js より前に読み込む） -->
<script src="/js/ai-chat-components.js"></script>
```

---

## 実装優先順位

| フェーズ | コンポーネント | 難易度 | 効果 |
|---------|--------------|--------|------|
| Phase 1 | QuickReplyGroup（既存拡張） | 低 | 高 |
| Phase 1 | CardCarousel（車両一覧） | 中 | 高 |
| Phase 1 | ConfirmPrompt（YES/NO） | 低 | 高 |
| Phase 2 | SingleSelect / MultiSelect | 中 | 高 |
| Phase 2 | DateTimePicker | 低 | 中 |
| Phase 3 | RangeSlider（価格帯） | 中 | 中 |
| Phase 3 | RatingWidget | 低 | 中 |
| Phase 3 | TextSuggestions | 低 | 低 |

---

## 実装チェックリスト

### バックエンド
- [ ] `NetYamlForge/Models/AI/UiComponents.cs` 新規作成
- [ ] `SendMessageResponse` に `Components` プロパティ追加
- [ ] `BaseChatService.BuildComponents()` 抽象メソッド or virtualメソッド追加
- [ ] `AutoDealerChatService.BuildComponents()` — 車両検索・予約フロー実装
- [ ] `JpiereChatService.BuildComponents()` — 契約・承認フロー実装
- [ ] `AutoDealerChatController` / `JpiereChatController` のレスポンスに `components` を含める
- [ ] 既存 `QuickReplyButton` モデルとの整合性確認

### フロントエンド
- [ ] `wwwroot/js/ai-chat-components.js` 新規作成（上記コード）
- [ ] `wwwroot/css/ai-chat-components.css` 新規作成（上記CSS）
- [ ] `ai-chat-widget.js` の `addMessageRow` を修正（`components` 優先表示）
- [ ] 各チャットLayoutに CSS/JS の `<link>` / `<script>` タグ追加
- [ ] `auto-dealer-chat-widget.js` の `onSubmit` コールバック接続確認
- [ ] `jpiere-chat-widget.js` の `onSubmit` コールバック接続確認

### テスト
- [ ] `AutoDealerChatController` 単体テスト — `components` フィールドを含む応答確認
- [ ] 各コンポーネントの dismiss 動作確認（ダブル送信防止）
- [ ] モバイルレイアウト確認（カルーセルの横スクロール）
- [ ] 旧 `quickReplies` (string[]) の後方互換確認

---

## 補足：インテント→コンポーネント マッピング（AutoDealer）

| ユーザー発言 | 検出インテント | 返すコンポーネント |
|------------|--------------|-----------------|
| 「SUVを探して」「予算500万以内で」 | `vehicle_search` | CardCarousel（結果）+ QuickReplyGroup（絞り込み） |
| 「試乗したい」「来店予約したい」 | `appointment_booking` | DateTimePicker |
| 「メーカーで絞り込みたい」 | `brand_selection` | MultiSelectGroup |
| 「価格帯を教えて」「いくらの車がありますか」 | `price_filter` | RangeSlider |
| 「○○で予約確定してください」 | `confirm_booking` | ConfirmPrompt（danger） |
| 「サービスはどうでしたか」 | `survey` | RatingWidget |
| 「どんな情報を入力すればいい？」 | `help` | TextSuggestions |
