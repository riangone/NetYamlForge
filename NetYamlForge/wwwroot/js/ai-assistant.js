// AI Assistant Panel JavaScript

(function() {
    'use strict';

    // 状態
    let connection = null;
    let currentTaskId = null;
    let currentSessionId = null; // マルチターン会話の継続用セッションID
    let isPanelOpen = false;
    let isPanelMinimized = false;
    let autoScroll = true;
    let chatHistory = []; // ページ跨ぎ用メモリ上の履歴

    // 入力履歴
    let inputHistory = [];       // 送信済みメッセージ履歴（新→古）
    let inputHistoryIndex = -1;  // -1 = 現在の下書き
    let inputCurrentDraft = '';  // 履歴ナビ開始前の下書き保持

    const STORAGE_KEY = 'ai_chat_history';
    const TOOL_STORAGE_KEY = 'ai_chat_tool';

    // 配置
    const CONFIG = {
        apiBaseUrl: '/api/ai',
        signalRUrl: '/aiProgressHub',
        defaultCliTool: 'claude'
    };

    // 初始化
    document.addEventListener('DOMContentLoaded', function() {
        console.log('AI Assistant: DOMContentLoaded');
        console.log('AI Assistant: data-user-authenticated =', document.body.getAttribute('data-user-authenticated'));
        
        // 检查用户是否已登录
        if (!isUserLoggedIn()) {
            console.log('AI Assistant: User not logged in, skipping initialization');
            return; // 未登录时不显示 AI 聊天窗
        }
        console.log('AI Assistant: User logged in, initializing...');
        initPanel();
        initSignalR();
        loadCliTools();
        loadSkills();
        configureMarked();
    });

    // 检查用户登录状态
    function isUserLoggedIn() {
        // 检查 body 标签上的 data-user-authenticated 属性
        const body = document.body;
        const authValue = body.getAttribute('data-user-authenticated');
        console.log('AI Assistant: isUserLoggedIn check, authValue =', authValue);
        return authValue === 'true';
    }

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

        // 1. sessionStorage から即時復元（ちらつき防止）
        restoreFromStorage();
        // 2. サーバーから最新履歴を取得して同期（別タブ・ブラウザ再起動対応）
        restoreFromServer();

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
                <div class="flex gap-1">
                    <button id="ai-maximize-btn" class="btn btn-ghost btn-sm btn-circle" title="最大化">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5l-5-5m5 5v-4m0 4h-4" />
                        </svg>
                    </button>
                    <button id="ai-collapse-btn" class="btn btn-ghost btn-sm btn-circle" title="最小化">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 12H4" />
                        </svg>
                    </button>
                    <button id="ai-close-btn" class="btn btn-ghost btn-sm btn-circle" title="閉じる">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
                        </svg>
                    </button>
                </div>
            </div>
            
            <div class="ai-panel-body" id="ai-messages-container">
                <div class="ai-message-row assistant">
                    <div class="ai-message-sender">AI Assistant</div>
                    <div class="ai-message-inner">
                        <div class="ai-message-avatar">
                            <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17H3a2 2 0 01-2-2V5a2 2 0 012-2h14a2 2 0 012 2v10a2 2 0 01-2 2h-2"/></svg>
                        </div>
                        <div class="ai-message assistant">
                            <div class="ai-message-content">
                                こんにちは！AI アシスタントです。以下のことをお手伝いできます：
                                <ul>
                                    <li>エンティティ定義の作成</li>
                                    <li>ページテンプレートの生成</li>
                                    <li>ビジネスロジックのコーディング</li>
                                    <li>プロジェクト構造の分析</li>
                                </ul>
                                何かお手伝いできることはありますか？
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            
            <button id="ai-auto-scroll-btn" class="ai-auto-scroll-btn btn btn-sm btn-circle opacity-75" title="Auto scroll">
                <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 14l-7 7m0 0l-7-7m7 7V3" />
                </svg>
            </button>
            
            <div id="ai-skills-bar" class="ai-skills-bar" style="display:none"></div>

            <div class="ai-panel-footer">
                <div class="ai-cli-selector">
                    <label for="ai-cli-tool" class="text-sm">AI:</label>
                    <select id="ai-cli-tool" class="select select-sm select-bordered">
                        <option value="claude">Claude Code</option>
                        <option value="qwen">Qwen Code</option>
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
    
    // モバイルキーボード表示時にパネルサイズを visualViewport に合わせる
    function adjustPanelForKeyboard() {
        const panel = document.getElementById('ai-assistant-panel');
        if (!panel || !isPanelOpen) return;
        const vv = window.visualViewport;
        panel.style.height = vv.height + 'px';
        panel.style.top = vv.offsetTop + 'px';
    }

    function resetPanelSize() {
        const panel = document.getElementById('ai-assistant-panel');
        if (!panel) return;
        panel.style.height = '';
        panel.style.top = '';
    }

    function bindPanelEvents() {
        // 最大化ボタン
        document.getElementById('ai-maximize-btn').onclick = toggleMaximize;

        // 最小化ボタン（折りたたむ）
        document.getElementById('ai-collapse-btn').onclick = toggleMinimize;

        // 閉じるボタン
        document.getElementById('ai-close-btn').onclick = function() {
            if (isPanelOpen) togglePanel();
        };

        // 最小化中にヘッダーをクリックすると展開
        document.querySelector('.ai-panel-header').addEventListener('click', function(e) {
            if (isPanelMinimized && !e.target.closest('button')) {
                toggleMinimize();
            }
        });

        // visualViewport で旧 iOS Safari のキーボード対応
        if (window.visualViewport) {
            window.visualViewport.addEventListener('resize', adjustPanelForKeyboard);
            window.visualViewport.addEventListener('scroll', adjustPanelForKeyboard);
        }
        
        // 发送按钮
        document.getElementById('ai-send-btn').onclick = sendMessage;
        
        // 停止按钮
        document.getElementById('ai-stop-btn').onclick = stopTask;
        
        // 清除按钮
        document.getElementById('ai-clear-btn').onclick = clearMessages;
        
        // 自动滚动按钮
        document.getElementById('ai-auto-scroll-btn').onclick = toggleAutoScroll;
        
        // 入力欄イベント（Enter送信 + ↑↓ 履歴ナビ）
        document.getElementById('ai-input-message').addEventListener('keydown', function(e) {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                sendMessage();
                return;
            }

            // ↑キー：カーソルが先頭にある場合に前の履歴へ
            if (e.key === 'ArrowUp' && this.selectionStart === 0) {
                if (inputHistory.length === 0) return;
                e.preventDefault();
                if (inputHistoryIndex === -1) {
                    inputCurrentDraft = this.value;
                }
                if (inputHistoryIndex < inputHistory.length - 1) {
                    inputHistoryIndex++;
                }
                this.value = inputHistory[inputHistoryIndex] || '';
                this.setSelectionRange(0, 0);
                return;
            }

            // ↓キー：カーソルが末尾にある場合に次の履歴（または下書き）へ
            if (e.key === 'ArrowDown' && this.selectionStart === this.value.length) {
                if (inputHistoryIndex === -1) return;
                e.preventDefault();
                inputHistoryIndex--;
                if (inputHistoryIndex === -1) {
                    this.value = inputCurrentDraft;
                } else {
                    this.value = inputHistory[inputHistoryIndex] || '';
                }
                const len = this.value.length;
                this.setSelectionRange(len, len);
                return;
            }

            // 他のキーを押した場合は履歴ナビをリセット
            if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown' && e.key !== 'Shift') {
                inputHistoryIndex = -1;
            }
        });
        
        // CLI 工具选択変化時に保存
        document.getElementById('ai-cli-tool').addEventListener('change', function() {
            try { sessionStorage.setItem(TOOL_STORAGE_KEY, this.value); } catch(e) {}
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
    
    // 轮询回退方案（已废弃，使用 pollTaskResult 替代）
    function initPollingFallback() {
        console.log('SignalR not available, using simple polling');
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
    
    // スキル一覧を読み込んでスキルバーに表示
    async function loadSkills() {
        try {
            const response = await fetch(`${CONFIG.apiBaseUrl}/skills`);
            if (!response.ok) return;
            const skills = await response.json();
            if (!skills || skills.length === 0) return;

            const bar = document.getElementById('ai-skills-bar');
            if (!bar) return;

            bar.innerHTML = '';
            skills.forEach(function(skill) {
                const btn = document.createElement('button');
                btn.className = 'ai-skill-btn';
                btn.title = skill.description || '';
                btn.innerHTML = `<span class="ai-skill-icon">${skill.icon || '⚡'}</span><span class="ai-skill-name">${skill.name}</span>`;
                btn.onclick = function() { applySkill(skill); };
                bar.appendChild(btn);
            });
            bar.style.display = 'flex';
        } catch (e) {
            console.error('Failed to load skills:', e);
        }
    }

    // スキルをクリックしたときの処理
    function applySkill(skill) {
        const input = document.getElementById('ai-input-message');
        if (!input) return;
        input.value = skill.prompt;
        input.focus();
        // 入力不要なスキルはそのまま送信
        if (!skill.needsInput) {
            sendMessage();
        } else {
            // カーソルを末尾に移動してユーザーに追記を促す
            input.setSelectionRange(input.value.length, input.value.length);
        }
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
    
    // tools は { "claude": {...}, "qwen-code": {...}, "mock": {...} } の辞書形式
    function updateCliSelector(tools) {
        const selector = document.getElementById('ai-cli-tool');
        if (!selector) return;

        // 現在の選択値を保持（再構築後に復元する）
        const prevValue = selector.value;
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

        // sessionStorage → 直前の選択値の順で復元
        const savedTool = (() => { try { return sessionStorage.getItem(TOOL_STORAGE_KEY); } catch(e) { return null; } })();
        const restoreValue = savedTool || prevValue;
        if (restoreValue && selector.querySelector(`option[value="${restoreValue}"]:not([disabled])`)) {
            selector.value = restoreValue;
        }

        updateCliStatusDisplay(tools[selector.value]);
    }

    async function checkCliStatus(toolName) {
        const statusEl = document.getElementById('cli-status');
        if (!statusEl) return;

        try {
            const response = await fetch(`${CONFIG.apiBaseUrl}/cli-tools`);
            if (response.ok) {
                const data = await response.json();
                // available は辞書形式: { "claude": {...}, ... }
                const tool = data.available ? data.available[toolName] : null;
                updateCliStatusDisplay(tool, statusEl);
            }
        } catch (error) {
            if (statusEl) statusEl.textContent = '?';
        }
    }

    function updateCliStatusDisplay(tool, statusEl) {
        statusEl = statusEl || document.getElementById('cli-status');
        if (!statusEl) return;

        if (!tool) {
            statusEl.textContent = '';
            return;
        }
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
    
    // 发送消息
    async function sendMessage() {
        const input = document.getElementById('ai-input-message');
        const cliSelector = document.getElementById('ai-cli-tool');
        const message = input.value.trim();

        if (!message) return;

        // 入力履歴に追加（重複を除く）
        if (inputHistory[0] !== message) {
            inputHistory.unshift(message);
            if (inputHistory.length > 100) inputHistory.pop();
        }
        inputHistoryIndex = -1;
        inputCurrentDraft = '';

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
                    streaming: false,  // 禁用流式，使用简单响应
                    sessionId: currentSessionId  // マルチターン継続用
                })
            });

            if (response.ok) {
                const data = await response.json();

                // タスク完成済みで結果がある場合は即時表示
                if (data.result && data.result.trim()) {
                    addMessage(data.result, 'assistant');
                    updateStatus('completed');
                    setSendingState(false);
                    return;
                }

                if (data.taskId) {
                    currentTaskId = data.taskId;
                    // タスクIDがある場合はポーリングで結果を取得
                    await pollTaskResult(data.taskId);
                    currentTaskId = null;
                } else {
                    addMessage('任务已提交', 'assistant');
                }

                updateStatus('completed');
            } else {
                const error = await response.json();
                addMessage(`错误：${error.error}`, 'system');
                updateStatus('error');
            }
        } catch (error) {
            console.error('Send message error:', error);
            addMessage(`请求失败：${error.message}`, 'system');
            updateStatus('error');
        } finally {
            setSendingState(false);
        }
    }

    // 轮询任务结果（只到完成为止）
    async function pollTaskResult(taskId) {
        const progressEl = addProgressContainer(taskId);
        let lastLogCount = 0;
        const TIMEOUT_MS = 30 * 60 * 1000;  // 最长等待 30 分钟
        const deadline = Date.now() + TIMEOUT_MS;

        while (Date.now() < deadline) {
            try {
                const response = await fetch(`${CONFIG.apiBaseUrl}/tasks/${taskId}`);
                if (!response.ok) break;

                const data = await response.json();
                console.log('Polling data:', data);  // 调试日志

                // 更新进度
                if (progressEl) {
                    const fill = progressEl.querySelector('.ai-progress-fill');
                    const percent = progressEl.querySelector('.progress-percent');
                    const status = progressEl.querySelector('.progress-status');
                    const logsContainer = progressEl.querySelector('.ai-logs ul');

                    if (fill) fill.style.width = `${data.progress}%`;
                    if (percent) percent.textContent = `${data.progress}%`;
                    if (status) status.textContent = translateStatus(data.status);

                    // 显示日志（仅新增的）
                    if (logsContainer && data.logs && data.logs.length > lastLogCount) {
                        for (let j = lastLogCount; j < data.logs.length; j++) {
                            const logText = parseLogEntry(data.logs[j]);
                            if (logText) {  // 跳过空日志
                                const li = document.createElement('li');
                                li.textContent = logText;
                                li.className = 'text-xs text-gray-600 py-1';
                                logsContainer.appendChild(li);
                            }
                        }
                        lastLogCount = data.logs.length;
                        scrollToBottom();
                    }
                }

                // 外部からキャンセルされた場合（stopTask が currentTaskId を null にした）
                if (!currentTaskId) return;

                // 检查完成状态
                if (data.status === 'Completed' || data.status === 2) {
                    // セッションIDを保存（次のリクエストで会話を継続）
                    if (data.sessionId) {
                        currentSessionId = data.sessionId;
                    }
                    if (data.result && data.result.trim()) {
                        addMessage(data.result, 'assistant');
                    }
                    return;
                } else if (data.status === 'Cancelled' || data.status === 4) {
                    // 停止ボタンによるキャンセルは stopTask 側でメッセージ表示済み
                    return;
                } else if (data.status === 'Failed' || data.status === 3) {
                    addMessage(`❌ ${data.error || 'タスクが失敗しました'}`, 'system');
                    return;
                }

                // 等待 2 秒后继续轮询
                await new Promise(resolve => setTimeout(resolve, 2000));
            } catch (error) {
                console.error('Polling error:', error);
                break;
            }
        }

        addMessage('⚠️ 任务超时或中断', 'system');
    }

    // 翻译状态
    function translateStatus(status) {
        if (typeof status === 'number') {
            switch (status) {
                case 0: return 'Pending';
                case 1: return 'Running';
                case 2: return 'Completed';
                case 3: return 'Failed';
                case 4: return 'Cancelled';
                default: return status;
            }
        }
        return status;
    }

    // ログエントリを表示テキストに変換する。
    // バックエンド (BaseCLIService.ParseStreamLine) が既に人間が読めるテキストを返すため、
    // そのまま返す。空文字・null は null を返してスキップする。
    function parseLogEntry(logEntry) {
        if (!logEntry || !logEntry.trim()) return null;
        return logEntry;
    }
    
    // SignalR からのリアルタイム進捗更新を処理する
    function handleProgressUpdate(data) {
        if (!data) return;

        // 該当タスクの進捗 UI を更新
        const progressEl = document.querySelector(`[data-task-id="${data.id}"]`);
        if (progressEl) {
            const fill = progressEl.querySelector('.ai-progress-fill');
            const percent = progressEl.querySelector('.progress-percent');
            const status = progressEl.querySelector('.progress-status');
            const logsContainer = progressEl.querySelector('.ai-logs ul');

            if (fill) fill.style.width = `${data.progress || 0}%`;
            if (percent) percent.textContent = `${data.progress || 0}%`;
            if (status) status.textContent = translateStatus(data.status);

            // ログを全件再描画（差分追加よりシンプルで正確）
            if (logsContainer && Array.isArray(data.logs) && data.logs.length > 0) {
                logsContainer.innerHTML = '';
                for (const log of data.logs) {
                    if (log && log.trim()) {
                        const li = document.createElement('li');
                        li.textContent = log;
                        li.className = 'text-xs text-gray-600 py-1';
                        logsContainer.appendChild(li);
                    }
                }
                if (autoScroll) scrollToBottom();
            }
        }

        // 完了・失敗時の処理（currentTaskId と一致する場合のみ UI をリセット）
        if (data.id !== currentTaskId) return;

        if (data.status === 'Completed' || data.status === 2) {
            if (data.result && data.result.trim()) {
                // Markdown のまま渡す（プレフィックス付けない）
                addMessage(data.result, 'assistant');
            }
            updateStatus('completed');
            setSendingState(false);
            currentTaskId = null;
        } else if (data.status === 'Failed' || data.status === 3) {
            addMessage(`❌ ${data.error || '任务失败'}`, 'system');
            updateStatus('error');
            setSendingState(false);
            currentTaskId = null;
        } else if (data.status === 'Cancelled' || data.status === 4) {
            updateStatus('idle');
            setSendingState(false);
            currentTaskId = null;
        }
    }

    // 停止任务
    async function stopTask() {
        if (!currentTaskId) return;

        // タスクIDを退避してから即座にUIをリセット（ポーリングループも終了させる）
        const taskId = currentTaskId;
        currentTaskId = null;
        setSendingState(false);
        updateStatus('idle');

        try {
            await fetch(`${CONFIG.apiBaseUrl}/tasks/${taskId}`, { method: 'DELETE' });
            addMessage('⏹ タスクをキャンセルしました', 'system');
        } catch (error) {
            console.error('Failed to stop task:', error);
        }
    }

    // 添加消息
    function addMessage(content, type, skipSave) {
        const container = document.getElementById('ai-messages-container');

        if (type === 'system') {
            const messageEl = document.createElement('div');
            messageEl.className = 'ai-message system';
            messageEl.textContent = content;
            container.appendChild(messageEl);
        } else {
            const rowEl = document.createElement('div');
            rowEl.className = `ai-message-row ${type}`;

            // 送信者ラベル
            const senderEl = document.createElement('div');
            senderEl.className = 'ai-message-sender';
            senderEl.textContent = type === 'user' ? 'You' : 'AI Assistant';
            rowEl.appendChild(senderEl);

            // アバター + バブル の横並びラッパー
            const innerEl = document.createElement('div');
            innerEl.className = 'ai-message-inner';

            const avatar = document.createElement('div');
            avatar.className = 'ai-message-avatar';
            if (type === 'user') {
                avatar.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z"/></svg>';
            } else {
                avatar.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17H3a2 2 0 01-2-2V5a2 2 0 012-2h14a2 2 0 012 2v10a2 2 0 01-2 2h-2"/></svg>';
            }

            const messageEl = document.createElement('div');
            messageEl.className = `ai-message ${type}`;

            if (type === 'assistant') {
                const contentEl = document.createElement('div');
                contentEl.className = 'ai-message-content';
                contentEl.innerHTML = renderMarkdown(content);
                // コードブロックにコピーボタンを追加
                contentEl.querySelectorAll('pre > code').forEach(addCopyButton);
                messageEl.appendChild(contentEl);
            } else {
                messageEl.innerHTML = `<div class="ai-message-content">${escapeHtml(content)}</div>`;
            }

            innerEl.appendChild(avatar);
            innerEl.appendChild(messageEl);
            rowEl.appendChild(innerEl);

            // コピー・引用アクションボタン
            const actionsEl = document.createElement('div');
            actionsEl.className = 'ai-message-actions';

            // コピーボタン
            const copyBtn = document.createElement('button');
            copyBtn.className = 'ai-msg-action-btn';
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

            // 引用ボタン
            const quoteBtn = document.createElement('button');
            quoteBtn.className = 'ai-msg-action-btn';
            quoteBtn.title = '引用して返信';
            quoteBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" class="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 10h10a8 8 0 018 8v2M3 10l6 6m-6-6l6-6"/></svg>';
            quoteBtn.onclick = function() {
                const input = document.getElementById('ai-input-message');
                if (!input) return;
                const quoted = content.split('\n').map(function(line) { return '> ' + line; }).join('\n');
                input.value = quoted + '\n' + input.value;
                input.focus();
                const len = input.value.length;
                input.setSelectionRange(len, len);
                // パネルが最小化されていたら展開
                if (isPanelMinimized) toggleMinimize();
                if (!isPanelOpen) openPanel();
            };
            actionsEl.appendChild(quoteBtn);

            rowEl.appendChild(actionsEl);
            container.appendChild(rowEl);
        }

        if (!skipSave) {
            chatHistory.push({ content, type });
            saveHistory();
            saveMessageToServer(content, type);
        }

        if (autoScroll) {
            scrollToBottom();
        }
    }

    function saveHistory() {
        try {
            sessionStorage.setItem(STORAGE_KEY, JSON.stringify(chatHistory));
        } catch (e) {}
    }

    // sessionStorage から即時復元（ページ遷移後のちらつき防止）
    function restoreFromStorage() {
        try {
            const saved = sessionStorage.getItem(STORAGE_KEY);
            if (!saved) return false;
            const history = JSON.parse(saved);
            if (!Array.isArray(history) || history.length === 0) return false;
            chatHistory = history;
            const container = document.getElementById('ai-messages-container');
            container.innerHTML = '';
            history.forEach(function(msg) {
                addMessage(msg.content, msg.type, true);
            });
            return true;
        } catch (e) {
            return false;
        }
    }

    // サーバーから履歴を取得して復元（ブラウザ再起動・別タブ対応）
    async function restoreFromServer() {
        try {
            const resp = await fetch(`${CONFIG.apiBaseUrl}/history?limit=50`);
            if (!resp.ok) return;
            const messages = await resp.json();
            if (!Array.isArray(messages) || messages.length === 0) return;
            // サーバーデータでローカルキャッシュを上書き
            chatHistory = messages.map(function(m) {
                return { content: m.content, type: m.type };
            });
            const container = document.getElementById('ai-messages-container');
            container.innerHTML = '';
            chatHistory.forEach(function(msg) {
                addMessage(msg.content, msg.type, true);
            });
            saveHistory();
        } catch (e) {
            // サーバー取得失敗時は sessionStorage のまま
        }
    }

    // サーバーにメッセージを非同期保存（fire-and-forget）
    function saveMessageToServer(content, type) {
        fetch(`${CONFIG.apiBaseUrl}/history`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ content, type })
        }).catch(function() {});
    }

    // marked.js は _Layout.cshtml で静的に読み込み済み（タイミング問題を回避）
    function configureMarked() {
        if (typeof marked === 'undefined') return;
        marked.setOptions({
            breaks: true,    // 改行を <br> に変換
            gfm: true        // GitHub Flavored Markdown
        });
    }

    // Markdown を HTML にレンダリング。marked.js が未ロードの場合は plaintext にフォールバック。
    function renderMarkdown(text) {
        if (typeof marked === 'undefined' || !text) {
            return `<p>${escapeHtml(text || '')}</p>`;
        }
        try {
            return marked.parse(text);
        } catch (e) {
            return `<p>${escapeHtml(text)}</p>`;
        }
    }

    // コードブロックにコピーボタンを追加
    function addCopyButton(codeEl) {
        const pre = codeEl.parentElement;
        pre.style.position = 'relative';

        const btn = document.createElement('button');
        btn.className = 'ai-code-copy-btn';
        btn.textContent = 'Copy';
        btn.onclick = function() {
            navigator.clipboard.writeText(codeEl.textContent).then(function() {
                btn.textContent = 'Copied!';
                setTimeout(function() { btn.textContent = 'Copy'; }, 2000);
            }).catch(function() {
                btn.textContent = 'Error';
                setTimeout(function() { btn.textContent = 'Copy'; }, 2000);
            });
        };
        pre.appendChild(btn);
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
        return progressEl;  // 返回元素引用
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
        trigger.classList.remove('hidden');
        trigger.classList.add('panel-open');
        isPanelOpen = true;
        // キーボードが既に出ている場合に備えてサイズ調整
        if (window.visualViewport) adjustPanelForKeyboard();
    }

    function closePanel() {
        const panel = document.getElementById('ai-assistant-panel');
        const trigger = document.getElementById('ai-assistant-trigger');

        panel.classList.remove('open');
        trigger.classList.remove('panel-open');
        isPanelOpen = false;
        resetPanelSize();
    }
    
    // 最小化（折りたたみ）トグル
    function toggleMinimize() {
        const panel = document.getElementById('ai-assistant-panel');
        const btn = document.getElementById('ai-collapse-btn');
        if (!panel) return;

        isPanelMinimized = !isPanelMinimized;
        panel.classList.toggle('ai-panel-minimized', isPanelMinimized);

        if (isPanelMinimized) {
            // 折りたたみ時：最大化を解除
            if (isMaximized) toggleMaximize();
            if (btn) btn.title = '展開';
            if (btn) btn.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 15l7-7 7 7" /></svg>`;
        } else {
            if (btn) btn.title = '最小化';
            if (btn) btn.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 12H4" /></svg>`;
        }
    }

    // 最大化状态
    let isMaximized = false;
    let previousWidth = '';
    let previousRight = '';

    // 切换最大化/还原
    function toggleMaximize() {
        const panel = document.getElementById('ai-assistant-panel');
        const trigger = document.getElementById('ai-assistant-trigger');
        const btn = document.getElementById('ai-maximize-btn');
        if (!panel) return;

        if (isMaximized) {
            // 还原
            panel.style.width = previousWidth || '';
            panel.style.right = previousRight || '';
            panel.classList.remove('ai-panel-maximized');
            if (trigger) trigger.classList.remove('hidden');
            isMaximized = false;
            if (btn) btn.title = 'Maximize';
            if (btn) btn.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5l-5-5m5 5v-4m0 4h-4" /></svg>`;
        } else {
            // 最大化
            previousWidth = panel.style.width;
            previousRight = panel.style.right;
            panel.style.width = '100vw';
            panel.style.right = '0';
            panel.classList.add('ai-panel-maximized');
            if (trigger) trigger.classList.add('hidden');
            isMaximized = true;
            if (btn) btn.title = 'Restore';
            if (btn) btn.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 9V4.5M9 9H4.5M9 9L3.75 3.75M9 15v4.5M9 15H4.5M9 15l-5.25 5.25M15 9h4.5M15 9V4.5M15 9l5.25-5.25M15 15h4.5M15 15v4.5m0-4.5l5.25 5.25" /></svg>`;
        }
    }

    // 清除消息
    function clearMessages() {
        // 実行中のタスクがあればキャンセル
        if (currentTaskId) {
            const taskId = currentTaskId;
            currentTaskId = null;
            fetch(`${CONFIG.apiBaseUrl}/tasks/${taskId}`, { method: 'DELETE' }).catch(function() {});
        }

        chatHistory = [];
        try { sessionStorage.removeItem(STORAGE_KEY); } catch (e) {}
        fetch(`${CONFIG.apiBaseUrl}/history`, { method: 'DELETE' }).catch(function() {});
        const container = document.getElementById('ai-messages-container');
        container.innerHTML = `
            <div class="ai-message assistant">
                <div class="ai-message-content">
                    🤖 对话已清除。有什么可以帮你的？
                </div>
            </div>
        `;
        currentTaskId = null;
        currentSessionId = null; // セッションをリセット（新規会話開始）
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
