/**
 * jpiere-chat-widget.js
 * JPiere チャットウィジェット互換ラッパー
 *
 * 統合実装: /js/ai-chat-widget.js
 *
 * 使用方法:
 *   <script src="/js/jpiere-chat-widget.js"></script>
 *   <script>JpiereChat.init({ project: 'jpiere-cs', userRole: 'employee' });</script>
 */
(function (global) {
  'use strict';

  const DEFAULTS = {
    project: 'jpiere-cs',
    userRole: 'employee',
    position: 'bottom-right'
  };

  function ensureWidget(cb) {
    if (global.AIChatWidget) return cb();
    if (global._awLoading) {
      global._awQueue = global._awQueue || [];
      global._awQueue.push(cb);
      return;
    }
    global._awLoading = true;
    global._awQueue = global._awQueue || [];
    global._awQueue.push(cb);
    const script = document.createElement('script');
    script.src = '/js/ai-chat-widget.js';
    script.async = true;
    script.onload = function () {
      global._awLoading = false;
      const queue = global._awQueue || [];
      global._awQueue = [];
      queue.forEach(function (fn) {
        try { fn(); } catch (e) { /* ignore */ }
      });
    };
    document.head.appendChild(script);
  }

  function init(options) {
    const opts = Object.assign({}, DEFAULTS, options || {});
    const cfg = {
      apiMode: 'framework',
      framework: { project: opts.project, role: opts.userRole, defaultCliTool: 'qwen' },
      triggerPosition: opts.position
    };

    const start = function () {
      global.AI_CHAT_CONFIG = Object.assign({}, global.AI_CHAT_CONFIG || {}, cfg);
      if (global.AIChatWidget) global.AIChatWidget.init(global.AI_CHAT_CONFIG);
    };

    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', function () { ensureWidget(start); });
    } else {
      ensureWidget(start);
    }
  }

  global.JpiereChat = { init };
})(window);
