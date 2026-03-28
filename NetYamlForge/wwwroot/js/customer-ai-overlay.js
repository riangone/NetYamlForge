/**
 * customer-ai-overlay.js
 * 顧客ログイン後のランディングページに表示する全画面 AI アシスタントオーバーレイ。
 *
 * 使い方:
 *   <script src="/js/customer-ai-overlay.js"
 *           data-project="auto-dealer-demo"
 *           data-dealer-name="AI 窓口ディーラー"></script>
 *
 * セッションストレージキー "ai_overlay_dismissed" が存在する場合はオーバーレイを表示しない。
 */
(function () {
  'use strict';

  const STORAGE_KEY = 'ai_overlay_dismissed';
  const POLL_INTERVAL_MS = 4000;

  // ── 設定取得 ────────────────────────────────────────────────
  const scriptEl = document.currentScript || document.querySelector('script[data-project]');
  const PROJECT = scriptEl?.dataset?.project || 'auto-dealer-demo';
  const DEALER_NAME = scriptEl?.dataset?.dealerName || 'AI アシスタント';
  const API_BASE = `/${PROJECT}/api/chat`;

  const QUICK_REPLIES = [
    '試乗の予約をしたい',
    '在庫車両を見たい',
    '車検・点検について',
    '担当者に相談したい',
  ];

  // ── 状態 ────────────────────────────────────────────────────
  let conversationId = null;
  let pollTimer = null;
  let lastPollTime = null;
  let isEscalated = false;

  // ── 初期化 ──────────────────────────────────────────────────
  function init() {
    // 既に閉じた場合はスキップ
    if (sessionStorage.getItem(STORAGE_KEY)) return;

    injectStyles();
    const overlay = buildOverlay();
    document.body.appendChild(overlay);

    // フォーカストラップ・ESC で閉じる
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') dismissOverlay();
    });

    startSession();
  }

  // ── セッション開始 ──────────────────────────────────────────
  async function startSession() {
    try {
      const res = await fetch(`${API_BASE}/session`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ channel: 'customer_portal' }),
      });
      if (!res.ok) throw new Error('session start failed');
      const data = await res.json();
      conversationId = data.conversationId;
      lastPollTime = new Date();

      appendMessage('ai', data.welcomeMessage || `こんにちは！${DEALER_NAME}です。本日はどのようなご用件でしょうか？`);
      showQuickReplies(QUICK_REPLIES);
    } catch (e) {
      appendMessage('ai', `こんにちは！${DEALER_NAME}です。本日はどのようなご用件でしょうか？`);
      showQuickReplies(QUICK_REPLIES);
    }
  }

  // ── メッセージ送信 ──────────────────────────────────────────
  async function sendMessage(text) {
    if (!text.trim()) return;
    appendMessage('customer', text);
    hideQuickReplies();
    showTypingIndicator();

    try {
      if (!conversationId) {
        await startSession();
      }
      const res = await fetch(`${API_BASE}/session/${conversationId}/message`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: text }),
      });
      if (!res.ok) throw new Error('message failed');
      const data = await res.json();

      hideTypingIndicator();
      appendMessage('ai', data.responseText || data.response || 'ただいま確認中です。少々お待ちください。');

      if (data.quickReplies?.length > 0) showQuickReplies(data.quickReplies);

      if (data.suggestHandover) {
        isEscalated = true;
        startPolling();
        showEscalationBadge();
      }

      // AI が特定のページを案内した場合のリンク表示
      if (data.redirectUrl) showActionButton(data.redirectLabel || '詳細を見る', data.redirectUrl);
    } catch {
      hideTypingIndicator();
      appendMessage('ai', 'ただいま一時的につながりにくい状況です。お手数ですが少し後にお試しください。');
    }
  }

  // ── ポーリング（エスカレーション後） ───────────────────────
  function startPolling() {
    if (pollTimer) return;
    pollTimer = setInterval(pollUpdates, POLL_INTERVAL_MS);
  }

  async function pollUpdates() {
    if (!conversationId) return;
    try {
      const since = lastPollTime?.toISOString() ?? '';
      const res = await fetch(`${API_BASE}/session/${conversationId}/updates?since=${encodeURIComponent(since)}`);
      if (!res.ok) return;
      const messages = await res.json();
      if (messages?.length > 0) {
        lastPollTime = new Date();
        messages.forEach((m) => {
          if (m.sender === 'agent') appendMessage('operator', m.content);
        });
      }
    } catch { /* ignore */ }
  }

  // ── オーバーレイを閉じる ─────────────────────────────────────
  function dismissOverlay() {
    sessionStorage.setItem(STORAGE_KEY, '1');
    if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }

    const overlay = document.getElementById('ai-customer-overlay');
    if (!overlay) return;
    overlay.style.opacity = '0';
    overlay.style.transform = 'scale(0.96)';
    setTimeout(() => {
      overlay.remove();
      injectFab();
    }, 280);
  }

  // ── FAB（縮小後のチャットボタン） ───────────────────────────
  function injectFab() {
    const fab = document.createElement('button');
    fab.id = 'ai-overlay-fab';
    fab.innerHTML = '🤖';
    fab.title = 'AIアシスタントを開く';
    fab.addEventListener('click', reopenOverlay);
    document.body.appendChild(fab);
  }

  function reopenOverlay() {
    const fab = document.getElementById('ai-overlay-fab');
    if (fab) fab.remove();
    sessionStorage.removeItem(STORAGE_KEY);

    const overlay = buildOverlay();
    document.body.appendChild(overlay);
    if (conversationId) {
      showQuickReplies(QUICK_REPLIES);
    } else {
      startSession();
    }
  }

  // ── DOM 構築 ─────────────────────────────────────────────────
  function buildOverlay() {
    const overlay = document.createElement('div');
    overlay.id = 'ai-customer-overlay';
    overlay.setAttribute('role', 'dialog');
    overlay.setAttribute('aria-label', 'AIアシスタント');
    overlay.innerHTML = `
      <div class="ai-overlay-backdrop"></div>
      <div class="ai-overlay-panel">
        <header class="ai-overlay-header">
          <div class="ai-overlay-avatar">🤖</div>
          <div class="ai-overlay-header-text">
            <span class="ai-overlay-title">${escHtml(DEALER_NAME)}</span>
            <span class="ai-overlay-subtitle">AIカスタマーサポート · 24時間対応</span>
          </div>
          <button class="ai-overlay-close" title="ダッシュボードへ" aria-label="閉じる">×</button>
        </header>

        <div class="ai-overlay-messages" id="ai-overlay-messages"></div>

        <div class="ai-overlay-quick" id="ai-overlay-quick" style="display:none"></div>

        <footer class="ai-overlay-footer">
          <form class="ai-overlay-form" id="ai-overlay-form" autocomplete="off">
            <input class="ai-overlay-input" id="ai-overlay-input"
                   type="text" placeholder="メッセージを入力…" maxlength="300" />
            <button type="submit" class="ai-overlay-send">送信</button>
          </form>
          <button class="ai-overlay-skip" id="ai-overlay-skip">
            ダッシュボードを表示する →
          </button>
        </footer>
      </div>
    `;

    overlay.querySelector('.ai-overlay-close').addEventListener('click', dismissOverlay);
    overlay.querySelector('#ai-overlay-skip').addEventListener('click', dismissOverlay);
    overlay.querySelector('#ai-overlay-form').addEventListener('submit', (e) => {
      e.preventDefault();
      const input = overlay.querySelector('#ai-overlay-input');
      const text = input.value.trim();
      if (text) { input.value = ''; sendMessage(text); }
    });

    // アニメーション
    requestAnimationFrame(() => {
      overlay.style.opacity = '1';
      overlay.querySelector('.ai-overlay-panel').style.transform = 'translateY(0)';
    });

    return overlay;
  }

  // ── メッセージ追加 ───────────────────────────────────────────
  function appendMessage(sender, text) {
    const container = document.getElementById('ai-overlay-messages');
    if (!container) return;

    const wrap = document.createElement('div');
    wrap.className = `ai-msg ai-msg-${sender}`;

    const bubble = document.createElement('div');
    bubble.className = 'ai-msg-bubble';
    bubble.textContent = text;

    const meta = document.createElement('span');
    meta.className = 'ai-msg-meta';
    meta.textContent = sender === 'ai' ? 'AI' : sender === 'operator' ? '担当者' : 'あなた';

    wrap.appendChild(meta);
    wrap.appendChild(bubble);
    container.appendChild(wrap);
    container.scrollTop = container.scrollHeight;
  }

  function showTypingIndicator() {
    const container = document.getElementById('ai-overlay-messages');
    if (!container || container.querySelector('.ai-typing')) return;
    const el = document.createElement('div');
    el.className = 'ai-msg ai-msg-ai ai-typing';
    el.innerHTML = '<div class="ai-msg-bubble"><span class="ai-dots"><span></span><span></span><span></span></span></div>';
    container.appendChild(el);
    container.scrollTop = container.scrollHeight;
  }

  function hideTypingIndicator() {
    document.querySelector('.ai-typing')?.remove();
  }

  function showQuickReplies(replies) {
    const el = document.getElementById('ai-overlay-quick');
    if (!el) return;
    el.style.display = 'flex';
    el.innerHTML = replies.map((r) =>
      `<button class="ai-quick-btn">${escHtml(r)}</button>`
    ).join('');
    el.querySelectorAll('.ai-quick-btn').forEach((btn) => {
      btn.addEventListener('click', () => {
        sendMessage(btn.textContent);
        hideQuickReplies();
      });
    });
  }

  function hideQuickReplies() {
    const el = document.getElementById('ai-overlay-quick');
    if (el) el.style.display = 'none';
  }

  function showEscalationBadge() {
    const header = document.querySelector('.ai-overlay-subtitle');
    if (header) header.textContent = '担当者接続中…';
  }

  function showActionButton(label, url) {
    const container = document.getElementById('ai-overlay-messages');
    if (!container) return;
    const btn = document.createElement('a');
    btn.href = url;
    btn.className = 'ai-action-link';
    btn.textContent = label + ' →';
    container.appendChild(btn);
    container.scrollTop = container.scrollHeight;
  }

  // ── ユーティリティ ───────────────────────────────────────────
  function escHtml(str) {
    return str.replace(/[&<>"']/g, (c) =>
      ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])
    );
  }

  // ── スタイル注入 ─────────────────────────────────────────────
  function injectStyles() {
    if (document.getElementById('ai-overlay-styles')) return;
    const style = document.createElement('style');
    style.id = 'ai-overlay-styles';
    style.textContent = `
      #ai-customer-overlay {
        position: fixed; inset: 0; z-index: 9000;
        opacity: 0;
        transition: opacity 0.28s ease;
        display: flex; align-items: center; justify-content: center;
      }
      .ai-overlay-backdrop {
        position: absolute; inset: 0;
        background: rgba(15,23,42,0.55);
        backdrop-filter: blur(4px);
      }
      .ai-overlay-panel {
        position: relative; z-index: 1;
        width: min(560px, calc(100vw - 32px));
        height: min(680px, calc(100vh - 48px));
        background: #fff;
        border-radius: 20px;
        box-shadow: 0 32px 80px rgba(0,0,0,0.25);
        display: flex; flex-direction: column;
        overflow: hidden;
        transform: translateY(24px);
        transition: transform 0.28s cubic-bezier(0.34,1.56,0.64,1);
      }
      .ai-overlay-header {
        display: flex; align-items: center; gap: 12px;
        padding: 16px 20px;
        background: linear-gradient(135deg, #1a73e8, #0d47a1);
        color: #fff;
        flex-shrink: 0;
      }
      .ai-overlay-avatar { font-size: 28px; }
      .ai-overlay-header-text { flex: 1; min-width: 0; }
      .ai-overlay-title { display: block; font-size: 16px; font-weight: 700; }
      .ai-overlay-subtitle { display: block; font-size: 12px; opacity: 0.82; }
      .ai-overlay-close {
        width: 32px; height: 32px; border: none; cursor: pointer;
        background: rgba(255,255,255,0.18); border-radius: 50%;
        color: #fff; font-size: 18px; line-height: 1;
        transition: background 0.15s;
      }
      .ai-overlay-close:hover { background: rgba(255,255,255,0.32); }

      .ai-overlay-messages {
        flex: 1; overflow-y: auto; padding: 16px 20px;
        display: flex; flex-direction: column; gap: 10px;
        background: #f8fafc;
      }
      .ai-msg { display: flex; flex-direction: column; gap: 3px; }
      .ai-msg-ai { align-items: flex-start; }
      .ai-msg-customer { align-items: flex-end; }
      .ai-msg-operator { align-items: flex-start; }
      .ai-msg-meta { font-size: 10px; color: #94a3b8; padding: 0 6px; }
      .ai-msg-bubble {
        max-width: 80%; padding: 10px 14px; border-radius: 16px;
        font-size: 14px; line-height: 1.6; white-space: pre-wrap; word-break: break-word;
      }
      .ai-msg-ai .ai-msg-bubble { background: #e8f0fe; color: #1e3a5f; border-radius: 4px 16px 16px 16px; }
      .ai-msg-customer .ai-msg-bubble { background: #1a73e8; color: #fff; border-radius: 16px 4px 16px 16px; }
      .ai-msg-operator .ai-msg-bubble { background: #d1fae5; color: #065f46; border-radius: 4px 16px 16px 16px; }

      /* タイピングインジケーター */
      .ai-dots { display: inline-flex; gap: 4px; align-items: center; }
      .ai-dots span {
        width: 7px; height: 7px; border-radius: 50%; background: #93a7c0;
        animation: ai-bounce 1.2s infinite ease-in-out;
      }
      .ai-dots span:nth-child(2) { animation-delay: 0.2s; }
      .ai-dots span:nth-child(3) { animation-delay: 0.4s; }
      @keyframes ai-bounce { 0%,80%,100%{ transform:scale(0.8); opacity:0.5 } 40%{ transform:scale(1.1); opacity:1 } }

      /* クイックリプライ */
      .ai-overlay-quick {
        flex-wrap: wrap; gap: 8px; padding: 12px 20px 4px;
        background: #f8fafc; border-top: 1px solid #e2e8f0;
        flex-shrink: 0;
      }
      .ai-quick-btn {
        padding: 6px 14px; border: 1.5px solid #1a73e8; border-radius: 20px;
        background: #fff; color: #1a73e8; font-size: 13px; cursor: pointer;
        transition: all 0.15s;
      }
      .ai-quick-btn:hover { background: #1a73e8; color: #fff; }

      /* フッター */
      .ai-overlay-footer {
        padding: 12px 20px 16px;
        border-top: 1px solid #e2e8f0;
        flex-shrink: 0; background: #fff;
      }
      .ai-overlay-form { display: flex; gap: 8px; margin-bottom: 8px; }
      .ai-overlay-input {
        flex: 1; padding: 10px 14px; border: 1.5px solid #e2e8f0; border-radius: 24px;
        font-size: 14px; outline: none; transition: border 0.15s;
      }
      .ai-overlay-input:focus { border-color: #1a73e8; }
      .ai-overlay-send {
        padding: 10px 20px; background: #1a73e8; color: #fff; border: none;
        border-radius: 24px; font-size: 14px; font-weight: 600; cursor: pointer;
        transition: background 0.15s;
      }
      .ai-overlay-send:hover { background: #1557b0; }
      .ai-overlay-skip {
        display: block; width: 100%; background: none; border: none;
        color: #64748b; font-size: 13px; cursor: pointer; text-align: center;
        padding: 4px; transition: color 0.15s;
      }
      .ai-overlay-skip:hover { color: #1a73e8; }

      /* アクションリンク */
      .ai-action-link {
        display: inline-block; padding: 8px 16px;
        background: #1a73e8; color: #fff !important; border-radius: 8px;
        font-size: 13px; font-weight: 600; text-decoration: none;
        margin: 4px 0; transition: background 0.15s;
      }
      .ai-action-link:hover { background: #1557b0; }

      /* FAB */
      #ai-overlay-fab {
        position: fixed; bottom: 24px; right: 24px; z-index: 8999;
        width: 56px; height: 56px; border-radius: 50%;
        background: linear-gradient(135deg, #1a73e8, #0d47a1);
        border: none; font-size: 26px; cursor: pointer;
        box-shadow: 0 4px 16px rgba(26,115,232,0.45);
        transition: transform 0.15s, box-shadow 0.15s;
      }
      #ai-overlay-fab:hover {
        transform: scale(1.08);
        box-shadow: 0 8px 24px rgba(26,115,232,0.55);
      }

      @media (max-width: 600px) {
        .ai-overlay-panel {
          width: 100vw; height: 100dvh;
          border-radius: 0;
        }
      }
    `;
    document.head.appendChild(style);
  }

  // ── エントリポイント ─────────────────────────────────────────
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
