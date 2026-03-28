/**
 * AI Chat Widget for Auto Dealer
 * 自動車ディーラー AI 窓口 Web チャットコンポーネント
 */

(function() {
    'use strict';

    // SignalR 接続
    let connection = null;
    let conversationId = null;
    let isTyping = false;

    // 設定
    const config = {
        apiBaseUrl: '/api/ai',
        signalRUrl: '/aiChatHub',
        themeColor: '#007bff',
        position: 'bottom-right',
        welcomeMessage: 'こんにちは！自動車ディーラー AI アシスタントです。どのようなご用件でしょうか？'
    };

    /**
     * チャットウィジェットを初期化
     */
    function initChatWidget() {
        createChatHTML();
        attachEventListeners();
        startNewConversation();
    }

    /**
     * チャット HTML を作成
     */
    function createChatHTML() {
        const chatHTML = `
            <div id="ai-chat-widget" class="ai-chat-widget">
                <!-- チャットボタン -->
                <button id="ai-chat-toggle" class="ai-chat-toggle">
                    <svg width="24" height="24" viewBox="0 0 24 24" fill="none">
                        <path d="M20 2H4C2.9 2 2 2.9 2 4V22L6 18H20C21.1 18 22 17.1 22 16V4C22 2.9 21.1 2 20 2Z"
                              fill="white"/>
                    </svg>
                </button>

                <!-- チャットウィンドウ -->
                <div id="ai-chat-window" class="ai-chat-window" style="display: none;">
                    <!-- ヘッダー -->
                    <div class="ai-chat-header">
                        <div class="ai-chat-header-title">
                            <span class="ai-chat-status-indicator"></span>
                            AI アシスタント
                        </div>
                        <button id="ai-chat-close" class="ai-chat-close">&times;</button>
                    </div>

                    <!-- メッセージエリア -->
                    <div id="ai-chat-messages" class="ai-chat-messages">
                        <div class="ai-chat-message ai-chat-message-ai">
                            <div class="ai-chat-message-avatar">AI</div>
                            <div class="ai-chat-message-body">
                                <div class="ai-chat-provider-label">AI</div>
                                <div class="ai-chat-message-content">${config.welcomeMessage}</div>
                                <div class="ai-chat-message-time">${formatTimestamp(new Date())}</div>
                            </div>
                        </div>
                    </div>

                    <!-- 入力エリア -->
                    <div class="ai-chat-input-area">
                        <div id="ai-chat-typing" class="ai-chat-typing-indicator" style="display: none;">
                            <span></span><span></span><span></span>
                        </div>
                        <input
                            type="text"
                            id="ai-chat-input"
                            class="ai-chat-input"
                            placeholder="メッセージを入力..."
                            autocomplete="off"
                        />
                        <button id="ai-chat-send" class="ai-chat-send">
                            <svg width="20" height="20" viewBox="0 0 24 24" fill="none">
                                <path d="M2.01 21L23 12L2.01 3L2 10L17 12L2 14L2.01 21Z" fill="white"/>
                            </svg>
                        </button>
                    </div>

                    <!-- クイック返信 -->
                    <div id="ai-chat-quick-replies" class="ai-chat-quick-replies"></div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', chatHTML);
        addStyles();
    }
    
    /**
     * タイムスタンプをフォーマット
     * @param {Date} date - 日付オブジェクト
     * @returns {string} フォーマット済み文字列 (yyyy/MM/dd HH:mm:ss)
     */
    function formatTimestamp(date) {
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        const hours = String(date.getHours()).padStart(2, '0');
        const minutes = String(date.getMinutes()).padStart(2, '0');
        const seconds = String(date.getSeconds()).padStart(2, '0');
        return `${year}/${month}/${day} ${hours}:${minutes}:${seconds}`;
    }

    /**
     * スタイルを追加
     */
    function addStyles() {
        const style = document.createElement('style');
        style.textContent = `
            .ai-chat-widget {
                position: fixed;
                bottom: 20px;
                right: 20px;
                z-index: 9999;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            }

            .ai-chat-toggle {
                width: 60px;
                height: 60px;
                border-radius: 50%;
                background: ${config.themeColor};
                border: none;
                cursor: pointer;
                box-shadow: 0 4px 12px rgba(0,0,0,0.15);
                transition: transform 0.3s, box-shadow 0.3s;
                display: flex;
                align-items: center;
                justify-content: center;
            }

            .ai-chat-toggle:hover {
                transform: scale(1.1);
                box-shadow: 0 6px 16px rgba(0,0,0,0.2);
            }

            .ai-chat-window {
                position: absolute;
                bottom: 80px;
                right: 0;
                width: 380px;
                height: 600px;
                background: white;
                border-radius: 12px;
                box-shadow: 0 8px 32px rgba(0,0,0,0.2);
                display: flex;
                flex-direction: column;
                overflow: hidden;
                animation: slideIn 0.3s ease-out;
            }

            @keyframes slideIn {
                from {
                    opacity: 0;
                    transform: translateY(20px);
                }
                to {
                    opacity: 1;
                    transform: translateY(0);
                }
            }

            .ai-chat-header {
                background: ${config.themeColor};
                color: white;
                padding: 16px;
                display: flex;
                justify-content: space-between;
                align-items: center;
                font-weight: 600;
            }

            .ai-chat-status-indicator {
                display: inline-block;
                width: 8px;
                height: 8px;
                background: #4ade80;
                border-radius: 50%;
                margin-right: 8px;
                animation: pulse 2s infinite;
            }

            @keyframes pulse {
                0%, 100% { opacity: 1; }
                50% { opacity: 0.5; }
            }

            .ai-chat-close {
                background: none;
                border: none;
                color: white;
                font-size: 28px;
                cursor: pointer;
                line-height: 1;
                padding: 0;
                width: 30px;
                height: 30px;
            }

            .ai-chat-messages {
                flex: 1;
                overflow-y: auto;
                padding: 16px;
                background: #f8fafc;
            }

            .ai-chat-message {
                display: flex;
                margin-bottom: 16px;
                animation: messageIn 0.3s ease-out;
            }

            @keyframes messageIn {
                from {
                    opacity: 0;
                    transform: translateY(10px);
                }
                to {
                    opacity: 1;
                    transform: translateY(0);
                }
            }

            .ai-chat-message-ai {
                justify-content: flex-start;
            }

            .ai-chat-message-user {
                justify-content: flex-end;
            }

            .ai-chat-message-avatar {
                width: 32px;
                height: 32px;
                border-radius: 50%;
                background: ${config.themeColor};
                color: white;
                display: flex;
                align-items: center;
                justify-content: center;
                font-size: 12px;
                font-weight: bold;
                margin-right: 8px;
                flex-shrink: 0;
            }

            .ai-chat-message-user .ai-chat-message-avatar {
                display: none;
            }

            .ai-chat-message-body {
                display: flex;
                flex-direction: column;
                align-items: flex-start;
                max-width: 75%;
            }

            .ai-chat-message-user .ai-chat-message-body {
                align-items: flex-end;
            }

            .ai-chat-provider-label {
                font-size: 11px;
                color: ${config.themeColor};
                font-weight: 600;
                margin-bottom: 4px;
                text-transform: uppercase;
                letter-spacing: 0.5px;
            }

            .ai-chat-message-content {
                padding: 12px 16px;
                border-radius: 12px;
                line-height: 1.5;
                word-wrap: break-word;
            }

            .ai-chat-message-ai .ai-chat-message-content {
                background: white;
                border: 1px solid #e2e8f0;
            }

            .ai-chat-message-user .ai-chat-message-content {
                background: ${config.themeColor};
                color: white;
            }

            .ai-chat-message-time {
                font-size: 11px;
                color: #94a3b8;
                margin-top: 6px;
                padding: 0 4px;
            }

            .ai-chat-message-user .ai-chat-message-time {
                color: rgba(255, 255, 255, 0.8);
                text-align: right;
            }

            .ai-chat-input-area {
                padding: 12px 16px;
                border-top: 1px solid #e2e8f0;
                background: white;
                position: relative;
            }

            .ai-chat-typing-indicator {
                position: absolute;
                top: -30px;
                left: 16px;
                display: flex;
                gap: 4px;
            }

            .ai-chat-typing-indicator span {
                width: 8px;
                height: 8px;
                background: #94a3b8;
                border-radius: 50%;
                animation: typing 1.4s infinite;
            }

            .ai-chat-typing-indicator span:nth-child(2) {
                animation-delay: 0.2s;
            }

            .ai-chat-typing-indicator span:nth-child(3) {
                animation-delay: 0.4s;
            }

            @keyframes typing {
                0%, 60%, 100% {
                    transform: translateY(0);
                }
                30% {
                    transform: translateY(-10px);
                }
            }

            .ai-chat-input {
                width: 100%;
                padding: 12px 16px;
                border: 1px solid #e2e8f0;
                border-radius: 24px;
                font-size: 14px;
                outline: none;
                transition: border-color 0.3s;
                box-sizing: border-box;
            }

            .ai-chat-input:focus {
                border-color: ${config.themeColor};
            }

            .ai-chat-send {
                position: absolute;
                right: 20px;
                bottom: 18px;
                background: ${config.themeColor};
                border: none;
                width: 36px;
                height: 36px;
                border-radius: 50%;
                cursor: pointer;
                display: flex;
                align-items: center;
                justify-content: center;
                transition: transform 0.2s;
            }

            .ai-chat-send:hover {
                transform: scale(1.1);
            }

            .ai-chat-quick-replies {
                padding: 8px 16px;
                background: white;
                border-top: 1px solid #e2e8f0;
                display: flex;
                flex-wrap: wrap;
                gap: 8px;
            }

            .ai-chat-quick-reply-btn {
                padding: 8px 16px;
                background: #f1f5f9;
                border: 1px solid #e2e8f0;
                border-radius: 20px;
                font-size: 13px;
                cursor: pointer;
                transition: all 0.2s;
            }

            .ai-chat-quick-reply-btn:hover {
                background: ${config.themeColor};
                color: white;
                border-color: ${config.themeColor};
            }
        `;
        document.head.appendChild(style);
    }

    /**
     * イベントリスナーをアタッチ
     */
    function attachEventListeners() {
        const toggleBtn = document.getElementById('ai-chat-toggle');
        const closeBtn = document.getElementById('ai-chat-close');
        const chatWindow = document.getElementById('ai-chat-window');
        const sendBtn = document.getElementById('ai-chat-send');
        const chatInput = document.getElementById('ai-chat-input');

        toggleBtn.addEventListener('click', () => {
            const isVisible = chatWindow.style.display !== 'none';
            chatWindow.style.display = isVisible ? 'none' : 'flex';
            if (!isVisible) {
                chatInput.focus();
            }
        });

        closeBtn.addEventListener('click', () => {
            chatWindow.style.display = 'none';
        });

        sendBtn.addEventListener('click', sendMessage);
        chatInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                sendMessage();
            }
        });
    }

    /**
     * 新しい対話を開始
     */
    async function startNewConversation() {
        try {
            const response = await fetch(`${config.apiBaseUrl}/conversations`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    channel: 'web',
                    initialMessage: null
                })
            });

            const data = await response.json();
            conversationId = data.conversationId;
            
            // SignalR 接続を開始
            startSignalRConnection();
        } catch (error) {
            console.error('Failed to start conversation:', error);
        }
    }

    /**
     * SignalR 接続を開始
     */
    function startSignalRConnection() {
        // SignalR JavaScript クライアントが必要
        // CDN から読み込み
        const script = document.createElement('script');
        script.src = 'https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js';
        script.onload = () => {
            connection = new signalR.HubConnectionBuilder()
                .withUrl(config.signalRUrl)
                .withAutomaticReconnect()
                .build();

            connection.on('ai_response', (data) => {
                addMessage('ai', data.message, data.provider);
                hideTypingIndicator();

                if (data.quickReplies && data.quickReplies.length > 0) {
                    showQuickReplies(data.quickReplies);
                }
            });

            connection.on('typing_start', () => {
                showTypingIndicator();
            });

            connection.on('typing_stop', () => {
                hideTypingIndicator();
            });

            connection.start()
                .then(() => {
                    console.log('SignalR connected');
                })
                .catch(err => console.error('SignalR error:', err));
        };
        document.head.appendChild(script);
    }

    /**
     * メッセージを送信
     */
    async function sendMessage() {
        const chatInput = document.getElementById('ai-chat-input');
        const message = chatInput.value.trim();
        
        if (!message || !conversationId) return;

        // ユーザーメッセージを表示
        addMessage('user', message);
        chatInput.value = '';

        // 入力中インジケーターを表示
        showTypingIndicator();

        // REST API で送信（SignalR が使えない場合のフォールバック）
        try {
            const response = await fetch(`${config.apiBaseUrl}/conversations/${conversationId}/messages`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    content: message,
                    messageType: 'text'
                })
            });

            const data = await response.json();
            hideTypingIndicator();

            // AI 応答を表示
            addMessage('ai', data.responseText, data.provider);

            if (data.quickReplies && data.quickReplies.length > 0) {
                showQuickReplies(data.quickReplies);
            }
        } catch (error) {
            console.error('Failed to send message:', error);
            hideTypingIndicator();
            addMessage('ai', '申し訳ございませんが、エラーが発生しました。');
        }
    }

    /**
     * メッセージを追加
     * @param {string} sender - 'user' | 'ai'
     * @param {string} content - メッセージ内容
     * @param {string} provider - AI プロバイダー名 (qwen, claude, copilot など)
     * @param {Date} timestamp - タイムスタンプ
     */
    function addMessage(sender, content, provider = null, timestamp = null) {
        const messagesContainer = document.getElementById('ai-chat-messages');
        const messageDiv = document.createElement('div');
        messageDiv.className = `ai-chat-message ai-chat-message-${sender}`;
        
        const now = timestamp || new Date();
        const timeStr = formatTimestamp(now);
        
        // AI アバターとプロバイダー名
        let avatarHtml = '';
        let providerLabel = '';
        if (sender === 'ai') {
            const providerName = provider || 'AI';
            avatarHtml = `<div class="ai-chat-message-avatar">${escapeHtml(providerName.toUpperCase())}</div>`;
            providerLabel = `<div class="ai-chat-provider-label">${escapeHtml(providerName)}</div>`;
        }

        messageDiv.innerHTML = `
            ${avatarHtml}
            <div class="ai-chat-message-body">
                ${providerLabel}
                <div class="ai-chat-message-content">${escapeHtml(content)}</div>
                <div class="ai-chat-message-time">${timeStr}</div>
            </div>
        `;

        messagesContainer.appendChild(messageDiv);
        messagesContainer.scrollTop = messagesContainer.scrollHeight;
    }

    /**
     * 入力中インジケーターを表示
     */
    function showTypingIndicator() {
        const indicator = document.getElementById('ai-chat-typing');
        indicator.style.display = 'flex';
    }

    /**
     * 入力中インジケーターを非表示
     */
    function hideTypingIndicator() {
        const indicator = document.getElementById('ai-chat-typing');
        indicator.style.display = 'none';
    }

    /**
     * クイック返信を表示
     */
    function showQuickReplies(replies) {
        const container = document.getElementById('ai-chat-quick-replies');
        container.innerHTML = '';
        
        replies.forEach(reply => {
            const btn = document.createElement('button');
            btn.className = 'ai-chat-quick-reply-btn';
            btn.textContent = reply.label;
            btn.addEventListener('click', () => {
                document.getElementById('ai-chat-input').value = reply.actionValue;
                sendMessage();
            });
            container.appendChild(btn);
        });
    }

    /**
     * HTML エスケープ
     */
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // ページ読み込み時に初期化
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initChatWidget);
    } else {
        initChatWidget();
    }
})();
