/**
 * auto-dealer-chat-widget.js
 * Auto Dealer チャットウィジェット互換ラッパー
 *
 * 統合実装: /js/ai-chat-widget.js
 *
 * 使用方法:
 *   <script src="/js/auto-dealer-chat-widget.js"></script>
 *   <script>AutoDealerChat.init({ project: 'auto-dealer-demo', mode: 'customer' });</script>
 */
(function (global) {
  'use strict';

  const DEFAULTS = {
    project: 'auto-dealer-demo',
    mode: 'customer', // 'customer' | 'staff'
    position: 'bottom-right'
  };

  function buildConfig(opts) {
    const mode = opts.mode === 'staff' ? 'staff' : 'customer';
    const isStaff = mode === 'staff';
    return {
      apiMode: 'dealer',
      dealer: { project: opts.project, mode },
      triggerPosition: opts.position || DEFAULTS.position,
      theme: isStaff
        ? {
            primaryColor: '#2e7d32',
            accentColor: '#1b5e20',
            headerBg: 'linear-gradient(135deg, #2e7d32, #1b5e20)',
            headerIcon: '🤝'
          }
        : {
            primaryColor: '#1a73e8',
            accentColor: '#0d47a1',
            headerBg: 'linear-gradient(135deg, #1a73e8, #0d47a1)',
            headerIcon: '🚗'
          },
      title: isStaff ? '🤝 AI 業務アシスタント' : '🚗 AI 窓口',
      subtitle: isStaff ? '業務支援 · リアルタイム対応' : '24 時間対応 · 平均応答 < 10 秒',
      placeholder: isStaff ? '業務に関する質問をどうぞ...' : 'ご用件をお聞かせください...',
      welcomeMessage: isStaff
        ? 'こんにちは！AI 業務アシスタントです。\nリード管理・予約確認・在庫照会など何でもご相談ください。'
        : 'こんにちは！AI カスタマーサポートです。\n試乗・ご購入・サービスのご相談は何でもどうぞ！'
    };
  }

  function ensureWidget(cb) {
    if (global.AIChatWidget || global.DealerChat) return cb();
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
    const cfg = buildConfig(opts);
    const start = function () {
      if (global.AIChatWidget) {
        global.AIChatWidget.init(cfg);
      } else if (global.DealerChat) {
        global.DealerChat.init({
          mode: opts.mode,
          project: opts.project,
          position: opts.position
        });
      } else {
        global.AI_CHAT_CONFIG = cfg;
      }
    };

    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', function () { ensureWidget(start); });
    } else {
      ensureWidget(start);
    }
  }

  global.AutoDealerChat = { init };
})(window);
