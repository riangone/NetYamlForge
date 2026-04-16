/**
 * ai-chat-widget.js — NetYamlForge 統合 AI チャットウィジェット
 *
 * 全プロジェクト共通の AI チャット UI 実装。
 * 色・アイコン・タイトル・API エンドポイントは window.AI_CHAT_CONFIG で外部から注入します。
 *
 * 対応 API モード:
 *   'framework' — AIController (タスクキュー + SignalR / ポーリング)
 *   'dealer'    — AutoDealerChatController (セッションベース、即時応答)
 *
 * 使用方法:
 *   window.AI_CHAT_CONFIG = { apiMode: 'dealer', dealer: { project: 'auto-dealer-demo', mode: 'staff' }, ... };
 *   // または
 *   AIChatWidget.init({ apiMode: 'framework', framework: { project: 'jpiere-cs', role: 'employee' } });
 *
 * レガシー互換:
 *   DealerChat.init({ mode: 'customer', project: 'auto-dealer-demo' })
 *   window.JPIERE_CHAT_CONFIG = { project: 'jpiere-cs', userRole: 'employee' }
 */
(function (global) {
  'use strict';

  // ── 状態 ─────────────────────────────────────────────────────
  let signalRConnection = null;
  let currentTaskId = null;
  let currentSessionId = null;
  let dealerConversationId = null;
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

  const STORAGE_KEY = 'aw_chat_history';
  const TOOL_STORAGE_KEY = 'aw_chat_tool';
  const DEALER_CONV_KEY = 'aw_dealer_conv'; // project-specific suffix added on use

  const SIGNALR_SOURCES = [
    '/lib/microsoft/signalr/dist/browser/signalr.min.js',
    '/lib/signalr/signalr.min.js',
    'https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js'
  ];

  // ── デフォルト設定 ───────────────────────────────────────────
  const DEFAULTS = {
    theme: {
      primaryColor: '#6366f1',
      accentColor: '#4f46e5',
      headerBg: 'linear-gradient(135deg, #6366f1, #4f46e5)',
      headerIcon: '🤖'
    },
    title: 'AI アシスタント',
    subtitle: '',
    placeholder: 'メッセージを入力... (Ctrl+Enter で送信)',
    welcomeMessage: 'こんにちは！AI アシスタントです。',
    triggerPosition: 'bottom-right',
    apiMode: 'framework',
    framework: { project: '', role: 'employee', defaultCliTool: 'qwen' },
    dealer: { project: 'auto-dealer-demo', mode: 'customer' }
  };

  // ── アクティブ設定 ───────────────────────────────────────────
  let cfg = {};

  // ── 初期化 ──────────────────────────────────────────────────

  function init(opts) {
    // 優先度: init 引数 > window.AI_CHAT_CONFIG > デフォルト
    cfg = deepMerge(DEFAULTS, window.AI_CHAT_CONFIG || {}, opts || {});

    // レガシー互換: JPIERE_CHAT_CONFIG
    if (window.JPIERE_CHAT_CONFIG && !opts) {
      cfg.apiMode = 'framework';
      cfg.framework = cfg.framework || {};
      cfg.framework.project = window.JPIERE_CHAT_CONFIG.project || 'jpiere-cs';
      cfg.framework.role = window.JPIERE_CHAT_CONFIG.userRole || 'employee';
      applyJpiereRoleConfig(cfg.framework.role);
    }

    if (!isUserLoggedIn()) return;

    injectStyles();
    buildPanel();
    bindEvents();
    initSignalR();
    if (cfg.apiMode === 'framework') {
      loadCliTools();
      restoreFromServer();
    } else if (cfg.apiMode === 'dealer') {
      // dealer mode: プロバイダー一覧を取得し、チャット履歴を復元
      loadDealerProviders();
      restoreDealerHistoryFromServer();
    }
    configureMarked();
  }

  // ── ユーティリティ ───────────────────────────────────────────

  function isUserLoggedIn() {
    return document.body?.getAttribute('data-user-authenticated') === 'true';
  }

  function deepMerge(...objs) {
    const result = {};
    for (const obj of objs) {
      if (!obj) continue;
      for (const [k, v] of Object.entries(obj)) {
        if (v && typeof v === 'object' && !Array.isArray(v)) {
          result[k] = deepMerge(result[k] || {}, v);
        } else if (v !== undefined) {
          result[k] = v;
        }
      }
    }
    return result;
  }

  function applyJpiereRoleConfig(role) {
    const roleConfigs = {
      employee:         { icon: '👤', title: 'AI 業務アシスタント',  subtitle: '契約・見積・TODO の照会をサポート' },
      contract_manager: { icon: '💼', title: 'AI 契約アシスタント',  subtitle: '契約・見積・請求の作成・分析' },
      accountant:       { icon: '💰', title: 'AI 会計アシスタント',  subtitle: '仕訳・会計・資金管理' },
      purchaser:        { icon: '📦', title: 'AI 購買アシスタント',  subtitle: '発注・受入・AP請求・支払' },
      approver:         { icon: '✅', title: 'AI 承認アシスタント',  subtitle: '承認ワークフローの確認・処理' },
      admin:            { icon: '⚙️', title: 'AI 管理アシスタント', subtitle: 'システム管理・設定・分析' }
    };
    const rc = roleConfigs[role] || roleConfigs.employee;
    cfg.theme = cfg.theme || {};
    if (!cfg.theme.headerIcon || cfg.theme.headerIcon === DEFAULTS.theme.headerIcon) cfg.theme.headerIcon = rc.icon;
    if (!cfg.title || cfg.title === DEFAULTS.title) cfg.title = rc.title;
    if (!cfg.subtitle) cfg.subtitle = rc.subtitle;
  }

  function escapeHtml(str) {
    if (!str) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  // AI プロバイダーの表示名を取得
  function getProviderDisplayName(provider) {
    if (!provider) return 'AI Assistant';
    const providerMap = {
      'qwen': 'Qwen Code',
      'claude': 'Claude Code',
      'codex': 'OpenAI Codex',
      'gemini': 'Google Gemini',
      'copilot': 'GitHub Copilot',
      'ollama': 'Ollama',
      'lmstudio': 'LM Studio',
      'mock': 'Mock'
    };
    return providerMap[provider.toLowerCase()] || provider;
  }

  // ── スタイル注入 ─────────────────────────────────────────────

  function injectStyles() {
    if (document.getElementById('aw-styles')) return;
    const p = cfg.theme.primaryColor;
    const a = cfg.theme.accentColor;
    const hBg = cfg.theme.headerBg;
    const isLeft = cfg.triggerPosition === 'bottom-left';

    const style = document.createElement('style');
    style.id = 'aw-styles';
    style.textContent = `
      #aw-trigger {
        position: fixed;
        bottom: 24px;
        ${isLeft ? 'left' : 'right'}: 24px;
        width: 60px; height: 60px;
        border-radius: 50%;
        background: ${p};
        color: #fff; border: none; cursor: pointer;
        font-size: 26px;
        box-shadow: 0 4px 16px rgba(0,0,0,0.28);
        transition: transform 0.2s, box-shadow 0.2s;
        z-index: 9998;
        display: flex; align-items: center; justify-content: center;
      }
      #aw-trigger:hover { transform: scale(1.1); box-shadow: 0 6px 20px rgba(0,0,0,0.35); }

      #aw-panel {
        position: fixed; bottom: 0;
        ${isLeft ? 'left' : 'right'}: -600px;
        width: 600px; max-width: 100vw;
        height: 100vh; height: 100dvh;
        background: #fff !important;
        box-shadow: ${isLeft ? '4px' : '-4px'} 0 24px rgba(0,0,0,0.2);
        transition: ${isLeft ? 'left' : 'right'} 0.3s ease-in-out;
        z-index: 2147483647 !important;
        display: flex; flex-direction: column; isolation: isolate;
      }
      #aw-panel.open { ${isLeft ? 'left' : 'right'}: 0; }
      #aw-panel.maximized { width: 100vw !important; ${isLeft ? 'left' : 'right'}: 0 !important; }
      #aw-panel.minimized {
        height: auto !important; bottom: 0 !important;
        ${isLeft ? 'left' : 'right'}: 20px !important;
        width: 320px !important;
        border-radius: 0.75rem 0.75rem 0 0 !important;
        box-shadow: ${isLeft ? '2px' : '-2px'} -4px 16px rgba(0,0,0,0.18) !important;
        transition: none !important;
      }
      #aw-panel.minimized .aw-body, #aw-panel.minimized .aw-footer,
      #aw-panel.minimized #aw-scroll-btn { display: none !important; }
      #aw-panel.minimized .aw-header {
        border-radius: 0.75rem 0.75rem 0 0; cursor: pointer; user-select: none;
      }

      .aw-header {
        display: flex; align-items: center; justify-content: space-between;
        padding: 1rem; border-bottom: 1px solid #e0e0e0;
        background: ${hBg}; color: #fff; flex-shrink: 0; position: relative;
      }
      .aw-header h3 { margin: 0; font-size: 1.1rem; font-weight: 600; display: flex; align-items: center; gap: 0.5rem; }
      .aw-header .aw-subtitle { font-size: 0.75rem; opacity: 0.85; margin-top: 0.2rem; }
      .aw-header .aw-btns { position: relative; z-index: 10; display: flex; gap: 0.25rem; }
      .aw-header button {
        pointer-events: auto !important; cursor: pointer !important; position: relative; z-index: 11 !important;
        background: rgba(255,255,255,0.1); border: none; color: #fff;
        padding: 4px 8px; border-radius: 4px; transition: background 0.15s;
      }
      .aw-header button:hover { background: rgba(255,255,255,0.2); }

      .aw-status-dot {
        display: inline-block; width: 8px; height: 8px; border-radius: 50%; margin-right: 6px;
      }
      .aw-status-dot.idle { background: rgba(255,255,255,0.5); }
      .aw-status-dot.running { background: #fff; animation: aw-pulse 1s infinite; }
      .aw-status-dot.completed { background: #4caf50; }
      .aw-status-dot.error { background: #f44336; }

      @keyframes aw-pulse { 0%,100% { opacity:1; } 50% { opacity:0.4; } }

      .aw-body {
        flex: 1; overflow-y: auto; padding: 1rem;
        display: flex; flex-direction: column; gap: 0.75rem;
        background: #f8f9fa !important; position: relative;
      }

      .aw-msg-row { display: flex; flex-direction: column; max-width: 100%; gap: 0.15rem; }
      .aw-msg-row.user { align-items: flex-end; }
      .aw-msg-row.assistant { align-items: flex-start; }
      .aw-msg-row.system { align-items: center; }

      .aw-msg-sender { font-size: 0.7rem; font-weight: 600; opacity: 0.55; padding: 0 0.5rem; }
      .aw-msg-sender-badge {
        opacity: 1 !important;
        display: inline-flex;
        align-items: center;
        gap: 4px;
        padding: 2px 8px;
        border-radius: 12px;
        background: #f0f0f0;
        font-size: 0.75rem;
      }
      .aw-msg-row.user .aw-msg-sender-badge { background: transparent; }

      .aw-msg-inner { display: flex; align-items: flex-end; gap: 0.5rem; max-width: 88%; }
      .aw-msg-row.user .aw-msg-inner { flex-direction: row-reverse; }

      .aw-avatar {
        width: 1.75rem; height: 1.75rem; border-radius: 50%;
        display: flex; align-items: center; justify-content: center; flex-shrink: 0;
      }
      .aw-msg-row.user .aw-avatar { background: ${p}; color: #fff; box-shadow: 0 2px 6px ${p}40; }
      .aw-msg-row.assistant .aw-avatar { background: #e0e0e0; color: #333; border: 1px solid #d0d0d0; }

      .aw-msg {
        max-width: 100%; padding: 0.6rem 0.9rem; border-radius: 1.1rem;
        word-wrap: break-word; font-size: 0.9rem; line-height: 1.55;
      }
      .aw-msg.user { background: ${p} !important; color: #fff; border-bottom-right-radius: 0.3rem; box-shadow: 0 2px 8px ${p}40; }
      .aw-msg.assistant {
        background: #fff !important; color: #212121;
        border-bottom-left-radius: 0.3rem; border: 1px solid #e0e0e0; box-shadow: 0 1px 4px rgba(0,0,0,0.07);
      }
      .aw-msg.system {
        align-self: center; background: #e3f2fd; color: #1565c0;
        font-size: 0.78rem; text-align: center; border-radius: 1rem;
        padding: 0.3rem 0.8rem; max-width: 90%; border: 1px solid #bbdefb;
      }
      .aw-msg-content p { margin: 0.4em 0; }
      .aw-msg-content p:first-child { margin-top: 0; }
      .aw-msg-content p:last-child { margin-bottom: 0; }
      .aw-msg-content pre { background: rgba(0,0,0,0.08); padding: 0.5em; border-radius: 4px; overflow-x: auto; }
      .aw-msg-content code { background: rgba(0,0,0,0.08); padding: 0.15em 0.4em; border-radius: 3px; font-size: 0.85em; }
      .aw-msg-content pre code { background: none; padding: 0; }
      .aw-msg-content table { border-collapse: collapse; width: 100%; font-size: 0.8rem; }
      .aw-msg-content th, .aw-msg-content td { border: 1px solid #ddd; padding: 0.3rem 0.5rem; }
      .aw-msg-content th { background: rgba(0,0,0,0.05); font-weight: 600; }

      .aw-msg-time {
        font-size: 0.7rem;
        color: rgba(0, 0, 0, 0.45);
        margin-top: 0.3rem;
        text-align: right;
        line-height: 1;
      }
      .aw-msg.user .aw-msg-time { color: rgba(255, 255, 255, 0.7); }

      .aw-quick-replies { display: flex; flex-wrap: wrap; gap: 0.4rem; margin-top: 0.4rem; }
      .aw-quick-btn {
        padding: 0.3rem 0.7rem; border: 1.5px solid ${p}; background: #fff; color: ${p};
        border-radius: 1rem; font-size: 0.8rem; cursor: pointer; transition: background 0.15s, color 0.15s;
      }
      .aw-quick-btn:hover { background: ${p}; color: #fff; }

      .aw-data-table { width: 100%; border-collapse: collapse; font-size: 0.78rem; margin-top: 0.5rem; }
      .aw-data-table th { background: ${p}20; border: 1px solid ${p}40; padding: 0.3rem 0.5rem; font-weight: 600; }
      .aw-data-table td { border: 1px solid #e0e0e0; padding: 0.3rem 0.5rem; }

      .aw-nav-link {
        display: inline-block; margin-top: 0.5rem; padding: 0.3rem 0.7rem;
        background: ${p}; color: #fff; border-radius: 0.5rem; font-size: 0.8rem;
        text-decoration: none; transition: opacity 0.15s;
      }
      .aw-nav-link:hover { opacity: 0.85; }

      .aw-progress {
        background: #f5f5f5; border-radius: 0.5rem; padding: 0.75rem; margin-top: 0.5rem;
      }
      .aw-progress-bar { height: 0.5rem; background: #e0e0e0; border-radius: 0.25rem; overflow: hidden; margin: 0.5rem 0; }
      .aw-progress-fill { height: 100%; background: ${p}; transition: width 0.3s ease; }
      .aw-progress-text { font-size: 0.75rem; color: #666; display: flex; justify-content: space-between; }
      .aw-logs ul { list-style: none; padding: 0; margin: 0.25rem 0 0; max-height: 120px; overflow-y: auto; }

      #aw-scroll-btn {
        position: absolute; bottom: 80px; right: 20px; z-index: 10;
        width: 36px; height: 36px; border-radius: 50%;
        background: ${p}; color: #fff; border: none; cursor: pointer;
        box-shadow: 0 2px 8px rgba(0,0,0,0.2); display: none;
      }

      .aw-footer {
        padding: 1rem; border-top: 1px solid #e0e0e0;
        background: #f5f5f5 !important; flex-shrink: 0; position: relative;
      }
      .aw-cli-row { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.5rem; font-size: 0.85rem; }
      .aw-cli-row select { border: 1px solid #e0e0e0; border-radius: 4px; padding: 4px 8px; font-size: 12px; max-width: 150px; }

      .aw-input-row { display: flex; gap: 0.5rem; margin-bottom: 0.5rem; }
      .aw-input-wrapper { position: relative; display: flex; align-items: flex-end; flex: 1; }
      .aw-input-wrapper textarea {
        flex: 1; resize: none; min-height: 60px; max-height: 120px;
        border: 1.5px solid #e0e0e0; border-radius: 8px; padding: 9px 14px;
        font-size: 13px; outline: none; transition: border 0.2s; font-family: inherit;
        padding-right: 2.5rem;
      }
      .aw-input-wrapper textarea:focus { border-color: ${p}; }
      .aw-clear-input-btn {
        position: absolute; right: 0.5rem; bottom: 0.5rem;
        width: 24px; height: 24px; border-radius: 50%;
        background: rgba(0,0,0,0.05); border: none; cursor: pointer;
        display: flex; align-items: center; justify-content: center;
        color: #666; transition: background 0.15s, color 0.15s;
      }
      .aw-clear-input-btn:hover { background: rgba(0,0,0,0.1); color: #333; }

      .aw-actions { display: flex; gap: 0.5rem; justify-content: flex-end; }
      .aw-btn { display: flex; align-items: center; gap: 0.3rem; padding: 6px 12px; border-radius: 6px; border: none; cursor: pointer; font-size: 0.8rem; transition: opacity 0.15s; }
      .aw-btn:hover { opacity: 0.85; }
      .aw-btn-ghost { background: transparent; color: #555; }
      .aw-btn-ghost:hover { background: #e0e0e0; }
      .aw-btn-primary { background: ${p}; color: #fff; }
      .aw-btn:disabled { opacity: 0.4; cursor: not-allowed; }

      .aw-hist-popup {
        position: absolute; bottom: 100%; left: 0; right: 0;
        background: #fff; border: 1px solid #e0e0e0; border-radius: 8px;
        box-shadow: 0 4px 12px rgba(0,0,0,0.15); z-index: 100; max-height: 200px; overflow-y: auto;
      }
      .aw-hist-popup-hdr { display: flex; justify-content: space-between; align-items: center; padding: 0.5rem 0.75rem; border-bottom: 1px solid #e0e0e0; font-size: 0.8rem; font-weight: 600; }
      .aw-hist-item { padding: 0.4rem 0.75rem; font-size: 0.8rem; cursor: pointer; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .aw-hist-item:hover { background: #f5f5f5; }
      .aw-hist-empty { padding: 0.5rem 0.75rem; font-size: 0.8rem; color: #999; }

      .aw-skills-bar { display: flex; flex-wrap: wrap; gap: 0.3rem; padding: 0.5rem 1rem 0; }
      .aw-skill-btn { padding: 0.2rem 0.6rem; border: 1px solid ${p}50; background: ${p}10; color: ${p}; border-radius: 1rem; font-size: 0.75rem; cursor: pointer; }
      .aw-skill-btn:hover { background: ${p}25; }

      /* メッセージアクションボタン */
      .aw-msg-actions {
        display: none;
        gap: 4px;
        margin-top: 3px;
        padding: 0 0.4rem;
      }
      .aw-msg-row:hover .aw-msg-actions {
        display: flex;
      }
      .aw-msg-row.user .aw-msg-actions {
        justify-content: flex-end;
      }
      .aw-msg-row.assistant .aw-msg-actions {
        justify-content: flex-start;
      }

      .aw-msg-action-btn {
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
      .aw-msg-action-btn:hover {
        background: #e3f2fd;
        color: #1565c0;
        border-color: #90caf9;
      }
    `;
    document.head.appendChild(style);
  }

  // ── パネル構築 ───────────────────────────────────────────────

  function buildPanel() {
    // トリガーボタン
    const trigger = document.createElement('button');
    trigger.id = 'aw-trigger';
    trigger.title = cfg.title;
    trigger.innerHTML = cfg.theme.headerIcon.startsWith('<')
      ? cfg.theme.headerIcon
      : `<span style="font-size:26px">${cfg.theme.headerIcon}</span>`;
    trigger.onclick = togglePanel;
    document.body.appendChild(trigger);

    // パネル
    const panel = document.createElement('div');
    panel.id = 'aw-panel';
    panel.innerHTML = buildPanelHTML();
    document.body.appendChild(panel);

    // ウェルカムメッセージ
    if (cfg.welcomeMessage) {
      renderMessage(cfg.welcomeMessage, 'assistant');
    }
  }

  function buildPanelHTML() {
    const isFramework = cfg.apiMode === 'framework';
    return `
      <div class="aw-header">
        <div>
          <h3>
            <span id="aw-status-dot" class="aw-status-dot idle"></span>
            <span>${escapeHtml(cfg.theme.headerIcon)} ${escapeHtml(cfg.title)}</span>
          </h3>
          ${cfg.subtitle ? `<div class="aw-subtitle">${escapeHtml(cfg.subtitle)}</div>` : ''}
        </div>
        <div class="aw-btns">
          <button id="aw-max-btn" title="最大化">
            <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" xmlns="http://www.w3.org/2000/svg">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5l-5-5m5 5v-4m0 4h-4"/>
            </svg>
          </button>
          <button id="aw-min-btn" title="最小化">
            <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" xmlns="http://www.w3.org/2000/svg">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 12H4"/>
            </svg>
          </button>
          <button id="aw-close-btn" title="閉じる">
            <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" xmlns="http://www.w3.org/2000/svg">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"/>
            </svg>
          </button>
        </div>
      </div>

      <div class="aw-body" id="aw-messages"></div>

      <button id="aw-scroll-btn" title="最下部へ">
        <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" xmlns="http://www.w3.org/2000/svg">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 14l-7 7m0 0l-7-7m7 7V3"/>
        </svg>
      </button>

      ${isFramework ? `<div id="aw-skills-bar" class="aw-skills-bar" style="display:none"></div>` : ''}

      <div class="aw-footer">
        ${isFramework ? `
        <div class="aw-cli-row">
          <label for="aw-cli-tool" style="color:#555">AI:</label>
          <select id="aw-cli-tool">
            <option value="claude">Claude Code</option>
            <option value="qwen">Qwen Code</option>
            <option value="codex">OpenAI Codex</option>
            <option value="gemini">Google Gemini</option>
            <option value="copilot">GitHub Copilot</option>
            <option value="ollama">Ollama</option>
            <option value="lmstudio">LM Studio</option>
            <option value="mock">Mock (Test)</option>
          </select>
          <span id="aw-cli-status" style="font-size:0.75rem;opacity:0.5;margin-left:0.5rem"></span>
        </div>` : `
        <div class="aw-cli-row" id="aw-dealer-provider-row" style="display:none">
          <label for="aw-dealer-provider" style="color:#555">AI:</label>
          <select id="aw-dealer-provider">
            <option value="">読み込み中...</option>
          </select>
          <span id="aw-dealer-provider-status" style="font-size:0.75rem;margin-left:0.4rem"></span>
        </div>`}

        <div class="aw-input-row">
          <div class="aw-input-wrapper">
            <textarea id="aw-input" placeholder="${escapeHtml(cfg.placeholder)}" rows="2"></textarea>
            <button id="aw-clear-input-btn" class="aw-clear-input-btn" title="清空输入" style="display:none">
              <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" xmlns="http://www.w3.org/2000/svg">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"/>
              </svg>
            </button>
          </div>
        </div>

        <div id="aw-hist-popup" class="aw-hist-popup" style="display:none">
          <div class="aw-hist-popup-hdr">
            <span>入力履歴</span>
            <button id="aw-hist-close" class="aw-btn aw-btn-ghost" style="padding:2px 6px">✕</button>
          </div>
          <ul id="aw-hist-list" style="list-style:none;padding:0;margin:0"></ul>
        </div>

        <div class="aw-actions">
          <button id="aw-hist-btn" class="aw-btn aw-btn-ghost" title="入力履歴">
            <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" xmlns="http://www.w3.org/2000/svg">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z"/>
            </svg>
          </button>
          <button id="aw-stop-btn" class="aw-btn aw-btn-ghost" disabled>
            <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" xmlns="http://www.w3.org/2000/svg">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 12a9 9 0 11-18 0 9 9 0 0118 0zM9 10a1 1 0 00-1 1v4a1 1 0 001 1h4a1 1 0 001-1v-4a1 1 0 00-1-1H9z"/>
            </svg>
            停止
          </button>
          <button id="aw-clear-btn" class="aw-btn aw-btn-ghost" style="display:none">
            <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" xmlns="http://www.w3.org/2000/svg">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"/>
            </svg>
          </button>
          <button id="aw-send-btn" class="aw-btn aw-btn-primary">
            <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" xmlns="http://www.w3.org/2000/svg">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8"/>
            </svg>
            送信
          </button>
        </div>
      </div>
    `;
  }

  // ── イベントバインディング ────────────────────────────────────

  function bindEvents() {
    document.getElementById('aw-max-btn').onclick = toggleMaximize;
    document.getElementById('aw-min-btn').onclick = toggleMinimize;
    document.getElementById('aw-close-btn').onclick = () => { if (isPanelOpen) togglePanel(); };
    document.querySelector('.aw-header').addEventListener('click', e => {
      if (isPanelMinimized && !e.target.closest('button')) toggleMinimize();
    });
    if (window.visualViewport) {
      window.visualViewport.addEventListener('resize', adjustForKeyboard);
      window.visualViewport.addEventListener('scroll', adjustForKeyboard);
    }
    document.getElementById('aw-send-btn').onclick = sendMessage;
    document.getElementById('aw-stop-btn').onclick = stopTask;
    document.getElementById('aw-clear-btn').onclick = clearMessages;
    document.getElementById('aw-scroll-btn').onclick = toggleAutoScroll;
    document.getElementById('aw-hist-btn').onclick = e => { e.stopPropagation(); toggleHistPopup(); };
    document.getElementById('aw-hist-close').onclick = e => { e.stopPropagation(); closeHistPopup(); };
    document.addEventListener('click', e => {
      const p = document.getElementById('aw-hist-popup');
      const b = document.getElementById('aw-hist-btn');
      if (p && p.style.display !== 'none' && !p.contains(e.target) && e.target !== b && !b?.contains(e.target))
        closeHistPopup();
    });

    const input = document.getElementById('aw-input');
    const clearInputBtn = document.getElementById('aw-clear-input-btn');

    // 清空输入框按钮
    if (clearInputBtn) {
      clearInputBtn.onclick = function() {
        input.value = '';
        clearInputBtn.style.display = 'none';
        input.focus();
      };
    }

    // 监听输入框变化，显示/隐藏清空按钮
    input.addEventListener('input', function() {
      if (clearInputBtn) {
        clearInputBtn.style.display = this.value.length > 0 ? 'flex' : 'none';
      }
    });

    input.addEventListener('keydown', e => {
      if (e.key === 'Enter' && e.ctrlKey) { e.preventDefault(); sendMessage(); return; }
      if (e.key === 'ArrowUp' && input.selectionStart === 0) {
        if (!inputHistory.length) return;
        e.preventDefault();
        if (inputHistoryIndex === -1) inputCurrentDraft = input.value;
        if (inputHistoryIndex < inputHistory.length - 1) inputHistoryIndex++;
        input.value = inputHistory[inputHistoryIndex] || '';
        input.setSelectionRange(0, 0);
        return;
      }
      if (e.key === 'ArrowDown' && input.selectionStart === input.value.length) {
        if (inputHistoryIndex === -1) return;
        e.preventDefault();
        if (--inputHistoryIndex === -1) input.value = inputCurrentDraft;
        else input.value = inputHistory[inputHistoryIndex] || '';
        const len = input.value.length;
        input.setSelectionRange(len, len);
        return;
      }
      if (!['ArrowUp', 'ArrowDown', 'Shift'].includes(e.key)) inputHistoryIndex = -1;
    });

    const cliTool = document.getElementById('aw-cli-tool');
    if (cliTool) {
      cliTool.addEventListener('change', function() {
        try { sessionStorage.setItem(TOOL_STORAGE_KEY, this.value); } catch(e) {}
      });
      const saved = (() => { try { return sessionStorage.getItem(TOOL_STORAGE_KEY); } catch(e) { return null; } })();
      if (saved) cliTool.value = saved;
      if (cfg.framework?.defaultCliTool) cliTool.value = cfg.framework.defaultCliTool;
    }

    const dealerProvider = document.getElementById('aw-dealer-provider');
    if (dealerProvider) {
      dealerProvider.addEventListener('change', function() {
        try { sessionStorage.setItem('aw_dealer_provider', this.value); } catch(e) {}
        const status = document.getElementById('aw-dealer-provider-status');
        if (status) {
          const sel = dealerProvider.options[dealerProvider.selectedIndex];
          const isAvail = sel?.dataset?.installed === 'true';
          status.textContent = isAvail ? '✓' : '⚠';
          status.style.color = isAvail ? '#4caf50' : '#ff9800';
          status.title = isAvail ? '利用可能' : '未インストールまたは未認証';
        }
      });
    }

    document.getElementById('aw-messages').addEventListener('scroll', e => {
      const el = e.target;
      const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 50;
      autoScroll = atBottom;
      const btn = document.getElementById('aw-scroll-btn');
      if (btn) btn.style.display = atBottom ? 'none' : 'flex';
    });
  }

  // ── パネル表示制御 ───────────────────────────────────────────

  function togglePanel() {
    isPanelOpen = !isPanelOpen;
    const panel = document.getElementById('aw-panel');
    panel.classList.toggle('open', isPanelOpen);
    if (isPanelOpen && autoScroll) scrollToBottom();
  }

  function toggleMinimize() {
    isPanelMinimized = !isPanelMinimized;
    const panel = document.getElementById('aw-panel');
    panel.classList.toggle('minimized', isPanelMinimized);
    const btn = document.getElementById('aw-min-btn');
    if (btn) {
      btn.title = isPanelMinimized ? '展開' : '最小化';
      btn.innerHTML = isPanelMinimized
        ? `<svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 15l7-7 7 7"/></svg>`
        : `<svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 12H4"/></svg>`;
    }
  }

  function toggleMaximize() {
    const panel = document.getElementById('aw-panel');
    const trigger = document.getElementById('aw-trigger');
    const btn = document.getElementById('aw-max-btn');
    isMaximized = !isMaximized;
    panel.classList.toggle('maximized', isMaximized);
    if (trigger) trigger.style.display = isMaximized ? 'none' : 'flex';
    if (btn) btn.title = isMaximized ? '元に戻す' : '最大化';
  }

  function adjustForKeyboard() {
    const panel = document.getElementById('aw-panel');
    if (!panel || !isPanelOpen || !window.visualViewport) return;
    panel.style.height = window.visualViewport.height + 'px';
    panel.style.top = window.visualViewport.offsetTop + 'px';
  }

  function scrollToBottom() {
    const el = document.getElementById('aw-messages');
    if (el) el.scrollTop = el.scrollHeight;
  }

  function toggleAutoScroll() {
    autoScroll = !autoScroll;
    if (autoScroll) scrollToBottom();
    const btn = document.getElementById('aw-scroll-btn');
    if (btn) btn.style.display = autoScroll ? 'none' : 'flex';
  }

  // ── メッセージ表示 ───────────────────────────────────────────

  function formatTimestamp(date) {
    const d = (date instanceof Date) ? date : new Date(date);
    if (!date || isNaN(d.getTime())) d.setTime(Date.now());
    const pad = n => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
  }

  function renderMessage(content, role, extra) {
    const container = document.getElementById('aw-messages');
    if (!container) return;

    // extra.timestamp または現在時刻を使用
    const timestamp = (extra && extra.timestamp) ? extra.timestamp : formatTimestamp(new Date());

    const row = document.createElement('div');
    row.className = `aw-msg-row ${role}`;

    if (role === 'system') {
      row.innerHTML = `<div class="aw-msg system"><div class="aw-msg-content">${escapeHtml(content)}</div></div>`;
    } else {
      const avatarHtml = role === 'user'
        ? `<div class="aw-avatar"><svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z"/></svg></div>`
        : `<div class="aw-avatar"><svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17H3a2 2 0 01-2-2V5a2 2 0 012-2h14a2 2 0 012 2v10a2 2 0 01-2 2h-2"/></svg></div>`;

      // 用户消息和助手消息都支持 Markdown 渲染
      const rendered = renderMarkdown(content);

      // 创建消息容器
      const msgInner = document.createElement('div');
      msgInner.className = 'aw-msg-inner';
      msgInner.innerHTML = `
        ${avatarHtml}
        <div class="aw-msg ${role}"><div class="aw-msg-content">${rendered}</div></div>
      `;

      // 添加时间戳到消息内容
      const contentEl = msgInner.querySelector('.aw-msg-content');
      const timeEl = document.createElement('div');
      timeEl.className = 'aw-msg-time';
      timeEl.textContent = timestamp;
      contentEl.appendChild(timeEl);

      row.appendChild(document.createElement('div'));
      row.querySelector(':scope > div').className = 'aw-msg-sender';

      // AI プロバイダー名を表示
      const provider = (extra && extra.provider) ? extra.provider : null;
      if (role === 'assistant' && provider) {
        const providerName = getProviderDisplayName(provider);
        row.querySelector(':scope > div').innerHTML = `🤖 ${providerName}`;
        row.querySelector(':scope > div').classList.add('aw-msg-sender-badge');
      } else {
        row.querySelector(':scope > div').textContent = role === 'user' ? 'You' : 'AI Assistant';
      }
      row.appendChild(msgInner);

      // 为所有消息添加复制和引用按钮（user 和 assistant）
      const actionsEl = document.createElement('div');
      actionsEl.className = 'aw-msg-actions';

      // 复制按钮
      const copyBtn = document.createElement('button');
      copyBtn.className = 'aw-msg-action-btn';
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

      // 引用按钮
      const quoteBtn = document.createElement('button');
      quoteBtn.className = 'aw-msg-action-btn';
      quoteBtn.title = '引用して返信';
      quoteBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 10h10a8 8 0 018 8v2M3 10l6 6m-6-6l6-6"/></svg>';
      quoteBtn.onclick = function() {
        const input = document.getElementById('aw-input');
        if (!input) return;
        const quoted = content.split('\n').map(function(line) { return '> ' + line; }).join('\n');
        input.value = quoted + '\n' + input.value;
        input.focus();
        const len = input.value.length;
        input.setSelectionRange(len, len);
        if (isPanelMinimized) toggleMinimize();
        if (!isPanelOpen) togglePanel();
      };
      actionsEl.appendChild(quoteBtn);

      row.appendChild(actionsEl);

      // ---- 新規: 構造化コンポーネントを優先表示 ----
      if (extra?.components?.length && typeof AiChatComponents !== 'undefined') {
        const compEl = AiChatComponents.render(extra.components, (value, label) => {
          // コンポーネント操作をメッセージとして送信
          // label（表示用テキスト）があればそれを使う、なければ value
          const displayValue = label || value;

          // ユーザーメッセージとして表示
          renderMessage(displayValue, 'user');

          // 入力欄をクリアして送信
          const inputEl = document.getElementById('aw-input');
          if (inputEl) inputEl.value = '';

          // コンポーネント値で送信（バックエンドでは value が使われる）
          sendDealerMessageWithComponentValue(value);
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
          btn.onclick = () => { document.getElementById('aw-input').value = r; sendMessage(); };
          qr.appendChild(btn);
        });
        row.appendChild(qr);
      }

      // データテーブル（dealer mode）
      console.log('[renderMessage] 数据表格渲染检查:', {
        hasDataRows: !!extra?.dataRows,
        dataRowsLength: extra?.dataRows?.length || 0,
        firstRow: extra?.dataRows?.[0]
      });
      if (extra?.dataRows?.length) {
        const keys = Object.keys(extra.dataRows[0]);
        console.log('[renderMessage] 渲染数据表格，字段:', keys);
        const tbl = document.createElement('table');
        tbl.className = 'aw-data-table';
        tbl.innerHTML = `<thead><tr>${keys.map(k => `<th>${escapeHtml(k)}</th>`).join('')}</tr></thead>
          <tbody>${extra.dataRows.map(r => `<tr>${keys.map(k => `<td>${escapeHtml(String(r[k] ?? ''))}</td>`).join('')}</tr>`).join('')}</tbody>`;
        row.appendChild(tbl);
      }

      // ナビゲーションリンク（dealer mode）
      if (extra?.navigationUrl) {
        const a = document.createElement('a');
        a.href = extra.navigationUrl;
        a.className = 'aw-nav-link';
        a.textContent = extra.navigationLabel || '一覧を開く';
        row.appendChild(a);
      }
    }

    container.appendChild(row);
    if (autoScroll) scrollToBottom();
    return row;
  }

  function renderMarkdown(text) {
    if (typeof marked !== 'undefined') {
      try { return marked.parse(text); } catch(e) {}
    }
    return escapeHtml(text).replace(/\n/g, '<br>');
  }

  function configureMarked() {
    if (typeof marked !== 'undefined') {
      marked.setOptions({ breaks: true, gfm: true });
    }
  }

  function clearMessages() {
    const container = document.getElementById('aw-messages');
    if (container) container.innerHTML = '';
    chatHistory = [];
    try { sessionStorage.removeItem(STORAGE_KEY); } catch(e) {}
    if (cfg.welcomeMessage) renderMessage(cfg.welcomeMessage, 'assistant');
  }

  // ── 送信状態管理 ─────────────────────────────────────────────

  function setSending(on) {
    const sendBtn = document.getElementById('aw-send-btn');
    const stopBtn = document.getElementById('aw-stop-btn');
    const input   = document.getElementById('aw-input');
    if (sendBtn) sendBtn.disabled = on;
    if (stopBtn) stopBtn.disabled = !on;
    if (input) input.disabled = on;
    updateStatusDot(on ? 'running' : 'idle');
  }

  function updateStatusDot(state) {
    const dot = document.getElementById('aw-status-dot');
    if (dot) dot.className = `aw-status-dot ${state}`;
  }

  // ── メッセージ送信（モード分岐） ─────────────────────────────

  async function sendMessage() {
    const input = document.getElementById('aw-input');
    const message = input?.value?.trim();
    if (!message) return;

    if (inputHistory[0] !== message) {
      inputHistory.unshift(message);
      if (inputHistory.length > 100) inputHistory.pop();
    }
    inputHistoryIndex = -1;
    inputCurrentDraft = '';

    renderMessage(message, 'user');
    input.value = '';
    setSending(true);

    try {
      if (cfg.apiMode === 'dealer') {
        await sendDealerMessage(message);
      } else {
        await sendFrameworkMessage(message);
      }
    } catch (err) {
      console.error('[AIChatWidget] sendMessage error:', err);
      let errMsg = err.message || '不明なエラー';
      if (errMsg.includes('Failed to fetch')) errMsg = 'サーバーに接続できません。';
      renderMessage(`リクエスト失敗：${errMsg}`, 'system');
      updateStatusDot('error');
    } finally {
      setSending(false);
    }
  }

  // ── Dealer Adapter ──────────────────────────────────────────

  function getDealerApiBase() {
    const project = cfg.dealer?.project || 'auto-dealer-demo';
    return `/${project}/api/ai/chat`;
  }

  function getDealerMsgPath() {
    return cfg.dealer?.mode === 'staff' ? 'staff' : 'session';
  }

  async function startDealerSession() {
    const apiBase = getDealerApiBase();
    const sessionPath = cfg.dealer?.mode === 'staff' ? 'staff/session' : 'session';
    const body = cfg.dealer?.mode === 'staff' ? {} : { guestSessionId: `guest-${Date.now()}` };

    const res = await fetch(`${apiBase}/${sessionPath}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });

    if (res.ok) {
      const data = await res.json();
      dealerConversationId = data.conversationId;
      // ページリフレッシュ後も conversationId を復元できるよう localStorage に保存
      const project = cfg.dealer?.project || 'auto-dealer-demo';
      const mode = cfg.dealer?.mode || 'customer';
      try { localStorage.setItem(`${DEALER_CONV_KEY}_${project}_${mode}`, dealerConversationId); } catch(e) {}
      if (data.welcomeMessage && data.welcomeMessage !== cfg.welcomeMessage) {
        renderMessage(data.welcomeMessage, 'assistant');
      }
    } else {
      throw new Error(`セッション開始失敗 (${res.status})`);
    }
  }

  async function sendDealerMessage(message) {
    if (!dealerConversationId) {
      await startDealerSession();
    }
    if (!dealerConversationId) {
      renderMessage('セッションを開始できませんでした。ページを再読み込みしてください。', 'system');
      return;
    }

    const apiBase = getDealerApiBase();
    const msgPath = getDealerMsgPath();
    const url = `${apiBase}/${msgPath}/${dealerConversationId}/message`;

    // dealer モードの選択プロバイダーを取得
    const dealerProviderSel = document.getElementById('aw-dealer-provider');
    const selectedProvider = dealerProviderSel?.value || null;

    const body = { message };
    if (selectedProvider) body.provider = selectedProvider;

    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });

    if (res.ok) {
      const data = await res.json();
      console.log('[DealerChat] API 响应:', data);
      const reply = data.responseText || data.result || data.message || '';
      const usedProvider = data.aiProvider || data.provider || selectedProvider || null;
      console.log('[DealerChat] responseText:', reply);
      console.log('[DealerChat] dataRows:', data.dataRows);
      if (reply.trim()) {
        renderMessage(reply, 'assistant', {
          components: data.components,
          quickReplies: data.quickReplies,
          dataRows: data.dataRows,
          navigationUrl: data.navigationUrl,
          navigationLabel: data.navigationLabel,
          provider: usedProvider
        });
      }
      updateStatusDot('completed');
    } else {
      let errMsg = `HTTP ${res.status}`;
      try { const b = await res.json(); errMsg = b.error || errMsg; } catch(_) { /* ignore parse error */ }
      renderMessage(`エラー: ${errMsg}`, 'system');
      updateStatusDot('error');
      console.error('[AIChatWidget] Dealer API error:', errMsg);
    }
  }

  // コンポーネント操作からの送信（ユーザーメッセージ表示済みなので応答のみ処理）
  async function sendDealerMessageWithComponentValue(value) {
    setSending(true);
    try {
      await sendDealerMessage(value);
    } finally {
      setSending(false);
    }
  }

  // ── Framework Adapter ────────────────────────────────────────

  // 注意：全局 AI 聊天始终使用 /api/AI（不带 project 前缀）
  // 因为聊天记录存储在 system.db 中，而不是项目级的 chat.db
  function getFrameworkApiBase() {
    return '/api/AI';
  }

  async function sendFrameworkMessage(message) {
    const apiBase = getFrameworkApiBase();
    const cliTool = document.getElementById('aw-cli-tool')?.value
      || cfg.framework?.defaultCliTool || 'qwen';

    // 注意：全局 AI 聊天不传递 projectName，消息始终存储到 system.db
    const body = { message, cliTool, sessionId: currentSessionId || undefined };

    const res = await fetch(`${apiBase}/chat`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });

    if (!res.ok) {
      let errMsg = `HTTP ${res.status}`;
      try { const b = await res.json(); errMsg = b.error || b.title || errMsg; } catch(_) { /* ignore parse error */ }
      renderMessage(`エラー：${errMsg}`, 'system');
      updateStatusDot('error');
      console.error('[AIChatWidget] Framework API error:', errMsg);
      return;
    }

    const data = await res.json();

    if (data.result?.trim()) {
      renderMessage(data.result, 'assistant', { provider: data.provider || cliTool });
      updateStatusDot('completed');
      return;
    }

    if (data.taskId) {
      currentTaskId = data.taskId;
      await pollTaskResult(data.taskId, apiBase, data.provider || cliTool);
      currentTaskId = null;
    }

    updateStatusDot('completed');
  }

  async function pollTaskResult(taskId, apiBase, provider) {
    const progressRow = addProgressContainer(taskId);
    let lastLogCount = 0;
    const deadline = Date.now() + 30 * 60 * 1000;

    while (Date.now() < deadline) {
      if (!currentTaskId) return; // stopTask が呼ばれた

      try {
        const res = await fetch(`${apiBase}/tasks/${taskId}`);
        if (!res.ok) break;

        const data = await res.json();
        updateProgressUI(progressRow, data, lastLogCount);
        lastLogCount = data.logs?.length || lastLogCount;

        if (data.status === 'Completed' || data.status === 2) {
          if (data.sessionId) currentSessionId = data.sessionId;
          currentTaskId = null;
          if (data.result?.trim()) renderMessage(data.result, 'assistant', { provider });
          return;
        } else if (data.status === 'Failed' || data.status === 3) {
          currentTaskId = null;
          renderMessage(`❌ ${data.error || 'タスクが失敗しました'}`, 'system');
          return;
        } else if (data.status === 'Cancelled' || data.status === 4) {
          currentTaskId = null;
          return;
        }

        await new Promise(r => setTimeout(r, 2000));
      } catch(err) {
        console.error('[AIChatWidget] polling error:', err);
        break;
      }
    }

    renderMessage('⚠️ タスクがタイムアウトしました', 'system');
  }

  function addProgressContainer(taskId) {
    const row = document.createElement('div');
    row.className = 'aw-msg-row assistant';
    row.dataset.taskId = taskId;
    row.innerHTML = `
      <div class="aw-msg assistant">
        <div class="aw-progress">
          <div class="aw-progress-text">
            <span class="progress-status">処理中...</span>
            <span class="progress-pct">0%</span>
          </div>
          <div class="aw-progress-bar"><div class="aw-progress-fill" style="width:0%"></div></div>
          <div class="aw-logs"><ul></ul></div>
        </div>
      </div>`;
    document.getElementById('aw-messages')?.appendChild(row);
    if (autoScroll) scrollToBottom();
    return row;
  }

  function updateProgressUI(row, data, lastLogCount) {
    if (!row) return;
    const fill   = row.querySelector('.aw-progress-fill');
    const status = row.querySelector('.progress-status');
    const pct    = row.querySelector('.progress-pct');
    const logs   = row.querySelector('.aw-logs ul');
    if (fill) fill.style.width = `${data.progress || 0}%`;
    if (status) status.textContent = translateStatus(data.status);
    if (pct) pct.textContent = `${data.progress || 0}%`;
    if (logs && data.logs?.length > lastLogCount) {
      for (let i = lastLogCount; i < data.logs.length; i++) {
        const txt = data.logs[i]?.trim();
        if (txt) {
          const li = document.createElement('li');
          li.textContent = txt;
          li.style.cssText = 'font-size:0.75rem;color:#666;padding:2px 0';
          logs.appendChild(li);
        }
      }
      if (autoScroll) scrollToBottom();
    }
  }

  function translateStatus(s) {
    if (typeof s === 'number') return ['Pending','Running','Completed','Failed','Cancelled'][s] || s;
    return s;
  }

  // ── SignalR ──────────────────────────────────────────────────

  function initSignalR() {
    loadSignalRFromSource(0, SIGNALR_SOURCES);
  }

  function loadSignalRFromSource(i, sources) {
    if (i >= sources.length) return;
    const s = document.createElement('script');
    s.src = sources[i];
    s.onload = connectSignalR;
    s.onerror = () => loadSignalRFromSource(i + 1, sources);
    document.head.appendChild(s);
  }

  function connectSignalR() {
    if (typeof signalR === 'undefined') return;
    signalRConnection = new signalR.HubConnectionBuilder()
      .withUrl('/aiProgressHub')
      .withAutomaticReconnect()
      .build();

    signalRConnection.on('ReceiveProgress', handleSignalRProgress);

    signalRConnection.start().catch(e => console.warn('[AIChatWidget] SignalR start failed:', e));
  }

  function handleSignalRProgress(data) {
    if (!data || data.id !== currentTaskId) return;

    const apiBase = getFrameworkApiBase();
    const row = document.querySelector(`[data-task-id="${data.id}"]`);
    if (row && data.logs) updateProgressUI(row, data, 0);

    if (data.status === 'Completed' || data.status === 2) {
      if (data.result?.trim()) renderMessage(data.result, 'assistant');
      updateStatusDot('completed');
      setSending(false);
      currentTaskId = null;
    } else if (data.status === 'Failed' || data.status === 3) {
      renderMessage(`❌ ${data.error || 'タスク失敗'}`, 'system');
      updateStatusDot('error');
      setSending(false);
      currentTaskId = null;
    } else if (data.status === 'Cancelled' || data.status === 4) {
      updateStatusDot('idle');
      setSending(false);
      currentTaskId = null;
    }
  }

  // ── タスク停止 ───────────────────────────────────────────────

  async function stopTask() {
    if (!currentTaskId) return;
    const taskId = currentTaskId;
    currentTaskId = null;
    setSending(false);
    updateStatusDot('idle');

    try {
      await fetch(`${getFrameworkApiBase()}/tasks/${taskId}`, { method: 'DELETE' });
      renderMessage('⏹ タスクをキャンセルしました', 'system');
    } catch(e) {
      renderMessage('⏹ キャンセル要求を送信しました', 'system');
    }
  }

  // ── 入力履歴ポップアップ ─────────────────────────────────────

  function toggleHistPopup() {
    const p = document.getElementById('aw-hist-popup');
    if (!p) return;
    p.style.display === 'none' ? openHistPopup() : closeHistPopup();
  }

  function openHistPopup() {
    const popup = document.getElementById('aw-hist-popup');
    const list  = document.getElementById('aw-hist-list');
    if (!popup || !list) return;

    list.innerHTML = '';
    if (!inputHistory.length) {
      list.innerHTML = `<li class="aw-hist-empty">履歴はありません</li>`;
    } else {
      inputHistory.forEach((text, idx) => {
        const li = document.createElement('li');
        li.className = 'aw-hist-item';
        li.title = text;
        li.textContent = text.length > 60 ? text.slice(0, 60) + '…' : text;
        li.onclick = () => {
          const inp = document.getElementById('aw-input');
          if (inp) { inp.value = text; inp.focus(); inputHistoryIndex = idx; }
          closeHistPopup();
        };
        list.appendChild(li);
      });
    }
    popup.style.display = 'block';
  }

  function closeHistPopup() {
    const p = document.getElementById('aw-hist-popup');
    if (p) p.style.display = 'none';
  }

  // ── CLI ツール読み込み（framework モード） ────────────────────

  async function loadCliTools() {
    try {
      const apiBase = getFrameworkApiBase();
      const res = await fetch(`${apiBase}/cli-tools`);
      if (!res.ok) return;
      const tools = await res.json();
      const sel = document.getElementById('aw-cli-tool');
      if (!sel || !tools?.length) return;
      sel.innerHTML = '';
      tools.forEach(t => {
        const opt = document.createElement('option');
        opt.value = t.id || t.name;
        opt.textContent = t.displayName || t.name || t.id;
        sel.appendChild(opt);
      });
      const saved = (() => { try { return sessionStorage.getItem(TOOL_STORAGE_KEY); } catch(e) { return null; } })();
      if (saved) sel.value = saved;
      else if (cfg.framework?.defaultCliTool) sel.value = cfg.framework.defaultCliTool;
    } catch(e) { /* ignore */ }
  }

  // ── dealer モード: プロバイダー一覧を読み込み ────────────────

  async function loadDealerProviders() {
    const row = document.getElementById('aw-dealer-provider-row');
    const sel = document.getElementById('aw-dealer-provider');
    if (!sel) return;

    try {
      const apiBase = getDealerApiBase();
      const res = await fetch(`${apiBase}/providers`);
      if (!res.ok) return;
      const providers = await res.json();
      if (!providers?.length) return;

      sel.innerHTML = '';
      providers.forEach(p => {
        const opt = document.createElement('option');
        opt.value = p.id;
        opt.dataset.installed = p.installed ? 'true' : 'false';
        let label = p.name || p.id;
        if (!p.installed) label += ' (未インストール)';
        else if (!p.authenticated) label += ' (未認証)';
        opt.textContent = label;
        if (!p.installed) opt.style.color = '#999';
        sel.appendChild(opt);
      });

      // セッションストレージから復元
      const saved = (() => { try { return sessionStorage.getItem('aw_dealer_provider'); } catch(e) { return null; } })();
      if (saved && [...sel.options].some(o => o.value === saved)) {
        sel.value = saved;
      } else {
        // デフォルト: インストール済みの最初のプロバイダーを選択
        const firstAvail = providers.find(p => p.installed);
        if (firstAvail) sel.value = firstAvail.id;
      }

      // ステータス表示
      const status = document.getElementById('aw-dealer-provider-status');
      if (status) {
        const cur = providers.find(p => p.id === sel.value);
        if (cur) {
          status.textContent = cur.installed && cur.authenticated ? '✓' : (cur.installed ? '⚠' : '✗');
          status.style.color = cur.installed && cur.authenticated ? '#4caf50' : (cur.installed ? '#ff9800' : '#f44336');
        }
      }

      // プロバイダー行を表示
      if (row) row.style.display = 'flex';
    } catch(e) {
      console.warn('[AIChatWidget] dealer providers 読み込み失敗:', e);
    }
  }

  // ── サーバーから履歴を復元（framework モード） ────────────────

  async function restoreFromServer() {
    try {
      const apiBase = getFrameworkApiBase();
      const res = await fetch(`${apiBase}/history?limit=20`);
      if (!res.ok) {
        console.warn('[AIChatWidget] 履歴取得失敗 HTTP', res.status);
        return;
      }
      const msgs = await res.json();
      const container = document.getElementById('aw-messages');
      if (!container) return;
      
      // 注意：取得したメッセージがある場合のみクリアし、メッセージをレンダリングする
      // メッセージがない場合はウェルカムメッセージを残す
      if (msgs && msgs.length > 0) {
        container.innerHTML = '';
        msgs.forEach(m => {
          const type = m.type || m.Type || '';
          const role = type === 'assistant' ? 'assistant' : 'user';
          const content = m.content || m.Content || '';
          const ts = m.displayTime || m.DisplayTime || m.createdAt || m.CreatedAt || '';
          const provider = m.provider || m.Provider || undefined;
          
          if (content) {
            renderMessage(content, role, { timestamp: ts, provider: provider });
          }
        });
      }
    } catch(e) {
      console.warn('[AIChatWidget] 履歴取得エラー:', e);
      // サイレント失敗: ウェルカムメッセージはそのまま表示
    }
  }

  // ── サーバーから履歴を復元（dealer モード） ─────────────────

  async function restoreDealerHistoryFromServer() {
    try {
      // まず localStorage から conversationId を復元
      let convId = dealerConversationId;

      if (!convId) {
        const project = cfg.dealer?.project || 'auto-dealer-demo';
        const mode = cfg.dealer?.mode || 'customer';
        try { convId = localStorage.getItem(`${DEALER_CONV_KEY}_${project}_${mode}`); } catch(e) {}
        if (convId) {
          dealerConversationId = convId;
          console.log('[AIChatWidget] localStorage から conversationId を復元:', convId);
        }
      }

      if (!convId) {
        // 認証セッションからサーバー側で会話IDを検索（クロスブラウザ対応）
        const apiBase = getDealerApiBase();
        try {
          const mySessionRes = await fetch(`${apiBase}/my-session`, {
            headers: { 'Accept': 'application/json' }
          });
          if (mySessionRes.ok) {
            const data = await mySessionRes.json();
            if (data.conversationId) {
              convId = data.conversationId;
              dealerConversationId = convId;
              // このブラウザの localStorage にも保存しておく（次回高速復元用）
              const project = cfg.dealer?.project || 'auto-dealer-demo';
              const mode = cfg.dealer?.mode || 'customer';
              try { localStorage.setItem(`${DEALER_CONV_KEY}_${project}_${mode}`, convId); } catch(e) {}
              console.log('[AIChatWidget] サーバー認証セッションから conversationId を復元:', convId);
            }
          }
        } catch(e) { /* 未ログインまたはネットワークエラー、次のフォールバックへ */ }
      }

      if (!convId) {
        // 最終フォールバック：userId を DOM から取得してサーバー検索
        const apiBase = getDealerApiBase();
        const userId = getCurrentUserId();
        if (!userId) {
          console.log('[AIChatWidget] 未ログインかつ conversationId なし、新規セッションを使用');
          return;
        }
        const historyUrl = `${apiBase}/user-history?userId=${encodeURIComponent(userId)}&limit=1`;
        const res = await fetch(historyUrl, { headers: { 'Accept': 'application/json' } });
        if (res.ok) {
          const data = await res.json();
          if (data.conversationId) {
            convId = data.conversationId;
            dealerConversationId = convId;
          }
        }
      }

      // 加载该会话的所有消息
      if (convId) {
        const apiBase = getDealerApiBase();
        const messagesUrl = `${apiBase}/session/${convId}/messages`;
        const res = await fetch(messagesUrl, {
          headers: { 'Accept': 'application/json' }
        });
        
        if (res.ok) {
          const messages = await res.json();
          if (messages && messages.length > 0) {
            const container = document.getElementById('aw-messages');
            if (container) {
              container.innerHTML = ''; // 清除欢迎消息
              
              messages.forEach(m => {
                const role = m.sender === 'customer' ? 'user' : 'assistant';
                const ts = m.timestamp || '';
                renderMessage(m.content || '', role, { 
                  timestamp: ts,
                  intent: m.intent || undefined
                });
              });
            }
          }
        } else {
          console.warn('[AIChatWidget] 消息加载失败 HTTP', res.status);
        }
      }
    } catch(e) {
      console.warn('[AIChatWidget] dealer 履歴取得エラー:', e);
      // 静默失败，显示欢迎消息
    }
  }

  // ── 获取当前用户 ID ────────────────────────────────────────

  function getCurrentUserId() {
    // 尝试从页面数据中获取用户 ID
    const userIdEl = document.querySelector('[data-user-id]');
    if (userIdEl) {
      return userIdEl.getAttribute('data-user-id');
    }
    
    // 尝试从 body 属性获取
    const bodyUserId = document.body?.getAttribute('data-user-id');
    if (bodyUserId) {
      return bodyUserId;
    }
    
    // 尝试从用户名 meta 标签获取
    const metaUser = document.querySelector('meta[name="user-name"]');
    if (metaUser) {
      return metaUser.getAttribute('content');
    }
    
    return null;
  }

  // ── 公開 API ─────────────────────────────────────────────────

  const AIChatWidget = {
    init,
    open: () => { if (!isPanelOpen) togglePanel(); },
    close: () => { if (isPanelOpen) togglePanel(); },
    sendMessage: msg => {
      const input = document.getElementById('aw-input');
      if (input) { input.value = msg; sendMessage(); }
    }
  };

  // レガシー互換: DealerChat.init({ mode, project })
  const DealerChat = {
    init(opts) {
      init({
        apiMode: 'dealer',
        dealer: { project: opts?.project || 'auto-dealer-demo', mode: opts?.mode || 'customer' },
        theme: (opts?.mode === 'staff')
          ? { primaryColor: '#2e7d32', accentColor: '#1b5e20', headerBg: 'linear-gradient(135deg, #2e7d32, #1b5e20)', headerIcon: '🤝' }
          : { primaryColor: '#1a73e8', accentColor: '#0d47a1', headerBg: 'linear-gradient(135deg, #1a73e8, #0d47a1)', headerIcon: '🚗' },
        title: opts?.mode === 'staff' ? '🤝 AI 業務アシスタント' : '🚗 AI 窓口',
        subtitle: opts?.mode === 'staff' ? '業務支援 · リアルタイム対応' : '24 時間対応 · 平均応答 < 10 秒',
        placeholder: opts?.mode === 'staff' ? '業務に関する質問をどうぞ...' : 'ご用件をお聞かせください...',
        welcomeMessage: opts?.mode === 'staff'
          ? 'こんにちは！AI 業務アシスタントです。\nリード管理・予約確認・在庫照会など何でもご相談ください。'
          : 'こんにちは！AI カスタマーサポートです。\n試乗・ご購入・サービスのご相談は何でもどうぞ！'
      });
    }
  };

  global.AIChatWidget = AIChatWidget;
  global.DealerChat = DealerChat;

  // window.AI_CHAT_CONFIG が事前設定されている場合は DOMContentLoaded 後に自動 init
  document.addEventListener('DOMContentLoaded', function() {
    if (window.AI_CHAT_CONFIG && !window._awInitialized) {
      window._awInitialized = true;
      init(window.AI_CHAT_CONFIG);
    }
  });

})(window);
