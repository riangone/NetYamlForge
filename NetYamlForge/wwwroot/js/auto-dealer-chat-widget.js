/**
 * auto-dealer-chat-widget.js
 * 自動車ディーラー AI 窓口 - 顧客向け埋め込みチャットウィジェット
 *
 * 使用方法（ディーラーサイトに埋め込み）:
 *   <script src="/js/auto-dealer-chat-widget.js"></script>
 *   <script>AutoDealerChat.init({ project: 'auto-dealer-demo' });</script>
 */
(function (global) {
  'use strict';

  // ── デフォルト設定 ──────────────────────────────────────────────────
  const DEFAULTS = {
    project: 'auto-dealer-demo',
    apiBase: '',                   // 空の場合は同一オリジン
    primaryColor: '#1a73e8',
    accentColor: '#0d47a1',
    title: '🚗 AI 窓口',
    subtitle: '24時間対応 · 平均応答 < 10秒',
    placeholder: 'メッセージを入力...',
    quickReplies: [
      '試乗の予約をしたい',
      '在庫車両を見たい',
      '車検の費用を知りたい',
      'ローンについて聞きたい',
    ],
    greetingMessage: 'こんにちは！ 自動車ディーラー AI 窓口です。\nご用件をお聞かせください。',
  };

  // ── ユーティリティ ─────────────────────────────────────────────────
  function esc(str) {
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  function randomId() {
    return 'WID-' + Math.random().toString(36).substr(2, 9).toUpperCase();
  }

  // ── CSS injection ──────────────────────────────────────────────────
  function injectStyles(primary, accent) {
    if (document.getElementById('_adcw-styles')) return;
    const style = document.createElement('style');
    style.id = '_adcw-styles';
    style.textContent = `
      #_adcw-fab {
        position: fixed; bottom: 24px; right: 24px; z-index: 9998;
        width: 60px; height: 60px; border-radius: 50%;
        background: ${primary}; color: #fff;
        border: none; cursor: pointer; font-size: 26px;
        box-shadow: 0 4px 16px rgba(0,0,0,.28);
        display: flex; align-items: center; justify-content: center;
        transition: transform .2s, box-shadow .2s;
      }
      #_adcw-fab:hover { transform: scale(1.1); box-shadow: 0 6px 20px rgba(0,0,0,.35); }
      #_adcw-badge {
        position: absolute; top: -4px; right: -4px;
        background: #e53935; color: #fff;
        border-radius: 50%; width: 20px; height: 20px;
        font-size: 11px; font-weight: 700;
        display: none; align-items: center; justify-content: center;
      }

      #_adcw-window {
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
      #_adcw-window._adcw-open {
        transform: scale(1); opacity: 1; pointer-events: all;
      }

      ._adcw-header {
        background: linear-gradient(135deg, ${primary}, ${accent});
        color: #fff; padding: 14px 16px;
        display: flex; align-items: center; gap: 12px;
        flex-shrink: 0;
      }
      ._adcw-header-icon { font-size: 28px; }
      ._adcw-header-text { flex: 1; }
      ._adcw-header-title { font-weight: 700; font-size: 15px; }
      ._adcw-header-sub { font-size: 11px; opacity: .85; margin-top: 2px; }
      ._adcw-close-btn {
        background: none; border: none; color: rgba(255,255,255,.8);
        font-size: 20px; cursor: pointer; padding: 4px; line-height: 1;
      }
      ._adcw-close-btn:hover { color: #fff; }

      ._adcw-messages {
        flex: 1; overflow-y: auto; padding: 16px 12px;
        display: flex; flex-direction: column; gap: 10px;
        background: #f8f9fa;
      }
      ._adcw-msg { display: flex; gap: 8px; }
      ._adcw-msg-ai   { justify-content: flex-start; }
      ._adcw-msg-user { justify-content: flex-end; }

      ._adcw-avatar {
        width: 32px; height: 32px; border-radius: 50%;
        background: ${primary}; color: #fff;
        display: flex; align-items: center; justify-content: center;
        font-size: 16px; flex-shrink: 0; align-self: flex-end;
      }
      ._adcw-bubble {
        max-width: 78%; padding: 10px 13px;
        border-radius: 18px; line-height: 1.5; word-break: break-word;
      }
      ._adcw-msg-ai  ._adcw-bubble {
        background: #fff; border-bottom-left-radius: 4px;
        color: #212121; box-shadow: 0 1px 4px rgba(0,0,0,.08);
      }
      ._adcw-msg-user ._adcw-bubble {
        background: ${primary}; color: #fff; border-bottom-right-radius: 4px;
      }
      ._adcw-time { font-size: 10px; opacity: .55; margin-top: 4px; }

      ._adcw-vehicle-card {
        background: #fff; border-radius: 12px; overflow: hidden;
        border: 1px solid #e0e0e0; margin-top: 6px;
        box-shadow: 0 2px 8px rgba(0,0,0,.07);
        cursor: pointer; transition: box-shadow .15s;
      }
      ._adcw-vehicle-card:hover { box-shadow: 0 4px 16px rgba(0,0,0,.14); }
      ._adcw-vehicle-img {
        width: 100%; height: 100px; object-fit: cover;
        background: #e3f2fd; display: flex; align-items: center;
        justify-content: center; font-size: 40px;
      }
      ._adcw-vehicle-info { padding: 10px 12px; }
      ._adcw-vehicle-name { font-weight: 700; font-size: 13px; }
      ._adcw-vehicle-price { color: ${primary}; font-weight: 700; font-size: 14px; margin-top: 4px; }
      ._adcw-vehicle-tag {
        display: inline-block; background: #e8f4fd; color: ${primary};
        font-size: 10px; padding: 2px 6px; border-radius: 99px; margin-top: 4px; margin-right: 4px;
      }

      ._adcw-quick-replies {
        padding: 8px 12px; display: flex; flex-wrap: wrap; gap: 6px; background: #f8f9fa;
      }
      ._adcw-qr-btn {
        background: #fff; border: 1.5px solid ${primary}; color: ${primary};
        border-radius: 99px; padding: 5px 12px; font-size: 12px;
        cursor: pointer; transition: all .15s; white-space: nowrap;
      }
      ._adcw-qr-btn:hover { background: ${primary}; color: #fff; }

      ._adcw-input-row {
        padding: 10px 12px; background: #fff;
        border-top: 1px solid #eeeeee;
        display: flex; gap: 8px; align-items: center; flex-shrink: 0;
      }
      ._adcw-input {
        flex: 1; border: 1.5px solid #e0e0e0; border-radius: 22px;
        padding: 9px 14px; font-size: 13px; outline: none; resize: none;
        max-height: 80px; overflow-y: auto; transition: border .2s;
        font-family: inherit;
      }
      ._adcw-input:focus { border-color: ${primary}; }
      ._adcw-send-btn {
        width: 38px; height: 38px; border-radius: 50%; background: ${primary};
        color: #fff; border: none; cursor: pointer; font-size: 16px;
        display: flex; align-items: center; justify-content: center;
        flex-shrink: 0; transition: background .15s;
      }
      ._adcw-send-btn:hover { background: ${accent}; }
      ._adcw-send-btn:disabled { background: #bdbdbd; cursor: not-allowed; }

      ._adcw-typing {
        display: flex; gap: 4px; padding: 8px 12px;
        align-items: center;
      }
      ._adcw-dot {
        width: 7px; height: 7px; border-radius: 50%; background: #9e9e9e;
        animation: _adcw-bounce .9s infinite;
      }
      ._adcw-dot:nth-child(2) { animation-delay: .15s; }
      ._adcw-dot:nth-child(3) { animation-delay: .3s; }
      @keyframes _adcw-bounce {
        0%, 80%, 100% { transform: translateY(0); }
        40%           { transform: translateY(-6px); }
      }

      ._adcw-rating {
        display: flex; gap: 6px; margin-top: 8px;
        justify-content: center;
      }
      ._adcw-star {
        font-size: 22px; cursor: pointer; transition: transform .1s;
        filter: grayscale(1); opacity: .5;
      }
      ._adcw-star:hover, ._adcw-star._adcw-active {
        filter: none; opacity: 1; transform: scale(1.2);
      }

      @media (max-width: 400px) {
        #_adcw-window { width: calc(100vw - 16px); right: 8px; bottom: 80px; }
      }
    `;
    document.head.appendChild(style);
  }

  // ── HTML テンプレート ──────────────────────────────────────────────
  function buildHTML(cfg) {
    return `
      <button id="_adcw-fab" title="AI 窓口を開く" aria-label="チャットを開く">
        💬<span id="_adcw-badge"></span>
      </button>

      <div id="_adcw-window" role="dialog" aria-label="AI チャット窓口">
        <div class="_adcw-header">
          <div class="_adcw-header-icon">🚗</div>
          <div class="_adcw-header-text">
            <div class="_adcw-header-title">${esc(cfg.title)}</div>
            <div class="_adcw-header-sub">${esc(cfg.subtitle)}</div>
          </div>
          <button class="_adcw-close-btn" id="_adcw-close" aria-label="閉じる">✕</button>
        </div>

        <div class="_adcw-messages" id="_adcw-messages"></div>

        <div class="_adcw-quick-replies" id="_adcw-qr"></div>

        <div class="_adcw-input-row">
          <textarea class="_adcw-input" id="_adcw-input"
            placeholder="${esc(cfg.placeholder)}" rows="1"
            aria-label="メッセージ入力"></textarea>
          <button class="_adcw-send-btn" id="_adcw-send" aria-label="送信">➤</button>
        </div>
      </div>
    `;
  }

  // ── 車両カード HTML ───────────────────────────────────────────────
  function vehicleCard(v) {
    return `
      <div class="_adcw-vehicle-card" onclick="AutoDealerChat._onVehicleClick('${esc(v.id)}')">
        <div class="_adcw-vehicle-img">🚗</div>
        <div class="_adcw-vehicle-info">
          <div class="_adcw-vehicle-name">${esc(v.name)}</div>
          <div class="_adcw-vehicle-price">${esc(v.price)}</div>
          <div>
            ${(v.tags || []).map(t => `<span class="_adcw-vehicle-tag">${esc(t)}</span>`).join('')}
          </div>
        </div>
      </div>
    `;
  }

  // ── 本体クラス ────────────────────────────────────────────────────
  function ChatWidget(cfg) {
    this.cfg = Object.assign({}, DEFAULTS, cfg);
    this.conversationId = null;   // サーバーから取得
    this.open = false;
    this.msgCount = 0;
    this.unread = 0;
    this.handoverId = null;
    this.pollTimer = null;
    this.lastPollTime = null;
    this.ratingShown = false;
  }

  ChatWidget.prototype.mount = function () {
    const container = document.createElement('div');
    container.id = '_adcw-root';
    container.innerHTML = buildHTML(this.cfg);
    document.body.appendChild(container);

    this.$fab    = document.getElementById('_adcw-fab');
    this.$window = document.getElementById('_adcw-window');
    this.$msgs   = document.getElementById('_adcw-messages');
    this.$qr     = document.getElementById('_adcw-qr');
    this.$input  = document.getElementById('_adcw-input');
    this.$send   = document.getElementById('_adcw-send');
    this.$badge  = document.getElementById('_adcw-badge');

    this.$fab.addEventListener('click', this.toggle.bind(this));
    document.getElementById('_adcw-close').addEventListener('click', this.toggle.bind(this));
    this.$send.addEventListener('click', this.sendUserMessage.bind(this));
    this.$input.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); this.sendUserMessage(); }
    }.bind(this));
    this.$input.addEventListener('input', function () {
      this.style.height = 'auto';
      this.style.height = Math.min(this.scrollHeight, 80) + 'px';
    });

    this.renderQuickReplies(this.cfg.quickReplies);
    this.startSession();
  };

  ChatWidget.prototype.toggle = function () {
    this.open = !this.open;
    if (this.open) {
      this.$window.classList.add('_adcw-open');
      this.$fab.innerHTML = '✕<span id="_adcw-badge" style="display:none"></span>';
      this.$badge = document.getElementById('_adcw-badge');
      this.setBadge(0);
      setTimeout(function () { this.$input.focus(); }.bind(this), 300);
    } else {
      this.$window.classList.remove('_adcw-open');
      this.$fab.innerHTML = '💬<span id="_adcw-badge"></span>';
      this.$badge = document.getElementById('_adcw-badge');
    }
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
      return '<button class="_adcw-qr-btn">' + esc(r) + '</button>';
    }).join('');
    const self = this;
    this.$qr.querySelectorAll('._adcw-qr-btn').forEach(function (btn) {
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

  // ── バックエンド API 呼び出し ──────────────────────────────────────

  ChatWidget.prototype.apiBase = function () {
    return (this.cfg.apiBase || '') + '/' + this.cfg.project + '/api/chat';
  };

  ChatWidget.prototype.startSession = function () {
    const self = this;
    fetch(self.apiBase() + '/session', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ channel: 'web' })
    })
    .then(function (r) { return r.json(); })
    .then(function (data) {
      self.conversationId = data.conversationId;
      setTimeout(function () {
        self.appendAiMessage(data.welcomeMessage || self.cfg.greetingMessage);
        self.setBadge(1);
      }, 400);
    })
    .catch(function () {
      // API 不達時はデフォルトメッセージを表示
      setTimeout(function () {
        self.appendAiMessage(self.cfg.greetingMessage);
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
      // セッション未開始の場合は短時間待機して再試行
      setTimeout(function () { self.sendMessage(text); }, 600);
      return;
    }

    fetch(self.apiBase() + '/session/' + self.conversationId + '/message', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: text })
    })
    .then(function (r) { return r.json(); })
    .then(function (data) {
      self.hideTyping();
      self.appendAiMessage(data.responseText);
      if (!self.open) self.setBadge(self.unread + 1);
      self.$send.disabled = false;

      if (data.suggestHandover) {
        self.handoverId = data.handoverId;
        self.stopPolling();
        self.startPolling();
      }

      if (data.quickReplies && data.quickReplies.length > 0) {
        self.renderQuickReplies(data.quickReplies);
      } else {
        self.renderQuickReplies(['他にも質問する', '予約したい', '担当者に繋いでほしい', 'ありがとう']);
      }

      // 5回目の返信後に評価リクエスト
      self.msgCount++;
      if (self.msgCount >= 5 && !self.ratingShown) {
        self.ratingShown = true;
        setTimeout(function () { self.appendRatingRequest(); }, 1000);
      }
    })
    .catch(function () {
      self.hideTyping();
      self.appendAiMessage('申し訳ありません。一時的に接続できません。しばらくお待ちください。');
      self.$send.disabled = false;
    });
  };

  // ── オペレーター返信ポーリング ──────────────────────────────────────

  ChatWidget.prototype.startPolling = function () {
    if (this.pollTimer) return;
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
    fetch(self.apiBase() + '/session/' + self.conversationId + '/updates?since=' + since)
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

  ChatWidget.prototype.appendUserMessage = function (text) {
    const now = new Date();
    const timeStr = now.getHours() + ':' + String(now.getMinutes()).padStart(2, '0');
    const div = document.createElement('div');
    div.className = '_adcw-msg _adcw-msg-user';
    div.innerHTML = `
      <div>
        <div class="_adcw-bubble">${esc(text).replace(/\n/g, '<br>')}</div>
        <div class="_adcw-time" style="text-align:right">${timeStr}</div>
      </div>
    `;
    this.$msgs.appendChild(div);
    this.scrollBottom();
  };

  ChatWidget.prototype.appendAiMessage = function (text) {
    const now = new Date();
    const timeStr = now.getHours() + ':' + String(now.getMinutes()).padStart(2, '0');
    const div = document.createElement('div');
    div.className = '_adcw-msg _adcw-msg-ai';
    div.innerHTML =
      '<div class="_adcw-avatar">🤖</div>' +
      '<div>' +
        '<div class="_adcw-bubble">' + esc(text).replace(/\n/g, '<br>') + '</div>' +
        '<div class="_adcw-time">' + timeStr + '</div>' +
      '</div>';
    this.$msgs.appendChild(div);
    this.scrollBottom();
  };

  ChatWidget.prototype.appendOperatorMessage = function (text) {
    const now = new Date();
    const timeStr = now.getHours() + ':' + String(now.getMinutes()).padStart(2, '0');
    const div = document.createElement('div');
    div.className = '_adcw-msg _adcw-msg-ai';
    div.innerHTML =
      '<div class="_adcw-avatar" style="background:#28a745">👤</div>' +
      '<div>' +
        '<div class="_adcw-bubble" style="background:#d4edda">' + esc(text).replace(/\n/g, '<br>') + '</div>' +
        '<div class="_adcw-time">担当者 ' + timeStr + '</div>' +
      '</div>';
    this.$msgs.appendChild(div);
    this.scrollBottom();
  };

  ChatWidget.prototype.appendRatingRequest = function () {
    const self = this;
    const div = document.createElement('div');
    div.className = '_adcw-msg _adcw-msg-ai';
    div.innerHTML = `
      <div class="_adcw-avatar">🤖</div>
      <div>
        <div class="_adcw-bubble">
          ご対応はいかがでしたか？<br>評価をお聞かせください。
          <div class="_adcw-rating" id="_adcw-rating">
            <span class="_adcw-star" data-v="1">⭐</span>
            <span class="_adcw-star" data-v="2">⭐</span>
            <span class="_adcw-star" data-v="3">⭐</span>
            <span class="_adcw-star" data-v="4">⭐</span>
            <span class="_adcw-star" data-v="5">⭐</span>
          </div>
        </div>
      </div>
    `;
    this.$msgs.appendChild(div);
    div.querySelectorAll('._adcw-star').forEach(function (star) {
      star.addEventListener('click', function () {
        const val = parseInt(star.getAttribute('data-v'));
        div.querySelectorAll('._adcw-star').forEach(function (s) {
          s.classList.toggle('_adcw-active', parseInt(s.getAttribute('data-v')) <= val);
        });
        // バックエンドに評価を送信
        if (self.conversationId) {
          fetch(self.apiBase() + '/session/' + self.conversationId + '/feedback', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ rating: val, comment: null })
          }).catch(function () { /* エラーは無視 */ });
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
    div.id = '_adcw-typing-indicator';
    div.className = '_adcw-msg _adcw-msg-ai';
    div.innerHTML = `
      <div class="_adcw-avatar">🤖</div>
      <div class="_adcw-bubble" style="padding:12px 16px">
        <div class="_adcw-typing">
          <div class="_adcw-dot"></div><div class="_adcw-dot"></div><div class="_adcw-dot"></div>
        </div>
      </div>
    `;
    this.$msgs.appendChild(div);
    this.scrollBottom();
  };

  ChatWidget.prototype.hideTyping = function () {
    const el = document.getElementById('_adcw-typing-indicator');
    if (el) el.remove();
  };

  ChatWidget.prototype.scrollBottom = function () {
    this.$msgs.scrollTop = this.$msgs.scrollHeight;
  };

  // ── 車両カードクリック ────────────────────────────────────────────
  ChatWidget.prototype.onVehicleClick = function (vehicleId) {
    this.sendMessage('「' + vehicleId + '」の詳細を教えてください');
  };

  // ── 公開 API ──────────────────────────────────────────────────────
  let _instance = null;

  global.AutoDealerChat = {
    init: function (options) {
      if (_instance) return;
      const cfg = Object.assign({}, DEFAULTS, options || {});
      injectStyles(cfg.primaryColor, cfg.accentColor);
      _instance = new ChatWidget(cfg);
      if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { _instance.mount(); });
      } else {
        _instance.mount();
      }
    },
    open:  function () { if (_instance && !_instance.open) _instance.toggle(); },
    close: function () { if (_instance && _instance.open)  _instance.toggle(); },
    _onVehicleClick: function (id) { if (_instance) _instance.onVehicleClick(id); },
  };

}(window));
