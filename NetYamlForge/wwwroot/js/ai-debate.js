/**
 * AI Debate Platform — フロントエンド JS
 * SignalR でリアルタイムにメッセージを受信し、対話形式で表示します。
 */
(function () {
    'use strict';

    const API_BASE = '/api/ai-debate';
    const HUB_URL = '/aiDebateHub';

    let connection = null;
    let currentSessionId = null;

    // ── DOM 要素 ──
    const createPanel   = document.getElementById('createPanel');
    const debateViewer  = document.getElementById('debateViewer');
    const sessionsPanel = document.getElementById('sessionsPanel');
    const sessionsList  = document.getElementById('sessionsList');

    const createBtn   = document.getElementById('createDebateBtn');
    const createError = document.getElementById('createError');

    const messagesContainer = document.getElementById('messagesContainer');
    const debateLoading     = document.getElementById('debateLoading');
    const loadingText       = document.getElementById('loadingText');
    const completedNotice   = document.getElementById('completedNotice');

    const sessionTopicDisplay = document.getElementById('sessionTopicDisplay');
    const sessionStatus       = document.getElementById('sessionStatus');
    const backToListBtn       = document.getElementById('backToListBtn');

    const leftProviderLabel  = document.getElementById('leftProviderLabel');
    const leftRoleLabel      = document.getElementById('leftRoleLabel');
    const rightProviderLabel = document.getElementById('rightProviderLabel');
    const rightRoleLabel     = document.getElementById('rightRoleLabel');

    // ── SignalR 接続 ──
    async function connectSignalR() {
        connection = new signalR.HubConnectionBuilder()
            .withUrl(HUB_URL)
            .withAutomaticReconnect()
            .build();

        connection.on('DebateMessageReceived', onMessageReceived);
        connection.on('DebateStatusChanged', onStatusChanged);

        try {
            await connection.start();
            console.log('[AIDebate] SignalR connected');
        } catch (err) {
            console.error('[AIDebate] SignalR connection failed:', err);
        }
    }

    async function joinSession(sessionId) {
        if (connection && connection.state === signalR.HubConnectionState.Connected) {
            await connection.invoke('JoinDebate', String(sessionId));
        }
    }

    // ── ディベート作成 ──
    createBtn.addEventListener('click', async () => {
        const topic       = document.getElementById('debateTopic').value.trim();
        const leftProvider  = document.getElementById('leftProvider').value;
        const rightProvider = document.getElementById('rightProvider').value;
        const leftRole    = document.getElementById('leftRole').value.trim() || 'Advocate';
        const rightRole   = document.getElementById('rightRole').value.trim() || 'Critic';

        if (!topic) {
            showError('討議テーマを入力してください');
            return;
        }

        hideError();
        createBtn.disabled = true;
        createBtn.textContent = '作成中...';

        try {
            // 1. セッション作成
            const createRes = await fetch(`${API_BASE}/sessions`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ topic, leftProvider, rightProvider, leftRole, rightRole })
            });

            if (!createRes.ok) {
                const err = await createRes.json().catch(() => ({}));
                throw new Error(err.error || `HTTP ${createRes.status}`);
            }

            const session = await createRes.json();

            // 2. ビューアを開いてメッセージ表示を初期化
            openDebateViewer(session);
            await joinSession(session.id);

            // 3. ディベート開始
            const startRes = await fetch(`${API_BASE}/sessions/${session.id}/start`, { method: 'POST' });
            if (!startRes.ok) {
                const err = await startRes.json().catch(() => ({}));
                throw new Error(err.error || `Start failed: HTTP ${startRes.status}`);
            }

            // ローディング表示
            showLoading('左側 AI が思考中...');

            // 4. セッション一覧に追加
            addSessionCard(session);

        } catch (err) {
            showError(`エラー: ${err.message}`);
            createBtn.disabled = false;
            createBtn.textContent = '🚀 ディベートを開始';
        }
    });

    // ── ビューアを開く ──
    function openDebateViewer(session) {
        currentSessionId = session.id;

        // UI の切り替え
        debateViewer.style.display = 'block';
        completedNotice.style.display = 'none';

        // セッション情報表示
        sessionTopicDisplay.textContent = `📌 ${session.topic}`;
        updateStatusBadge(session.status);

        leftProviderLabel.textContent  = session.leftProvider.toUpperCase();
        leftRoleLabel.textContent      = session.leftRole;
        rightProviderLabel.textContent = session.rightProvider.toUpperCase();
        rightRoleLabel.textContent     = session.rightRole;

        // メッセージエリアをクリア
        messagesContainer.innerHTML = '';
        messagesContainer.appendChild(debateLoading);
        debateLoading.style.display = 'none';

        // ボタン状態をリセット
        createBtn.disabled = false;
        createBtn.textContent = '🚀 ディベートを開始';

        // スクロール
        debateViewer.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    // 既存セッションを読み込んで表示
    async function loadSession(sessionId, meta) {
        currentSessionId = sessionId;

        debateViewer.style.display = 'block';
        completedNotice.style.display = 'none';

        sessionTopicDisplay.textContent = `📌 ${meta.topic}`;
        updateStatusBadge(meta.status);

        leftProviderLabel.textContent  = meta.leftProvider.toUpperCase();
        leftRoleLabel.textContent      = meta.leftRole;
        rightProviderLabel.textContent = meta.rightProvider.toUpperCase();
        rightRoleLabel.textContent     = meta.rightRole;

        messagesContainer.innerHTML = '';

        await joinSession(sessionId);

        try {
            const res = await fetch(`${API_BASE}/sessions/${sessionId}/messages`);
            if (res.ok) {
                const messages = await res.json();
                messages.forEach(m => renderMessage(m, false));
            }
        } catch (err) {
            console.error('[AIDebate] Failed to load messages:', err);
        }

        if (meta.status === 'completed') {
            completedNotice.style.display = 'block';
        } else if (meta.status === 'active') {
            showLoading('AI が思考中...');
        }

        debateViewer.scrollIntoView({ behavior: 'smooth', block: 'start' });
        scrollToBottom();
    }

    // ── メッセージ受信ハンドラ ──
    function onMessageReceived(event) {
        if (event.sessionId !== currentSessionId) return;

        hideLoading();
        renderMessage(event, true);
        scrollToBottom();

        // 次の AI に渡す前のローディング表示
        if (!event.isFinal) {
            const nextSide = event.side === 'left' ? '右側' : '左側';
            setTimeout(() => showLoading(`${nextSide} AI が思考中...`), 200);
        }
    }

    function onStatusChanged(event) {
        if (event.sessionId !== currentSessionId) return;

        updateStatusBadge(event.status);

        if (event.status === 'completed') {
            hideLoading();
            completedNotice.style.display = 'block';
            scrollToBottom();
        } else if (event.status === 'error') {
            hideLoading();
            const errEl = document.createElement('div');
            errEl.className = 'alert alert-danger';
            errEl.style.margin = '12px';
            errEl.textContent = `⚠️ ${event.message || 'エラーが発生しました'}`;
            messagesContainer.appendChild(errEl);
            scrollToBottom();
        }
    }

    // ── メッセージレンダリング ──
    function renderMessage(msg, animate) {
        const isLeft   = msg.side === 'left';
        const isFinal  = msg.isFinal || msg.messageNumber === 11;
        const provider = (msg.provider || '').toUpperCase();
        const role     = msg.role || '';
        const time     = formatTime(msg.createdAt);

        const wrapper = document.createElement('div');
        wrapper.className = `debate-message ${isLeft ? 'left-message' : 'right-message'}${isFinal ? ' is-final' : ''}`;
        if (!animate) wrapper.style.animation = 'none';

        const initial = provider.charAt(0) || (isLeft ? 'L' : 'R');

        wrapper.innerHTML = `
            <div class="message-avatar">${initial}</div>
            <div class="message-body">
                <div class="message-meta">
                    <span class="message-provider">${escHtml(provider)}</span>
                    <span class="message-role">${escHtml(role)}</span>
                    <span class="message-round">第${msg.messageNumber}回</span>
                    ${isFinal ? '<span class="message-final-badge">総括</span>' : ''}
                </div>
                <div class="message-bubble">${renderMarkdownContent(msg.content)}</div>
                <div class="message-time">${time}</div>
            </div>`;

        // ローディングの前に挿入
        if (messagesContainer.contains(debateLoading)) {
            messagesContainer.insertBefore(wrapper, debateLoading);
        } else {
            messagesContainer.appendChild(wrapper);
        }
    }

    // ── Markdown レンダリング ──
    function renderMarkdownContent(text) {
        if (typeof marked === 'undefined' || !text) {
            return `<p>${escHtml(text || '')}</p>`;
        }
        try {
            return marked.parse(text);
        } catch (e) {
            return `<p>${escHtml(text)}</p>`;
        }
    }

    // ── セッションカードを一覧に追加 ──
    function addSessionCard(session) {
        if (!sessionsList) return;

        const card = document.createElement('div');
        card.className = 'session-card';
        card.dataset.sessionId    = session.id;
        card.dataset.sessionTopic = session.topic;
        card.dataset.leftProvider = session.leftProvider;
        card.dataset.rightProvider = session.rightProvider;
        card.dataset.leftRole     = session.leftRole;
        card.dataset.rightRole    = session.rightRole;
        card.dataset.status       = session.status;

        card.innerHTML = `
            <div class="session-card-topic">${escHtml(session.topic)}</div>
            <div class="session-card-meta">
                <span class="provider-tag left-tag">${escHtml(session.leftProvider)} (${escHtml(session.leftRole)})</span>
                <span class="vs-small">vs</span>
                <span class="provider-tag right-tag">${escHtml(session.rightProvider)} (${escHtml(session.rightRole)})</span>
                <span class="status-badge status-${session.status}">待機中</span>
                <span class="session-date">${formatTime(session.createdAt)}</span>
            </div>`;

        sessionsList.prepend(card);
        bindCardClick(card);
    }

    // ── セッションカードクリック ──
    function bindCardClick(card) {
        card.addEventListener('click', () => {
            const id = parseInt(card.dataset.sessionId, 10);
            loadSession(id, {
                topic:         card.dataset.sessionTopic,
                status:        card.dataset.status,
                leftProvider:  card.dataset.leftProvider,
                rightProvider: card.dataset.rightProvider,
                leftRole:      card.dataset.leftRole,
                rightRole:     card.dataset.rightRole,
            });
        });
    }

    document.querySelectorAll('.session-card').forEach(bindCardClick);

    // ── 戻るボタン ──
    backToListBtn.addEventListener('click', () => {
        debateViewer.style.display = 'none';
        currentSessionId = null;
        sessionsPanel.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });

    // ── ステータスバッジ更新 ──
    function updateStatusBadge(status) {
        const labels = { pending: '待機中', active: '進行中 🟢', completed: '完了 ✅', error: 'エラー ⚠️' };
        sessionStatus.textContent = labels[status] || status;
    }

    // ── ローディング表示制御 ──
    function showLoading(text) {
        if (loadingText) loadingText.textContent = text || 'AI が思考中...';
        debateLoading.style.display = 'flex';
        scrollToBottom();
    }

    function hideLoading() {
        debateLoading.style.display = 'none';
    }

    // ── ユーティリティ ──
    function scrollToBottom() {
        setTimeout(() => {
            messagesContainer.scrollTop = messagesContainer.scrollHeight;
        }, 50);
    }

    function formatTime(dateStr) {
        if (!dateStr) return '';
        try {
            const d = new Date(dateStr.replace(' ', 'T') + 'Z');
            return d.toLocaleString('ja-JP', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' });
        } catch {
            return dateStr.substring(0, 16);
        }
    }

    function escHtml(str) {
        if (!str) return '';
        return str
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function showError(msg) {
        createError.textContent = msg;
        createError.classList.remove('d-none');
    }

    function hideError() {
        createError.classList.add('d-none');
    }

    // ── 起動 ──
    connectSignalR();

})();
