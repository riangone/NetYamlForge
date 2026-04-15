/**
 * chat-app.js — NetYaml AI スタンドアロン チャット製品
 *
 * API:  POST /api/AI/chat        → タスクID 取得
 *       GET  /api/AI/tasks/{id}  → ポーリング
 *       GET  /api/AI/cli-tools   → モデル一覧
 *       GET  /api/AI/history     → 会話履歴
 * リアルタイム: SignalR /aiChatHub
 */
'use strict';

const ChatApp = (() => {

  /* ── 状態 ─────────────────────────────────────────── */
  let selectedTool   = localStorage.getItem('nyf_tool') || 'claude';
  let sessionId      = null;   // Claude --resume セッション ID
  let currentTaskId  = null;
  let isLoading      = false;
  let pollTimer      = null;
  let signalR        = null;
  let conversations  = [];     // {id, title, ts} のリスト（ローカル管理）
  let currentConvId  = null;
  let userId         = '';

  /* ── DOM 参照 ─────────────────────────────────────── */
  const $ = id => document.getElementById(id);

  const dom = {};
  function bindDom() {
    dom.sidebar       = $('sidebar');
    dom.sidebarClose  = $('sidebarCloseBtn');
    dom.sidebarToggle = $('sidebarToggleBtn');
    dom.newChatBtn    = $('newChatBtn');
    dom.convList      = $('convList');
    dom.modelSelect   = $('modelSelect');
    dom.statusDot     = $('statusDot');
    dom.statusText    = $('statusText');
    dom.messagesArea  = $('messagesArea');
    dom.welcomeScreen = $('welcomeScreen');
    dom.messagesList  = $('messagesList');
    dom.typingInd     = $('typingIndicator');
    dom.typingLabel   = $('typingLabel');
    dom.input         = $('messageInput');
    dom.sendBtn       = $('sendBtn');
    dom.charCount     = $('charCount');
  }

  /* ── 初期化 ───────────────────────────────────────── */
  async function init(opts = {}) {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', () => init(opts));
      return;
    }

    userId = (window.__AI_USER__?.id) || 'anon';
    bindDom();
    bindEvents();

    // モデル一覧をロード
    await loadTools();

    // 選択済みモデルを復元
    if (dom.modelSelect && selectedTool) {
      dom.modelSelect.value = selectedTool;
    }

    // SignalR 接続
    connectSignalR();

    // 会話履歴をサイドバーにロード
    await loadConversationHistory();

    setStatus('ready', '準備完了');
  }

  /* ── イベント ─────────────────────────────────────── */
  function bindEvents() {
    // サイドバー開閉
    dom.sidebarClose?.addEventListener('click', () => setSidebar(false));
    dom.sidebarToggle?.addEventListener('click', () => setSidebar(!isSidebarOpen()));

    // 新しい会話
    dom.newChatBtn?.addEventListener('click', startNewChat);

    // モデル変更
    dom.modelSelect?.addEventListener('change', () => {
      selectedTool = dom.modelSelect.value;
      localStorage.setItem('nyf_tool', selectedTool);
    });

    // 入力欄
    dom.input?.addEventListener('input',   onInputChange);
    dom.input?.addEventListener('keydown',  onInputKeyDown);
    dom.sendBtn?.addEventListener('click',  sendMessage);

    // サジェスションカード
    document.querySelectorAll('.suggestion-card').forEach(btn => {
      btn.addEventListener('click', () => {
        const msg = btn.getAttribute('data-msg');
        if (msg) triggerSend(msg);
      });
    });
  }

  /* ── サイドバー ───────────────────────────────────── */
  function isSidebarOpen() {
    return !dom.sidebar?.classList.contains('collapsed');
  }

  function setSidebar(open) {
    if (!dom.sidebar) return;
    dom.sidebar.classList.toggle('collapsed', !open);
    // モバイル用オーバーレイ
    let overlay = document.querySelector('.sidebar-overlay');
    if (open && window.innerWidth < 640) {
      if (!overlay) {
        overlay = document.createElement('div');
        overlay.className = 'sidebar-overlay';
        document.body.appendChild(overlay);
        overlay.addEventListener('click', () => setSidebar(false));
      }
      overlay.classList.add('visible');
    } else if (overlay) {
      overlay.classList.remove('visible');
    }
  }

  /* ── モデル一覧 ───────────────────────────────────── */
  async function loadTools() {
    try {
      const res = await fetchApi('/api/AI/cli-tools');
      if (!res.ok) return;
      const data = await res.json();

      // available は {toolName: {name, installed}} のオブジェクト
      const available = data.available || {};
      if (!dom.modelSelect) return;

      dom.modelSelect.innerHTML = '';
      const allTools = Object.keys(available);
      if (allTools.length === 0) return;

      allTools.forEach(key => {
        const info = available[key];
        const installed = info?.installed ?? false;
        const opt = document.createElement('option');
        opt.value = key;
        opt.textContent = formatToolName(key) + (installed ? '' : ' (未インストール)');
        opt.disabled = !installed;
        dom.modelSelect.appendChild(opt);
      });

      // 保存済みツールを選択（なければ最初のインストール済みを選択）
      const saved = localStorage.getItem('nyf_tool');
      const validSaved = saved && available[saved]?.installed;
      if (validSaved) {
        dom.modelSelect.value = saved;
        selectedTool = saved;
      } else {
        const first = allTools.find(k => available[k]?.installed);
        if (first) { dom.modelSelect.value = first; selectedTool = first; }
      }
    } catch (e) {
      console.warn('loadTools error:', e);
    }
  }

  function formatToolName(key) {
    const map = {
      claude:   'Claude (Anthropic)',
      qwen:     'Qwen Code',
      gemini:   'Gemini',
      ollama:   'Ollama',
      lmstudio: 'LM Studio',
      codex:    'OpenAI Codex',
      copilot:  'GitHub Copilot',
      mock:     'Mock (テスト)',
    };
    return map[key] || key;
  }

  /* ── SignalR ──────────────────────────────────────── */
  function connectSignalR() {
    if (typeof signalR === 'undefined' || !signalR?.HubConnectionBuilder) return;

    const conn = new signalR.HubConnectionBuilder()
      .withUrl('/aiChatHub')
      .withAutomaticReconnect()
      .build();

    conn.on('TaskCompleted', onTaskCompleted);
    conn.on('TaskProgress',  onTaskProgress);
    conn.on('TaskError',     onTaskError);

    conn.start()
      .then(() => { console.log('[SignalR] Connected'); })
      .catch(e  => { console.warn('[SignalR] Failed:', e); });

    signalR = conn;
  }

  /* ── 会話履歴 ─────────────────────────────────────── */
  async function loadConversationHistory() {
    try {
      const res = await fetchApi('/api/AI/history?limit=50&context=framework');
      if (!res.ok) return;
      const messages = await res.json();

      // session_id でグループ化して会話リストを作成
      const sessMap = new Map();
      for (const msg of (messages || [])) {
        const sid = msg.sessionId || msg.session_id || 'nosession';
        if (!sessMap.has(sid)) {
          sessMap.set(sid, { id: sid, title: '', ts: msg.createdAt || msg.timestamp || '' });
        }
        // 最初のユーザーメッセージをタイトルに使う
        if (!sessMap.get(sid).title && msg.role === 'user') {
          sessMap.get(sid).title = (msg.content || msg.message || '').slice(0, 40);
        }
      }

      conversations = [...sessMap.values()].reverse();
      renderConvList();
    } catch (e) {
      console.warn('loadConversationHistory error:', e);
    }
  }

  function renderConvList() {
    if (!dom.convList) return;
    if (conversations.length === 0) {
      dom.convList.innerHTML = '<div class="conv-empty">会話履歴がありません</div>';
      return;
    }
    dom.convList.innerHTML = '';
    const tpl = document.getElementById('tplConvItem');

    conversations.forEach(conv => {
      const item = tpl ? tpl.content.cloneNode(true).querySelector('.conv-item') : document.createElement('div');
      item.className = 'conv-item' + (conv.id === currentConvId ? ' active' : '');
      item.querySelector('.conv-item-text').textContent = conv.title || '（無題）';
      item.addEventListener('click', () => restoreConversation(conv.id));
      dom.convList.appendChild(item);
    });
  }

  async function restoreConversation(convId) {
    currentConvId = convId;
    sessionId     = convId !== 'nosession' ? convId : null;
    clearMessages();

    // 履歴を再ロードしてフィルタリング
    try {
      const res = await fetchApi('/api/AI/history?limit=200&context=framework');
      if (!res.ok) return;
      const messages = await res.json();
      const filtered = (messages || []).filter(m =>
        (m.sessionId || m.session_id || 'nosession') === convId);

      hideWelcome();
      filtered.forEach(m => {
        if (m.role === 'user') appendUserMessage(m.content || m.message || '');
        else                   appendAiMessage(m.content || m.message || '', m.provider || '');
      });
      scrollToBottom();
    } catch (e) {
      console.warn('restoreConversation error:', e);
    }

    renderConvList();
    // モバイルではサイドバーを閉じる
    if (window.innerWidth < 640) setSidebar(false);
  }

  /* ── 新しい会話 ───────────────────────────────────── */
  function startNewChat() {
    sessionId     = null;
    currentConvId = null;
    clearMessages();
    showWelcome();
    renderConvList();
    dom.input?.focus();
    if (window.innerWidth < 640) setSidebar(false);
  }

  /* ── メッセージ送信 ──────────────────────────────── */
  function onInputChange() {
    const val  = dom.input.value;
    const len  = val.length;
    if (dom.charCount) dom.charCount.textContent = `${len} / 50000`;

    // 高さ自動調整
    dom.input.style.height = 'auto';
    dom.input.style.height = Math.min(dom.input.scrollHeight, 200) + 'px';

    // 送信ボタン有効化
    if (dom.sendBtn) dom.sendBtn.disabled = (len === 0 || isLoading);
  }

  function onInputKeyDown(e) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      sendMessage();
    }
  }

  function triggerSend(text) {
    if (dom.input) {
      dom.input.value = text;
      onInputChange();
      sendMessage();
    }
  }

  async function sendMessage() {
    const text = dom.input?.value.trim();
    if (!text || isLoading) return;

    setLoading(true);
    appendUserMessage(text);
    hideWelcome();

    dom.input.value = '';
    dom.input.style.height = 'auto';
    if (dom.charCount) dom.charCount.textContent = '0 / 50000';
    if (dom.sendBtn) dom.sendBtn.disabled = true;

    showTyping('考え中...');

    try {
      const body = {
        message:    text,
        cliTool:    selectedTool,
        streaming:  true,
        sessionId:  sessionId || undefined,
        context:    'framework',
      };

      const res  = await fetchApi('/api/AI/chat', { method: 'POST', body: JSON.stringify(body) });
      const data = await res.json();

      if (!res.ok) {
        hideTyping();
        appendAiMessage(`❌ エラー: ${data.error || data.message || 'リクエストに失敗しました'}`, '');
        setLoading(false);
        return;
      }

      currentTaskId = data.taskId;
      if (data.sessionId) sessionId = data.sessionId;

      // SignalR が接続されていればイベント待ち、そうでなければポーリング
      if (isSignalRConnected()) {
        setStatus('loading', '応答中...');
        startPolling(currentTaskId); // バックアップポーリング
      } else {
        startPolling(currentTaskId);
      }
    } catch (err) {
      hideTyping();
      appendAiMessage(`❌ 通信エラー: ${err.message}`, '');
      setLoading(false);
      setStatus('error', 'エラー');
    }
  }

  /* ── ポーリング ───────────────────────────────────── */
  function startPolling(taskId) {
    stopPolling();
    let attempts = 0;
    const MAX    = 360; // 最大 6 分 (1s × 360)

    pollTimer = setInterval(async () => {
      if (++attempts > MAX) {
        stopPolling();
        hideTyping();
        appendAiMessage('⏰ タイムアウトしました。もう一度お試しください。', '');
        setLoading(false);
        return;
      }

      try {
        const res  = await fetchApi(`/api/AI/tasks/${taskId}`);
        const task = await res.json();

        if (task.status === 1 /* Completed */ || task.status === 'Completed') {
          onTaskCompleted({ taskId, result: task.result, sessionId: task.sessionId, provider: task.provider });
        } else if (task.status === 2 /* Failed */ || task.status === 'Failed') {
          onTaskError({ taskId, error: task.result || 'タスクが失敗しました' });
        } else if (task.progress) {
          showTyping(`処理中... ${task.progress}%`);
        }
      } catch (e) {
        console.warn('poll error:', e);
      }
    }, 1000);
  }

  function stopPolling() {
    if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
  }

  /* ── SignalR ハンドラー ───────────────────────────── */
  function onTaskCompleted(payload) {
    const { taskId, result, sessionId: sid, provider } = payload;
    if (taskId && taskId !== currentTaskId) return;
    stopPolling();
    hideTyping();

    if (sid) sessionId = sid;
    appendAiMessage(result || '（空の応答）', provider || selectedTool);
    setLoading(false);
    setStatus('ready', '準備完了');

    // 会話リストを更新
    updateConvList(text => {}, result);
    loadConversationHistory();
  }

  function onTaskProgress(payload) {
    if (payload.taskId && payload.taskId !== currentTaskId) return;
    const msg = payload.message || `処理中... ${payload.progress ?? ''}%`;
    showTyping(msg);
  }

  function onTaskError(payload) {
    if (payload.taskId && payload.taskId !== currentTaskId) return;
    stopPolling();
    hideTyping();
    appendAiMessage(`❌ エラー: ${payload.error || '不明なエラー'}`, '');
    setLoading(false);
    setStatus('error', 'エラー');
  }

  /* ── メッセージ描画 ──────────────────────────────── */
  function appendUserMessage(text) {
    const tpl  = document.getElementById('tplUserMsg');
    const node = tpl.content.cloneNode(true);
    const el   = node.querySelector('.msg-user');
    el.querySelector('.msg-bubble').textContent = text;
    el.querySelector('.msg-time').textContent   = now();
    dom.messagesList.appendChild(el);
    scrollToBottom();
  }

  function appendAiMessage(text, provider) {
    const tpl  = document.getElementById('tplAiMsg');
    const node = tpl.content.cloneNode(true);
    const el   = node.querySelector('.msg-ai');
    const bubble = el.querySelector('.msg-bubble');

    // Markdown レンダリング
    if (typeof marked !== 'undefined') {
      bubble.innerHTML = marked.parse(text || '');
    } else {
      bubble.textContent = text || '';
    }

    el.querySelector('.msg-time').textContent = now();

    if (provider) {
      el.querySelector('.msg-provider').textContent = provider;
    } else {
      el.querySelector('.msg-provider').style.display = 'none';
    }

    // コピーボタン
    el.querySelector('.copy-btn')?.addEventListener('click', () => {
      navigator.clipboard.writeText(text).then(() => {
        const btn = el.querySelector('.copy-btn');
        btn.title = 'コピーしました！';
        setTimeout(() => { btn.title = 'コピー'; }, 2000);
      });
    });

    dom.messagesList.appendChild(el);
    scrollToBottom();
  }

  function clearMessages() {
    if (dom.messagesList) dom.messagesList.innerHTML = '';
  }

  /* ── ウェルカム画面 ──────────────────────────────── */
  function showWelcome() {
    if (dom.welcomeScreen) dom.welcomeScreen.style.display = '';
    if (dom.messagesList)  dom.messagesList.innerHTML = '';
  }

  function hideWelcome() {
    if (dom.welcomeScreen) dom.welcomeScreen.style.display = 'none';
  }

  /* ── タイピングインジケーター ─────────────────────── */
  function showTyping(label = '考え中...') {
    if (dom.typingInd) dom.typingInd.style.display = 'flex';
    if (dom.typingLabel) dom.typingLabel.textContent = label;
    scrollToBottom();
  }

  function hideTyping() {
    if (dom.typingInd) dom.typingInd.style.display = 'none';
  }

  /* ── ローディング状態 ────────────────────────────── */
  function setLoading(v) {
    isLoading = v;
    if (dom.sendBtn) dom.sendBtn.disabled = v;
    if (dom.input)   dom.input.disabled   = v;
  }

  /* ── ステータス表示 ──────────────────────────────── */
  function setStatus(type, text) {
    if (dom.statusDot) {
      dom.statusDot.className = 'status-dot ' + type;
    }
    if (dom.statusText) {
      dom.statusText.textContent = text;
    }
  }

  /* ── 会話リスト更新 ──────────────────────────────── */
  function updateConvList(getInput, result) {
    // 現在の会話を先頭に追加（新規の場合）
    if (!currentConvId && sessionId) {
      currentConvId = sessionId;
    }
  }

  /* ── ユーティリティ ──────────────────────────────── */
  function scrollToBottom() {
    if (dom.messagesArea) {
      requestAnimationFrame(() => {
        dom.messagesArea.scrollTop = dom.messagesArea.scrollHeight;
      });
    }
  }

  function now() {
    return new Date().toLocaleTimeString('ja-JP', { hour: '2-digit', minute: '2-digit' });
  }

  function isSignalRConnected() {
    return signalR && typeof signalR.state !== 'undefined' &&
           signalR.state === (signalR.HubConnectionState?.Connected ?? 'Connected');
  }

  async function fetchApi(url, opts = {}) {
    const headers = { 'Content-Type': 'application/json', ...(opts.headers || {}) };
    return fetch(url, { ...opts, headers, credentials: 'same-origin' });
  }

  /* ── 公開 API ─────────────────────────────────────── */
  return { init };

})();
