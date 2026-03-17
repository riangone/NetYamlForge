// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// ── toggle-group / bool-toggle ────────────────────────────────────────────────
// data-toggle-group コンテナ内のラジオ変更時に btn-active/btn-primary を切り替える
document.addEventListener('change', function (e) {
    var radio = e.target;
    if (radio.type !== 'radio' || !radio.closest('[data-toggle-group]')) return;
    var group = radio.closest('[data-toggle-group]');
    group.querySelectorAll('input[type="radio"]').forEach(function (r) {
        var span = r.nextElementSibling;
        if (!span) return;
        if (r.checked) {
            span.classList.remove('btn-outline');
            span.classList.add('btn-primary', 'btn-active');
        } else {
            span.classList.remove('btn-primary', 'btn-active');
            span.classList.add('btn-outline');
        }
    });
});

// HTMX でフィルターが再挿入された後も動作するよう htmx:afterSettle で再スキャンは不要
// (change イベントはバブリングするため document レベルで十分)

// ── 検索インプット デバウンス ────────────────────────────────────────────────
// #search-input への入力後 350ms 内に追加入力がなければ HTMX リクエストを発火する。
// キーストロークごとに発火するのを防ぎ、サーバー負荷とちらつきを軽減する。
(function () {
    var DEBOUNCE_MS = 350;

    function attachDebounce(input) {
        if (input._debounceAttached) return;
        input._debounceAttached = true;

        var timer = null;
        input.addEventListener('input', function () {
            clearTimeout(timer);
            timer = setTimeout(function () {
                // HTMX が hx-trigger="input" を処理している場合は手動発火不要。
                // form submit 方式（hx-trigger="submit"）では明示的に form を submit する。
                var form = input.closest('form[hx-get], form[hx-post]');
                if (form) {
                    htmx.trigger(form, 'submit');
                }
            }, DEBOUNCE_MS);
        });
    }

    function scanAndAttach() {
        var input = document.getElementById('search-input');
        if (input) attachDebounce(input);
    }

    // 初回アタッチ
    document.addEventListener('DOMContentLoaded', scanAndAttach);
    // HTMX 部分更新後に再スキャン（フィルターの再挿入などで search-input が再生成された場合）
    document.addEventListener('htmx:afterSettle', scanAndAttach);
}());

// ── ファイル・画像アップロード処理 ────────────────────────────────────────────────
// file 型・image 型のフィールドでファイル選択時に hidden フィールドにパスを設定する

/**
 * ファイル選択時の処理
 * @param {HTMLInputElement} input - ファイル入力要素
 * @param {string} fieldName - フィールド名
 */
function handleFileSelect(input, fieldName) {
    var file = input.files[0];
    if (!file) return;

    // 簡易検証：ファイルサイズ（10MB まで）
    var maxSize = 10 * 1024 * 1024;
    if (file.size > maxSize) {
        alert('ファイルサイズが大きすぎます（最大 10MB）');
        input.value = '';
        return;
    }

    // 画像の場合はプレビュー表示
    if (input.type === 'file' && input.accept === 'image/*') {
        handleImageSelect(input, fieldName);
    }
}

/**
 * 画像選択時のプレビュー表示処理
 * @param {HTMLInputElement} input - ファイル入力要素
 * @param {string} fieldName - フィールド名
 */
function handleImageSelect(input, fieldName) {
    var file = input.files[0];
    if (!file) return;

    // 簡易検証：ファイルサイズ（5MB まで）
    var maxSize = 5 * 1024 * 1024;
    if (file.size > maxSize) {
        alert('画像ファイルサイズが大きすぎます（最大 5MB）');
        input.value = '';
        return;
    }

    // 簡易検証：画像ファイルか
    if (!file.type.startsWith('image/')) {
        alert('画像ファイルを選択してください');
        input.value = '';
        return;
    }

    var reader = new FileReader();
    reader.onload = function (e) {
        // プレビュー表示
        var preview = document.getElementById('image-preview-' + fieldName);
        if (preview) {
            preview.src = e.target.result;
        }
        // hidden フィールドに Base64 データを設定（簡易実装）
        // 本番ではサーバーアップロード後にパスを設定
        var hidden = document.getElementById('image-value-' + fieldName);
        if (hidden) {
            // 簡易：ファイル名のみ設定（実際にはサーバーサイドでアップロード処理）
            hidden.value = '/uploads/' + file.name;
        }
    };
    reader.readAsDataURL(file);
}

/**
 * ファイル入力クリア処理
 * @param {string} fieldName - フィールド名
 */
function clearFileInput(fieldName) {
    // file input
    var fileInput = document.getElementById('file-input-' + fieldName);
    if (fileInput) fileInput.value = '';

    // image input
    var imageInput = document.getElementById('image-input-' + fieldName);
    if (imageInput) imageInput.value = '';

    // hidden values
    var fileValue = document.getElementById('file-value-' + fieldName);
    if (fileValue) fileValue.value = '';

    var imageValue = document.getElementById('image-value-' + fieldName);
    if (imageValue) imageValue.value = '';

    // プレビュー画像削除
    var preview = document.getElementById('image-preview-' + fieldName);
    if (preview) {
        preview.src = '';
        preview.style.display = 'none';
    }

    // 表示コンテナの再描画のため HTMX でフォームをリロード
    var form = document.querySelector('form[hx-post*="Create"], form[hx-post*="Edit"]');
    if (form) {
        htmx.trigger(form, 'submit');
    }
}

// HTMX 部分更新後にファイル入力要素を再スキャン
document.addEventListener('htmx:afterSettle', function () {
    // 画像プレビュー要素の再初期化が必要な場合ここに実装
});

// ── Phase 3: 選択系コンポーネント ────────────────────────────────────────────────

/**
 * チェックボックスグループの値を更新
 */
function toggleCheckboxGroup(fieldName, checkbox) {
    var hidden = document.getElementById('checkbox-group-value-' + fieldName);
    var checkboxes = checkbox.closest('.checkbox-group-wrapper').querySelectorAll('input[type="checkbox"]');
    var values = [];
    checkboxes.forEach(function(cb) {
        if (cb.checked) values.push(cb.value);
    });
    hidden.value = values.join(',');
}

/**
 * スイッチグループの値を更新
 */
function toggleSwitchGroup(fieldName, toggle) {
    var hidden = document.getElementById('switch-group-value-' + fieldName);
    var toggles = toggle.closest('.switch-group-wrapper').querySelectorAll('input[type="checkbox"]');
    var values = [];
    toggles.forEach(function(t) {
        if (t.checked) values.push(t.value);
    });
    hidden.value = values.join(',');
}

// ── Phase 3: JSON 検証 ────────────────────────────────────────────────

/**
 * JSON 形式検証
 */
function validateJson(fieldName) {
    var textarea = document.querySelector('textarea[name="' + fieldName + '"]');
    var errorSpan = document.getElementById('json-error-' + fieldName);
    try {
        JSON.parse(textarea.value);
        errorSpan.textContent = '✓ 有効な JSON です';
        errorSpan.className = 'text-success text-sm mt-1';
    } catch (e) {
        errorSpan.textContent = '✕ 無効な JSON: ' + e.message;
        errorSpan.className = 'text-error text-sm mt-1';
    }
}

// ── Phase 3: サイン板 ────────────────────────────────────────────────

/**
 * サイン入力クリア
 */
function clearSignature(fieldName) {
    var canvas = document.getElementById('signature-canvas-' + fieldName);
    var ctx = canvas.getContext('2d');
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    var hidden = document.getElementById('signature-value-' + fieldName);
    hidden.value = '';
}

// ── Phase 3: 日時範囲 ────────────────────────────────────────────────

// datetime-range はフォーム送信時に start/end を結合する処理が必要
document.addEventListener('DOMContentLoaded', function() {
    document.querySelectorAll('[id^="datetime-range-value-"]').forEach(function(hidden) {
        var fieldName = hidden.id.replace('datetime-range-value-', '');
        var startInput = document.querySelector('input[name="' + fieldName + '-start"]');
        var endInput = document.querySelector('input[name="' + fieldName + '-end"]');
        
        if (startInput && endInput) {
            startInput.addEventListener('change', updateDatetimeRange);
            endInput.addEventListener('change', updateDatetimeRange);
        }
    });
});

function updateDatetimeRange() {
    var fieldName = this.name.replace('-start', '').replace('-end', '');
    var startInput = document.querySelector('input[name="' + fieldName + '-start"]');
    var endInput = document.querySelector('input[name="' + fieldName + '-end"]');
    var hidden = document.getElementById('datetime-range-value-' + fieldName);

    if (startInput && endInput && hidden) {
        hidden.value = startInput.value + '|' + endInput.value;
    }
}

// ── Markdown プレビュー ────────────────────────────────────────────────
function toggleMarkdownPreview(fieldName) {
    var textarea = document.querySelector('textarea[name="' + fieldName + '"]');
    var preview = document.getElementById('markdown-preview-' + fieldName);
    if (!textarea || !preview) return;
    
    if (preview.style.display === 'none' || preview.style.display === '') {
        // 簡易 Markdown レンダリング
        var md = textarea.value
            .replace(/^### (.*$)/gim, '<h3>$1</h3>')
            .replace(/^## (.*$)/gim, '<h2>$1</h2>')
            .replace(/^# (.*$)/gim, '<h1>$1</h1>')
            .replace(/\*\*(.*)\*\*/gim, '<strong>$1</strong>')
            .replace(/\*(.*)\*/gim, '<em>$1</em>')
            .replace(/`(.*)`/gim, '<code>$1</code>')
            .replace(/\n/gim, '<br>');
        preview.innerHTML = md;
        preview.style.display = 'block';
    } else {
        preview.style.display = 'none';
    }
}
