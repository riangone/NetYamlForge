// AI 自然语言查询聊天组件 JavaScript

(function() {
    'use strict';

    // 状态变量
    let connection = null;
    let isConnected = false;
    let currentProject = null;
    let messageCount = 0;

    // 配置
    const CONFIG = {
        signalRUrl: '/nlQueryHub',
        maxMessages: 50
    };

    // 初始化
    document.addEventListener('DOMContentLoaded', function() {
        const container = document.getElementById('ai-query-chat-container');
        if (!container) return;

        currentProject = container.dataset.project;
        console.log('AI Query Chat: 初始化，项目 =', currentProject);

        initSignalR();
        bindEvents();
    });

    // 初始化 SignalR 连接
    function initSignalR() {
        if (typeof signalR === 'undefined') {
            console.error('AI Query Chat: SignalR 库未加载');
            showError('SignalR 库未加载，请刷新页面重试');
            return;
        }

        connection = new signalR.HubConnectionBuilder()
            .withUrl(CONFIG.signalRUrl)
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .build();

        // 注册事件处理
        registerSignalREvents();

        // 启动连接
        startConnection();
    }

    // 启动 SignalR 连接
    async function startConnection() {
        try {
            await connection.start();
            console.log('AI Query Chat: SignalR 连接已建立');
            isConnected = true;

            // 连接到项目
            if (currentProject) {
                await connection.invoke('Connect', currentProject);
            }

            updateConnectionStatus(true);
        } catch (err) {
            console.error('AI Query Chat: 连接失败:', err);
            isConnected = false;
            updateConnectionStatus(false);
            setTimeout(startConnection, 5000);
        }
    }

    // 注册 SignalR 事件
    function registerSignalREvents() {
        // 连接成功
        connection.on('connected', (data) => {
            console.log('AI Query Chat: 已连接到服务器', data);
        });

        // 思考开始
        connection.on('thinking_start', (data) => {
            showThinking(true);
            addUserMessage(data.query);
        });

        // 解析完成
        connection.on('parsing_complete', (data) => {
            console.log('AI Query Chat: 解析完成', data);
            showPreview(data.parsedQuery);
        });

        // 查询完成
        connection.on('query_complete', (data) => {
            console.log('AI Query Chat: 查询完成', data);
            showThinking(false);
            hidePreview();
            addAssistantMessage(data.markdown, data);
        });

        // 思考停止
        connection.on('thinking_stop', () => {
            showThinking(false);
        });

        // 错误
        connection.on('error', (data) => {
            console.error('AI Query Chat: 错误', data);
            showThinking(false);
            showError(data.message);
        });

        // 查询取消
        connection.on('query_cancelled', () => {
            console.log('AI Query Chat: 查询已取消');
            showThinking(false);
        });

        // 连接重连
        connection.onreconnecting(() => {
            console.log('AI Query Chat: 正在重连...');
            updateConnectionStatus(false);
        });

        connection.onreconnected(() => {
            console.log('AI Query Chat: 重连成功');
            isConnected = true;
            updateConnectionStatus(true);
        });

        connection.onclose(() => {
            console.log('AI Query Chat: 连接已关闭');
            isConnected = false;
            updateConnectionStatus(false);
            setTimeout(startConnection, 5000);
        });
    }

    // 绑定事件
    function bindEvents() {
        const sendBtn = document.getElementById('ai-query-send-btn');
        const input = document.getElementById('ai-query-input');
        const previewClose = document.getElementById('ai-query-preview-close');
        const suggestions = document.querySelectorAll('.ai-query-suggestions li');

        // 发送按钮点击
        if (sendBtn) {
            sendBtn.addEventListener('click', sendQuery);
        }

        // 输入框键盘事件
        if (input) {
            input.addEventListener('keydown', function(e) {
                if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    sendQuery();
                }
            });
        }

        // 关闭预览
        if (previewClose) {
            previewClose.addEventListener('click', hidePreview);
        }

        // 建议点击
        suggestions.forEach(li => {
            li.addEventListener('click', function() {
                if (input) {
                    input.value = this.textContent;
                    sendQuery();
                }
            });
        });
    }

    // 发送查询
    function sendQuery() {
        const input = document.getElementById('ai-query-input');
        if (!input || !input.value.trim()) return;

        const query = input.value.trim();
        input.value = '';
        input.style.height = 'auto'; // 重置高度

        if (!isConnected) {
            showError('尚未连接到服务器，请等待连接...');
            return;
        }

        console.log('AI Query Chat: 发送查询:', query);
        connection.invoke('SendQuery', query, currentProject, null)
            .catch(err => console.error('发送失败:', err));
    }

    // 添加用户消息
    function addUserMessage(text) {
        const messagesContainer = document.getElementById('ai-query-messages');
        if (!messagesContainer) return;

        const messageDiv = createMessageElement('user', text);
        messagesContainer.appendChild(messageDiv);
        scrollToBottom();
        trimMessages();
    }

    // 添加助手消息
    function addAssistantMessage(markdown, data) {
        const messagesContainer = document.getElementById('ai-query-messages');
        if (!messagesContainer) return;

        const htmlContent = renderMarkdown(markdown);

        const messageDiv = document.createElement('div');
        messageDiv.className = 'ai-query-message assistant';
        messageDiv.innerHTML = `
            <div class="ai-query-message-avatar">🤖</div>
            <div class="ai-query-message-content">
                <div class="ai-query-message-text ai-query-markdown">${htmlContent}</div>
                <div class="ai-query-message-meta" style="font-size: 0.75rem; color: #999; margin-top: 0.5rem;">
                    耗时：${data.executionTimeMs}ms | 结果：${data.total}条
                </div>
            </div>
        `;

        messagesContainer.appendChild(messageDiv);
        scrollToBottom();
        trimMessages();
    }

    // 创建消息元素
    function createMessageElement(type, content) {
        const div = document.createElement('div');
        div.className = `ai-query-message ${type}`;

        const avatar = type === 'user' ? '👤' : '🤖';
        const textClass = type === 'user' ? 'ai-query-message-text' : 'ai-query-message-text ai-query-markdown';

        div.innerHTML = `
            <div class="ai-query-message-avatar">${avatar}</div>
            <div class="ai-query-message-content">
                <div class="${textClass}">${type === 'user' ? escapeHtml(content) : renderMarkdown(content)}</div>
            </div>
        `;

        return div;
    }

    // 渲染 Markdown
    function renderMarkdown(markdown) {
        if (typeof marked !== 'undefined') {
            return marked.parse(markdown);
        }

        // 简单的 Markdown 回退
        return markdown
            .replace(/### (.*?)$/gm, '<h3>$1</h3>')
            .replace(/#### (.*?)$/gm, '<h4>$1</h4>')
            .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
            .replace(/\|(.*?)\|/g, '<table><tr>$1</tr></table>')
            .replace(/\n/g, '<br>');
    }

    // 显示/隐藏思考状态
    function showThinking(show) {
        const thinking = document.getElementById('ai-query-thinking');
        if (thinking) {
            thinking.classList.toggle('hidden', !show);
        }
    }

    // 显示预览
    function showPreview(parsedQuery) {
        const preview = document.getElementById('ai-query-preview');
        const content = document.getElementById('ai-query-preview-content');
        if (preview && content) {
            content.textContent = JSON.stringify(parsedQuery, null, 2);
            preview.classList.remove('hidden');
            setTimeout(() => preview.classList.add('hidden'), 5000);
        }
    }

    // 隐藏预览
    function hidePreview() {
        const preview = document.getElementById('ai-query-preview');
        if (preview) {
            preview.classList.add('hidden');
        }
    }

    // 显示错误
    function showError(message) {
        const messagesContainer = document.getElementById('ai-query-messages');
        if (!messagesContainer) return;

        const errorDiv = document.createElement('div');
        errorDiv.className = 'ai-query-error';
        errorDiv.textContent = message;
        messagesContainer.appendChild(errorDiv);
        scrollToBottom();
    }

    // 更新连接状态
    function updateConnectionStatus(connected) {
        const statusIndicator = document.getElementById('ai-query-status-indicator');
        if (statusIndicator) {
            statusIndicator.className = `ai-status-indicator ${connected ? 'connected' : 'disconnected'}`;
        }
    }

    // 滚动到底部
    function scrollToBottom() {
        const messagesContainer = document.getElementById('ai-query-messages');
        if (messagesContainer) {
            messagesContainer.scrollTop = messagesContainer.scrollHeight;
        }
    }

    // 修剪消息数量
    function trimMessages() {
        const messagesContainer = document.getElementById('ai-query-messages');
        if (!messagesContainer) return;

        const messages = messagesContainer.querySelectorAll('.ai-query-message');
        while (messages.length > CONFIG.maxMessages) {
            messages[0].remove();
            messages.shift();
        }
    }

    // HTML 转义
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // 暴露全局函数（用于调试）
    window.AIQueryChat = {
        sendQuery,
        reconnect: startConnection,
        getStatus: () => ({ isConnected, project: currentProject })
    };
})();
