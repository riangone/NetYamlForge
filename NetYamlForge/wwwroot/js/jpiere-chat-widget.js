/**
 * jpiere-chat-widget.js
 * JPiere 契約サービス 統一 AI チャットウィジェット
 * 業務役割に特化した AI アシスタントパネル
 *
 * 使用方法:
 *   <script src="/js/jpiere-chat-widget.js"></script>
 *   <script>
 *     JpiereChat.init({ project: 'jpiere-cs', userRole: 'employee' });
 *   </script>
 */
(function (global) {
  'use strict';

  // ── 状態管理 ────────────────────────────────────────────────
  let connection = null;
  let currentSessionId = null;
  let conversationId = null;
  let isPanelOpen = false;
  let isPanelMinimized = false;
  let autoScroll = true;
  let chatHistory = [];

  const STORAGE_KEY = 'jpiere_chat_history';
  const TOOL_STORAGE_KEY = 'jpiere_chat_tool';

  // ── 役割別設定 ──────────────────────────────────────────────
  const ROLE_CONFIGS = {
    employee: {
      icon: '👤',
      title: 'AI 業務アシスタント',
      subtitle: '契約・見積・TODO の照会をサポート',
      placeholder: '業務に関する質問をどうぞ...',
      welcomeMessage: 'こんにちは！JPiere の AI 業務アシスタントです。📋\n契約・見積・TODO の照会など、業務全般を支援します！',
      color: '#1a5276'
    },
    contract_manager: {
      icon: '💼',
      title: 'AI 契約アシスタント',
      subtitle: '契約・見積・請求の作成・分析',
      placeholder: '契約・見積・請求に関する質問をどうぞ...',
      welcomeMessage: 'こんにちは！JPiere 契約担当 AI アシスタントです。💼\n契約・見積・請求の作成・分析をお手伝いします！',
      color: '#2980b9'
    },
    accountant: {
      icon: '💰',
      title: 'AI 会計アシスタント',
      subtitle: '仕訳・会計・資金管理',
      placeholder: '会計・仕訳・入金に関する質問をどうぞ...',
      welcomeMessage: 'こんにちは！JPiere 会計担当 AI アシスタントです。💰\n仕訳・会計・入金・支払の管理を支援します！',
      color: '#27ae60'
    },
    purchaser: {
      icon: '📦',
      title: 'AI 購買アシスタント',
      subtitle: '発注・受入・AP請求・支払',
      placeholder: '購買に関する質問をどうぞ...',
      welcomeMessage: 'こんにちは！JPiere 購買担当 AI アシスタントです。📦\n発注・受入・AP請求・支払のフローを支援します！',
      color: '#e67e22'
    },
    approver: {
      icon: '✅',
      title: 'AI 承認アシスタント',
      subtitle: '承認ワークフローの確認・処理',
      placeholder: '承認に関する質問をどうぞ...',
      welcomeMessage: 'こんにちは！JPiere 承認 AI アシスタントです。✅\n承認ワークフローの確認・処理を支援します！',
      color: '#8e44ad'
    },
    admin: {
      icon: '⚙️',
      title: 'AI 管理アシスタント',
      subtitle: 'システム管理・設定・分析',
      placeholder: 'システム管理に関する質問をどうぞ...',
      welcomeMessage: 'こんにちは！JPiere 管理者 AI アシスタントです。⚙️\nシステム全体的管理・設定変更を支援します！',
      color: '#c0392b'
    }
  };

  let currentRole = 'employee';
  let currentProject = 'jpiere-cs';
  let currentConfig = ROLE_CONFIGS.employee;

  // ── 初期化 ──────────────────────────────────────────────────
  function init(opts) {
    opts = opts || {};
    currentProject = opts.project || 'jpiere-cs';
    currentRole = opts.userRole || 'employee';
    currentConfig = ROLE_CONFIGS[currentRole] || ROLE_CONFIGS.employee;

    if (!isUserLoggedIn()) {
      console.log('JpiereChat: User not logged in, skipping initialization');
      return;
    }

    console.log('JpiereChat: Initializing for role=', currentRole);
    injectStyles();
    initPanel();
    initSignalR();
    configureMarked();
  }

  function isUserLoggedIn() {
    const body = document.body;
    const authValue = body.getAttribute('data-user-authenticated');
    return authValue === 'true';
  }

  // ── スタイル注入 ────────────────────────────────────────────
  function injectStyles() {
    if (document.getElementById('jpiere-chat-styles')) return;

    const style = document.createElement('style');
    style.id = 'jpiere-chat-styles';
    style.textContent = `
      /* トリガーボタン */
      #jpiere-chat-trigger {
        position: fixed;
        bottom: 24px;
        right: 24px;
        z-index: 9998;
        width: 56px;
        height: 56px;
        border-radius: 50%;
        background: linear-gradient(135deg, ${currentConfig.color}, ${currentConfig.color}dd);
        border: none;
        box-shadow: 0 4px 12px rgba(0,0,0,0.15);
        cursor: pointer;
        transition: all 0.3s ease;
        display: flex;
        align-items: center;
        justify-content: center;
        font-size: 24px;
        color: white;
      }
      #jpiere-chat-trigger:hover {
        transform: scale(1.1);
        box-shadow: 0 6px 16px rgba(0,0,0,0.2);
      }

      /* チャットパネル */
      #jpiere-chat-panel {
        position: fixed;
        bottom: 100px;
        right: 24px;
        z-index: 9999;
        width: 400px;
        height: 600px;
        background: white;
        border-radius: 12px;
        box-shadow: 0 8px 32px rgba(0,0,0,0.2);
        display: none;
        flex-direction: column;
        overflow: hidden;
        transition: all 0.3s ease;
      }
      #jpiere-chat-panel.open {
        display: flex;
      }
      #jpiere-chat-panel.minimized {
        height: 60px;
      }

      /* ヘッダー */
      .jpiere-chat-header {
        background: linear-gradient(135deg, ${currentConfig.color}, ${currentConfig.color}dd);
        color: white;
        padding: 16px;
        display: flex;
        align-items: center;
        justify-content: space-between;
      }
      .jpiere-chat-header h3 {
        margin: 0;
        font-size: 16px;
        font-weight: 600;
      }
      .jpiere-chat-header p {
        margin: 4px 0 0 0;
        font-size: 12px;
        opacity: 0.9;
      }
      .jpiere-chat-header-actions {
        display: flex;
        gap: 4px;
      }
      .jpiere-chat-header-actions button {
        background: rgba(255,255,255,0.2);
        border: none;
        color: white;
        width: 28px;
        height: 28px;
        border-radius: 6px;
        cursor: pointer;
        display: flex;
        align-items: center;
        justify-content: center;
      }
      .jpiere-chat-header-actions button:hover {
        background: rgba(255,255,255,0.3);
      }

      /* メッセージエリア */
      .jpiere-chat-messages {
        flex: 1;
        overflow-y: auto;
        padding: 16px;
        display: flex;
        flex-direction: column;
        gap: 12px;
      }
      .jpiere-chat-message {
        max-width: 85%;
        padding: 10px 14px;
        border-radius: 12px;
        line-height: 1.5;
        word-wrap: break-word;
      }
      .jpiere-chat-message.user {
        align-self: flex-end;
        background: ${currentConfig.color};
        color: white;
        border-bottom-right-radius: 4px;
      }
      .jpiere-chat-message.ai {
        align-self: flex-start;
        background: #f0f2f5;
        color: #1a1a1a;
        border-bottom-left-radius: 4px;
      }
      .jpiere-chat-message.welcome {
        background: linear-gradient(135deg, #f0f9ff, #e0f2fe);
        border: 1px solid #bae6fd;
      }
      .jpiere-chat-message.welcome h4 {
        margin: 0 0 8px 0;
        color: ${currentConfig.color};
      }
      .jpiere-chat-message.welcome p {
        margin: 4px 0;
        font-size: 13px;
      }

      /* 入力エリア */
      .jpiere-chat-input-area {
        padding: 12px;
        border-top: 1px solid #e5e7eb;
        background: white;
      }
      .jpiere-chat-input-wrapper {
        display: flex;
        gap: 8px;
        align-items: flex-end;
      }
      #jpiere-chat-input {
        flex: 1;
        padding: 10px 12px;
        border: 1px solid #d1d5db;
        border-radius: 8px;
        font-size: 14px;
        resize: none;
        max-height: 120px;
        font-family: inherit;
      }
      #jpiere-chat-input:focus {
        outline: none;
        border-color: ${currentConfig.color};
        box-shadow: 0 0 0 2px ${currentConfig.color}20;
      }
      #jpiere-chat-send-btn {
        padding: 10px 20px;
        background: ${currentConfig.color};
        color: white;
        border: none;
        border-radius: 8px;
        cursor: pointer;
        font-weight: 500;
        transition: all 0.2s;
      }
      #jpiere-chat-send-btn:hover {
        background: ${currentConfig.color}dd;
      }
      #jpiere-chat-send-btn:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }

      /* ローディング */
      .jpiere-chat-thinking {
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 12px;
        color: #6b7280;
        font-size: 13px;
      }
      .jpiere-chat-thinking .spinner {
        width: 16px;
        height: 16px;
        border: 2px solid #e5e7eb;
        border-top-color: ${currentConfig.color};
        border-radius: 50%;
        animation: spin 0.8s linear infinite;
      }
      @keyframes spin {
        to { transform: rotate(360deg); }
      }

      /* クイックリプライ */
      .jpiere-chat-quick-replies {
        display: flex;
        flex-wrap: wrap;
        gap: 6px;
        margin-top: 8px;
      }
      .jpiere-chat-quick-reply {
        padding: 6px 12px;
        background: white;
        border: 1px solid ${currentConfig.color};
        color: ${currentConfig.color};
        border-radius: 16px;
        font-size: 12px;
        cursor: pointer;
        transition: all 0.2s;
      }
      .jpiere-chat-quick-reply:hover {
        background: ${currentConfig.color};
        color: white;
      }

      /* ステータスインジケーター */
      .jpiere-chat-status {
        display: inline-block;
        width: 8px;
        height: 8px;
        border-radius: 50%;
        margin-right: 6px;
      }
      .jpiere-chat-status.connected {
        background: #10b981;
      }
      .jpiere-chat-status.disconnected {
        background: #ef4444;
      }
    `;
    document.head.appendChild(style);
  }

  // ── パネル初期化 ────────────────────────────────────────────
  function initPanel() {
    // トリガーボタン
    const trigger = document.createElement('button');
    trigger.id = 'jpiere-chat-trigger';
    trigger.innerHTML = currentConfig.icon;
    trigger.onclick = togglePanel;
    document.body.appendChild(trigger);

    // チャットパネル
    const panel = document.createElement('div');
    panel.id = 'jpiere-chat-panel';
    panel.innerHTML = buildPanelHTML();
    document.body.appendChild(panel);

    // サーバーから履歴を復元
    restoreFromServer();

    // イベントバインド
    bindPanelEvents();
  }

  function buildPanelHTML() {
    return `
      <div class="jpiere-chat-header">
        <div>
          <h3>${currentConfig.icon} ${currentConfig.title}</h3>
          <p>${currentConfig.subtitle}</p>
        </div>
        <div class="jpiere-chat-header-actions">
          <button id="jpiere-minimize-btn" title="最小化">
            <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="5" y1="12" x2="19" y2="12"></line>
            </svg>
          </button>
          <button id="jpiere-close-btn" title="閉じる">
            <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>
        </div>
      </div>
      <div class="jpiere-chat-messages" id="jpiere-chat-messages">
        <div class="jpiere-chat-message ai welcome">
          <h4>${currentConfig.icon} ${currentConfig.title}</h4>
          <p>${currentConfig.welcomeMessage}</p>
        </div>
      </div>
      <div class="jpiere-chat-thinking" id="jpiere-chat-thinking" style="display: none;">
        <div class="spinner"></div>
        <span>AI が考えています...</span>
      </div>
      <div class="jpiere-chat-input-area">
        <div class="jpiere-chat-input-wrapper">
          <textarea
            id="jpiere-chat-input"
            placeholder="${currentConfig.placeholder}"
            rows="1"></textarea>
          <button id="jpiere-chat-send-btn">送信</button>
        </div>
      </div>
    `;
  }

  function bindPanelEvents() {
    const input = document.getElementById('jpiere-chat-input');
    const sendBtn = document.getElementById('jpiere-chat-send-btn');
    const minimizeBtn = document.getElementById('jpiere-minimize-btn');
    const closeBtn = document.getElementById('jpiere-close-btn');

    // 送信
    sendBtn.onclick = () => sendMessage();
    input.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        sendMessage();
      }
    });

    // 自動リサイズ
    input.addEventListener('input', () => {
      input.style.height = 'auto';
      input.style.height = Math.min(input.scrollHeight, 120) + 'px';
    });

    // 最小化
    minimizeBtn.onclick = () => {
      isPanelMinimized = !isPanelMinimized;
      const panel = document.getElementById('jpiere-chat-panel');
      panel.classList.toggle('minimized', isPanelMinimized);
    };

    // 閉じる
    closeBtn.onclick = togglePanel;
  }

  // ── パネル切り替え ─────────────────────────────────────────
  function togglePanel() {
    isPanelOpen = !isPanelOpen;
    const panel = document.getElementById('jpiere-chat-panel');
    const trigger = document.getElementById('jpiere-chat-trigger');

    if (isPanelOpen) {
      panel.classList.add('open');
      trigger.style.display = 'none';
      document.getElementById('jpiere-chat-input')?.focus();
    } else {
      panel.classList.remove('open');
      trigger.style.display = 'flex';
    }
  }

  // ── メッセージ送信 ─────────────────────────────────────────
  async function sendMessage() {
    const input = document.getElementById('jpiere-chat-input');
    const message = input.value.trim();
    if (!message) return;

    // セッション開始（初回のみ）
    if (!conversationId) {
      await startSession();
    }

    // UI にユーザーメッセージ追加
    addMessageToUI(message, 'user');
    input.value = '';
    input.style.height = 'auto';

    // ローディング表示
    showThinking(true);

    try {
      // API 送信
      const response = await fetch(`/${currentProject}/api/ai/chat/session/${conversationId}/message`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({ message })
      });

      if (!response.ok) {
        throw new Error('送信に失敗しました');
      }

      const result = await response.json();

      // AI 応答を表示
      showThinking(false);
      addMessageToUI(result.responseText, 'ai');

      // クイックリプライがあれば表示
      if (result.quickReplies && result.quickReplies.length > 0) {
        showQuickReplies(result.quickReplies);
      }

      // データ行があれば表示
      if (result.dataRows && result.dataRows.length > 0) {
        addDataRowsToUI(result.dataRows);
      }

      // ナビゲーション URL があれば表示
      if (result.navigationUrl) {
        addNavigationToUI(result.navigationUrl, result.navigationLabel);
      }

    } catch (error) {
      console.error('送信エラー:', error);
      showThinking(false);
      addMessageToUI('⚠️ 送信エラーが発生しました。もう一度お試しください。', 'ai');
    }
  }

  // ── セッション開始 ─────────────────────────────────────────
  async function startSession() {
    try {
      const response = await fetch(`/${currentProject}/api/ai/chat/session`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          channel: 'web',
          guestSessionId: null
        })
      });

      if (!response.ok) {
        throw new Error('セッション開始に失敗しました');
      }

      const result = await response.json();
      conversationId = result.conversationId;
      console.log('JpiereChat: Session started:', conversationId);

      // セッションID を保存
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify({
        conversationId: conversationId,
        role: currentRole,
        timestamp: Date.now()
      }));

    } catch (error) {
      console.error('セッション開始エラー:', error);
    }
  }

  // ── メッセージ UI 追加 ─────────────────────────────────────
  function addMessageToUI(text, sender) {
    const container = document.getElementById('jpiere-chat-messages');
    if (!container) return;

    const msgDiv = document.createElement('div');
    msgDiv.className = `jpiere-chat-message ${sender}`;

    // Markdown 変換（marked.js 使用）
    if (typeof marked !== 'undefined') {
      msgDiv.innerHTML = marked.parse(text);
    } else {
      msgDiv.textContent = text;
    }

    container.appendChild(msgDiv);
    scrollToBottom();

    // 履歴に保存
    chatHistory.push({ sender, text, timestamp: Date.now() });
  }

  function showThinking(show) {
    const el = document.getElementById('jpiere-chat-thinking');
    if (el) {
      el.style.display = show ? 'flex' : 'none';
    }
  }

  function showQuickReplies(replies) {
    const container = document.getElementById('jpiere-chat-messages');
    if (!container) return;

    const quickRepliesDiv = document.createElement('div');
    quickRepliesDiv.className = 'jpiere-chat-quick-replies';

    replies.forEach(reply => {
      const btn = document.createElement('button');
      btn.className = 'jpiere-chat-quick-reply';
      btn.textContent = reply;
      btn.onclick = () => {
        document.getElementById('jpiere-chat-input').value = reply;
        sendMessage();
      };
      quickRepliesDiv.appendChild(btn);
    });

    container.appendChild(quickRepliesDiv);
    scrollToBottom();
  }

  function addDataRowsToUI(dataRows) {
    const container = document.getElementById('jpiere-chat-messages');
    if (!container || !dataRows || dataRows.length === 0) return;

    const tableDiv = document.createElement('div');
    tableDiv.className = 'jpiere-chat-message ai';
    tableDiv.style.maxWidth = '100%';

    let html = '<table style="width:100%; border-collapse: collapse; font-size: 12px;">';
    html += '<thead><tr style="background: #f3f4f6;">';

    // ヘッダー
    Object.keys(dataRows[0]).forEach(key => {
      html += `<th style="padding: 6px; border: 1px solid #e5e7eb; text-align: left;">${key}</th>`;
    });
    html += '</tr></thead><tbody>';

    // データ行（最大 10 件）
    dataRows.slice(0, 10).forEach(row => {
      html += '<tr>';
      Object.values(row).forEach(val => {
        html += `<td style="padding: 6px; border: 1px solid #e5e7eb;">${val}</td>`;
      });
      html += '</tr>';
    });
    html += '</tbody></table>';

    if (dataRows.length > 10) {
      html += `<p style="margin-top: 8px; font-size: 11px; color: #6b7280;">他 ${dataRows.length - 10} 件...</p>`;
    }

    tableDiv.innerHTML = html;
    container.appendChild(tableDiv);
    scrollToBottom();
  }

  function addNavigationToUI(url, label) {
    const container = document.getElementById('jpiere-chat-messages');
    if (!container || !url) return;

    const navDiv = document.createElement('div');
    navDiv.className = 'jpiere-chat-message ai';
    navDiv.innerHTML = `
      <a href="${url}" target="_blank" style="color: ${currentConfig.color}; text-decoration: none; font-weight: 500;">
        🔗 ${label || '詳細を見る'}
      </a>
    `;
    container.appendChild(navDiv);
    scrollToBottom();
  }

  // ── サーバーから履歴復元 ───────────────────────────────────
  async function restoreFromServer() {
    // セッション情報復元
    const stored = sessionStorage.getItem(STORAGE_KEY);
    if (stored) {
      try {
        const data = JSON.parse(stored);
        conversationId = data.conversationId;
        currentRole = data.role || currentRole;
        console.log('JpiereChat: Restored session:', conversationId);
      } catch (e) {
        console.error('JpiereChat: Failed to restore session:', e);
      }
    }

    // サーバーからメッセージ取得（TODO: 実装後有効化）
    // if (conversationId) {
    //   await loadMessagesFromServer();
    // }
  }

  // ── SignalR 初期化 ────────────────────────────────────────
  function initSignalR() {
    // SignalR 接続（将来の拡張用）
    console.log('JpiereChat: SignalR initialization skipped (using REST API)');
  }

  // ── marked.js 設定 ────────────────────────────────────────
  function configureMarked() {
    if (typeof marked !== 'undefined') {
      marked.setOptions({
        breaks: true,
        gfm: true
      });
    }
  }

  // ── ユーティリティ ────────────────────────────────────────
  function scrollToBottom() {
    const container = document.getElementById('jpiere-chat-messages');
    if (container && autoScroll) {
      setTimeout(() => {
        container.scrollTop = container.scrollHeight;
      }, 50);
    }
  }

  // ── グローバル公開 ────────────────────────────────────────
  global.JpiereChat = {
    init,
    togglePanel,
    sendMessage
  };

})(window);
