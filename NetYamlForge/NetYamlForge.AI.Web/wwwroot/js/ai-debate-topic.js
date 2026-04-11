// AI Debate Topic フロントエンド (AIDebateOrchestrator 対応)
let debateConnection = null;
let currentTopicId = null;
let currentStatus = null;

function initDebatePage(topicId, initialStatus) {
    currentTopicId = topicId;
    currentStatus = initialStatus;
    connectSignalR(topicId);
    scrollToBottom();
}

function connectSignalR(topicId) {
    debateConnection = new signalR.HubConnectionBuilder()
        .withUrl('/aiDebateHub')
        .withAutomaticReconnect()
        .build();

    debateConnection.on('Connected', function (data) {
        debateConnection.invoke('JoinTopic', topicId).catch(console.error);
    });

    debateConnection.on('NewMessage', function (msg) {
        appendMessage(msg);
        updateCounters(msg.aiSide, msg.messageIndex);
        hideTypingIndicator();
        scrollToBottom();
    });

    debateConnection.on('StatusUpdate', function (update) {
        updateStatus(update);
    });

    debateConnection.start().then(function () {
        debateConnection.invoke('JoinTopic', topicId).catch(console.error);
    }).catch(function (err) {
        console.error('SignalR connection failed:', err);
    });
}

function appendMessage(msg) {
    const container = document.getElementById('messagesContainer');
    const indicator = document.getElementById('typingIndicator');

    const row = document.createElement('div');
    row.className = 'message-row ' + msg.aiSide + '-side';
    row.id = 'msg-' + msg.id;

    const time = msg.createdAt ? msg.createdAt.substring(11, 19) : new Date().toLocaleTimeString('ja-JP');
    const isFinal = msg.messageIndex >= 11;

    row.innerHTML =
        '<div class="message-bubble ' + msg.aiSide + '">' +
            '<div class="message-meta">' +
                '<span class="message-sender">🤖 ' + escapeHtml(msg.aiProvider) + '</span>' +
                '<span class="message-turn">第' + msg.messageIndex + '発言' + (isFinal ? ' (最終)' : '') + '</span>' +
                '<span class="message-time">' + time + '</span>' +
            '</div>' +
            '<div class="message-content markdown-content">' + renderDebateMarkdown(msg.content) + '</div>' +
        '</div>';

    container.insertBefore(row, indicator);
    showTypingIndicator();
}

function updateCounters(aiSide, messageIndex) {
    if (aiSide === 'left') {
        const el = document.getElementById('leftCount');
        if (el) el.textContent = messageIndex;
    } else {
        const el = document.getElementById('rightCount');
        if (el) el.textContent = messageIndex;
    }
}

function updateStatus(update) {
    currentStatus = update.status;

    const badge = document.getElementById('statusBadge');
    if (badge) {
        badge.className = 'status-badge status-' + update.status;
        badge.textContent = getStatusLabel(update.status);
    }

    // コントロールバーを更新
    const controlBar = document.querySelector('.control-bar');
    if (controlBar) {
        if (update.status === 'running') {
            controlBar.innerHTML =
                '<button class="btn-cancel" onclick="cancelDebate(' + currentTopicId + ')">⏹ 議論を中止</button>' +
                '<div class="running-indicator">' +
                    '<div class="spinner"></div>' +
                    '<span>議論中... 左: ' + update.leftCount + '/11 右: ' + update.rightCount + '/11</span>' +
                '</div>';
        } else if (update.status === 'completed') {
            hideTypingIndicator();
            controlBar.innerHTML = '<div class="completed-badge">✅ 議論が完了しました (左: ' + update.leftCount + '/11 右: ' + update.rightCount + '/11)</div>';
        } else if (update.status === 'cancelled') {
            hideTypingIndicator();
            controlBar.innerHTML =
                '<div class="cancelled-badge">⛔ 議論がキャンセルされました</div>' +
                '<button class="btn-start" onclick="startDebate(' + currentTopicId + ')">▶ 再開</button>';
        } else if (update.status === 'error') {
            hideTypingIndicator();
            const errMsg = update.error ? ': ' + update.error : '';
            controlBar.innerHTML =
                '<div class="error-badge">❌ エラーが発生しました' + errMsg + '</div>' +
                '<button class="btn-start" onclick="startDebate(' + currentTopicId + ')">▶ 再試行</button>';
        }
    }

    if (update.leftCount) {
        const el = document.getElementById('leftCount');
        if (el) el.textContent = update.leftCount;
    }
    if (update.rightCount) {
        const el = document.getElementById('rightCount');
        if (el) el.textContent = update.rightCount;
    }
}

async function startDebate(topicId) {
    try {
        const resp = await fetch('/api/debate/topics/' + topicId + '/start', { method: 'POST' });
        if (!resp.ok) {
            const data = await resp.json();
            alert(data.error || '開始に失敗しました');
        }
    } catch (e) {
        alert('通信エラー: ' + e.message);
    }
}

async function cancelDebate(topicId) {
    if (!confirm('議論を中止してもよいですか？')) return;
    try {
        await fetch('/api/debate/topics/' + topicId + '/cancel', { method: 'POST' });
    } catch (e) {
        alert('通信エラー: ' + e.message);
    }
}

function showTypingIndicator() {
    const el = document.getElementById('typingIndicator');
    if (el && currentStatus === 'running') {
        el.classList.remove('hidden');
        scrollToBottom();
    }
}

function hideTypingIndicator() {
    const el = document.getElementById('typingIndicator');
    if (el) el.classList.add('hidden');
}

function scrollToBottom() {
    const container = document.getElementById('messagesContainer');
    if (container) {
        setTimeout(function () {
            container.scrollTop = container.scrollHeight;
        }, 100);
    }
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.appendChild(document.createTextNode(text || ''));
    return div.innerHTML;
}

function renderDebateMarkdown(text) {
    if (!text) return '';
    if (typeof marked !== 'undefined') {
        try {
            return marked.parse(text);
        } catch (e) { /* fallthrough */ }
    }
    return '<p>' + escapeHtml(text) + '</p>';
}

function getStatusLabel(status) {
    const labels = {
        pending: '⏳ 待機中',
        running: '🔄 議論中',
        completed: '✅ 完了',
        cancelled: '⛔ キャンセル',
        error: '❌ エラー'
    };
    return labels[status] || status;
}
