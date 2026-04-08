# AI チャットアイコンが表示されない場合の確認手順

## 1. まず確認すること

- `data-user-authenticated="true"` になっているか
- `ai-chat-widget.js` が読み込まれているか
- 旧 `ai-assistant.js` / `ai-assistant.css` が残っていないか（キャッシュ含む）

> 2026-04 以降、UI 実装は `ai-chat-widget.js` に統合されています。

---

## 2. 画面上で確認

以下をブラウザの DevTools コンソールで実行：

```js
console.log('Authenticated:', document.body?.getAttribute('data-user-authenticated'));
console.log('AIChatWidget:', typeof window.AIChatWidget);
console.log('Trigger exists:', document.getElementById('aw-trigger') !== null);
```

- `AIChatWidget` が `undefined` の場合、JS が読み込まれていません。
- `aw-trigger` が存在しない場合、初期化が行われていません。

---

## 3. Network タブで確認

`ai-chat-widget.js` が 200 で取得できているか確認します。

---

## 4. キャッシュ問題

以下のいずれかを試してください：

- Hard Reload（強制リロード）
- Service Worker の削除
- 旧 `ai-assistant.js` / `ai-assistant.css` のキャッシュ無効化

---

## 5. よくある原因

- `data-user-authenticated="false"` になっている
- `_Layout.cshtml` で `ai-chat-widget.js` が読み込まれていない
- `window.AI_CHAT_CONFIG` の設定が不足している

---

## 6. 参考: 最小初期化例

```html
<script src="/js/ai-chat-widget.js"></script>
<script>
  window.AI_CHAT_CONFIG = {
    apiMode: 'framework',
    framework: { project: 'your-project', defaultCliTool: 'qwen' }
  };
</script>
```
