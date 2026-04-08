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

        injectStyles();
        initSignalR();
        bindEvents();
    });

    // 注入样式
    function injectStyles() {
        if (document.getElementById('ai-query-chat-styles')) return;
        
        const style = document.createElement('style');
        style.id = 'ai-query-chat-styles';
        style.textContent = `
            .ai-query-input-wrapper {
                position: relative;
                flex: 1;
            }
            .ai-query-input-wrapper textarea {
                padding-right: 2.5rem;
            }
            .ai-query-clear-input-btn {
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
            .ai-query-clear-input-btn:hover {
                background: rgba(0,0,0,0.1);
                color: #333;
            }
            .ai-query-message {
                position: relative;
            }
            .ai-query-message-inner {
                display: flex;
                gap: 0.5rem;
            }
            .ai-query-msg-actions {
                display: none;
                gap: 4px;
                margin-top: 4px;
                padding: 0 2.5rem 0 2.5rem;
            }
            .ai-query-message:hover .ai-query-msg-actions {
                display: flex;
            }
            .ai-query-message.user .ai-query-msg-actions {
                justify-content: flex-end;
            }
            .ai-query-message.assistant .ai-query-msg-actions {
                justify-content: flex-start;
            }
            .ai-query-msg-action-btn {
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
            .ai-query-msg-action-btn:hover {
                background: #e3f2fd;
                color: #1565c0;
                border-color: #90caf9;
            }
        `;
        document.head.appendChild(style);
    }

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
        const clearInputBtn = document.getElementById('ai-query-clear-input-btn');
        const previewClose = document.getElementById('ai-query-preview-close');
        const suggestions = document.querySelectorAll('.ai-query-suggestions li');

        // 发送按钮点击
        if (sendBtn) {
            sendBtn.addEventListener('click', sendQuery);
        }

        // 清空输入框按钮
        if (clearInputBtn) {
            clearInputBtn.onclick = function() {
                input.value = '';
                clearInputBtn.style.display = 'none';
                input.focus();
            };
        }

        // 监听输入框变化，显示/隐藏清空按钮
        if (input) {
            input.addEventListener('input', function() {
                if (clearInputBtn) {
                    clearInputBtn.style.display = this.value.length > 0 ? 'flex' : 'none';
                }
            });
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

        const contentEl = document.createElement('div');
        contentEl.className = 'ai-query-message-content';
        contentEl.innerHTML = `<div class="ai-query-message-text ai-query-markdown">${htmlContent}</div>`;

        const metaEl = document.createElement('div');
        metaEl.className = 'ai-query-message-meta';
        metaEl.style.cssText = 'font-size: 0.75rem; color: #999; margin-top: 0.5rem; display: flex; justify-content: space-between; align-items: center;';
        const now = new Date();
        const timeStr = now.getFullYear() + '-' +
            String(now.getMonth() + 1).padStart(2, '0') + '-' +
            String(now.getDate()).padStart(2, '0') + ' ' +
            String(now.getHours()).padStart(2, '0') + ':' +
            String(now.getMinutes()).padStart(2, '0') + ':' +
            String(now.getSeconds()).padStart(2, '0');
        metaEl.innerHTML = `<span>耗时：${data.executionTimeMs}ms | 结果：${data.total}条</span><span class="ai-query-message-time">${timeStr}</span>`;
        contentEl.appendChild(metaEl);

        const innerDiv = document.createElement('div');
        innerDiv.className = 'ai-query-message-inner';
        innerDiv.innerHTML = '<div class="ai-query-message-avatar">🤖</div>';
        innerDiv.appendChild(contentEl);

        messageDiv.appendChild(innerDiv);

        // 添加复制和引用按钮
        const actionsEl = document.createElement('div');
        actionsEl.className = 'ai-query-msg-actions';

        const copyBtn = document.createElement('button');
        copyBtn.className = 'ai-query-msg-action-btn';
        copyBtn.title = '复制';
        copyBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z"/></svg>';
        copyBtn.onclick = function() {
            navigator.clipboard.writeText(markdown).then(function() {
                copyBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"/></svg>';
                setTimeout(function() {
                    copyBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z"/></svg>';
                }, 2000);
            }).catch(function() {});
        };
        actionsEl.appendChild(copyBtn);

        const quoteBtn = document.createElement('button');
        quoteBtn.className = 'ai-query-msg-action-btn';
        quoteBtn.title = '引用回复';
        quoteBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 10h10a8 8 0 018 8v2M3 10l6 6m-6-6l6-6"/></svg>';
        quoteBtn.onclick = function() {
            const input = document.getElementById('ai-query-input');
            if (!input) return;
            const quoted = markdown.split('\n').map(function(line) { return '> ' + line; }).join('\n');
            input.value = quoted + '\n' + input.value;
            input.focus();
        };
        actionsEl.appendChild(quoteBtn);

        messageDiv.appendChild(actionsEl);

        messagesContainer.appendChild(messageDiv);
        scrollToBottom();
        trimMessages();
    }

    // 创建消息元素
    function createMessageElement(type, content) {
        const div = document.createElement('div');
        div.className = `ai-query-message ${type}`;

        const avatar = type === 'user' ? '👤' : '🤖';
        // 用户消息和助手消息都支持 Markdown 渲染
        const textClass = type === 'user' ? 'ai-query-message-text ai-query-markdown' : 'ai-query-message-text ai-query-markdown';

        const contentDiv = document.createElement('div');
        contentDiv.className = 'ai-query-message-content';
        contentDiv.innerHTML = `<div class="${textClass}">${renderMarkdown(content)}</div>`;

        // 添加时间戳
        const now = new Date();
        const timeStr = now.getFullYear() + '-' +
            String(now.getMonth() + 1).padStart(2, '0') + '-' +
            String(now.getDate()).padStart(2, '0') + ' ' +
            String(now.getHours()).padStart(2, '0') + ':' +
            String(now.getMinutes()).padStart(2, '0') + ':' +
            String(now.getSeconds()).padStart(2, '0');
        const timeEl = document.createElement('div');
        timeEl.className = 'ai-query-message-time';
        timeEl.textContent = timeStr;
        contentDiv.appendChild(timeEl);

        const innerDiv = document.createElement('div');
        innerDiv.className = 'ai-query-message-inner';
        innerDiv.innerHTML = `<div class="ai-query-message-avatar">${avatar}</div>`;
        innerDiv.appendChild(contentDiv);

        div.appendChild(innerDiv);

        // 添加复制和引用按钮
        const actionsEl = document.createElement('div');
        actionsEl.className = 'ai-query-msg-actions';

        // 复制按钮
        const copyBtn = document.createElement('button');
        copyBtn.className = 'ai-query-msg-action-btn';
        copyBtn.title = '复制';
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
        quoteBtn.className = 'ai-query-msg-action-btn';
        quoteBtn.title = '引用回复';
        quoteBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 10h10a8 8 0 018 8v2M3 10l6 6m-6-6l6-6"/></svg>';
        quoteBtn.onclick = function() {
            const input = document.getElementById('ai-query-input');
            if (!input) return;
            const quoted = content.split('\n').map(function(line) { return '> ' + line; }).join('\n');
            input.value = quoted + '\n' + input.value;
            input.focus();
        };
        actionsEl.appendChild(quoteBtn);

        div.appendChild(actionsEl);

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
