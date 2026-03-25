// AI Assistant Panel JavaScript

(function() {
    'use strict';
    
    // 状态
    let connection = null;
    let currentTaskId = null;
    let isPanelOpen = false;
    let autoScroll = true;
    
    // 配置
    const CONFIG = {
        apiBaseUrl: '/api/ai',
        signalRUrl: '/aiProgressHub',
        defaultCliTool: 'claude'
    };
    
    // 初始化
    document.addEventListener('DOMContentLoaded', function() {
        initPanel();
        initSignalR();
        loadCliTools();
    });
    
    // 初始化面板
    function initPanel() {
        // 创建触发按钮
        const trigger = document.createElement('button');
        trigger.id = 'ai-assistant-trigger';
        trigger.className = 'ai-assistant-trigger btn btn-primary btn-circle';
        trigger.innerHTML = `
            <svg xmlns="http://www.w3.org/2000/svg" class="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 10h.01M12 10h.01M16 10h.01M9 16H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-5l-5 5v-5z" />
            </svg>
        `;
        trigger.onclick = togglePanel;
        document.body.appendChild(trigger);
        
        // 创建面板
        const panel = document.createElement('div');
        panel.id = 'ai-assistant-panel';
        panel.className = 'ai-assistant-panel';
        panel.innerHTML = buildPanelHTML();
        document.body.appendChild(panel);
        
        // 绑定事件
        bindPanelEvents();
    }
    
    function buildPanelHTML() {
        return `
            <div class="ai-panel-header">
                <div class="flex items-center gap-2">
                    <span id="ai-status-indicator" class="ai-status-indicator idle"></span>
                    <h3>AI Assistant</h3>
                </div>
                <div class="flex gap-2">
                    <button id="ai-minimize-btn" class="btn btn-ghost btn-sm btn-circle" title="Minimize">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 12H4" />
                        </svg>
                    </button>
                    <button id="ai-close-btn" class="btn btn-ghost btn-sm btn-circle" title="Close">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
                        </svg>
                    </button>
                </div>
            </div>
            
            <div class="ai-panel-body" id="ai-messages-container">
                <div class="ai-message assistant">
                    <div class="ai-message-content">
                        🤖 你好！我是你的 AI 助手。我可以帮助你：
                        <ul>
                            <li>创建实体定义</li>
                            <li>生成页面模板</li>
                            <li>编写业务逻辑代码</li>
                            <li>分析项目结构</li>
                        </ul>
                        请告诉我你需要什么帮助？
                    </div>
                </div>
            </div>
            
            <button id="ai-auto-scroll-btn" class="ai-auto-scroll-btn btn btn-sm btn-circle opacity-75" title="Auto scroll">
                <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 14l-7 7m0 0l-7-7m7 7V3" />
                </svg>
            </button>
            
            <div class="ai-panel-footer">
                <div class="ai-cli-selector">
                    <label for="ai-cli-tool" class="text-sm">AI:</label>
                    <select id="ai-cli-tool" class="select select-sm select-bordered">
                        <option value="claude">Claude Code</option>
                        <option value="qwen-code">Qwen Code</option>
                        <option value="mock">Mock (Test)</option>
                    </select>
                    <span id="cli-status" class="text-xs opacity-50 ml-2"></span>
                </div>
                
                <div class="ai-input-container">
                    <textarea 
                        id="ai-input-message" 
                        class="textarea textarea-bordered" 
                        placeholder="输入指令..."
                        rows="2"></textarea>
                </div>
                
                <div class="ai-input-actions">
                    <button id="ai-stop-btn" class="btn btn-ghost btn-sm" disabled>
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 10a1 1 0 00-1 1v4a1 1 0 001 1h4a1 1 0 001-1v-4a1 1 0 00-1-1H9z" />
                        </svg>
                        停止
                    </button>
                    <button id="ai-clear-btn" class="btn btn-ghost btn-sm">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                        </svg>
                    </button>
                    <button id="ai-send-btn" class="btn btn-primary btn-sm">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8" />
                        </svg>
                        发送
                    </button>
                </div>
            </div>
        `;
    }
    
    function bindPanelEvents() {
        // 关闭按钮
        document.getElementById('ai-close-btn').onclick = closePanel;
        document.getElementById('ai-minimize-btn').onclick = minimizePanel;
        
        // 发送按钮
        document.getElementById('ai-send-btn').onclick = sendMessage;
        
        // 停止按钮
        document.getElementById('ai-stop-btn').onclick = stopTask;
        
        // 清除按钮
        document.getElementById('ai-clear-btn').onclick = clearMessages;
        
        // 自动滚动按钮
        document.getElementById('ai-auto-scroll-btn').onclick = toggleAutoScroll;
        
        // 输入框回车发送
        document.getElementById('ai-input-message').addEventListener('keydown', function(e) {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                sendMessage();
            }
        });
        
        // CLI 工具选择变化
        document.getElementById('ai-cli-tool').addEventListener('change', function() {
            checkCliStatus(this.value);
        });
    }
    
    // 初始化 SignalR
    function initSignalR() {
        // 尝试从多个来源加载 SignalR 客户端
        const signalRSources = [
            '/lib/microsoft/signalr/dist/browser/signalr.min.js',
            '/lib/signalr/signalr.min.js',
            'https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js'
        ];
        
        loadSignalRClient(0);
    }
    
    function loadSignalRClient(index) {
        if (index >= signalRSources.length) {
            console.warn('SignalR client not available, using polling fallback');
            initPollingFallback();
            return;
        }
        
        const script = document.createElement('script');
        script.src = signalRSources[index];
        script.onload = function() {
            console.log('SignalR client loaded from:', signalRSources[index]);
            connectSignalR();
        };
        script.onerror = function() {
            console.warn('Failed to load SignalR from:', signalRSources[index]);
            loadSignalRClient(index + 1);
        };
        document.head.appendChild(script);
    }
    
    // 轮询回退方案
    function initPollingFallback() {
        console.log('Using polling fallback for progress updates');
        window.aiPollingInterval = null;
    }
    
    function startPolling(taskId) {
        stopPolling();
        window.aiPollingInterval = setInterval(async function() {
            try {
                const response = await fetch(`${CONFIG.apiBaseUrl}/tasks/${taskId}`);
                if (response.ok) {
                    const data = await response.json();
                    handleProgressUpdate({
                        id: data.id,
                        status: data.status,
                        progress: data.progress,
                        logs: data.logs,
                        result: data.result,
                        error: data.error
                    });
                }
            } catch (error) {
                console.error('Polling error:', error);
            }
        }, 1000);
    }
    
    function stopPolling() {
        if (window.aiPollingInterval) {
            clearInterval(window.aiPollingInterval);
            window.aiPollingInterval = null;
        }
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
        
        connection.on('Connected', function(data) {
            console.log('SignalR connected:', data);
        });
        
        connection.start()
            .then(() => console.log('SignalR connected'))
            .catch(err => console.error('SignalR connection error:', err));
    }
    
    // 加载 CLI 工具列表
    async function loadCliTools() {
        try {
            const response = await fetch(`${CONFIG.apiBaseUrl}/cli-tools`);
            if (response.ok) {
                const data = await response.json();
                updateCliSelector(data.available);
            }
        } catch (error) {
            console.error('Failed to load CLI tools:', error);
        }
    }
    
    function updateCliSelector(tools) {
        const selector = document.getElementById('ai-cli-tool');
        if (!selector) return;
        
        selector.innerHTML = '';
        
        for (const [name, tool] of Object.entries(tools)) {
            const option = document.createElement('option');
            option.value = name;
            option.textContent = tool.displayName || name;
            if (!tool.installed) {
                option.disabled = true;
                option.textContent += ' (未安装)';
            }
            selector.appendChild(option);
        }
        
        // 检查默认工具状态
        checkCliStatus(selector.value);
    }
    
    async function checkCliStatus(toolName) {
        const statusEl = document.getElementById('cli-status');
        if (!statusEl) return;
        
        try {
            const response = await fetch(`${CONFIG.apiBaseUrl}/cli-tools`);
            if (response.ok) {
                const data = await response.json();
                const tool = data.available[toolName];
                if (tool) {
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
            }
        } catch (error) {
            statusEl.textContent = '?';
        }
    }
    
    // 发送消息
    async function sendMessage() {
        const input = document.getElementById('ai-input-message');
        const cliSelector = document.getElementById('ai-cli-tool');
        const message = input.value.trim();
        
        if (!message) return;
        
        // 添加用户消息
        addMessage(message, 'user');
        input.value = '';
        
        // 更新状态
        updateStatus('running');
        setSendingState(true);
        
        try {
            const response = await fetch(`${CONFIG.apiBaseUrl}/chat`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    message: message,
                    cliTool: cliSelector.value,
                    streaming: true
                })
            });
            
            if (response.ok) {
                const data = await response.json();
                currentTaskId = data.taskId;

                // 添加进度容器
                addProgressContainer(data.taskId);
                
                // 如果没有 SignalR 连接，使用轮询
                if (!connection || connection.state !== 'Connected') {
                    startPolling(data.taskId);
                }
            } else {
                const error = await response.json();
                addMessage(`错误：${error.error}`, 'system');
                updateStatus('error');
            }
        } catch (error) {
            addMessage(`请求失败：${error.message}`, 'system');
            updateStatus('error');
        } finally {
            setSendingState(false);
        }
    }
    
    // 停止任务
    async function stopTask() {
        if (!currentTaskId) return;
        
        try {
            await fetch(`${CONFIG.apiBaseUrl}/tasks/${currentTaskId}`, {
                method: 'DELETE'
            });
            
            addMessage('任务已取消', 'system');
            updateStatus('idle');
            currentTaskId = null;
        } catch (error) {
            console.error('Failed to stop task:', error);
        }
    }
    
    // 处理进度更新
    function handleProgressUpdate(data) {
        if (!currentTaskId || data.id !== currentTaskId) return;
        
        const progressContainer = document.querySelector(`[data-task-id="${data.id}"]`);
        if (!progressContainer) return;
        
        // 更新进度条
        const fill = progressContainer.querySelector('.ai-progress-fill');
        const text = progressContainer.querySelector('.ai-progress-text');
        
        if (fill) {
            fill.style.width = `${data.progress}%`;
        }
        
        if (text) {
            text.querySelector('.progress-percent').textContent = `${data.progress}%`;
        }
        
        // 更新日志
        if (data.logs && data.logs.length > 0) {
            const logsContainer = progressContainer.querySelector('.ai-logs ul');
            if (logsContainer) {
                data.logs.forEach(log => {
                    const li = document.createElement('li');
                    li.textContent = log;
                    logsContainer.appendChild(li);
                });
                logsContainer.scrollTop = logsContainer.scrollHeight;
            }
        }
        
        // 检查完成状态
        if (data.status === 'Completed') {
            updateStatus('completed');
            addMessage(`✅ 任务完成：${data.result || ''}`, 'assistant');
            currentTaskId = null;
            setSendingState(false);
            stopPolling();
        } else if (data.status === 'Failed' || data.status === 'Cancelled') {
            updateStatus('error');
            addMessage(`❌ ${data.error || '任务失败'}`, 'system');
            currentTaskId = null;
            setSendingState(false);
            stopPolling();
        }
        
        // 自动滚动
        if (autoScroll) {
            scrollToBottom();
        }
    }
    
    // 添加消息
    function addMessage(content, type) {
        const container = document.getElementById('ai-messages-container');
        const messageEl = document.createElement('div');
        messageEl.className = `ai-message ${type}`;
        
        if (type === 'assistant' || type === 'user') {
            messageEl.innerHTML = `<div class="ai-message-content">${escapeHtml(content)}</div>`;
        } else {
            messageEl.textContent = content;
        }
        
        container.appendChild(messageEl);
        
        if (autoScroll) {
            scrollToBottom();
        }
    }
    
    // 添加进度容器
    function addProgressContainer(taskId) {
        const container = document.getElementById('ai-messages-container');
        const progressEl = document.createElement('div');
        progressEl.className = 'ai-message assistant';
        progressEl.setAttribute('data-task-id', taskId);
        progressEl.innerHTML = `
            <div>⏳ 任务进行中...</div>
            <div class="ai-progress-container">
                <div class="ai-progress-bar">
                    <div class="ai-progress-fill" style="width: 0%"></div>
                </div>
                <div class="ai-progress-text">
                    <span class="progress-status">正在处理...</span>
                    <span class="progress-percent">0%</span>
                </div>
                <div class="ai-logs">
                    <ul></ul>
                </div>
            </div>
        `;
        container.appendChild(progressEl);
        scrollToBottom();
    }
    
    // 更新状态指示器
    function updateStatus(status) {
        const indicator = document.getElementById('ai-status-indicator');
        if (indicator) {
            indicator.className = `ai-status-indicator ${status}`;
        }
    }
    
    // 设置发送状态
    function setSendingState(sending) {
        const sendBtn = document.getElementById('ai-send-btn');
        const stopBtn = document.getElementById('ai-stop-btn');
        const input = document.getElementById('ai-input-message');
        
        sendBtn.disabled = sending;
        stopBtn.disabled = !sending;
        input.disabled = sending;
        
        if (sending) {
            sendBtn.innerHTML = `
                <svg class="animate-spin h-4 w-4" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                    <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                    <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                </svg>
                发送中...
            `;
        } else {
            sendBtn.innerHTML = `
                <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8" />
                </svg>
                发送
            `;
        }
    }
    
    // 切换面板
    function togglePanel() {
        if (isPanelOpen) {
            closePanel();
        } else {
            openPanel();
        }
    }
    
    function openPanel() {
        const panel = document.getElementById('ai-assistant-panel');
        const trigger = document.getElementById('ai-assistant-trigger');
        
        panel.classList.add('open');
        trigger.classList.add('hidden');
        isPanelOpen = true;
    }
    
    function closePanel() {
        const panel = document.getElementById('ai-assistant-panel');
        const trigger = document.getElementById('ai-assistant-trigger');
        
        panel.classList.remove('open');
        trigger.classList.remove('hidden');
        isPanelOpen = false;
    }
    
    function minimizePanel() {
        closePanel();
    }
    
    // 清除消息
    function clearMessages() {
        const container = document.getElementById('ai-messages-container');
        container.innerHTML = `
            <div class="ai-message assistant">
                <div class="ai-message-content">
                    🤖 对话已清除。有什么可以帮你的？
                </div>
            </div>
        `;
        currentTaskId = null;
        updateStatus('idle');
        setSendingState(false);
    }
    
    // 切换自动滚动
    function toggleAutoScroll() {
        autoScroll = !autoScroll;
        const btn = document.getElementById('ai-auto-scroll-btn');
        btn.classList.toggle('opacity-75');
        btn.classList.toggle('btn-active');
    }
    
    // 滚动到底部
    function scrollToBottom() {
        const container = document.getElementById('ai-messages-container');
        if (container) {
            container.scrollTop = container.scrollHeight;
        }
    }
    
    // HTML 转义
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
    
})();
