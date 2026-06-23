/* FormForge Builder */
'use strict';

const FORM_ID = window.FORM_ID;
let state = {
    title: window.FORM_DATA.title,
    description: window.FORM_DATA.description || '',
    themeColor: window.FORM_DATA.themeColor || '#7C3AED',
    acceptsResponses: window.FORM_DATA.acceptsResponses ?? true,
    questions: window.FORM_DATA.questions.map(q => ({
        id: q.id,
        type: q.type,
        title: q.title,
        description: q.description || '',
        required: q.required,
        options: q.options || [],
        scaleMin: q.scaleMin ?? 1,
        scaleMax: q.scaleMax ?? 5,
        scaleMinLabel: q.scaleMinLabel || '',
        scaleMaxLabel: q.scaleMaxLabel || '',
    })),
    activeId: null,
    dirty: false,
    saving: false,
};

let saveTimer = null;

/* ── DOM refs ── */
const $list = document.getElementById('questions-list');
const $formTitleInput = document.getElementById('form-title-input');
const $metaTitleDisplay = document.getElementById('meta-title-display');
const $metaDesc = document.getElementById('meta-desc');
const $colorPicker = document.getElementById('theme-color-picker');
const $saveBtn = document.getElementById('save-btn');
const $publishBtn = document.getElementById('publish-btn');
const $saveStatus = document.getElementById('save-status');
const $addBtn = document.getElementById('add-question-btn');
const $addTypePills = document.getElementById('add-type-pills');
const $metaCard = document.getElementById('meta-card');
const tpl = document.getElementById('q-card-tpl');

/* ── Init ── */
function init() {
    applyTheme(state.themeColor);
    renderAll();
    bindHeaderEvents();
    bindAddSection();
    bindTabSwitching();
    markSaved();
}

/* ── Theme ── */
function applyTheme(color) {
    document.documentElement.style.setProperty('--builder-color', color);
    $metaCard.style.borderTopColor = color;
}

/* ── Render ── */
function renderAll() {
    $list.innerHTML = '';
    state.questions.forEach(q => $list.appendChild(createCard(q)));
    initDragDrop();
}

function createCard(q) {
    const node = tpl.content.cloneNode(true);
    const card = node.querySelector('.q-card');
    card.dataset.id = q.id;

    const titleInput = card.querySelector('.q-title-input');
    titleInput.value = q.title;
    titleInput.addEventListener('input', e => {
        updateQuestion(q.id, { title: e.target.value });
        scheduleSave();
    });

    const typeSelect = card.querySelector('.q-type-select');
    typeSelect.value = q.type;
    typeSelect.addEventListener('change', e => {
        updateQuestion(q.id, { type: e.target.value });
        rerenderCard(q.id);
        scheduleSave();
    });

    const descInput = card.querySelector('.q-desc-input');
    descInput.value = q.description;
    descInput.addEventListener('input', e => {
        updateQuestion(q.id, { description: e.target.value });
        scheduleSave();
    });

    const reqChk = card.querySelector('.q-required-chk');
    reqChk.checked = q.required;
    reqChk.addEventListener('change', e => {
        updateQuestion(q.id, { required: e.target.checked });
        scheduleSave();
    });

    card.querySelector('.duplicate-btn').addEventListener('click', () => duplicateQuestion(q.id));
    card.querySelector('.delete-btn').addEventListener('click', () => deleteQuestion(q.id));
    card.addEventListener('click', () => setActive(q.id));

    renderCardContent(card, q);
    if (state.activeId === q.id) card.classList.add('active');

    return card;
}

function renderCardContent(card, q) {
    const area = card.querySelector('.q-content-area');
    area.innerHTML = '';

    if (q.type === 'short_text') {
        area.innerHTML = `<div class="q-text-preview">Short answer text</div>`;
    } else if (q.type === 'paragraph') {
        area.innerHTML = `<div class="q-text-preview" style="height:56px;display:flex;align-items:flex-start;padding-top:8px">Long answer text</div>`;
    } else if (q.type === 'multiple_choice' || q.type === 'checkboxes') {
        area.appendChild(buildOptionsUI(q));
    } else if (q.type === 'dropdown') {
        area.appendChild(buildDropdownUI(q));
    } else if (q.type === 'linear_scale') {
        area.appendChild(buildScaleUI(q));
    } else if (q.type === 'date') {
        area.innerHTML = `<div class="q-datetime-preview">MM/DD/YYYY</div>`;
    } else if (q.type === 'time') {
        area.innerHTML = `<div class="q-datetime-preview">--:-- --</div>`;
    }
}

function buildOptionsUI(q) {
    const wrap = document.createElement('div');
    wrap.className = 'q-options-list';

    const render = () => {
        wrap.innerHTML = '';
        q.options.forEach((opt, idx) => {
            const row = document.createElement('div');
            row.className = 'q-option-item';

            const iconSvg = q.type === 'checkboxes'
                ? `<svg class="opt-icon" viewBox="0 0 18 18" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="2" y="2" width="14" height="14" rx="3"/></svg>`
                : `<svg class="opt-icon" viewBox="0 0 18 18" fill="none" stroke="currentColor" stroke-width="1.5"><circle cx="9" cy="9" r="7"/></svg>`;

            const input = document.createElement('input');
            input.type = 'text';
            input.className = 'q-option-input';
            input.value = opt;
            input.placeholder = `Option ${idx + 1}`;
            input.addEventListener('input', e => {
                q.options[idx] = e.target.value;
                updateQuestion(q.id, { options: [...q.options] });
                scheduleSave();
            });
            input.addEventListener('keydown', e => {
                if (e.key === 'Enter') { e.preventDefault(); addOption(); }
            });

            const delBtn = document.createElement('button');
            delBtn.type = 'button';
            delBtn.className = 'opt-del-btn';
            delBtn.title = 'Remove option';
            delBtn.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6L6 18M6 6l12 12"/></svg>`;
            delBtn.addEventListener('click', () => removeOption(idx));

            row.innerHTML = iconSvg;
            row.appendChild(input);
            row.appendChild(delBtn);
            wrap.appendChild(row);
        });

        // Add option button
        const addRow = document.createElement('div');
        addRow.className = 'q-option-item';
        const addIcon = q.type === 'checkboxes'
            ? `<svg class="opt-icon" viewBox="0 0 18 18" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="2" y="2" width="14" height="14" rx="3"/></svg>`
            : `<svg class="opt-icon" viewBox="0 0 18 18" fill="none" stroke="currentColor" stroke-width="1.5"><circle cx="9" cy="9" r="7"/></svg>`;
        const addBtn = document.createElement('button');
        addBtn.type = 'button';
        addBtn.className = 'add-option-btn';
        addBtn.innerHTML = `${addIcon} <span>Add option</span>`;
        addBtn.addEventListener('click', addOption);
        addRow.appendChild(addBtn);
        wrap.appendChild(addRow);
    };

    const addOption = () => {
        q.options.push('');
        updateQuestion(q.id, { options: [...q.options] });
        render();
        scheduleSave();
        const inputs = wrap.querySelectorAll('.q-option-input');
        if (inputs.length) inputs[inputs.length - 1].focus();
    };

    const removeOption = (idx) => {
        q.options.splice(idx, 1);
        updateQuestion(q.id, { options: [...q.options] });
        render();
        scheduleSave();
    };

    if (q.options.length === 0) q.options.push('Option 1');
    render();
    return wrap;
}

function buildDropdownUI(q) {
    const wrap = document.createElement('div');
    wrap.className = 'q-options-list';

    const header = document.createElement('div');
    header.style.cssText = 'font-size:12px;color:#9CA3AF;margin-bottom:8px';
    header.textContent = 'Dropdown options:';
    wrap.appendChild(header);

    const optWrap = document.createElement('div');
    optWrap.className = 'q-options-list';
    wrap.appendChild(optWrap);

    const render = () => {
        optWrap.innerHTML = '';
        q.options.forEach((opt, idx) => {
            const row = document.createElement('div');
            row.className = 'q-option-item';

            const num = document.createElement('span');
            num.style.cssText = 'width:18px;text-align:center;font-size:13px;color:#9CA3AF;flex-shrink:0';
            num.textContent = `${idx + 1}.`;

            const input = document.createElement('input');
            input.type = 'text';
            input.className = 'q-option-input';
            input.value = opt;
            input.placeholder = `Option ${idx + 1}`;
            input.addEventListener('input', e => {
                q.options[idx] = e.target.value;
                updateQuestion(q.id, { options: [...q.options] });
                scheduleSave();
            });

            const delBtn = document.createElement('button');
            delBtn.type = 'button';
            delBtn.className = 'opt-del-btn';
            delBtn.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6L6 18M6 6l12 12"/></svg>`;
            delBtn.addEventListener('click', () => { q.options.splice(idx, 1); updateQuestion(q.id, { options: [...q.options] }); render(); scheduleSave(); });

            row.appendChild(num);
            row.appendChild(input);
            row.appendChild(delBtn);
            optWrap.appendChild(row);
        });

        const addRow = document.createElement('button');
        addRow.type = 'button';
        addRow.className = 'add-option-btn';
        addRow.innerHTML = `<span class="opt-icon">+</span><span>Add option</span>`;
        addRow.addEventListener('click', () => { q.options.push(''); updateQuestion(q.id, { options: [...q.options] }); render(); scheduleSave(); });
        optWrap.appendChild(addRow);
    };

    if (q.options.length === 0) q.options.push('Option 1');
    render();
    return wrap;
}

function buildScaleUI(q) {
    const wrap = document.createElement('div');
    wrap.className = 'q-scale-config';

    wrap.innerHTML = `
        <div class="scale-range-row">
            <span>From</span>
            <input class="scale-input" type="number" min="0" max="1" value="${q.scaleMin}" data-field="scaleMin" />
            <span>to</span>
            <input class="scale-input" type="number" min="2" max="10" value="${q.scaleMax}" data-field="scaleMax" />
        </div>
        <div class="scale-label-row">
            <input class="scale-label-input" type="text" placeholder="Label (optional)" value="${q.scaleMinLabel || ''}" data-field="scaleMinLabel" />
            <input class="scale-label-input" type="text" placeholder="Label (optional)" value="${q.scaleMaxLabel || ''}" data-field="scaleMaxLabel" />
        </div>
    `;

    wrap.querySelectorAll('[data-field]').forEach(el => {
        el.addEventListener('input', e => {
            const field = e.target.dataset.field;
            const val = e.target.type === 'number' ? parseInt(e.target.value) || 1 : e.target.value;
            updateQuestion(q.id, { [field]: val });
            scheduleSave();
        });
    });

    return wrap;
}

function rerenderCard(id) {
    const q = state.questions.find(x => x.id === id);
    if (!q) return;
    const card = $list.querySelector(`[data-id="${id}"]`);
    if (!card) return;
    renderCardContent(card, q);
}

/* ── State mutations ── */
function updateQuestion(id, patch) {
    const q = state.questions.find(x => x.id === id);
    if (q) Object.assign(q, patch);
    state.dirty = true;
}

function setActive(id) {
    state.activeId = id;
    $list.querySelectorAll('.q-card').forEach(c => c.classList.toggle('active', c.dataset.id === id));
}

function addQuestion(type = 'short_text') {
    const q = {
        id: crypto.randomUUID().replace(/-/g, ''),
        type,
        title: '',
        description: '',
        required: false,
        options: [],
        scaleMin: 1,
        scaleMax: 5,
        scaleMinLabel: '',
        scaleMaxLabel: '',
    };
    state.questions.push(q);
    $list.appendChild(createCard(q));
    setActive(q.id);
    state.dirty = true;
    initDragDrop();

    // Focus new question title
    const newCard = $list.querySelector(`[data-id="${q.id}"]`);
    if (newCard) {
        newCard.scrollIntoView({ behavior: 'smooth', block: 'center' });
        newCard.querySelector('.q-title-input')?.focus();
    }
}

function duplicateQuestion(id) {
    const q = state.questions.find(x => x.id === id);
    if (!q) return;
    const idx = state.questions.indexOf(q);
    const clone = JSON.parse(JSON.stringify(q));
    clone.id = crypto.randomUUID().replace(/-/g, '');
    state.questions.splice(idx + 1, 0, clone);
    renderAll();
    setActive(clone.id);
    state.dirty = true;
    scheduleSave();
}

function deleteQuestion(id) {
    state.questions = state.questions.filter(x => x.id !== id);
    if (state.activeId === id) state.activeId = null;
    const card = $list.querySelector(`[data-id="${id}"]`);
    if (card) {
        card.style.transition = 'opacity 0.15s, transform 0.15s';
        card.style.opacity = '0';
        card.style.transform = 'scale(0.96)';
        setTimeout(() => card.remove(), 150);
    }
    state.dirty = true;
    scheduleSave();
}

/* ── Save ── */
function scheduleSave() {
    state.dirty = true;
    $saveStatus.textContent = 'Unsaved changes';
    $saveStatus.style.color = '#F59E0B';
    clearTimeout(saveTimer);
    saveTimer = setTimeout(save, 1500);
}

async function save() {
    if (state.saving) return;
    state.saving = true;
    $saveStatus.textContent = 'Saving…';
    $saveStatus.style.color = '#9CA3AF';

    try {
        const body = {
            title: state.title,
            description: state.description,
            themeColor: state.themeColor,
            acceptsResponses: state.acceptsResponses,
            questions: state.questions,
        };
        const res = await fetch(`/api/forms/${FORM_ID}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        });
        if (res.ok) {
            markSaved();
            state.dirty = false;
        } else {
            $saveStatus.textContent = 'Save failed';
            $saveStatus.style.color = '#EF4444';
        }
    } catch {
        $saveStatus.textContent = 'Save failed';
        $saveStatus.style.color = '#EF4444';
    } finally {
        state.saving = false;
    }
}

function markSaved() {
    $saveStatus.textContent = 'All changes saved';
    $saveStatus.style.color = '#10B981';
}

/* ── Header events ── */
function bindHeaderEvents() {
    // Sync title inputs
    $formTitleInput.addEventListener('input', e => {
        state.title = e.target.value;
        $metaTitleDisplay.value = e.target.value;
        scheduleSave();
    });
    $metaTitleDisplay.addEventListener('input', e => {
        state.title = e.target.value;
        $formTitleInput.value = e.target.value;
        scheduleSave();
    });
    $metaDesc.addEventListener('input', e => {
        state.description = e.target.value;
        scheduleSave();
    });

    $colorPicker.addEventListener('input', e => {
        state.themeColor = e.target.value;
        applyTheme(e.target.value);
        scheduleSave();
    });

    $saveBtn.addEventListener('click', () => { clearTimeout(saveTimer); save(); });

    $publishBtn.addEventListener('click', async () => {
        // Save first
        await save();
        const published = $publishBtn.dataset.published !== 'true';
        const res = await fetch(`/api/forms/${FORM_ID}/publish`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ published }),
        });
        if (res.ok) {
            $publishBtn.dataset.published = published.toString();
            $publishBtn.textContent = published ? 'Unpublish' : 'Publish';
            $publishBtn.classList.toggle('published', published);
            if (published) {
                showToast(`Form published! Share: ${location.origin}/f/${FORM_ID}`);
            } else {
                showToast('Form unpublished.');
            }
        }
    });
}

function bindAddSection() {
    $addBtn.addEventListener('click', () => {
        $addTypePills.classList.toggle('visible');
        $addBtn.textContent = $addTypePills.classList.contains('visible')
            ? '× Close'
            : '+ Add question';
        if (!$addTypePills.classList.contains('visible')) {
            $addBtn.innerHTML = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><path d="M12 5v14M5 12h14"/></svg> Add question`;
        }
    });

    $addTypePills.querySelectorAll('.type-pill').forEach(pill => {
        pill.addEventListener('click', () => {
            addQuestion(pill.dataset.type);
            $addTypePills.classList.remove('visible');
            $addBtn.innerHTML = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><path d="M12 5v14M5 12h14"/></svg> Add question`;
        });
    });
}

/* ── Tab switching ── */
function bindTabSwitching() {
    document.querySelectorAll('.bh-tab').forEach(tab => {
        tab.addEventListener('click', () => {
            document.querySelectorAll('.bh-tab').forEach(t => t.classList.remove('active'));
            tab.classList.add('active');
            const which = tab.dataset.tab;
            document.getElementById('tab-build').classList.toggle('hidden', which !== 'build');
            document.getElementById('tab-preview').classList.toggle('hidden', which !== 'preview');
            if (which === 'preview') renderPreview();
        });
    });
}

function renderPreview() {
    const container = document.getElementById('preview-container');
    const themeColor = state.themeColor;

    container.innerHTML = '';

    // Header card
    const header = document.createElement('div');
    header.className = 'fill-header-card';
    header.style.cssText = `background:white;border-radius:12px;border-top:8px solid ${themeColor};padding:32px 36px;box-shadow:0 1px 3px rgba(0,0,0,.06)`;
    header.innerHTML = `<h1 style="font-size:24px;font-weight:700;color:#1E1B4B;margin-bottom:8px">${escHtml(state.title || 'Untitled Form')}</h1>
        ${state.description ? `<p style="color:#6B7280;font-size:14px">${escHtml(state.description)}</p>` : ''}`;
    container.appendChild(header);

    // Questions
    state.questions.forEach(q => {
        const card = document.createElement('div');
        card.style.cssText = 'background:white;border-radius:12px;padding:24px 32px;border:1.5px solid #F0EBF8;box-shadow:0 1px 3px rgba(0,0,0,.04)';

        let contentHtml = '';
        if (q.type === 'short_text') {
            contentHtml = `<div style="border-bottom:1.5px solid #E8E4F4;padding:8px 0;color:#D1D5DB;font-size:15px">Short answer text</div>`;
        } else if (q.type === 'paragraph') {
            contentHtml = `<div style="border-bottom:1.5px solid #E8E4F4;padding:8px 0;color:#D1D5DB;font-size:15px;height:70px">Long answer text</div>`;
        } else if (q.type === 'multiple_choice' || q.type === 'checkboxes') {
            contentHtml = q.options.map(opt => {
                const icon = q.type === 'checkboxes'
                    ? `<span style="width:18px;height:18px;border-radius:4px;border:2px solid #D1D5DB;display:inline-block;flex-shrink:0"></span>`
                    : `<span style="width:18px;height:18px;border-radius:50%;border:2px solid #D1D5DB;display:inline-block;flex-shrink:0"></span>`;
                return `<div style="display:flex;align-items:center;gap:12px;padding:8px 0">${icon}<span style="font-size:14px">${escHtml(opt)}</span></div>`;
            }).join('');
        } else if (q.type === 'dropdown') {
            contentHtml = `<div style="border:1.5px solid #E8E4F4;border-radius:8px;padding:10px 12px;color:#D1D5DB;font-size:15px">Choose an option</div>`;
        } else if (q.type === 'linear_scale') {
            const nums = [];
            for (let v = q.scaleMin; v <= q.scaleMax; v++) {
                nums.push(`<div style="display:flex;flex-direction:column;align-items:center;gap:4px">
                    <div style="width:40px;height:40px;display:flex;align-items:center;justify-content:center;border-radius:8px;border:1.5px solid #E8E4F4;font-size:14px;color:#6B7280">${v}</div>
                </div>`);
            }
            contentHtml = `<div style="display:flex;gap:6px">${nums.join('')}</div>`;
        } else if (q.type === 'date') {
            contentHtml = `<div style="border-bottom:1.5px solid #E8E4F4;padding:8px 0;color:#D1D5DB;font-size:15px;width:180px">MM / DD / YYYY</div>`;
        } else if (q.type === 'time') {
            contentHtml = `<div style="border-bottom:1.5px solid #E8E4F4;padding:8px 0;color:#D1D5DB;font-size:15px;width:120px">--:-- --</div>`;
        }

        card.innerHTML = `
            <div style="margin-bottom:12px">
                <span style="font-size:15px;font-weight:500;color:#1E1B4B">${escHtml(q.title || 'Question')}</span>
                ${q.required ? '<span style="color:#EF4444;margin-left:3px">*</span>' : ''}
                ${q.description ? `<p style="font-size:13px;color:#9CA3AF;margin-top:4px">${escHtml(q.description)}</p>` : ''}
            </div>
            ${contentHtml}
        `;
        container.appendChild(card);
    });

    // Submit button
    const submitRow = document.createElement('div');
    submitRow.innerHTML = `<button style="padding:12px 40px;border-radius:9px;font-size:15px;font-weight:600;background:${themeColor};color:white;border:none;cursor:default;opacity:0.9" disabled>Submit</button>`;
    container.appendChild(submitRow);
}

/* ── Drag & Drop ── */
function initDragDrop() {
    const cards = $list.querySelectorAll('.q-card');
    let dragging = null;

    cards.forEach(card => {
        const handle = card.querySelector('.q-drag-handle');

        handle.addEventListener('mousedown', () => { card.draggable = true; });
        handle.addEventListener('mouseup', () => { card.draggable = false; });

        card.addEventListener('dragstart', e => {
            dragging = card;
            card.classList.add('dragging');
            e.dataTransfer.effectAllowed = 'move';
        });

        card.addEventListener('dragend', () => {
            card.classList.remove('dragging');
            card.draggable = false;
            $list.querySelectorAll('.q-card').forEach(c => c.classList.remove('drag-over'));
            syncQuestionsOrder();
            dragging = null;
        });

        card.addEventListener('dragover', e => {
            e.preventDefault();
            if (dragging && card !== dragging) {
                $list.querySelectorAll('.q-card').forEach(c => c.classList.remove('drag-over'));
                card.classList.add('drag-over');
                const rect = card.getBoundingClientRect();
                const mid = rect.top + rect.height / 2;
                if (e.clientY < mid) {
                    $list.insertBefore(dragging, card);
                } else {
                    $list.insertBefore(dragging, card.nextSibling);
                }
            }
        });

        card.addEventListener('dragleave', () => card.classList.remove('drag-over'));
    });
}

function syncQuestionsOrder() {
    const cards = [...$list.querySelectorAll('.q-card')];
    const newOrder = cards.map(c => state.questions.find(q => q.id === c.dataset.id)).filter(Boolean);
    state.questions = newOrder;
    scheduleSave();
}

/* ── Utilities ── */
function escHtml(str) {
    return String(str).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}

function showToast(msg) {
    let toast = document.getElementById('ff-toast');
    if (!toast) {
        toast = document.createElement('div');
        toast.id = 'ff-toast';
        toast.style.cssText = 'position:fixed;bottom:24px;left:50%;transform:translateX(-50%);background:#1E1B4B;color:white;padding:12px 20px;border-radius:10px;font-size:14px;z-index:9999;box-shadow:0 4px 20px rgba(0,0,0,.25);max-width:460px;text-align:center;transition:opacity 0.3s';
        document.body.appendChild(toast);
    }
    toast.textContent = msg;
    toast.style.opacity = '1';
    clearTimeout(toast._timer);
    toast._timer = setTimeout(() => { toast.style.opacity = '0'; }, 4000);
}

/* ── Keyboard shortcuts ── */
document.addEventListener('keydown', e => {
    if ((e.ctrlKey || e.metaKey) && e.key === 's') {
        e.preventDefault();
        clearTimeout(saveTimer);
        save();
    }
});

/* ── Start ── */
init();
