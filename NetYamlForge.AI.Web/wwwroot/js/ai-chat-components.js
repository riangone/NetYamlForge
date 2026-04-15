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
      case 'card_carousel':     return renderCardCarousel(comp);
      case 'confirm':           return renderConfirm(comp, onSubmit);
      case 'rating':            return renderRating(comp, onSubmit);
      case 'text_suggestions':  return renderTextSuggestions(comp);
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
        onSubmit(item.value, item.label);
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
    }

    const submitBtn = document.createElement('button');
    submitBtn.className = 'aic-submit-btn';
    submitBtn.textContent = comp.submitLabel || '選択';
    submitBtn.addEventListener('click', () => {
      if (!selected) return;
      const opt = comp.options.find(o => o.value === selected);
      dismissGroup(div);
      onSubmit(opt?.value || selected, opt?.label || selected);
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
      onSubmit(formatted, formatted);
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
  function renderCardCarousel(comp) {
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
          const btn = document.createElement('button');
          btn.className = 'aic-card-action-btn';
          btn.textContent = action.label;
          btn.setAttribute('type', 'button'); // ✅ 防止表单提交
          
          // ✅ 添加调试日志
          btn.addEventListener('click', (e) => {
            e.preventDefault(); // ✅ 防止默认行为
            e.stopPropagation(); // ✅ 防止事件冒泡
            console.log('[AI Chat Components] Button clicked:', action.label, 'Value:', action.value);
            
            // カルーセル内のボタンは直接onSubmitを呼び出す
            if (typeof onSubmit === 'function') {
              console.log('[AI Chat Components] Calling onSubmit with:', action.value);
              onSubmit(action.value);
            } else {
              console.error('[AI Chat Components] onSubmit is not a function!');
            }
          });
          actionsDiv.appendChild(btn);
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
      onSubmit(comp.confirmValue || 'yes', comp.confirmLabel || 'はい');
    });

    const cancelBtn = document.createElement('button');
    cancelBtn.className = 'aic-confirm-btn cancel';
    cancelBtn.textContent = comp.cancelLabel || 'いいえ';
    cancelBtn.addEventListener('click', () => {
      dismissGroup(div);
      onSubmit(comp.cancelValue || 'no', comp.cancelLabel || 'いいえ');
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
  function renderTextSuggestions(comp) {
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
