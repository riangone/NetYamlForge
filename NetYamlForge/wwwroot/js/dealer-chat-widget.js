/**
 * dealer-chat-widget.js
 * 自動車ディーラー 統一 AI チャットウィジェット
 * コアフレームワークの AI Assistant と同一 UI・機能を実装
 * トリガーボタンは右下に表示
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

  // ── 状態管理 ────────────────────────────────────────────────
  let connection = null;
  let currentTaskId = null;
  let currentSessionId = null;
  let dealerConversationId = null; // auto-dealer セッション ID
  let isPanelOpen = false;
  let isPanelMinimized = false;
  let isMaximized = false;
  let autoScroll = true;
  let chatHistory = [];
  let inputHistory = [];
  let inputHistoryIndex = -1;
  let inputCurrentDraft = '';
  let previousWidth = '';
  let previousRight = '';

  const STORAGE_KEY_PREFIX = 'dealer_chat_history_';
  const TOOL_STORAGE_KEY = 'dealer_chat_tool';

  // ── 設定 ────────────────────────────────────────────────────
  const CONFIG = {
    apiBaseUrl: '',
    signalRUrl: '/aiProgressHub',
    defaultCliTool: 'qwen'
  };

  // ── テーマ定義 ──────────────────────────────────────────────
  const THEMES = {
    customer: {
      primaryColor: '#1a73e8',
      accentColor: '#0d47a1',
      headerBg: 'linear-gradient(135deg, #1a73e8, #0d47a1)',
      headerIcon: '🚗',
      title: '🚗 AI 窓口',
      subtitle: '24 時間対応 · 平均応答 < 10 秒',
      placeholder: 'ご用件をお聞かせください...',
      welcomeMessage: 'こんにちは！AI カスタマーサポートです。\n試乗・ご購入・サービスのご相談は何でもどうぞ！',
      apiPath: 'session',
      msgPath: 'session'
    },
    staff: {
      primaryColor: '#2e7d32',
      accentColor: '#1b5e20',
      headerBg: 'linear-gradient(135deg, #2e7d32, #1b5e20)',
      headerIcon: '🤝',
      title: '🤝 AI 業務アシスタント',
      subtitle: '業務支援 · リアルタイム対応',
      placeholder: '業務に関する質問をどうぞ...',
      welcomeMessage: 'こんにちは！AI 業務アシスタントです。\nリード管理・予約確認・在庫照会など何でもご相談ください。',
      apiPath: 'staff/session',
      msgPath: 'staff'
    }
  };

  let currentTheme = null;
  let currentMode = 'customer';
  let currentProject = 'auto-dealer-demo';

  // ── 初期化 ──────────────────────────────────────────────────
  function init(opts) {
    opts = opts || {};
    currentMode = opts.mode || 'customer';
    currentProject = opts.project || 'auto-dealer-demo';
    currentTheme = THEMES[currentMode] || THEMES.customer;

    CONFIG.apiBaseUrl = (opts.apiBase || '') + '/' + currentProject + '/api/ai';
    CONFIG.chatApiBase = CONFIG.apiBaseUrl + '/chat'; // auto-dealer チャット API ベース

    if (!isUserLoggedIn()) {
      console.log('DealerChat: User not logged in, skipping initialization');
      return;
    }

    injectStyles();
    initPanel();
    initSignalR();
    // auto-dealer モードでは CLI ツール不要（フレームワーク AI 専用機能）
    configureMarked();
  }

  function isUserLoggedIn() {
    const body = document.body;
    const authValue = body.getAttribute('data-user-authenticated');
    return authValue === 'true';
  }

  // ── スタイル注入 ────────────────────────────────────────────
  function injectStyles() {
    if (document.getElementById('_dcw-styles')) return;

    const style = document.createElement('style');
    style.id = '_dcw-styles';
    style.textContent = getStylesCSS();
    document.head.appendChild(style);
  }

  function getStylesCSS() {
    const p = currentTheme.primaryColor;
    const a = currentTheme.accentColor;

    return `
      /* トリガーボタン（右下） */
      #dealer-chat-trigger {
        position: fixed;
        bottom: 24px;
        right: 24px;
        width: 60px;
        height: 60px;
        border-radius: 50%;
        background: ${p};
        color: #fff;
        border: none;
        cursor: pointer;
        font-size: 26px;
        box-shadow: 0 4px 16px rgba(0,0,0,0.28);
        transition: transform 0.2s, box-shadow 0.2s;
        z-index: 9998;
        display: flex;
        align-items: center;
        justify-content: center;
      }
      #dealer-chat-trigger:hover {
        transform: scale(1.1);
        box-shadow: 0 6px 20px rgba(0,0,0,0.35);
      }

      /* パネル */
      #dealer-chat-panel {
        position: fixed;
        bottom: 0;
        right: -600px;
        width: 600px;
        max-width: 100vw;
        height: 100vh;
        height: 100dvh;
        background: #fff !important;
        box-shadow: -4px 0 24px rgba(0, 0, 0, 0.2);
        transition: right 0.3s ease-in-out;
        z-index: 2147483647 !important;
        display: flex;
        flex-direction: column;
        isolation: isolate;
      }
      #dealer-chat-panel.open {
        right: 0;
      }

      /* 最大化 */
      #dealer-chat-panel.maximized {
        width: 100vw !important;
        right: 0 !important;
      }

      /* 最小化（折りたたみ） */
      #dealer-chat-panel.minimized {
        height: auto !important;
        bottom: 0 !important;
        right: 20px !important;
        width: 320px !important;
        border-radius: 0.75rem 0.75rem 0 0 !important;
        box-shadow: -2px -4px 16px rgba(0, 0, 0, 0.18) !important;
        transition: none !important;
      }
      #dealer-chat-panel.minimized .dc-panel-body,
      #dealer-chat-panel.minimized .dc-panel-footer,
      #dealer-chat-panel.minimized #dc-auto-scroll-btn {
        display: none !important;
      }
      #dealer-chat-panel.minimized .dc-panel-header {
        border-radius: 0.75rem 0.75rem 0 0;
        cursor: pointer;
        user-select: none;
      }

      /* ヘッダー */
      .dc-panel-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: 1rem;
        border-bottom: 1px solid #e0e0e0;
        background: ${currentTheme.headerBg};
        color: #fff;
        flex-shrink: 0;
        position: relative;
      }
      .dc-panel-header h3 {
        margin: 0;
        font-size: 1.125rem;
        font-weight: 600;
        display: flex;
        align-items: center;
        gap: 0.5rem;
      }
      .dc-panel-header .dc-header-sub {
        font-size: 0.75rem;
        opacity: 0.85;
        margin-top: 0.25rem;
      }
      .dc-panel-header > div.flex {
        position: relative;
        z-index: 10;
      }
      .dc-panel-header button {
        pointer-events: auto !important;
        cursor: pointer !important;
        position: relative;
        z-index: 11 !important;
        background: rgba(255,255,255,0.1);
        border: none;
        color: #fff;
        padding: 4px 8px;
        border-radius: 4px;
        transition: background 0.15s;
      }
      .dc-panel-header button:hover {
        background: rgba(255,255,255,0.2);
      }

      /* メッセージ領域 */
      .dc-panel-body {
        flex: 1;
        overflow-y: auto;
        padding: 1rem;
        display: flex;
        flex-direction: column;
        gap: 0.75rem;
        background: #f8f9fa !important;
        position: relative;
      }

      .dc-message-row {
        display: flex;
        flex-direction: column;
        max-width: 100%;
        gap: 0.15rem;
      }
      .dc-message-row.user {
        align-items: flex-end;
      }
      .dc-message-row.assistant {
        align-items: flex-start;
      }

      .dc-message-sender {
        font-size: 0.7rem;
        font-weight: 600;
        opacity: 0.55;
        padding: 0 0.5rem;
        letter-spacing: 0.03em;
      }

      .dc-message-inner {
        display: flex;
        align-items: flex-end;
        gap: 0.5rem;
        max-width: 88%;
      }
      .dc-message-row.user .dc-message-inner {
        flex-direction: row-reverse;
      }
      .dc-message-row.assistant .dc-message-inner {
        flex-direction: row;
      }

      .dc-message-avatar {
        width: 1.75rem;
        height: 1.75rem;
        border-radius: 50%;
        display: flex;
        align-items: center;
        justify-content: center;
        flex-shrink: 0;
      }
      .dc-message-row.user .dc-message-avatar {
        background: ${p};
        color: #fff;
        box-shadow: 0 2px 6px ${p}40;
      }
      .dc-message-row.assistant .dc-message-avatar {
        background-color: #e0e0e0;
        color: #333;
        border: 1px solid #d0d0d0;
      }

      .dc-message {
        max-width: 100%;
        padding: 0.6rem 0.9rem;
        border-radius: 1.1rem;
        word-wrap: break-word;
        font-size: 0.9rem;
        line-height: 1.55;
      }
      .dc-message.user {
        background: ${p} !important;
        color: #fff;
        border-bottom-right-radius: 0.3rem;
        box-shadow: 0 2px 8px ${p}40;
      }
      .dc-message.assistant {
        background-color: #fff !important;
        color: #212121;
        border-bottom-left-radius: 0.3rem;
        border: 1px solid #e0e0e0;
        box-shadow: 0 1px 4px rgba(0,0,0,0.07);
      }
      .dc-message.system {
        align-self: center;
        background-color: #e3f2fd;
        color: #1565c0;
        font-size: 0.78rem;
        text-align: center;
        border-radius: 1rem;
        padding: 0.3rem 0.8rem;
        max-width: 90%;
        border: 1px solid #bbdefb;
      }

      /* 進行状況 */
      .dc-progress-container {
        background-color: #f5f5f5;
        border-radius: 0.5rem;
        padding: 0.75rem;
        margin-top: 0.5rem;
      }
      .dc-progress-bar {
        height: 0.5rem;
        background-color: #e0e0e0;
        border-radius: 0.25rem;
        overflow: hidden;
        margin: 0.5rem 0;
      }
      .dc-progress-fill {
        height: 100%;
        background-color: ${p};
        transition: width 0.3s ease;
      }
      .dc-progress-text {
        font-size: 0.75rem;
        color: #666;
        display: flex;
        justify-content: space-between;
      }

      /* フッター */
      .dc-panel-footer {
        padding: 1rem;
        border-top: 1px solid #e0e0e0;
        background: #f5f5f5 !important;
        flex-shrink: 0;
        position: relative;
      }

      .dc-input-container {
        display: flex;
        gap: 0.5rem;
        margin-bottom: 0.5rem;
        position: relative;
      }
      .dc-input-container textarea {
        flex: 1;
        resize: none;
        min-height: 60px;
        max-height: 120px;
        border: 1.5px solid #e0e0e0;
        border-radius: 8px;
        padding: 9px 14px;
        font-size: 13px;
        outline: none;
        transition: border 0.2s;
        font-family: inherit;
        padding-right: 2.5rem;
      }
      .dc-input-container textarea:focus {
        border-color: ${p};
      }
      .dc-clear-input-btn {
        position: absolute;
        right: 0.5rem;
        bottom: 0.5rem;
        width: 24px;
        height: 24px;
        border-radius: 50%;
        background: rgba(0,0,0,0.05);
        border: none;
        cursor: pointer;
        display: flex;
        align-items: center;
        justify-content: center;
        color: #666;
        transition: background 0.15s, color 0.15s;
      }
      .dc-clear-input-btn:hover {
        background: rgba(0,0,0,0.1);
        color: #333;
      }

      .dc-input-actions {
        display: flex;
        gap: 0.5rem;
        justify-content: flex-end;
      }

      /* CLI 選択器 */
      .dc-cli-selector {
        display: flex;
        align-items: center;
        gap: 0.5rem;
        margin-bottom: 0.5rem;
      }
      .dc-cli-selector select {
        max-width: 150px;
        border: 1px solid #e0e0e0;
        border-radius: 4px;
        padding: 4px 8px;
        font-size: 12px;
      }

      /* ステータスインジケーター */
      .dc-status-indicator {
        display: inline-block;
        width: 0.5rem;
        height: 0.5rem;
        border-radius: 50%;
        margin-right: 0.5rem;
      }
      .dc-status-indicator.idle {
        background-color: #9e9e9e;
      }
      .dc-status-indicator.running {
        background-color: ${p};
        animation: dc-pulse 1s infinite;
      }
      .dc-status-indicator.completed {
        background-color: #4caf50;
      }
      .dc-status-indicator.error {
        background-color: #f44336;
      }

      @keyframes dc-pulse {
        0%, 100% { opacity: 1; }
        50% { opacity: 0.5; }
      }

      /* 自動スクロールボタン */
      #dc-auto-scroll-btn {
        position: absolute;
        bottom: 80px;
        right: 20px;
        z-index: 10;
        width: 36px;
        height: 36px;
        border-radius: 50%;
        background: #fff;
        border: 1px solid #e0e0e0;
        cursor: pointer;
        display: flex;
        align-items: center;
        justify-content: center;
        box-shadow: 0 2px 8px rgba(0,0,0,0.1);
      }
      #dc-auto-scroll-btn:hover {
        background: #f5f5f5;
      }

      /* アクションボタン */
      .dc-message-actions {
        display: none;
        gap: 4px;
        margin-top: 3px;
        padding: 0 0.4rem;
      }
      .dc-message-row:hover .dc-message-actions {
        display: flex;
      }
      .dc-message-row.user .dc-message-actions {
        justify-content: flex-end;
      }
      .dc-message-row.assistant .dc-message-actions {
        justify-content: flex-start;
      }

      .dc-msg-action-btn {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 1.5rem;
        height: 1.5rem;
        border-radius: 0.3rem;
        border: 1px solid #e0e0e0;
        background: #fafafa;
        color: #757575;
        cursor: pointer;
        transition: background 0.12s, color 0.12s, border-color 0.12s;
        padding: 0;
      }
      .dc-msg-action-btn:hover {
        background: #e3f2fd;
        color: #1565c0;
        border-color: #90caf9;
      }

      .dc-message-time {
        font-size: 0.65rem;
        opacity: 0.5;
        margin-top: 0.25rem;
        padding: 0 0.25rem;
        white-space: nowrap;
      }

      /* 履歴ポップアップ */
      .dc-history-popup {
        position: absolute;
        bottom: calc(100% + 4px);
        left: 0;
        right: 0;
        background: #fff;
        border: 1px solid #e0e0e0;
        border-radius: 0.5rem;
        box-shadow: 0 -4px 16px rgba(0, 0, 0, 0.12);
        z-index: 100;
        max-height: 260px;
        display: flex;
        flex-direction: column;
        overflow: hidden;
      }
      .dc-history-popup-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: 0.4rem 0.75rem;
        border-bottom: 1px solid #e0e0e0;
        font-size: 0.75rem;
        font-weight: 600;
        color: #666;
        flex-shrink: 0;
      }
      .dc-history-popup-list {
        list-style: none;
        margin: 0;
        padding: 0.25rem 0;
        overflow-y: auto;
        flex: 1;
      }
      .dc-history-item {
        padding: 0.45rem 0.75rem;
        font-size: 0.8rem;
        cursor: pointer;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        color: #333;
        transition: background 0.1s;
      }
      .dc-history-item:hover {
        background: #f5f5f5;
        color: ${p};
      }
      .dc-history-empty {
        padding: 0.6rem 0.75rem;
        font-size: 0.8rem;
        color: #999;
        text-align: center;
      }

      /* Markdown スタイル */
      .dc-message-content p {
        margin: 0.4rem 0;
      }
      .dc-message-content h1,
      .dc-message-content h2,
      .dc-message-content h3,
      .dc-message-content h4 {
        font-weight: 700;
        margin: 0.9rem 0 0.4rem;
        line-height: 1.3;
      }
      .dc-message-content h1 { font-size: 1.25rem; }
      .dc-message-content h2 { font-size: 1.1rem; border-bottom: 1px solid #e0e0e0; padding-bottom: 0.2rem; }
      .dc-message-content h3 { font-size: 1rem; }
      .dc-message-content h4 { font-size: 0.9rem; }
      .dc-message-content ul,
      .dc-message-content ol {
        margin: 0.4rem 0;
        padding-left: 1.4rem;
      }
      .dc-message-content li { margin: 0.2rem 0; }
      .dc-message-content :not(pre) > code {
        font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
        font-size: 0.85em;
        background-color: rgba(0, 0, 0, 0.08);
        color: #c0392b;
        padding: 0.15em 0.4em;
        border-radius: 0.3rem;
        white-space: nowrap;
      }
      .dc-message-content pre {
        position: relative;
        background-color: #1e1e2e;
        color: #cdd6f4;
        padding: 1rem 1rem 0.75rem;
        border-radius: 0.5rem;
        overflow-x: auto;
        margin: 0.6rem 0;
        font-size: 0.82rem;
        line-height: 1.6;
      }
      .dc-message-content pre > code {
        font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
        background: none;
        color: inherit;
        padding: 0;
        white-space: pre;
        font-size: inherit;
      }
      .dc-code-copy-btn {
        position: absolute;
        top: 0.4rem;
        right: 0.4rem;
        font-size: 0.7rem;
        padding: 0.2rem 0.5rem;
        background: rgba(255,255,255,0.15);
        color: #cdd6f4;
        border: 1px solid rgba(255,255,255,0.2);
        border-radius: 0.25rem;
        cursor: pointer;
        transition: background 0.15s;
        line-height: 1.4;
      }
      .dc-code-copy-btn:hover {
        background: rgba(255,255,255,0.25);
      }
      .dc-message-content table {
        width: 100%;
        border-collapse: collapse;
        margin: 0.6rem 0;
        font-size: 0.875rem;
      }
      .dc-message-content th,
      .dc-message-content td {
        border: 1px solid #e0e0e0;
        padding: 0.35rem 0.7rem;
        text-align: left;
      }
      .dc-message-content th {
        background-color: #f5f5f5;
        font-weight: 600;
      }
      .dc-message-content tr:nth-child(even) td {
        background-color: #fafafa;
      }
      .dc-message-content blockquote {
        border-left: 3px solid ${p};
        margin: 0.5rem 0;
        padding: 0.25rem 0.75rem;
        background-color: #f5f5f5;
        border-radius: 0 0.3rem 0.3rem 0;
        color: #666;
        font-style: italic;
      }
      .dc-message-content hr {
        border: none;
        border-top: 1px solid #e0e0e0;
        margin: 0.75rem 0;
      }
      .dc-message-content a {
        color: ${p};
        text-decoration: underline;
        text-underline-offset: 2px;
      }
      .dc-message-content a:hover {
        color: ${a};
      }
      .dc-message-content strong { font-weight: 700; }
      .dc-message-content em { font-style: italic; }

      @media (max-width: 768px) {
        #dealer-chat-panel {
          width: 100vw;
          right: -100vw;
        }
        #dealer-chat-trigger {
          bottom: 16px;
          right: 16px;
          width: 50px;
          height: 50px;
          font-size: 22px;
        }
      }
    `;
  }

  // ── パネル構築 ──────────────────────────────────────────────
  function initPanel() {
    const trigger = document.createElement('button');
    trigger.id = 'dealer-chat-trigger';
    trigger.innerHTML = currentTheme.headerIcon;
    trigger.onclick = togglePanel;
    document.body.appendChild(trigger);

    const panel = document.createElement('div');
    panel.id = 'dealer-chat-panel';
    panel.className = '';
    panel.innerHTML = buildPanelHTML();
    document.body.appendChild(panel);

    startDealerSession().then(function() {
      // サーバーから履歴を復元（sessionStorage はフォールバック用）
      restoreFromServer();
    });

    bindPanelEvents();
  }

  // ── auto-dealer セッション開始 ──────────────────────────────
  async function startDealerSession() {
    const lsKey = 'aw_dealer_conv_' + (CONFIG.project || 'auto-dealer-demo') + '_' + currentMode;

    // 1️⃣ localStorage から復元（クロスブラウザ対応）
    try {
      const storedConvId = localStorage.getItem(lsKey);
      if (storedConvId) {
        dealerConversationId = storedConvId;
        console.log('DealerChat: localStorage から conversationId を復元しました', dealerConversationId);
        return;
      }
    } catch(e) {}

    // 2️⃣ 認証セッションからサーバー側で検索（クロスブラウザ・クロスデバイス対応）
    try {
      const mySessionResp = await fetch(CONFIG.chatApiBase + '/my-session');
      if (mySessionResp.ok) {
        const data = await mySessionResp.json();
        if (data.conversationId) {
          dealerConversationId = data.conversationId;
          try { localStorage.setItem(lsKey, dealerConversationId); } catch(e) {}
          console.log('DealerChat: サーバー認証セッションから conversationId を復元しました', dealerConversationId);
          return;
        }
      }
    } catch (e) {
      console.warn('DealerChat: my-session 取得失敗、次のフォールバックへ', e);
    }

    // 3️⃣ userId ベースのフォールバック（未ログインゲスト向け）
    try {
      const userId = getUserId();
      if (userId) {
        const historyUrl = CONFIG.chatApiBase + '/user-history?userId=' + encodeURIComponent(userId) + '&limit=1';
        const resp = await fetch(historyUrl);
        if (resp.ok) {
          const data = await resp.json();
          if (data.conversationId) {
            dealerConversationId = data.conversationId;
            try { localStorage.setItem(lsKey, dealerConversationId); } catch(e) {}
            console.log('DealerChat: user-history から conversationId を復元しました', dealerConversationId);
            return;
          }
        }
      }
    } catch (e) {
      console.warn('DealerChat: user-history 取得失敗、新規セッションを作成します', e);
    }

    // 4️⃣ 新規セッション作成
    try {
      const sessionUrl = CONFIG.chatApiBase + '/' + currentTheme.apiPath;
      console.log('DealerChat: セッション開始 URL:', sessionUrl);

      const resp = await fetch(sessionUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ channel: currentMode })
      });

      if (resp.ok) {
        const data = await resp.json();
        dealerConversationId = data.conversationId;
        try { localStorage.setItem(lsKey, dealerConversationId); } catch(e) {}
        console.log('DealerChat: セッションを開始しました', dealerConversationId);
      } else {
        const errText = await resp.text().catch(() => '');
        console.error('DealerChat: セッション開始失敗', resp.status, errText);
        // エラーメッセージを表示
        if (resp.status === 401) {
          addMessage('ログインが必要です。ログインしてください。', 'system');
        } else if (resp.status === 500) {
          addMessage('サーバーエラーが発生しました。しばらくお待ちください。', 'system');
        } else {
          addMessage('セッションの開始に失敗しました：' + resp.status, 'system');
        }
      }
    } catch (e) {
      console.error('DealerChat: セッション開始エラー', e);
      let errorMsg = 'セッション開始エラー：' + e.message;
      if (e.message.includes('Failed to fetch')) {
        errorMsg = 'サーバーに接続できません。サーバーが起動しているか確認してください。';
      }
      addMessage(errorMsg, 'system');
    }
  }

  // ── 获取当前用户 ID ──────────────────────────────
  function getUserId() {
    // 从页面 data 属性或 localStorage 获取用户 ID
    const body = document.body;
    const userId = body.getAttribute('data-user-id') || 
                   body.getAttribute('data-username') ||
                   localStorage.getItem('userName');
    return userId || null;
  }

  function buildPanelHTML() {
    return `
      <div class="dc-panel-header">
        <div>
          <h3>
            <span>${currentTheme.headerIcon}</span>
            <span>${currentTheme.title}</span>
          </h3>
          <div class="dc-header-sub">${currentTheme.subtitle}</div>
        </div>
        <div class="flex gap-1">
          <button id="dc-maximize-btn" class="btn btn-ghost btn-sm btn-circle" title="最大化">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5l-5-5m5 5v-4m0 4h-4" />
            </svg>
          </button>
          <button id="dc-collapse-btn" class="btn btn-ghost btn-sm btn-circle" title="最小化">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 12H4" />
            </svg>
          </button>
          <button id="dc-close-btn" class="btn btn-ghost btn-sm btn-circle" title="閉じる">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
      </div>

      <div class="dc-panel-body" id="dc-messages-container">
        <div class="dc-message-row assistant">
          <div class="dc-message-sender">AI Assistant</div>
          <div class="dc-message-inner">
            <div class="dc-message-avatar">
              <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17H3a2 2 0 01-2-2V5a2 2 0 012-2h14a2 2 0 012 2v10a2 2 0 01-2 2h-2"/>
              </svg>
            </div>
            <div class="dc-message assistant">
              <div class="dc-message-content">
                ${escapeHtml(currentTheme.welcomeMessage)}
              </div>
            </div>
          </div>
        </div>
      </div>

      <button id="dc-auto-scroll-btn" class="btn btn-sm btn-circle opacity-75" title="Auto scroll">
        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 14l-7 7m0 0l-7-7m7 7V3" />
        </svg>
      </button>

      <div class="dc-panel-footer">
        <div class="dc-cli-selector">
          <label for="dc-cli-tool" class="text-sm">AI:</label>
          <select id="dc-cli-tool" class="select select-sm select-bordered">
            <option value="claude">Claude Code</option>
            <option value="qwen">Qwen Code</option>
            <option value="codex">OpenAI Codex</option>
            <option value="gemini">Google Gemini</option>
            <option value="copilot">GitHub Copilot</option>
            <option value="ollama">Ollama (本地模型)</option>
            <option value="lmstudio">LM Studio (本地)</option>
            <option value="mock">Mock (Test)</option>
          </select>
          <span id="cli-status" class="text-xs opacity-50 ml-2"></span>
        </div>

        <div class="dc-input-container">
          <textarea
            id="dc-input-message"
            class="textarea textarea-bordered"
            placeholder="${escapeHtml(currentTheme.placeholder)}"
            rows="2"></textarea>
          <button id="dc-clear-input-btn" class="dc-clear-input-btn" title="清空输入" style="display:none">
            <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" xmlns="http://www.w3.org/2000/svg">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"/>
            </svg>
          </button>
        </div>

        <div id="dc-history-popup" class="dc-history-popup" style="display:none">
          <div class="dc-history-popup-header">
            <span>入力履歴</span>
            <button id="dc-history-popup-close" class="btn btn-ghost btn-xs btn-circle">✕</button>
          </div>
          <ul id="dc-history-popup-list" class="dc-history-popup-list"></ul>
        </div>

        <div class="dc-input-actions">
          <button id="dc-history-popup-btn" class="btn btn-ghost btn-sm" title="入力履歴を表示">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          </button>
          <button id="dc-stop-btn" class="btn btn-ghost btn-sm" disabled>
            <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 10a1 1 0 00-1 1v4a1 1 0 001 1h4a1 1 0 001-1v-4a1 1 0 00-1-1H9z" />
            </svg>
            停止
          </button>
          <button id="dc-clear-btn" class="btn btn-ghost btn-sm" style="display:none">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
            </svg>
          </button>
          <button id="dc-send-btn" class="btn btn-primary btn-sm">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8" />
            </svg>
            发送
          </button>
        </div>
      </div>
    `;
  }

  // ── イベントバインディング ───────────────────────────────────
  function bindPanelEvents() {
    document.getElementById('dc-maximize-btn').onclick = toggleMaximize;
    document.getElementById('dc-collapse-btn').onclick = toggleMinimize;
    document.getElementById('dc-close-btn').onclick = function() {
      if (isPanelOpen) togglePanel();
    };

    document.querySelector('.dc-panel-header').addEventListener('click', function(e) {
      if (isPanelMinimized && !e.target.closest('button')) {
        toggleMinimize();
      }
    });

    if (window.visualViewport) {
      window.visualViewport.addEventListener('resize', adjustPanelForKeyboard);
      window.visualViewport.addEventListener('scroll', adjustPanelForKeyboard);
    }

    document.getElementById('dc-send-btn').onclick = sendMessage;
    document.getElementById('dc-stop-btn').onclick = stopTask;
    document.getElementById('dc-clear-btn').onclick = clearMessages;
    document.getElementById('dc-auto-scroll-btn').onclick = toggleAutoScroll;

    const dcInput = document.getElementById('dc-input-message');
    const dcClearInputBtn = document.getElementById('dc-clear-input-btn');

    // 清空输入框按钮
    if (dcClearInputBtn) {
      dcClearInputBtn.onclick = function() {
        dcInput.value = '';
        dcClearInputBtn.style.display = 'none';
        dcInput.focus();
      };
    }

    // 监听输入框变化，显示/隐藏清空按钮
    dcInput.addEventListener('input', function() {
      if (dcClearInputBtn) {
        dcClearInputBtn.style.display = this.value.length > 0 ? 'flex' : 'none';
      }
    });

    document.getElementById('dc-input-message').addEventListener('keydown', function(e) {
      if (e.key === 'Enter' && e.ctrlKey) {
        e.preventDefault();
        sendMessage();
        return;
      }
      if (e.key === 'ArrowUp' && this.selectionStart === 0) {
        if (inputHistory.length === 0) return;
        e.preventDefault();
        if (inputHistoryIndex === -1) {
          inputCurrentDraft = this.value;
        }
        if (inputHistoryIndex < inputHistory.length - 1) {
          inputHistoryIndex++;
        }
        this.value = inputHistory[inputHistoryIndex] || '';
        this.setSelectionRange(0, 0);
        return;
      }
      if (e.key === 'ArrowDown' && this.selectionStart === this.value.length) {
        if (inputHistoryIndex === -1) return;
        e.preventDefault();
        inputHistoryIndex--;
        if (inputHistoryIndex === -1) {
          this.value = inputCurrentDraft;
        } else {
          this.value = inputHistory[inputHistoryIndex] || '';
        }
        const len = this.value.length;
        this.setSelectionRange(len, len);
        return;
      }
      if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown' && e.key !== 'Shift') {
        inputHistoryIndex = -1;
      }
    });

    document.getElementById('dc-history-popup-btn').onclick = function(e) {
      e.stopPropagation();
      toggleHistoryPopup();
    };
    document.getElementById('dc-history-popup-close').onclick = function(e) {
      e.stopPropagation();
      closeHistoryPopup();
    };

    document.addEventListener('click', function(e) {
      const popup = document.getElementById('dc-history-popup');
      const btn = document.getElementById('dc-history-popup-btn');
      if (popup && popup.style.display !== 'none' &&
          !popup.contains(e.target) && e.target !== btn && !btn.contains(e.target)) {
        closeHistoryPopup();
      }
    });

    document.getElementById('dc-cli-tool').addEventListener('change', function() {
      try { sessionStorage.setItem(TOOL_STORAGE_KEY, this.value); } catch(e) {}
      checkCliStatus(this.value);
    });
  }

  // ── SignalR 初期化 ──────────────────────────────────────────
  function initSignalR() {
    const signalRSources = [
      '/lib/microsoft/signalr/dist/browser/signalr.min.js',
      '/lib/signalr/signalr.min.js',
      'https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js'
    ];
    loadSignalRClient(0, signalRSources);
  }

  function loadSignalRClient(index, sources) {
    if (index >= sources.length) {
      console.warn('SignalR client not available, using polling fallback');
      return;
    }
    const script = document.createElement('script');
    script.src = sources[index];
    script.onload = function() {
      console.log('SignalR client loaded from:', sources[index]);
      connectSignalR();
    };
    script.onerror = function() {
      console.warn('Failed to load SignalR from:', sources[index]);
      loadSignalRClient(index + 1, sources);
    };
    document.head.appendChild(script);
  }

  function connectSignalR() {
    if (typeof signalR === 'undefined') {
      console.warn('SignalR not available');
      return;
    }
    connection = new signalR.HubConnectionBuilder()
      .withUrl(CONFIG.signalRUrl)
      .withAutomaticReconnect()
      .build();

    connection.on('ReceiveProgress', function(data) {
      handleProgressUpdate(data);
    });

    connection.start()
      .then(() => console.log('SignalR connected'))
      .catch(err => console.error('SignalR connection error:', err));
  }

  // ── CLI ツール読み込み ──────────────────────────────────────
  async function loadCliTools() {
    try {
      const response = await fetch(CONFIG.apiBaseUrl.replace('/api/ai', '/cli-tools'));
      if (response.ok) {
        const data = await response.json();
        if (data.defaultTool) {
          CONFIG.defaultCliTool = data.defaultTool;
        }
        updateCliSelector(data.available, data.defaultTool);
      }
    } catch (error) {
      console.error('Failed to load CLI tools:', error);
    }
  }

  function updateCliSelector(tools, serverDefault) {
    const selector = document.getElementById('dc-cli-tool');
    if (!selector) return;

    const prevValue = selector.value;
    selector.innerHTML = '';

    for (const [name, tool] of Object.entries(tools || {})) {
      const option = document.createElement('option');
      option.value = name;
      option.textContent = tool.displayName || name;
      if (!tool.installed) {
        option.disabled = true;
        option.textContent += ' (未安装)';
      }
      selector.appendChild(option);
    }

    const savedTool = (() => { try { return sessionStorage.getItem(TOOL_STORAGE_KEY); } catch(e) { return null; } })();
    const restoreValue = savedTool || prevValue || serverDefault || CONFIG.defaultCliTool;
    if (restoreValue && selector.querySelector(`option[value="${restoreValue}"]:not([disabled])`)) {
      selector.value = restoreValue;
    }

    updateCliStatusDisplay(tools ? tools[selector.value] : null);
  }

  async function checkCliStatus(toolName) {
    const statusEl = document.getElementById('cli-status');
    if (!statusEl) return;
    try {
      const response = await fetch(CONFIG.apiBaseUrl.replace('/api/ai', '/cli-tools'));
      if (response.ok) {
        const data = await response.json();
        const tool = data.available ? data.available[toolName] : null;
        updateCliStatusDisplay(tool, statusEl);
      }
    } catch (error) {
      if (statusEl) statusEl.textContent = '?';
    }
  }

  function updateCliStatusDisplay(tool, statusEl) {
    statusEl = statusEl || document.getElementById('cli-status');
    if (!statusEl) return;
    if (!tool) {
      statusEl.textContent = '';
      return;
    }
    if (tool.installed && tool.authenticated) {
      statusEl.textContent = '✓ 就绪';
      statusEl.className = 'text-xs text-success ml-2';
    } else if (tool.installed) {
      statusEl.textContent = '⚠ 未认证';
      statusEl.className = 'text-xs text-warning ml-2';
    } else {
      statusEl.textContent = '✗ 未安装';
      statusEl.className = 'text-xs text-error ml-2';
    }
  }

  // ── パネル操作 ──────────────────────────────────────────────
  function togglePanel() {
    if (isPanelOpen) {
      closePanel();
    } else {
      openPanel();
    }
  }

  function openPanel() {
    const panel = document.getElementById('dealer-chat-panel');
    const trigger = document.getElementById('dealer-chat-trigger');
    panel.classList.add('open');
    trigger.style.display = 'none';
    isPanelOpen = true;
    if (window.visualViewport) adjustPanelForKeyboard();
  }

  function closePanel() {
    const panel = document.getElementById('dealer-chat-panel');
    const trigger = document.getElementById('dealer-chat-trigger');
    panel.classList.remove('open');
    trigger.style.display = 'flex';
    isPanelOpen = false;
    resetPanelSize();
  }

  function toggleMinimize() {
    const panel = document.getElementById('dealer-chat-panel');
    const btn = document.getElementById('dc-collapse-btn');
    if (!panel) return;

    isPanelMinimized = !isPanelMinimized;
    panel.classList.toggle('minimized', isPanelMinimized);

    if (isPanelMinimized) {
      if (isMaximized) toggleMaximize();
      if (btn) btn.title = '展開';
      btn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 15l7-7 7 7" /></svg>';
    } else {
      if (btn) btn.title = '最小化';
      btn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 12H4" /></svg>';
    }
  }

  function toggleMaximize() {
    const panel = document.getElementById('dealer-chat-panel');
    const trigger = document.getElementById('dealer-chat-trigger');
    const btn = document.getElementById('dc-maximize-btn');
    if (!panel) return;

    if (isMaximized) {
      panel.style.width = previousWidth || '';
      panel.style.right = previousRight || '';
      panel.classList.remove('maximized');
      if (trigger) trigger.style.display = 'flex';
      isMaximized = false;
      if (btn) btn.title = 'Maximize';
      btn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5l-5-5m5 5v-4m0 4h-4" /></svg>';
    } else {
      previousWidth = panel.style.width;
      previousRight = panel.style.right;
      panel.style.width = '100vw';
      panel.style.right = '0';
      panel.classList.add('maximized');
      if (trigger) trigger.style.display = 'none';
      isMaximized = true;
      if (btn) btn.title = 'Restore';
      btn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 9V4.5M9 9H4.5M9 9L3.75 3.75M9 15v4.5M9 15H4.5M9 15l-5.25 5.25M15 9h4.5M15 9V4.5M15 9l5.25-5.25M15 15h4.5M15 15v4.5m0-4.5l5.25 5.25" /></svg>';
    }
  }

  function adjustPanelForKeyboard() {
    const panel = document.getElementById('dealer-chat-panel');
    if (!panel || !isPanelOpen) return;
    const vv = window.visualViewport;
    panel.style.height = vv.height + 'px';
    panel.style.top = vv.offsetTop + 'px';
  }

  function resetPanelSize() {
    const panel = document.getElementById('dealer-chat-panel');
    if (!panel) return;
    panel.style.height = '';
    panel.style.top = '';
  }

  // ── メッセージ送信 ──────────────────────────────────────────
  async function sendMessage() {
    const input = document.getElementById('dc-input-message');
    const cliSelector = document.getElementById('dc-cli-tool');
    const message = input.value.trim();

    if (!message) return;

    if (inputHistory[0] !== message) {
      inputHistory.unshift(message);
      if (inputHistory.length > 100) inputHistory.pop();
    }
    inputHistoryIndex = -1;
    inputCurrentDraft = '';

    addMessage(message, 'user');
    input.value = '';
    updateStatus('running');
    setSendingState(true);

    try {
      // セッションがなければ先に開始
      if (!dealerConversationId) {
        await startDealerSession();
      }
      if (!dealerConversationId) {
        addMessage('セッションを開始できませんでした。ページを再読み込みしてください。', 'system');
        updateStatus('error');
        setSendingState(false);
        return;
      }

      const msgUrl = CONFIG.chatApiBase + '/' + currentTheme.msgPath + '/' + dealerConversationId + '/message';
      const response = await fetch(msgUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: message })
      });

      if (response.ok) {
        const data = await response.json();
        const reply = data.responseText || data.result || data.message || '';
        if (reply.trim()) {
          addMessage(reply, 'assistant');
        }
        updateStatus('completed');
      } else {
        let errMsg = 'エラーが発生しました';
        try {
          const errBody = await response.json();
          errMsg = errBody.error || errMsg;
        } catch (_) {}
        addMessage(`エラー: ${errMsg}`, 'system');
        updateStatus('error');
      }
    } catch (error) {
      console.error('Send message error:', error);
      let errorMsg = error.message || '不明なエラー'; if (errorMsg.includes('Failed to fetch')) { errorMsg = 'サーバーに接続できません。サーバーが起動しているか確認してください。'; } addMessage(`リクエスト失敗：${errorMsg}`, 'system');
      updateStatus('error');
    } finally {
      setSendingState(false);
    }
  }

  // ── ポーリング ──────────────────────────────────────────────
  async function pollTaskResult(taskId) {
    const progressEl = addProgressContainer(taskId);
    let lastLogCount = 0;
    const TIMEOUT_MS = 30 * 60 * 1000;
    const deadline = Date.now() + TIMEOUT_MS;

    while (Date.now() < deadline) {
      try {
        const response = await fetch(CONFIG.apiBaseUrl + '/tasks/' + taskId);
        if (!response.ok) break;

        const data = await response.json();

        if (progressEl) {
          const fill = progressEl.querySelector('.dc-progress-fill');
          const status = progressEl.querySelector('.progress-status');
          const logsContainer = progressEl.querySelector('.dc-logs ul');

          if (fill) fill.style.width = data.progress + '%';
          if (status) status.textContent = translateStatus(data.status);

          if (logsContainer && data.logs && data.logs.length > lastLogCount) {
            for (let j = lastLogCount; j < data.logs.length; j++) {
              const logText = parseLogEntry(data.logs[j]);
              if (logText) {
                const li = document.createElement('li');
                li.textContent = logText;
                li.className = 'text-xs text-gray-600 py-1';
                logsContainer.appendChild(li);
              }
            }
            lastLogCount = data.logs.length;
            scrollToBottom();
          }
        }

        if (!currentTaskId) return;

        if (data.status === 'Completed' || data.status === 2) {
          if (data.sessionId) {
            currentSessionId = data.sessionId;
          }
          currentTaskId = null;
          if (data.result && data.result.trim()) {
            addMessage(data.result, 'assistant');
          }
          return;
        } else if (data.status === 'Cancelled' || data.status === 4) {
          currentTaskId = null;
          return;
        } else if (data.status === 'Failed' || data.status === 3) {
          currentTaskId = null;
          addMessage('❌ ' + (data.error || 'タスクが失敗しました'), 'system');
          return;
        }

        await new Promise(resolve => setTimeout(resolve, 2000));
      } catch (error) {
        console.error('Polling error:', error);
        break;
      }
    }

    addMessage('⚠️ 任务超时或中断', 'system');
  }

  function translateStatus(status) {
    if (typeof status === 'number') {
      switch (status) {
        case 0: return 'Pending';
        case 1: return 'Running';
        case 2: return 'Completed';
        case 3: return 'Failed';
        case 4: return 'Cancelled';
        default: return status;
      }
    }
    return status;
  }

  function parseLogEntry(logEntry) {
    if (!logEntry || !logEntry.trim()) return null;
    return logEntry;
  }

  function handleProgressUpdate(data) {
    if (!data) return;
    const progressEl = document.querySelector('[data-task-id="' + data.id + '"]');
    if (progressEl) {
      const fill = progressEl.querySelector('.dc-progress-fill');
      const status = progressEl.querySelector('.progress-status');
      const logsContainer = progressEl.querySelector('.dc-logs ul');

      if (fill) fill.style.width = (data.progress || 0) + '%';
      if (status) status.textContent = translateStatus(data.status);

      if (logsContainer && Array.isArray(data.logs) && data.logs.length > 0) {
        logsContainer.innerHTML = '';
        for (const log of data.logs) {
          if (log && log.trim()) {
            const li = document.createElement('li');
            li.textContent = log;
            li.className = 'text-xs text-gray-600 py-1';
            logsContainer.appendChild(li);
          }
        }
        if (autoScroll) scrollToBottom();
      }
    }

    if (data.id !== currentTaskId) return;

    if (data.status === 'Completed' || data.status === 2) {
      if (data.result && data.result.trim()) {
        addMessage(data.result, 'assistant');
      }
      updateStatus('completed');
      setSendingState(false);
      currentTaskId = null;
    } else if (data.status === 'Failed' || data.status === 3) {
      addMessage('❌ ' + (data.error || '任务失败'), 'system');
      updateStatus('error');
      setSendingState(false);
      currentTaskId = null;
    } else if (data.status === 'Cancelled' || data.status === 4) {
      updateStatus('idle');
      setSendingState(false);
      currentTaskId = null;
    }
  }

  async function stopTask() {
    if (!currentTaskId) return;
    const taskId = currentTaskId;
    currentTaskId = null;
    setSendingState(false);
    updateStatus('idle');
    try {
      await fetch(CONFIG.apiBaseUrl + '/tasks/' + taskId, { method: 'DELETE' });
      addMessage('⏹ タスクをキャンセルしました', 'system');
    } catch (error) {
      console.error('Failed to stop task:', error);
    }
  }

  // ── メッセージ表示 ──────────────────────────────────────────
  function formatTimestamp(date) {
    const d = (date instanceof Date) ? date : new Date(date);
    if (!date || isNaN(d.getTime())) d.setTime(Date.now());
    const pad = function(n) { return String(n).padStart(2, '0'); };
    return d.getFullYear() + '/' + pad(d.getMonth() + 1) + '/' + pad(d.getDate()) +
           ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
  }

  function addMessage(content, type, skipSave, timestamp) {
    const container = document.getElementById('dc-messages-container');
    const timeStr = timestamp || formatTimestamp(new Date());

    if (type === 'system') {
      const messageEl = document.createElement('div');
      messageEl.className = 'dc-message system';
      messageEl.textContent = content;
      container.appendChild(messageEl);
    } else {
      const rowEl = document.createElement('div');
      rowEl.className = 'dc-message-row ' + type;

      const senderEl = document.createElement('div');
      senderEl.className = 'dc-message-sender';
      senderEl.textContent = type === 'user' ? 'You' : 'AI Assistant';
      rowEl.appendChild(senderEl);

      const innerEl = document.createElement('div');
      innerEl.className = 'dc-message-inner';

      const avatar = document.createElement('div');
      avatar.className = 'dc-message-avatar';
      if (type === 'user') {
        avatar.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z"/></svg>';
      } else {
        avatar.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17H3a2 2 0 01-2-2V5a2 2 0 012-2h14a2 2 0 012 2v10a2 2 0 01-2 2h-2"/></svg>';
      }

      const messageEl = document.createElement('div');
      messageEl.className = 'dc-message ' + type;

      const contentEl = document.createElement('div');
      contentEl.className = 'dc-message-content';
      if (type === 'assistant') {
        contentEl.innerHTML = renderMarkdown(content);
        contentEl.querySelectorAll('pre > code').forEach(addCopyButton);
      } else {
        contentEl.innerHTML = renderMarkdown(content);
      }

      const timeEl = document.createElement('div');
      timeEl.className = 'dc-message-time';
      timeEl.textContent = timeStr;
      contentEl.appendChild(timeEl);

      messageEl.appendChild(contentEl);
      innerEl.appendChild(avatar);
      innerEl.appendChild(messageEl);
      rowEl.appendChild(innerEl);

      const actionsEl = document.createElement('div');
      actionsEl.className = 'dc-message-actions';

      const copyBtn = document.createElement('button');
      copyBtn.className = 'dc-msg-action-btn';
      copyBtn.title = 'コピー';
      copyBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z"/></svg>';
      copyBtn.onclick = function() {
        navigator.clipboard.writeText(content).then(function() {
          copyBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"/></svg>';
          setTimeout(function() {
            copyBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z"/></svg>';
          }, 2000);
        }).catch(function() {});
      };
      actionsEl.appendChild(copyBtn);

      const quoteBtn = document.createElement('button');
      quoteBtn.className = 'dc-msg-action-btn';
      quoteBtn.title = '引用して返信';
      quoteBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 10h10a8 8 0 018 8v2M3 10l6 6m-6-6l6-6"/></svg>';
      quoteBtn.onclick = function() {
        const input = document.getElementById('dc-input-message');
        if (!input) return;
        const quoted = content.split('\n').map(function(line) { return '> ' + line; }).join('\n');
        input.value = quoted + '\n' + input.value;
        input.focus();
        const len = input.value.length;
        input.setSelectionRange(len, len);
        if (isPanelMinimized) toggleMinimize();
        if (!isPanelOpen) openPanel();
      };
      actionsEl.appendChild(quoteBtn);

      rowEl.appendChild(actionsEl);
      container.appendChild(rowEl);
    }

    if (!skipSave) {
      chatHistory.push({ content: content, type: type, timestamp: timeStr });
      saveHistory();
      saveMessageToServer(content, type);
    }

    if (autoScroll) {
      scrollToBottom();
    }
  }

  function saveHistory() {
    try {
      const key = STORAGE_KEY_PREFIX + currentMode;
      sessionStorage.setItem(key, JSON.stringify(chatHistory));
    } catch (e) {}
  }

  function restoreFromStorage() {
    try {
      const key = STORAGE_KEY_PREFIX + currentMode;
      const saved = sessionStorage.getItem(key);
      if (!saved) return false;
      const history = JSON.parse(saved);
      if (!Array.isArray(history) || history.length === 0) return false;
      chatHistory = history;
      const container = document.getElementById('dc-messages-container');
      container.innerHTML = '';
      history.forEach(function(msg) {
        addMessage(msg.content, msg.type, true, msg.timestamp);
      });
      return true;
    } catch (e) {
      return false;
    }
  }

  async function restoreFromServer() {
    // ✅ 修复: 优先使用业务 API (ai_messages 表) 而不是 AI CLI 历史 (chat.db)
    // 如果有会话 ID,从业务数据库获取消息
    if (dealerConversationId) {
      try {
        const resp = await fetch(CONFIG.chatApiBase + '/session/' + dealerConversationId + '/messages');
        if (resp.ok) {
          const messages = await resp.json();
          if (Array.isArray(messages) && messages.length > 0) {
            const container = document.getElementById('dc-messages-container');
            container.innerHTML = '';
            chatHistory = [];
            messages.forEach(function(m) {
              // sender: customer | ai | agent → user | assistant
              const type = (m.sender === 'customer') ? 'user' : 'assistant';
              const ts = m.timestamp || '';
              chatHistory.push({ content: m.content, type: type, timestamp: ts });
              addMessage(m.content, type, true, ts);
            });
            saveHistory();
            return; // ✅ 成功获取,直接返回
          }
        }
      } catch (e) {
        console.warn('从业务 API 恢复失败,尝试 AI CLI 历史 API:', e);
      }
    }

    // フォールバック: AI CLI 历史 API (chat.db)
    const chatContext = currentMode === 'customer' ? 'dealer-customer' : 'dealer-staff';
    try {
      const resp = await fetch(CONFIG.apiBaseUrl + '/history?limit=50&context=' + chatContext);
      if (!resp.ok) {
        // サーバーに履歴がない場合は sessionStorage から復元（フォールバック）
        restoreFromStorage();
        return;
      }
      const messages = await resp.json();
      if (!Array.isArray(messages) || messages.length === 0) {
        // サーバーに履歴がない場合は sessionStorage もクリア（整合性保持）
        const key = STORAGE_KEY_PREFIX + currentMode;
        sessionStorage.removeItem(key);
        restoreFromStorage();
        return;
      }
      // サーバーデータでローカルキャッシュを上書き
      chatHistory = messages.map(function(m) {
        return { content: m.content, type: m.type, timestamp: m.displayTime || m.createdAt || '' };
      });
      const container = document.getElementById('dc-messages-container');
      container.innerHTML = '';
      chatHistory.forEach(function(msg) {
        addMessage(msg.content, msg.type, true, msg.timestamp);
      });
      // sessionStorage もサーバーデータで更新
      saveHistory();
    } catch (e) {
      // サーバー取得失敗時は sessionStorage から復元（フォールバック）
      restoreFromStorage();
    }
  }

  // セッション API から履歴を復元（旧バージョンとの互換性）
  async function restoreFromSessionApi() {
    if (!dealerConversationId) return;
    try {
      const resp = await fetch(CONFIG.chatApiBase + '/session/' + dealerConversationId + '/messages');
      if (!resp.ok) return;
      const messages = await resp.json();
      if (!Array.isArray(messages) || messages.length === 0) return;
      const container = document.getElementById('dc-messages-container');
      container.innerHTML = '';
      chatHistory = [];
      messages.forEach(function(m) {
        // sender: customer | ai | agent → user | assistant
        const type = (m.sender === 'customer') ? 'user' : 'assistant';
        const ts = m.timestamp || '';
        chatHistory.push({ content: m.content, type: type, timestamp: ts });
        addMessage(m.content, type, true, ts);
      });
      saveHistory();
    } catch (e) {
      // セッション API も失敗した場合は何もしない
    }
  }

  function saveMessageToServer(content, type) {
    // グローバル AI 履歴にも保存（別タブ・ブラウザ再起動対応）
    const chatContext = currentMode === 'customer' ? 'dealer-customer' : 'dealer-staff';
    fetch(CONFIG.apiBaseUrl + '/history', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content: content, type: type, chatContext: chatContext })
    }).catch(function() {});
  }

  function configureMarked() {
    if (typeof marked === 'undefined') return;
    marked.setOptions({
      breaks: true,
      gfm: true
    });
  }

  function renderMarkdown(text) {
    if (typeof marked === 'undefined' || !text) {
      return '<p>' + escapeHtml(text || '') + '</p>';
    }
    try {
      return marked.parse(text);
    } catch (e) {
      return '<p>' + escapeHtml(text) + '</p>';
    }
  }

  function addCopyButton(codeEl) {
    const pre = codeEl.parentElement;
    pre.style.position = 'relative';
    const btn = document.createElement('button');
    btn.className = 'dc-code-copy-btn';
    btn.textContent = 'Copy';
    btn.onclick = function() {
      navigator.clipboard.writeText(codeEl.textContent).then(function() {
        btn.textContent = 'Copied!';
        setTimeout(function() { btn.textContent = 'Copy'; }, 2000);
      }).catch(function() {
        btn.textContent = 'Error';
        setTimeout(function() { btn.textContent = 'Copy'; }, 2000);
      });
    };
    pre.appendChild(btn);
  }

  function addProgressContainer(taskId) {
    const container = document.getElementById('dc-messages-container');
    const progressEl = document.createElement('div');
    progressEl.className = 'dc-message assistant';
    progressEl.setAttribute('data-task-id', taskId);
    progressEl.innerHTML = `
      <div>⏳ 実行中...</div>
      <div class="dc-progress-container">
        <div class="dc-progress-bar">
          <div class="dc-progress-fill" style="width: 0%"></div>
        </div>
        <div class="dc-progress-text">
          <span class="progress-status">処理中...</span>
        </div>
        <div class="dc-logs">
          <ul></ul>
        </div>
      </div>
    `;
    container.appendChild(progressEl);
    scrollToBottom();
    return progressEl;
  }

  function updateStatus(status) {
    const indicator = document.getElementById('dc-status-indicator');
    if (indicator) {
      indicator.className = 'dc-status-indicator ' + status;
    }
  }

  function setSendingState(sending, phase) {
    const sendBtn = document.getElementById('dc-send-btn');
    const stopBtn = document.getElementById('dc-stop-btn');
    const input = document.getElementById('dc-input-message');

    sendBtn.disabled = sending;
    stopBtn.disabled = !sending;
    input.disabled = sending;

    if (sending) {
      const spinnerSvg = '<svg class="animate-spin h-4 w-4" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">' +
        '<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>' +
        '<path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>' +
        '</svg>';
      const label = (phase === 'executing') ? '実行中...' : '送信中...';
      sendBtn.innerHTML = spinnerSvg + label;
    } else {
      sendBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">' +
        '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8" />' +
        '</svg> 发送';
    }
  }

  function clearMessages() {
    if (currentTaskId) {
      const taskId = currentTaskId;
      currentTaskId = null;
      fetch(CONFIG.apiBaseUrl + '/tasks/' + taskId, { method: 'DELETE' }).catch(function() {});
    }

    chatHistory = [];
    try {
      const key = STORAGE_KEY_PREFIX + currentMode;
      sessionStorage.removeItem(key);
    } catch (e) {}
    fetch(CONFIG.apiBaseUrl + '/history', { method: 'DELETE' }).catch(function() {});

    const container = document.getElementById('dc-messages-container');
    container.innerHTML = '<div class="dc-message assistant"><div class="dc-message-content">🤖 对话已清除。有什么可以帮你的？</div></div>';

    currentTaskId = null;
    currentSessionId = null;
    updateStatus('idle');
    setSendingState(false);
  }

  function toggleAutoScroll() {
    autoScroll = !autoScroll;
    const btn = document.getElementById('dc-auto-scroll-btn');
    btn.classList.toggle('opacity-75');
    btn.classList.toggle('btn-active');
  }

  function scrollToBottom() {
    const container = document.getElementById('dc-messages-container');
    if (container) {
      container.scrollTop = container.scrollHeight;
    }
  }

  function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }

  // ── 履歴ポップアップ ────────────────────────────────────────
  function toggleHistoryPopup() {
    const popup = document.getElementById('dc-history-popup');
    if (!popup) return;
    if (popup.style.display === 'none') {
      openHistoryPopup();
    } else {
      closeHistoryPopup();
    }
  }

  function openHistoryPopup() {
    const popup = document.getElementById('dc-history-popup');
    const list = document.getElementById('dc-history-popup-list');
    if (!popup || !list) return;

    list.innerHTML = '';
    if (inputHistory.length === 0) {
      const empty = document.createElement('li');
      empty.className = 'dc-history-empty';
      empty.textContent = '履歴はありません';
      list.appendChild(empty);
    } else {
      inputHistory.forEach(function(text, idx) {
        const li = document.createElement('li');
        li.className = 'dc-history-item';
        li.title = text;
        li.textContent = text.length > 60 ? text.slice(0, 60) + '…' : text;
        li.onclick = function() {
          const input = document.getElementById('dc-input-message');
          if (input) {
            input.value = text;
            input.focus();
            inputHistoryIndex = idx;
          }
          closeHistoryPopup();
        };
        list.appendChild(li);
      });
    }
    popup.style.display = 'block';
  }

  function closeHistoryPopup() {
    const popup = document.getElementById('dc-history-popup');
    if (popup) popup.style.display = 'none';
  }

  // ── グローバル公開 ──────────────────────────────────────────
  global.DealerChat = {
    init: init
  };

})(typeof window !== 'undefined' ? window : this);
