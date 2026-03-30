// 自然语言查询组件
// 支持用户通过自然语言进行数据查询

(function() {
    'use strict';

    let connection = null;
    let currentProject = null;
    let isPanelOpen = false;
    let queryHistory = [];

    const CONFIG = {
        signalRUrl: '/naturalLanguageQueryHub',
        apiBaseUrl: '/api/ai-query'
    };

    // 初始化
    document.addEventListener('DOMContentLoaded', function() {
        if (!isUserLoggedIn()) {
            return;
        }
        initQueryPanel();
        initSignalR();
    });

    // 检查用户登录状态
    function isUserLoggedIn() {
        const body = document.body;
        const authValue = body.getAttribute('data-user-authenticated');
        return authValue === 'true';
    }

    // 获取当前项目
    function getCurrentProject() {
        if (currentProject) return currentProject;
        
        // 从 URL 路径获取项目名
        const pathParts = window.location.pathname.split('/').filter(p => p);
        if (pathParts.length > 0) {
            currentProject = pathParts[0];
        }
        return currentProject;
    }

    // 初始化面板
    function initQueryPanel() {
        // 创建触发按钮
        const trigger = document.createElement('button');
        trigger.id = 'nl-query-trigger';
        trigger.className = 'nl-query-trigger btn btn-success btn-circle';
        trigger.innerHTML = `
            <svg xmlns="http://www.w3.org/2000/svg" class="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
        `;
        trigger.onclick = togglePanel;
        trigger.title = '自然语言查询';
        document.body.appendChild(trigger);

        // 创建面板
        const panel = document.createElement('div');
        panel.id = 'nl-query-panel';
        panel.className = 'nl-query-panel';
        panel.innerHTML = buildPanelHTML();
        document.body.appendChild(panel);

        bindPanelEvents();
    }

    function buildPanelHTML() {
        return `
            <div class="nl-panel-header">
                <div class="flex items-center gap-2">
                    <span class="nl-status-indicator" id="nl-status"></span>
                    <h3>💬 自然语言查询</h3>
                </div>
                <div class="flex gap-1">
                    <button id="nl-clear-btn" class="btn btn-ghost btn-sm btn-circle" title="清空历史">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                        </svg>
                    </button>
                    <button id="nl-minimize-btn" class="btn btn-ghost btn-sm btn-circle" title="最小化">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 12H4" />
                        </svg>
                    </button>
                    <button id="nl-close-btn" class="btn btn-ghost btn-sm btn-circle" title="关闭">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
                        </svg>
                    </button>
                </div>
            </div>

            <div class="nl-panel-body">
                <!-- 查询历史 -->
                <div class="nl-history" id="nl-history">
                    <div class="nl-empty-state">
                        <p>💡 试试这样说：</p>
                        <ul class="nl-suggestions">
                            <li>显示上个月销售额前 10 的产品</li>
                            <li>找出库存低于 5 的商品</li>
                            <li>展示本季度的订单</li>
                            <li>统计每个客户的订单数量</li>
                        </ul>
                    </div>
                </div>

                <!-- 输入区域 -->
                <div class="nl-input-area">
                    <div class="nl-input-wrapper">
                        <textarea 
                            id="nl-query-input" 
                            class="nl-input"
                            placeholder="请输入查询内容，例如：显示上个月销售额前 10 的产品"
                            rows="3"
                        ></textarea>
                        <button id="nl-send-btn" class="nl-send-btn" disabled>
                            <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8" />
                            </svg>
                        </button>
                    </div>
                    <div class="nl-input-footer">
                        <span class="nl-hint">按 Enter 发送，Shift+Enter 换行</span>
                    </div>
                </div>
            </div>

            <!-- 加载状态 -->
            <div class="nl-loading" id="nl-loading" style="display: none;">
                <div class="nl-spinner"></div>
                <p id="nl-loading-text">AI 正在思考...</p>
            </div>
        `;
    }

    // 绑定事件
    function bindPanelEvents() {
        const panel = document.getElementById('nl-query-panel');
        
        // 关闭按钮
        document.getElementById('nl-close-btn').onclick = () => {
            panel.style.display = 'none';
            isPanelOpen = false;
        };

        // 最小化按钮
        document.getElementById('nl-minimize-btn').onclick = () => {
            panel.classList.toggle('minimized');
        };

        // 清空历史
        document.getElementById('nl-clear-btn').onclick = () => {
            queryHistory = [];
            document.getElementById('nl-history').innerHTML = `
                <div class="nl-empty-state">
                    <p>💡 试试这样说：</p>
                    <ul class="nl-suggestions">
                        <li>显示上个月销售额前 10 的产品</li>
                        <li>找出库存低于 5 的商品</li>
                        <li>展示本季度的订单</li>
                        <li>统计每个客户的订单数量</li>
                    </ul>
                </div>
            `;
        };

        // 输入框
        const input = document.getElementById('nl-query-input');
        const sendBtn = document.getElementById('nl-send-btn');

        input.addEventListener('input', () => {
            sendBtn.disabled = !input.value.trim();
        });

        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                if (!sendBtn.disabled) {
                    sendQuery();
                }
            }
        });

        // 发送按钮
        sendBtn.onclick = sendQuery;

        // 建议点击
        panel.addEventListener('click', (e) => {
            if (e.target.classList.contains('nl-suggestion-item')) {
                input.value = e.target.textContent;
                sendBtn.disabled = false;
                sendQuery();
            }
        });
    }

    // 切换面板显示
    function togglePanel() {
        const panel = document.getElementById('nl-query-panel');
        if (isPanelOpen) {
            panel.style.display = 'none';
        } else {
            panel.style.display = 'flex';
        }
        isPanelOpen = !isPanelOpen;
    }

    // 初始化 SignalR
    function initSignalR() {
        if (typeof signalR === 'undefined') {
            console.error('SignalR 未加载');
            return;
        }

        const project = getCurrentProject();
        connection = new signalR.HubConnectionBuilder()
            .withUrl(`/${project}${CONFIG.signalRUrl}`)
            .withAutomaticReconnect()
            .build();

        // 连接事件
        connection.on('connected', (data) => {
            console.log('NLQuery 已连接:', data);
            updateStatus('connected');
        });

        // 思考中
        connection.on('thinking_start', (data) => {
            showLoading('正在解析查询...');
        });

        // 解析完成
        connection.on('parsing_complete', (data) => {
            showLoading('正在执行查询...');
            console.log('解析结果:', data.parsedQuery);
        });

        // 查询完成
        connection.on('query_complete', (data) => {
            hideLoading();
            addQueryResult(data);
        });

        // 错误
        connection.on('error', (data) => {
            hideLoading();
            showError(data.message);
        });

        // 启动连接
        connection.start()
            .then(() => {
                console.log('NLQuery SignalR 连接成功');
                connection.invoke('connect', project)
                    .catch(err => console.error('connect 失败:', err));
            })
            .catch(err => console.error('NLQuery SignalR 连接失败:', err));
    }

    // 发送查询
    function sendQuery() {
        const input = document.getElementById('nl-query-input');
        const query = input.value.trim();
        
        if (!query || !connection) return;

        const project = getCurrentProject();
        
        // 添加用户消息到历史
        addUserMessage(query);
        
        // 清空输入
        input.value = '';
        input.dispatchEvent(new Event('input'));

        // 发送到服务器
        connection.invoke('sendQuery', query, project)
            .catch(err => {
                console.error('发送查询失败:', err);
                showError('发送失败：' + err.message);
            });
    }

    // 添加用户消息
    function addUserMessage(query) {
        const history = document.getElementById('nl-history');
        const emptyState = history.querySelector('.nl-empty-state');
        if (emptyState) {
            emptyState.remove();
        }

        const msgDiv = document.createElement('div');
        msgDiv.className = 'nl-message nl-message-user';
        msgDiv.innerHTML = `
            <div class="nl-message-header">
                <span class="nl-message-sender">👤 您</span>
                <span class="nl-message-time">${new Date().toLocaleTimeString()}</span>
            </div>
            <div class="nl-message-content">${escapeHtml(query)}</div>
        `;
        history.appendChild(msgDiv);
        history.scrollTop = history.scrollHeight;
    }

    // 添加查询结果
    function addQueryResult(result) {
        const history = document.getElementById('nl-history');

        const msgDiv = document.createElement('div');
        msgDiv.className = 'nl-message nl-message-ai';
        msgDiv.innerHTML = `
            <div class="nl-message-header">
                <span class="nl-message-sender">🤖 AI</span>
                <span class="nl-message-time">${new Date().toLocaleTimeString()}</span>
                <span class="nl-message-meta">
                    ${result.total}条记录 · ${result.executionTimeMs}ms
                </span>
            </div>
            <div class="nl-message-content nl-markdown">
                ${marked.parse(result.markdown)}
            </div>
            ${result.parsedQuery ? `
                <details class="nl-query-details">
                    <summary>查看查询参数</summary>
                    <pre class="nl-query-json">${JSON.stringify(result.parsedQuery, null, 2)}</pre>
                </details>
            ` : ''}
        `;
        history.appendChild(msgDiv);
        history.scrollTop = history.scrollHeight;

        // 保存到历史
        queryHistory.push({
            query: result.parsedQuery,
            result: result,
            timestamp: new Date()
        });
    }

    // 显示加载状态
    function showLoading(text) {
        const loading = document.getElementById('nl-loading');
        const loadingText = document.getElementById('nl-loading-text');
        loadingText.textContent = text;
        loading.style.display = 'flex';
    }

    // 隐藏加载状态
    function hideLoading() {
        document.getElementById('nl-loading').style.display = 'none';
    }

    // 显示错误
    function showError(message) {
        const history = document.getElementById('nl-history');
        const msgDiv = document.createElement('div');
        msgDiv.className = 'nl-message nl-message-error';
        msgDiv.innerHTML = `
            <div class="nl-message-content">
                ❌ ${escapeHtml(message)}
            </div>
        `;
        history.appendChild(msgDiv);
        history.scrollTop = history.scrollHeight;
    }

    // 更新状态指示器
    function updateStatus(status) {
        const indicator = document.getElementById('nl-status');
        indicator.className = `nl-status-indicator ${status}`;
    }

    // HTML 转义
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

})();
