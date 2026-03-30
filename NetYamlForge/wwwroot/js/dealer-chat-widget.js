/**
 * dealer-chat-widget.js
 * 自動車ディーラー 統一AIチャットウィジェット
 *
 * 顧客モードと社員モードで同じUI構造・異なるテーマを提供します。
 *
 * 使用方法:
 *   <script src="/js/dealer-chat-widget.js"></script>
 *   <script>
 *     DealerChat.init({ mode: 'customer', project: 'auto-dealer-demo' });
 *     // または
 *     DealerChat.init({ mode: 'staff', project: 'auto-dealer-demo' });
 *   </script>
 */
(function (global) {
  'use strict';

  // ── テーマ定義 ────────────────────────────────────────────────
  const THEMES = {
    customer: {
      primaryColor:   '#1a73e8',
      accentColor:    '#0d47a1',
      headerBg:       'linear-gradient(135deg, #1a73e8, #0d47a1)',
      fabIcon:        '💬',
      headerIcon:     '🚗',
      avatarIcon:     '🤖',
      title:          '🚗 AI 窓口',
      subtitle:       '24時間対応 · 平均応答 < 10秒',
      placeholder:    'ご用件をお聞かせください...',
      welcomeDefault: 'こんにちは！AIカスタマーサポートです。\n試乗・ご購入・サービスのご相談は何でもどうぞ！',
      quickReplies:   ['試乗の予約をしたい', '在庫車両を見たい', '車検・点検について', 'ローンについて聞きたい'],
      apiPath:        'session',         // POST /{project}/api/chat/session
      msgPath:        'session',         // POST /{project}/api/chat/session/{id}/message
    },
    staff: {
      primaryColor:   '#2e7d32',
      accentColor:    '#1b5e20',
      headerBg:       'linear-gradient(135deg, #2e7d32, #1b5e20)',
      fabIcon:        '💼',
      headerIcon:     '🤝',
      avatarIcon:     '🤖',
      title:          '🤝 AI 業務アシスタント',
      subtitle:       '業務支援 · リアルタイム対応',
      placeholder:    '業務に関する質問をどうぞ...',
      welcomeDefault: 'こんにちは！AI業務アシスタントです。\nリード管理・予約確認・在庫照会など何でもご相談ください。',
      quickReplies:   ['今日の予約を確認', 'ホットリードを確認', '顧客情報を検索', '在庫状況を確認'],
      apiPath:        'staff/session',   // POST /{project}/api/chat/staff/session
      msgPath:        'staff',           // POST /{project}/api/chat/staff/{id}/message
    },
  };

  // ── ユーティリティ ─────────────────────────────────────────────
  function esc(str) {
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  // ── Markdown レンダラー（軽量実装） ───────────────────────────────
  function renderMarkdown(text) {
    var lines = String(text).split('\n');
    var html = '';
    var inUl = false, inOl = false;

    function closeList() {
      if (inUl) { html += '</ul>'; inUl = false; }
      if (inOl) { html += '</ol>'; inOl = false; }
    }

    function inlineEsc(str) {
      // エスケープ後にインライン要素を変換
      return str
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/`([^`]+)`/g, '<code>$1</code>')
        .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
        .replace(/\*([^*]+)\*/g, '<em>$1</em>')
        .replace(/_([^_]+)_/g, '<em>$1</em>')
        .replace(/\[([^\]]+)\]\((https?:\/\/[^)]+)\)/g, '<a href="$2" target="_blank" rel="noopener">$1</a>');
    }

    lines.forEach(function (line) {
      // 見出し
      var hMatch = line.match(/^(#{1,3})\s+(.+)$/);
      if (hMatch) {
        closeList();
        var level = hMatch[1].length;
        html += '<h' + level + ' class="_dcw-md-h">' + inlineEsc(hMatch[2]) + '</h' + level + '>';
        return;
      }
      // 水平線
      if (/^---+$/.test(line.trim())) { closeList(); html += '<hr class="_dcw-md-hr">'; return; }
      // 番号付きリスト
      var olMatch = line.match(/^\d+\.\s+(.+)$/);
      if (olMatch) {
        if (inUl) { html += '</ul>'; inUl = false; }
        if (!inOl) { html += '<ol class="_dcw-md-ol">'; inOl = true; }
        html += '<li>' + inlineEsc(olMatch[1]) + '</li>';
        return;
      }
      // 箇条書き
      var ulMatch = line.match(/^[-*]\s+(.+)$/);
      if (ulMatch) {
        if (inOl) { html += '</ol>'; inOl = false; }
        if (!inUl) { html += '<ul class="_dcw-md-ul">'; inUl = true; }
        html += '<li>' + inlineEsc(ulMatch[1]) + '</li>';
        return;
      }
      // 空行 → リスト終端
      if (line.trim() === '') { closeList(); html += '<br>'; return; }
      // 通常行
      closeList();
      html += '<span class="_dcw-md-p">' + inlineEsc(line) + '</span><br>';
    });

    closeList();
    return html;
  }

  // ── CSS インジェクション ────────────────────────────────────────
  function injectStyles(theme) {
    if (document.getElementById('_dcw-styles')) return;
    const p = theme.primaryColor;
    const a = theme.accentColor;
    const style = document.createElement('style');
    style.id = '_dcw-styles';
    style.textContent = `
      #_dcw-fab {
        position: fixed; bottom: 24px; right: 24px; z-index: 9998;
        width: 60px; height: 60px; border-radius: 50%;
        background: var(--dcw-primary, ${p}); color: #fff;
        border: none; cursor: pointer; font-size: 26px;
        box-shadow: 0 4px 16px rgba(0,0,0,.28);
        display: flex; align-items: center; justify-content: center;
        transition: transform .2s, box-shadow .2s;
      }
      #_dcw-fab:hover { transform: scale(1.1); box-shadow: 0 6px 20px rgba(0,0,0,.35); }
      #_dcw-badge {
        position: absolute; top: -4px; right: -4px;
        background: #e53935; color: #fff;
        border-radius: 50%; width: 20px; height: 20px;
        font-size: 11px; font-weight: 700;
        display: none; align-items: center; justify-content: center;
      }

      #_dcw-window {
        position: fixed; bottom: 96px; right: 24px; z-index: 9999;
        width: 370px; max-width: calc(100vw - 48px);
        height: 560px; max-height: calc(100vh - 120px);
        background: #fff; border-radius: 16px;
        box-shadow: 0 8px 40px rgba(0,0,0,.22);
        display: flex; flex-direction: column;
        overflow: hidden; font-family: 'Segoe UI', sans-serif;
        font-size: 14px;
        transform: scale(0); transform-origin: bottom right;
        transition: transform .25s cubic-bezier(.34,1.56,.64,1), opacity .2s;
        opacity: 0; pointer-events: none;
      }
      #_dcw-window._dcw-open {
        transform: scale(1); opacity: 1; pointer-events: all;
      }

      #_dcw-window._dcw-fullscreen {
        bottom: 0 !important; right: 0 !important;
        width: 100vw !important; height: 100dvh !important;
        max-width: 100vw !important; max-height: 100dvh !important;
        border-radius: 0 !important;
        transform: scale(1) !important;
      }

      ._dcw-maximize-btn {
        background: none; border: none; color: rgba(255,255,255,.75);
        font-size: 15px; cursor: pointer; padding: 4px 6px; line-height: 1;
        border-radius: 4px; transition: background .15s, color .15s;
        flex-shrink: 0;
      }
      ._dcw-maximize-btn:hover { background: rgba(255,255,255,.2); color: #fff; }

      ._dcw-header {
        background: var(--dcw-header-bg, ${theme.headerBg});
        color: #fff; padding: 14px 16px;
        display: flex; align-items: center; gap: 12px;
        flex-shrink: 0;
      }
      ._dcw-header-icon { font-size: 28px; }
      ._dcw-header-text { flex: 1; }
      ._dcw-header-title { font-weight: 700; font-size: 15px; }
      ._dcw-header-sub { font-size: 11px; opacity: .85; margin-top: 2px; }
      ._dcw-close-btn {
        background: none; border: none; color: rgba(255,255,255,.8);
        font-size: 20px; cursor: pointer; padding: 4px; line-height: 1;
      }
      ._dcw-close-btn:hover { color: #fff; }

      ._dcw-messages {
        flex: 1; overflow-y: auto; padding: 16px 12px;
        display: flex; flex-direction: column; gap: 10px;
        background: #f8f9fa;
      }
      ._dcw-msg { display: flex; gap: 8px; }
      ._dcw-msg-ai   { justify-content: flex-start; }
      ._dcw-msg-user { justify-content: flex-end; }

      ._dcw-avatar {
        width: 32px; height: 32px; border-radius: 50%;
        background: var(--dcw-primary, ${p}); color: #fff;
        display: flex; align-items: center; justify-content: center;
        font-size: 16px; flex-shrink: 0; align-self: flex-end;
      }
      ._dcw-bubble {
        max-width: 78%; padding: 10px 13px;
        border-radius: 18px; line-height: 1.5; word-break: break-word;
      }
      ._dcw-msg-ai  ._dcw-bubble {
        background: #fff; border-bottom-left-radius: 4px;
        color: #212121; box-shadow: 0 1px 4px rgba(0,0,0,.08);
      }
      ._dcw-msg-user ._dcw-bubble {
        background: var(--dcw-primary, ${p}); color: #fff; border-bottom-right-radius: 4px;
      }
      ._dcw-time { font-size: 10px; opacity: .55; margin-top: 4px; }

      ._dcw-quick-replies {
        padding: 8px 12px; display: flex; flex-wrap: wrap; gap: 6px; background: #f8f9fa;
      }
      ._dcw-qr-btn {
        background: #fff; border: 1.5px solid var(--dcw-primary, ${p});
        color: var(--dcw-primary, ${p});
        border-radius: 99px; padding: 5px 12px; font-size: 12px;
        cursor: pointer; transition: all .15s; white-space: nowrap;
      }
      ._dcw-qr-btn:hover { background: var(--dcw-primary, ${p}); color: #fff; }

      ._dcw-input-row {
        padding: 10px 12px; background: #fff;
        border-top: 1px solid #eeeeee;
        display: flex; gap: 8px; align-items: center; flex-shrink: 0;
      }
      ._dcw-input {
        flex: 1; border: 1.5px solid #e0e0e0; border-radius: 22px;
        padding: 9px 14px; font-size: 13px; outline: none; resize: none;
        max-height: 80px; overflow-y: auto; transition: border .2s;
        font-family: inherit;
      }
      ._dcw-input:focus { border-color: var(--dcw-primary, ${p}); }
      ._dcw-send-btn {
        width: 38px; height: 38px; border-radius: 50%;
        background: var(--dcw-primary, ${p});
        color: #fff; border: none; cursor: pointer; font-size: 16px;
        display: flex; align-items: center; justify-content: center;
        flex-shrink: 0; transition: background .15s;
      }
      ._dcw-send-btn:hover { background: var(--dcw-accent, ${a}); }
      ._dcw-send-btn:disabled { background: #bdbdbd; cursor: not-allowed; }

      ._dcw-typing {
        display: flex; gap: 4px; padding: 8px 12px;
        align-items: center;
      }
      ._dcw-dot {
        width: 7px; height: 7px; border-radius: 50%; background: #9e9e9e;
        animation: _dcw-bounce .9s infinite;
      }
      ._dcw-dot:nth-child(2) { animation-delay: .15s; }
      ._dcw-dot:nth-child(3) { animation-delay: .3s; }
      @keyframes _dcw-bounce {
        0%, 80%, 100% { transform: translateY(0); }
        40%           { transform: translateY(-6px); }
      }

      ._dcw-rating {
        display: flex; gap: 6px; margin-top: 8px;
        justify-content: center;
      }
      ._dcw-star {
        font-size: 22px; cursor: pointer; transition: transform .1s;
        filter: grayscale(1); opacity: .5;
      }
      ._dcw-star:hover, ._dcw-star._dcw-active {
        filter: none; opacity: 1; transform: scale(1.2);
      }

      ._dcw-msg-actions {
        display: none; gap: 4px; margin-top: 3px;
      }
      ._dcw-msg:hover ._dcw-msg-actions { display: flex; }
      ._dcw-msg-user ._dcw-msg-actions { justify-content: flex-end; }
      ._dcw-action-btn {
        background: none; border: 1px solid #ddd; border-radius: 6px;
        padding: 2px 6px; font-size: 11px; cursor: pointer; color: #757575;
        transition: background .15s, color .15s;
      }
      ._dcw-action-btn:hover { background: #e3f2fd; color: #1565c0; border-color: #90caf9; }
      ._dcw-copy-toast {
        position: fixed; bottom: 80px; left: 50%; transform: translateX(-50%);
        background: rgba(0,0,0,.75); color: #fff; padding: 6px 14px;
        border-radius: 20px; font-size: 12px; z-index: 10000;
        animation: _dcw-fade-in-out 1.6s forwards;
        pointer-events: none;
      }
      @keyframes _dcw-fade-in-out {
        0%   { opacity: 0; }
        15%  { opacity: 1; }
        70%  { opacity: 1; }
        100% { opacity: 0; }
      }

      /* ── DB データテーブル ───────────────────────────────── */
      ._dcw-data-table-wrap {
        margin-top: 8px; overflow-x: auto; border-radius: 8px;
        border: 1px solid #e0e0e0; font-size: 12px;
      }
      ._dcw-data-table {
        width: 100%; border-collapse: collapse; white-space: nowrap;
        background: #fafafa;
      }
      ._dcw-data-table th {
        background: var(--dcw-primary, ${p}); color: #fff;
        padding: 5px 8px; font-weight: 600; text-align: left;
      }
      ._dcw-data-table td {
        padding: 4px 8px; border-top: 1px solid #e8e8e8; color: #333;
      }
      ._dcw-data-table tr:hover td { background: #f0f4ff; }

      /* ── ナビゲーションボタン ────────────────────────────── */
      ._dcw-nav-btn-wrap { margin-top: 8px; }
      ._dcw-nav-btn {
        display: inline-block;
        background: var(--dcw-primary, ${p}); color: #fff;
        border-radius: 6px; padding: 5px 12px; font-size: 12px;
        text-decoration: none; font-weight: 600;
        transition: background .15s;
      }
      ._dcw-nav-btn:hover { background: var(--dcw-accent, ${a}); color: #fff; }

      /* ── Markdown スタイル ─────────────────────────────── */
      ._dcw-bubble h1._dcw-md-h, ._dcw-bubble h2._dcw-md-h, ._dcw-bubble h3._dcw-md-h {
        margin: 6px 0 3px; font-weight: 700; line-height: 1.3;
      }
      ._dcw-bubble h1._dcw-md-h { font-size: 15px; }
      ._dcw-bubble h2._dcw-md-h { font-size: 14px; }
      ._dcw-bubble h3._dcw-md-h { font-size: 13px; }
      ._dcw-bubble ul._dcw-md-ul, ._dcw-bubble ol._dcw-md-ol {
        margin: 4px 0; padding-left: 18px;
      }
      ._dcw-bubble li { margin: 2px 0; }
      ._dcw-bubble hr._dcw-md-hr { border: none; border-top: 1px solid #e0e0e0; margin: 6px 0; }
      ._dcw-bubble code { background: #f0f0f0; border-radius: 3px; padding: 1px 4px; font-size: 12px; font-family: monospace; }
      ._dcw-msg-user ._dcw-bubble code { background: rgba(255,255,255,0.2); }
      ._dcw-bubble strong { font-weight: 700; }
      ._dcw-bubble em { font-style: italic; }
      ._dcw-bubble a { color: var(--dcw-primary, ${p}); text-decoration: underline; }
      ._dcw-msg-user ._dcw-bubble a { color: rgba(255,255,255,0.9); }

      @media (max-width: 400px) {
        #_dcw-window { width: calc(100vw - 16px); right: 8px; bottom: 80px; }
      }
    `;
    document.head.appendChild(style);
  }

  // ── HTML テンプレート ──────────────────────────────────────────
  function buildHTML(theme) {
    return `
      <button id="_dcw-fab" title="チャットを開く" aria-label="チャットを開く">
        ${theme.fabIcon}<span id="_dcw-badge"></span>
      </button>

      <div id="_dcw-window" role="dialog" aria-label="AIチャット">
        <div class="_dcw-header">
          <div class="_dcw-header-icon">${theme.headerIcon}</div>
          <div class="_dcw-header-text">
            <div class="_dcw-header-title">${esc(theme.title)}</div>
            <div class="_dcw-header-sub">${esc(theme.subtitle)}</div>
          </div>
          <button class="_dcw-maximize-btn" id="_dcw-maximize" aria-label="全画面" title="全画面 / 縮小">⛶</button>
          <button class="_dcw-close-btn" id="_dcw-close" aria-label="閉じる">✕</button>
        </div>

        <div class="_dcw-messages" id="_dcw-messages"></div>

        <div class="_dcw-quick-replies" id="_dcw-qr"></div>

        <div class="_dcw-input-row">
          <textarea class="_dcw-input" id="_dcw-input"
            placeholder="${esc(theme.placeholder)}" rows="1"
            aria-label="メッセージ入力"></textarea>
          <button class="_dcw-send-btn" id="_dcw-send" aria-label="送信">➤</button>
        </div>
      </div>
    `;
  }

  // ── 本体クラス ────────────────────────────────────────────────
  function ChatWidget(opts, theme) {
    this.opts = opts;
    this.theme = theme;
    this.project = opts.project || 'auto-dealer-demo';
    this.apiBase = (opts.apiBase || '') + '/' + this.project + '/api/chat';
    this.conversationId = null;
    this.open = false;
    this.msgCount = 0;
    this.unread = 0;
    this.pollTimer = null;
    this.lastPollTime = null;
    this.ratingShown = false;
    // localStorage キー（conversationId のみ保持、モード+プロジェクト単位）
    this._convIdKey = '_dcw_cid_' + (opts.mode || 'customer') + '_' + this.project;
  }

  ChatWidget.prototype.mount = function () {
    // テーマの CSS カスタムプロパティを body に適用
    document.documentElement.style.setProperty('--dcw-primary', this.theme.primaryColor);
    document.documentElement.style.setProperty('--dcw-accent',  this.theme.accentColor);
    document.documentElement.style.setProperty('--dcw-header-bg', this.theme.headerBg);

    const container = document.createElement('div');
    container.id = '_dcw-root';
    container.innerHTML = buildHTML(this.theme);
    document.body.appendChild(container);

    this.$fab    = document.getElementById('_dcw-fab');
    this.$window = document.getElementById('_dcw-window');
    this.$msgs   = document.getElementById('_dcw-messages');
    this.$qr     = document.getElementById('_dcw-qr');
    this.$input  = document.getElementById('_dcw-input');
    this.$send   = document.getElementById('_dcw-send');
    this.$badge  = document.getElementById('_dcw-badge');

    this.$fab.addEventListener('click', this.toggle.bind(this));
    document.getElementById('_dcw-close').addEventListener('click', this.toggle.bind(this));
    document.getElementById('_dcw-maximize').addEventListener('click', this.toggleFullscreen.bind(this));
    this.$send.addEventListener('click', this.sendUserMessage.bind(this));
    this.$input.addEventListener('input', function () {
      this.style.height = 'auto';
      this.style.height = Math.min(this.scrollHeight, 80) + 'px';
    });

    // ESC キーで全画面を解除
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && this.fullscreen) this.toggleFullscreen();
    }.bind(this));

    this.renderQuickReplies(this.theme.quickReplies);
    this.startSession();
  };

  ChatWidget.prototype.toggle = function () {
    this.open = !this.open;
    if (this.open) {
      this.$window.classList.add('_dcw-open');
      this.$fab.innerHTML = '✕<span id="_dcw-badge" style="display:none"></span>';
      this.$badge = document.getElementById('_dcw-badge');
      this.setBadge(0);
      setTimeout(function () { this.$input.focus(); }.bind(this), 300);
    } else {
      this.$window.classList.remove('_dcw-open');
      this.$fab.innerHTML = this.theme.fabIcon + '<span id="_dcw-badge"></span>';
      this.$badge = document.getElementById('_dcw-badge');
    }
  };

  ChatWidget.prototype.toggleFullscreen = function () {
    this.fullscreen = !this.fullscreen;
    const btn = document.getElementById('_dcw-maximize');

    if (this.fullscreen) {
      // 全画面に切り替え
      this.$window.classList.add('_dcw-fullscreen');
      if (this.$fab) this.$fab.style.display = 'none';
      if (btn) btn.textContent = '⊡';
      if (btn) btn.title = '縮小';
      // 未オープンの場合は開く
      if (!this.open) {
        this.$window.classList.add('_dcw-open');
        this.open = true;
      }
    } else {
      // 通常サイズに戻す
      this.$window.classList.remove('_dcw-fullscreen');
      if (this.$fab) this.$fab.style.display = '';
      if (btn) btn.textContent = '⛶';
      if (btn) btn.title = '全画面 / 縮小';
    }
    this.scrollBottom();
  };

  ChatWidget.prototype.setBadge = function (n) {
    this.unread = n;
    if (this.$badge) {
      this.$badge.textContent = n;
      this.$badge.style.display = n > 0 ? 'flex' : 'none';
    }
  };

  ChatWidget.prototype.renderQuickReplies = function (replies) {
    this.$qr.innerHTML = replies.map(function (r) {
      return '<button class="_dcw-qr-btn">' + esc(r) + '</button>';
    }).join('');
    const self = this;
    this.$qr.querySelectorAll('._dcw-qr-btn').forEach(function (btn) {
      btn.addEventListener('click', function () {
        self.sendMessage(btn.textContent);
      });
    });
  };

  ChatWidget.prototype.sendUserMessage = function () {
    const text = this.$input.value.trim();
    if (!text) return;
    this.$input.value = '';
    this.$input.style.height = 'auto';
    this.sendMessage(text);
  };

  // ── API 呼び出し ───────────────────────────────────────────────

  ChatWidget.prototype.startSession = function () {
    const self = this;

    // localStorage から以前の conversationId を復元し、DB から履歴を取得
    const storedId = self._getStoredConvId();
    if (storedId) {
      self.conversationId = storedId;
      self._loadHistoryFromDb(storedId).then(function (loaded) {
        if (!loaded) {
          // 履歴取得に失敗（セッション期限切れ等）→ 新規セッションを作成
          self.conversationId = null;
          self._storeConvId(null);
          self._createNewSession();
        }
      });
      return;
    }

    self._createNewSession();
  };

  /** localStorage から conversationId を読み取ります。 */
  ChatWidget.prototype._getStoredConvId = function () {
    try { return localStorage.getItem(this._convIdKey) || null; } catch (e) { return null; }
  };

  /** conversationId を localStorage に保存します（null で削除）。 */
  ChatWidget.prototype._storeConvId = function (id) {
    try {
      if (id) localStorage.setItem(this._convIdKey, id);
      else    localStorage.removeItem(this._convIdKey);
    } catch (e) { /* 無視 */ }
  };

  /** DB から会話履歴を取得してウィジェットに描画します。 */
  ChatWidget.prototype._loadHistoryFromDb = function (conversationId) {
    const self = this;
    return fetch(self.apiBase + '/session/' + conversationId + '/messages')
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (messages) {
        if (!messages || messages.length === 0) return false;
        messages.forEach(function (m) {
          const t = _timeFromTimestamp(m.timestamp);
          if (m.sender === 'customer')     self._renderUserMessage(m.content, t, false);
          else if (m.sender === 'ai')      self._renderAiMessage(m.content, t, false);
          else if (m.sender === 'agent')   self._renderOperatorMessage(m.content, t, false);
        });
        self.msgCount = messages.filter(function (m) { return m.sender === 'customer'; }).length;
        return true;
      })
      .catch(function () { return false; });
  };

  /** 新規セッションを API で作成します。 */
  ChatWidget.prototype._createNewSession = function () {
    const self = this;
    const channel = self.opts.mode === 'staff' ? 'staff' : 'web';
    const url = self.apiBase + '/' + self.theme.apiPath;

    fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ channel: channel })
    })
    .then(function (r) { return r.json(); })
    .then(function (data) {
      self.conversationId = data.conversationId;
      self._storeConvId(data.conversationId);
      setTimeout(function () {
        self.appendAiMessage(data.welcomeMessage || self.theme.welcomeDefault);
        self.setBadge(1);
      }, 400);
    })
    .catch(function () {
      setTimeout(function () {
        self.appendAiMessage(self.theme.welcomeDefault);
        self.setBadge(1);
      }, 400);
    });
  };

  ChatWidget.prototype.sendMessage = function (text) {
    this.$qr.innerHTML = '';
    this.appendUserMessage(text);
    this.$send.disabled = true;
    const self = this;
    this.showTyping();

    if (!self.conversationId) {
      setTimeout(function () { self.sendMessage(text); }, 600);
      return;
    }

    // 顧客: /session/{id}/message  社員: /staff/{id}/message
    const msgUrl = self.opts.mode === 'staff'
      ? self.apiBase + '/staff/' + self.conversationId + '/message'
      : self.apiBase + '/session/' + self.conversationId + '/message';

    fetch(msgUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: text })
    })
    .then(function (r) { return r.json(); })
    .then(function (data) {
      self.hideTyping();
      self.appendAiMessage(data.responseText, data.dataRows, data.navigationUrl, data.navigationLabel);
      if (!self.open) self.setBadge(self.unread + 1);
      self.$send.disabled = false;

      if (data.suggestHandover) {
        self.stopPolling();
        self.startPolling();
      }

      if (data.quickReplies && data.quickReplies.length > 0) {
        self.renderQuickReplies(data.quickReplies);
      } else {
        self.renderQuickReplies(self.theme.quickReplies);
      }

      // 顧客モードのみ5回後に評価リクエスト
      if (self.opts.mode === 'customer') {
        self.msgCount++;
        if (self.msgCount >= 5 && !self.ratingShown) {
          self.ratingShown = true;
          setTimeout(function () { self.appendRatingRequest(); }, 1000);
        }
      }
    })
    .catch(function () {
      self.hideTyping();
      self.appendAiMessage('申し訳ありません。一時的に接続できません。しばらくお待ちください。');
      self.$send.disabled = false;
    });
  };

  // ── オペレーター返信ポーリング（顧客モードのみ） ────────────────

  ChatWidget.prototype.startPolling = function () {
    if (this.pollTimer || this.opts.mode !== 'customer') return;
    this.lastPollTime = new Date().toISOString();
    const self = this;
    this.pollTimer = setInterval(function () { self.pollUpdates(); }, 8000);
  };

  ChatWidget.prototype.stopPolling = function () {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
  };

  ChatWidget.prototype.pollUpdates = function () {
    if (!this.conversationId) return;
    const self = this;
    const since = encodeURIComponent(self.lastPollTime || '');
    fetch(self.apiBase + '/session/' + self.conversationId + '/updates?since=' + since)
    .then(function (r) { return r.json(); })
    .then(function (msgs) {
      if (!msgs || msgs.length === 0) return;
      self.lastPollTime = new Date().toISOString();
      msgs.forEach(function (msg) {
        if (msg.sender === 'agent') {
          self.appendOperatorMessage(msg.content);
          if (!self.open) self.setBadge(self.unread + 1);
        }
      });
    })
    .catch(function () { /* ポーリングエラーは無視 */ });
  };

  // ── メッセージ描画 ────────────────────────────────────────────

  /** コピー/引用アクションボタン HTML を生成します。 */
  function _actionsHtml(align) {
    return '<div class="_dcw-msg-actions" style="justify-content:' + (align === 'right' ? 'flex-end' : 'flex-start') + '">'
      + '<button class="_dcw-action-btn _dcw-btn-copy" title="コピー">📋 コピー</button>'
      + '<button class="_dcw-action-btn _dcw-btn-quote" title="引用">💬 引用</button>'
      + '</div>';
  }

  /** メッセージ要素にコピー/引用イベントを設定します。 */
  ChatWidget.prototype._bindActions = function (div, text) {
    const self = this;
    const copyBtn = div.querySelector('._dcw-btn-copy');
    const quoteBtn = div.querySelector('._dcw-btn-quote');
    if (copyBtn) {
      copyBtn.addEventListener('click', function (e) {
        e.stopPropagation();
        navigator.clipboard.writeText(text).then(function () {
          self._showToast('コピーしました');
        }).catch(function () {
          // clipboard API が使えない場合のフォールバック
          const ta = document.createElement('textarea');
          ta.value = text;
          ta.style.position = 'fixed'; ta.style.opacity = '0';
          document.body.appendChild(ta);
          ta.select();
          document.execCommand('copy');
          document.body.removeChild(ta);
          self._showToast('コピーしました');
        });
      });
    }
    if (quoteBtn) {
      quoteBtn.addEventListener('click', function (e) {
        e.stopPropagation();
        const quoted = '> ' + text.replace(/\n/g, '\n> ') + '\n';
        self.$input.value = quoted + self.$input.value;
        self.$input.focus();
        self.$input.style.height = 'auto';
        self.$input.style.height = Math.min(self.$input.scrollHeight, 80) + 'px';
      });
    }
  };

  ChatWidget.prototype._showToast = function (msg) {
    const t = document.createElement('div');
    t.className = '_dcw-copy-toast';
    t.textContent = msg;
    document.body.appendChild(t);
    setTimeout(function () { if (t.parentNode) t.parentNode.removeChild(t); }, 1700);
  };

  /** ユーザーメッセージを描画します。 */
  ChatWidget.prototype._renderUserMessage = function (text, timeStr) {
    const div = document.createElement('div');
    div.className = '_dcw-msg _dcw-msg-user';
    div.innerHTML = '<div>'
      + '<div class="_dcw-bubble">' + esc(text).replace(/\n/g, '<br>') + '</div>'
      + '<div class="_dcw-time" style="text-align:right">' + esc(timeStr) + '</div>'
      + _actionsHtml('right')
      + '</div>';
    this.$msgs.appendChild(div);
    this._bindActions(div, text);
    this.scrollBottom();
  };

  /** データ行テーブル HTML を生成します（スタッフ向け）。 */
  function _dataTableHtml(rows) {
    if (!rows || rows.length === 0) return '';
    var cols = Object.keys(rows[0]);
    var html = '<div class="_dcw-data-table-wrap"><table class="_dcw-data-table"><thead><tr>';
    cols.forEach(function (c) { html += '<th>' + esc(c) + '</th>'; });
    html += '</tr></thead><tbody>';
    rows.forEach(function (row) {
      html += '<tr>';
      cols.forEach(function (c) { html += '<td>' + esc(row[c] || '') + '</td>'; });
      html += '</tr>';
    });
    html += '</tbody></table></div>';
    return html;
  }

  /** ナビゲーションボタン HTML を生成します。 */
  function _navButtonHtml(url, label) {
    if (!url) return '';
    return '<div class="_dcw-nav-btn-wrap"><a class="_dcw-nav-btn" href="' + esc(url) + '" target="_blank">'
      + esc(label || '画面を開く') + ' →</a></div>';
  }

  /** AI メッセージを描画します。dataRows/navigationUrl が指定されると DB データとナビボタンも表示します。 */
  ChatWidget.prototype._renderAiMessage = function (text, timeStr, scroll, dataRows, navigationUrl, navigationLabel) {
    var div = document.createElement('div');
    div.className = '_dcw-msg _dcw-msg-ai';
    div.innerHTML =
      '<div class="_dcw-avatar">' + this.theme.avatarIcon + '</div>' +
      '<div style="flex:1;min-width:0">' +
        '<div class="_dcw-bubble">' + renderMarkdown(text) +
          _dataTableHtml(dataRows) +
          _navButtonHtml(navigationUrl, navigationLabel) +
        '</div>' +
        '<div class="_dcw-time">' + esc(timeStr) + '</div>' +
        _actionsHtml('left') +
      '</div>';
    this.$msgs.appendChild(div);
    this._bindActions(div, text);
    if (scroll !== false) this.scrollBottom();
  };

  /** オペレーターメッセージを描画します。 */
  ChatWidget.prototype._renderOperatorMessage = function (text, timeStr) {
    const div = document.createElement('div');
    div.className = '_dcw-msg _dcw-msg-ai';
    div.innerHTML =
      '<div class="_dcw-avatar" style="background:#28a745">👤</div>' +
      '<div>' +
        '<div class="_dcw-bubble" style="background:#d4edda">' + esc(text).replace(/\n/g, '<br>') + '</div>' +
        '<div class="_dcw-time">担当者 ' + esc(timeStr) + '</div>' +
        _actionsHtml('left') +
      '</div>';
    this.$msgs.appendChild(div);
    this._bindActions(div, text);
    this.scrollBottom();
  };

  ChatWidget.prototype.appendUserMessage = function (text) {
    this._renderUserMessage(text, _timeStr());
  };

  ChatWidget.prototype.appendAiMessage = function (text, dataRows, navigationUrl, navigationLabel) {
    this._renderAiMessage(text, _timeStr(), true, dataRows, navigationUrl, navigationLabel);
  };

  ChatWidget.prototype.appendOperatorMessage = function (text) {
    this._renderOperatorMessage(text, _timeStr());
  };

  ChatWidget.prototype.appendRatingRequest = function () {
    const self = this;
    const div = document.createElement('div');
    div.className = '_dcw-msg _dcw-msg-ai';
    div.innerHTML = `
      <div class="_dcw-avatar">${self.theme.avatarIcon}</div>
      <div>
        <div class="_dcw-bubble">
          ご対応はいかがでしたか？<br>評価をお聞かせください。
          <div class="_dcw-rating" id="_dcw-rating">
            <span class="_dcw-star" data-v="1">⭐</span>
            <span class="_dcw-star" data-v="2">⭐</span>
            <span class="_dcw-star" data-v="3">⭐</span>
            <span class="_dcw-star" data-v="4">⭐</span>
            <span class="_dcw-star" data-v="5">⭐</span>
          </div>
        </div>
      </div>
    `;
    this.$msgs.appendChild(div);
    div.querySelectorAll('._dcw-star').forEach(function (star) {
      star.addEventListener('click', function () {
        const val = parseInt(star.getAttribute('data-v'));
        div.querySelectorAll('._dcw-star').forEach(function (s) {
          s.classList.toggle('_dcw-active', parseInt(s.getAttribute('data-v')) <= val);
        });
        if (self.conversationId) {
          fetch(self.apiBase + '/session/' + self.conversationId + '/feedback', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ rating: val, comment: null })
          }).catch(function () {});
        }
        setTimeout(function () {
          self.appendAiMessage('ありがとうございます！ ' + val + ' 点のご評価をいただきました。\nまたいつでもお気軽にご相談ください 😊');
          self.stopPolling();
        }, 500);
      });
    });
    this.scrollBottom();
  };

  ChatWidget.prototype.showTyping = function () {
    const div = document.createElement('div');
    div.id = '_dcw-typing-indicator';
    div.className = '_dcw-msg _dcw-msg-ai';
    div.innerHTML = `
      <div class="_dcw-avatar">${this.theme.avatarIcon}</div>
      <div class="_dcw-bubble" style="padding:12px 16px">
        <div class="_dcw-typing">
          <div class="_dcw-dot"></div><div class="_dcw-dot"></div><div class="_dcw-dot"></div>
        </div>
      </div>
    `;
    this.$msgs.appendChild(div);
    this.scrollBottom();
  };

  ChatWidget.prototype.hideTyping = function () {
    const el = document.getElementById('_dcw-typing-indicator');
    if (el) el.remove();
  };

  ChatWidget.prototype.scrollBottom = function () {
    this.$msgs.scrollTop = this.$msgs.scrollHeight;
  };

  // ── 時刻文字列 ────────────────────────────────────────────────
  function _timeStr() {
    const now = new Date();
    return now.getHours() + ':' + String(now.getMinutes()).padStart(2, '0');
  }

  /** DB の timestamp 文字列（UTC）を表示用の時刻文字列に変換します。
   *  当日のメッセージは HH:MM、それ以前は M/D HH:MM で表示します。 */
  function _timeFromTimestamp(ts) {
    if (!ts) return _timeStr();
    try {
      // SQLite は "yyyy-MM-dd HH:mm:ss" (UTC) で保存しているため末尾に Z を付けて変換
      const raw = ts.trim().includes('T') ? ts : ts.replace(' ', 'T') + 'Z';
      const d = new Date(raw);
      if (isNaN(d.getTime())) return _timeStr();
      const now = new Date();
      const sameDay = d.getFullYear() === now.getFullYear()
                   && d.getMonth()    === now.getMonth()
                   && d.getDate()     === now.getDate();
      const hm = d.getHours() + ':' + String(d.getMinutes()).padStart(2, '0');
      return sameDay ? hm : (d.getMonth() + 1) + '/' + d.getDate() + ' ' + hm;
    } catch (e) { return _timeStr(); }
  }

  // ── 公開 API ──────────────────────────────────────────────────
  let _instance = null;

  global.DealerChat = {
    /**
     * ウィジェットを初期化します。
     * @param {object} opts
     * @param {string} opts.mode       - 'customer' | 'staff'
     * @param {string} opts.project    - プロジェクト名（例: 'auto-dealer-demo'）
     * @param {string} [opts.apiBase]  - API ベース URL（省略時は同一オリジン）
     */
    init: function (opts) {
      if (_instance) return;
      const mode  = (opts && opts.mode === 'staff') ? 'staff' : 'customer';
      const theme = THEMES[mode];
      injectStyles(theme);
      _instance = new ChatWidget(Object.assign({ mode: mode }, opts || {}), theme);
      if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { _instance.mount(); });
      } else {
        _instance.mount();
      }
    },
    open:           function () { if (_instance && !_instance.open) _instance.toggle(); },
    close:          function () { if (_instance && _instance.open)  _instance.toggle(); },
    fullscreen:     function () { if (_instance && !_instance.fullscreen) _instance.toggleFullscreen(); },
    exitFullscreen: function () { if (_instance && _instance.fullscreen)  _instance.toggleFullscreen(); },
  };

}(window));
