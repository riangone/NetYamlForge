// Content script: AI assistant sidebar for BizDocs forms
(function () {
  'use strict';
  if (document.getElementById('bizdocs-ai-host')) return;

  // Set by initUI — allows the message listener to trigger the panel directly
  let _toggleSidebar = null;

  // ─── Field Detection ────────────────────────────────────────────────────────

  function getFormFields() {
    const form = document.querySelector('form');
    if (!form) return [];

    const fields = [];
    const seen = new Set();

    // Standard inputs, selects, textareas
    form.querySelectorAll(
      'input:not([type=hidden]):not([name^="_"]):not([name="__RequestVerificationToken"]), select[name], textarea[name]'
    ).forEach(el => {
      const name = el.getAttribute('name');
      if (!name || seen.has(name)) return;
      seen.add(name);

      const field = {
        name,
        label: resolveLabel(el, form),
        type: el.tagName === 'SELECT' ? 'select' : el.tagName === 'TEXTAREA' ? 'textarea' : (el.type || 'text'),
        required: el.required,
        element: el,
      };

      if (el.tagName === 'SELECT') {
        field.options = Array.from(el.options)
          .filter(o => o.value !== '')
          .map(o => ({ value: o.value, text: o.textContent.trim() }));
      }

      fields.push(field);
    });

    // Entity picker fields: hidden input#picker-value-{name} + display input#picker-display-{name}
    document.querySelectorAll('input[id^="picker-value-"]').forEach(el => {
      const name = el.id.replace('picker-value-', '');
      if (seen.has(name)) return;
      seen.add(name);

      const displayEl = document.getElementById(`picker-display-${name}`);
      fields.push({
        name,
        label: displayEl ? resolveLabel(displayEl, document) : name,
        type: 'picker',
        required: false,
        element: el,
        displayElement: displayEl,
      });
    });

    return fields;
  }

  function resolveLabel(el, root) {
    if (el.id) {
      const lbl = (root.querySelector || document.querySelector.bind(document))
        .call(root, `label[for="${el.id}"]`);
      if (lbl) return lbl.textContent.replace(/[*：:]/g, '').trim();
    }
    const parentLbl = el.closest('label');
    if (parentLbl) return parentLbl.textContent.replace(/[*：:]/g, '').trim();

    let node = el.parentElement;
    for (let i = 0; i < 5 && node; i++, node = node.parentElement) {
      const lbl = node.querySelector('label');
      if (lbl) return lbl.textContent.replace(/[*：:]/g, '').trim();
      if (node.tagName === 'FORM') break;
    }
    return el.placeholder || el.name;
  }

  // ─── Form Filling ────────────────────────────────────────────────────────────

  function fillFields(fieldData, fields) {
    const results = [];

    for (const [name, value] of Object.entries(fieldData)) {
      if (value === null || value === undefined) continue;

      const field = fields.find(f => f.name === name);
      if (!field) { results.push({ name, value, success: false, error: 'field not found' }); continue; }

      try {
        if (field.type === 'picker') {
          applyPicker(field, value);
        } else if (field.type === 'select') {
          applySelect(field.element, value);
        } else if (field.type === 'checkbox') {
          field.element.checked = Boolean(value);
          field.element.dispatchEvent(new Event('change', { bubbles: true }));
        } else {
          field.element.value = String(value);
          field.element.dispatchEvent(new Event('input', { bubbles: true }));
          field.element.dispatchEvent(new Event('change', { bubbles: true }));
        }
        results.push({ name, value, success: true });
      } catch (e) {
        results.push({ name, value, success: false, error: e.message });
      }
    }

    return results;
  }

  function applyPicker(field, value) {
    if (typeof value === 'object' && value !== null) {
      field.element.value = String(value.id ?? '');
      if (field.displayElement) field.displayElement.value = String(value.display ?? value.name ?? '');
    } else {
      field.element.value = String(value);
      if (field.displayElement) field.displayElement.value = String(value);
    }
    field.element.dispatchEvent(new Event('change', { bubbles: true }));
    if (field.displayElement) field.displayElement.dispatchEvent(new Event('change', { bubbles: true }));
  }

  function applySelect(selectEl, value) {
    const str = String(value).toLowerCase();
    const opts = Array.from(selectEl.options);
    const match = opts.find(o => o.value.toLowerCase() === str) ||
                  opts.find(o => o.textContent.trim().toLowerCase() === str);
    if (match) {
      selectEl.value = match.value;
      selectEl.dispatchEvent(new Event('change', { bubbles: true }));
    }
  }

  // ─── Messaging ───────────────────────────────────────────────────────────────

  function sendToBackground(type, data) {
    return new Promise((resolve, reject) => {
      chrome.runtime.sendMessage({ type, data }, response => {
        if (chrome.runtime.lastError) { reject(new Error(chrome.runtime.lastError.message)); return; }
        if (!response) { reject(new Error('No response from background')); return; }
        resolve(response);
      });
    });
  }

  async function runAIFill(instruction) {
    const fields = getFormFields();
    if (!fields.length) throw new Error('No form fields detected on this page');

    const response = await sendToBackground('AI_FILL_FORM', {
      instruction,
      pageUrl: window.location.href,
      fields: fields.map(({ name, label, type, required, options }) => ({ name, label, type, required, options })),
    });

    if (!response.success) throw new Error(response.error);
    return { fieldData: response.fieldData, results: fillFields(response.fieldData, fields) };
  }

  async function runFileProcess(fileContent, fileName, fileType, instruction) {
    const fields = getFormFields();
    if (!fields.length) throw new Error('No form fields detected on this page');

    const response = await sendToBackground('AI_PROCESS_FILE', {
      fileContent, fileName, fileType, instruction,
      fields: fields.map(({ name, label, type, required }) => ({ name, label, type, required })),
    });

    if (!response.success) throw new Error(response.error);
    return { fieldData: response.fieldData, results: fillFields(response.fieldData, fields) };
  }

  // ─── UI ──────────────────────────────────────────────────────────────────────

  function buildSidebarHTML() {
    return `
<style>
* { box-sizing: border-box; margin: 0; padding: 0; }

#ai-toggle {
  position: absolute; bottom: 80px; right: 16px;
  width: 52px; height: 52px; border-radius: 50%;
  background: linear-gradient(135deg, #6366f1, #8b5cf6);
  border: none; cursor: pointer;
  display: flex; align-items: center; justify-content: center;
  box-shadow: 0 4px 20px rgba(99,102,241,.5);
  transition: all .2s; z-index: 2; font-size: 24px;
  pointer-events: auto;
}
#ai-toggle:hover { transform: scale(1.1); box-shadow: 0 6px 28px rgba(99,102,241,.7); }
#ai-toggle.open { right: 396px; background: linear-gradient(135deg,#ef4444,#dc2626); }
.spin { display:none; width:24px; height:24px; border:3px solid rgba(255,255,255,.3);
  border-top-color:#fff; border-radius:50%; animation: rot .7s linear infinite; }
@keyframes rot { to { transform:rotate(360deg); } }
#ai-toggle.busy .ic { display:none; }
#ai-toggle.busy .spin { display:block; }

#ai-panel {
  position: absolute; top: 0; right: -400px; width: 380px; height: 100%;
  background: #13131f; border-left: 1px solid #252540;
  display: flex; flex-direction: column;
  transition: right .3s cubic-bezier(.4,0,.2,1);
  box-shadow: -8px 0 40px rgba(0,0,0,.6); overflow: hidden;
  z-index: 1; pointer-events: auto;
}
#ai-panel.open { right: 0; }

.hdr {
  background: linear-gradient(135deg, #4f46e5, #7c3aed);
  padding: 13px 14px; display: flex; align-items: center; gap: 10px; flex-shrink: 0;
}
.hdr-logo { font-size: 22px; }
.hdr-info { flex:1; }
.hdr-title { color:#fff; font-size:15px; font-weight:600; font-family:system-ui,sans-serif; }
.hdr-sub { color:rgba(255,255,255,.65); font-size:11px; font-family:system-ui,sans-serif; }
.hdr-close {
  background:rgba(255,255,255,.15); border:none; color:#fff;
  width:28px; height:28px; border-radius:50%; cursor:pointer;
  display:flex; align-items:center; justify-content:center; font-size:15px; transition:background .2s;
}
.hdr-close:hover { background:rgba(255,255,255,.28); }

.status {
  padding: 5px 13px; background: #1a1a2e; font-size: 11px;
  display: flex; align-items: center; gap: 6px;
  border-bottom: 1px solid #252540; flex-shrink: 0; font-family:system-ui,sans-serif;
}
.dot { width:7px; height:7px; border-radius:50%; background:#4b5563; }
.dot.ok { background:#10b981; }
.dot.err { background:#ef4444; }
.status-txt { color:#9ca3af; }

.tabs { display:flex; background:#1a1a2e; border-bottom:1px solid #252540; flex-shrink:0; }
.tab {
  flex:1; padding:9px 4px; background:none; border:none; color:#6b7280; font-size:12px;
  cursor:pointer; border-bottom:2px solid transparent; transition:all .2s;
  display:flex; align-items:center; justify-content:center; gap:4px; font-family:system-ui,sans-serif;
}
.tab:hover { color:#a5b4fc; background:rgba(99,102,241,.06); }
.tab.active { color:#818cf8; border-bottom-color:#6366f1; background:rgba(99,102,241,.09); }

.panels { flex:1; overflow:hidden; position:relative; }
.panel { position:absolute; inset:0; display:none; flex-direction:column; }
.panel.active { display:flex; }

/* Chat */
.msgs {
  flex:1; overflow-y:auto; padding:12px; display:flex; flex-direction:column; gap:8px;
  scrollbar-width:thin; scrollbar-color:#252540 transparent;
}
.msgs::-webkit-scrollbar { width:4px; }
.msgs::-webkit-scrollbar-thumb { background:#252540; border-radius:2px; }

.msg {
  max-width:90%; padding:8px 12px; border-radius:10px; font-size:13px; line-height:1.5;
  word-break:break-word; font-family:system-ui,sans-serif;
}
.msg.u { align-self:flex-end; background:#4f46e5; color:#fff; border-bottom-right-radius:2px; }
.msg.a {
  align-self:flex-start; background:#1e1e3a; color:#e2e8f0;
  border:1px solid #252540; border-bottom-left-radius:2px;
}
.msg.sys {
  align-self:center; background:#1a1a2e; color:#6b7280; font-size:11px;
  padding:3px 10px; border-radius:20px; border:1px solid #252540; max-width:100%;
}
.msg.err {
  align-self:center; background:rgba(239,68,68,.1); color:#ef4444;
  border:1px solid rgba(239,68,68,.3); font-size:12px; max-width:100%;
}
.fill-results { margin-top:6px; font-size:11px; }
.ri { display:flex; align-items:center; gap:3px; padding:1px 0; }
.ri.ok { color:#10b981; }
.ri.fail { color:#ef4444; }

.chat-foot { padding:10px 12px; background:#1a1a2e; border-top:1px solid #252540; flex-shrink:0; }
.chat-row { display:flex; gap:8px; align-items:flex-end; }
.chat-in {
  flex:1; background:#0d0d1a; border:1px solid #252540; border-radius:8px; color:#e2e8f0;
  font-size:13px; padding:8px 10px; resize:none; min-height:38px; max-height:120px;
  overflow-y:auto; transition:border-color .2s; font-family:system-ui,sans-serif; line-height:1.5;
}
.chat-in:focus { outline:none; border-color:#6366f1; }
.chat-in::placeholder { color:#374151; }
.send-btn {
  background:#4f46e5; border:none; color:#fff; width:38px; height:38px;
  border-radius:8px; cursor:pointer; display:flex; align-items:center; justify-content:center;
  font-size:16px; transition:background .2s; flex-shrink:0;
}
.send-btn:hover { background:#4338ca; }
.send-btn:disabled { background:#252540; cursor:not-allowed; }

/* Upload */
.up-wrap {
  flex:1; padding:14px; display:flex; flex-direction:column; gap:12px;
  overflow-y:auto; scrollbar-width:thin; scrollbar-color:#252540 transparent;
}
.up-wrap::-webkit-scrollbar { width:4px; }
.up-wrap::-webkit-scrollbar-thumb { background:#252540; border-radius:2px; }

.drop {
  border:2px dashed #252540; border-radius:10px; padding:28px 14px; text-align:center;
  color:#6b7280; cursor:pointer; transition:all .2s; background:#1a1a2e;
  font-family:system-ui,sans-serif;
}
.drop:hover, .drop.over { border-color:#6366f1; background:rgba(99,102,241,.06); color:#a5b4fc; }
.drop-icon { font-size:34px; margin-bottom:8px; }
.drop-txt { font-size:13px; }
.drop-hint { font-size:11px; color:#374151; margin-top:4px; }
#file-inp { display:none; }

.file-card {
  background:#1e1e3a; border:1px solid #252540; border-radius:8px;
  padding:10px 12px; display:none; align-items:center; gap:10px;
}
.file-card.show { display:flex; }
.fc-icon { font-size:22px; }
.fc-info { flex:1; min-width:0; font-family:system-ui,sans-serif; }
.fc-name { font-size:13px; color:#e2e8f0; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
.fc-size { font-size:11px; color:#6b7280; }
.fc-rm { background:none; border:none; color:#6b7280; cursor:pointer; font-size:20px; padding:0 2px; transition:color .2s; }
.fc-rm:hover { color:#ef4444; }

.up-note { background:#1a1a2e; border:1px solid #252540; border-radius:8px; padding:10px; }
.up-note label { display:block; font-size:11px; color:#6b7280; margin-bottom:5px; text-transform:uppercase; letter-spacing:.5px; font-family:system-ui,sans-serif; }
.up-note textarea {
  width:100%; background:#0d0d1a; border:1px solid #252540; border-radius:6px; color:#e2e8f0;
  font-size:12px; padding:7px; resize:none; height:56px; font-family:system-ui,sans-serif; transition:border-color .2s;
}
.up-note textarea:focus { outline:none; border-color:#6366f1; }

.ext-btn {
  background: linear-gradient(135deg,#059669,#10b981); border:none; color:#fff;
  padding:10px; border-radius:8px; cursor:pointer; font-size:13px; font-weight:500;
  width:100%; transition:opacity .2s; font-family:system-ui,sans-serif;
}
.ext-btn:hover { opacity:.88; }
.ext-btn:disabled { background:#252540; cursor:not-allowed; opacity:1; }
.up-status { font-size:12px; color:#6b7280; text-align:center; min-height:18px; font-family:system-ui,sans-serif; }

/* Fields */
.flds-wrap {
  flex:1; overflow-y:auto; padding:12px;
  scrollbar-width:thin; scrollbar-color:#252540 transparent;
}
.flds-wrap::-webkit-scrollbar { width:4px; }
.flds-wrap::-webkit-scrollbar-thumb { background:#252540; border-radius:2px; }
.flds-empty { text-align:center; color:#374151; font-size:12px; padding:40px 16px; font-family:system-ui,sans-serif; }
.flds-hdr { font-size:11px; color:#374151; margin-bottom:8px; text-transform:uppercase; letter-spacing:.5px; font-family:system-ui,sans-serif; }
.fi {
  background:#1a1a2e; border:1px solid #252540; border-radius:6px;
  padding:8px 10px; margin-bottom:6px; font-family:system-ui,sans-serif;
}
.fi-name { color:#a5b4fc; font-weight:500; font-family:monospace; font-size:11px; }
.fi-label { color:#e2e8f0; font-size:12px; margin-top:1px; }
.fi-meta { display:flex; gap:5px; margin-top:3px; }
.badge { border-radius:3px; padding:1px 5px; font-size:10px; }
.badge-type { background:rgba(99,102,241,.18); color:#a5b4fc; }
.badge-req { background:rgba(239,68,68,.15); color:#f87171; }
</style>

<button id="ai-toggle" title="BizDocs AI Assistant">
  <span class="ic">🤖</span>
  <div class="spin"></div>
</button>

<div id="ai-panel">
  <div class="hdr">
    <span class="hdr-logo">🤖</span>
    <div class="hdr-info">
      <div class="hdr-title">BizDocs AI</div>
      <div class="hdr-sub">powered by antigravity</div>
    </div>
    <button class="hdr-close" id="ai-close">✕</button>
  </div>

  <div class="status">
    <div class="dot" id="s-dot"></div>
    <span class="status-txt" id="s-txt">Checking connection…</span>
  </div>

  <div class="tabs">
    <button class="tab active" data-tab="chat">💬 Chat</button>
    <button class="tab" data-tab="upload">📎 Upload</button>
    <button class="tab" data-tab="fields">📋 Fields</button>
  </div>

  <div class="panels">

    <!-- Chat -->
    <div class="panel active" id="p-chat">
      <div class="msgs" id="msgs">
        <div class="msg sys">BizDocs AI ready · type an instruction to fill the form.</div>
      </div>
      <div class="chat-foot">
        <div class="chat-row">
          <textarea class="chat-in" id="chat-in" placeholder="e.g. Fill invoice for Shanghai Trading, ¥500,000, due in 30 days" rows="1"></textarea>
          <button class="send-btn" id="send-btn">➤</button>
        </div>
      </div>
    </div>

    <!-- Upload -->
    <div class="panel" id="p-upload">
      <div class="up-wrap">
        <div class="drop" id="drop-zone">
          <div class="drop-icon">📄</div>
          <div class="drop-txt">Drop a file or click to browse</div>
          <div class="drop-hint">PDF · JPG/PNG · CSV · TXT · Excel · Word</div>
        </div>
        <input type="file" id="file-inp" accept=".pdf,.jpg,.jpeg,.png,.gif,.csv,.txt,.xlsx,.xls,.docx,.doc">
        <div class="file-card" id="file-card">
          <span class="fc-icon" id="fc-icon">📄</span>
          <div class="fc-info">
            <div class="fc-name" id="fc-name"></div>
            <div class="fc-size" id="fc-size"></div>
          </div>
          <button class="fc-rm" id="fc-rm" title="Remove">×</button>
        </div>
        <div class="up-note">
          <label>Additional instruction (optional)</label>
          <textarea id="up-instr" placeholder="e.g. Extract customer name, invoice number, and total amount"></textarea>
        </div>
        <button class="ext-btn" id="ext-btn" disabled>🔍 Extract &amp; Fill Form</button>
        <div class="up-status" id="up-status"></div>
      </div>
    </div>

    <!-- Fields -->
    <div class="panel" id="p-fields">
      <div class="flds-wrap" id="flds-list">
        <div class="flds-empty">Detecting form fields…</div>
      </div>
    </div>

  </div>
</div>`;
  }

  function initUI(shadow) {
    const toggle  = shadow.getElementById('ai-toggle');
    const panel   = shadow.getElementById('ai-panel');
    const closeB  = shadow.getElementById('ai-close');
    const sDot    = shadow.getElementById('s-dot');
    const sTxt    = shadow.getElementById('s-txt');
    const msgs    = shadow.getElementById('msgs');
    const chatIn  = shadow.getElementById('chat-in');
    const sendBtn = shadow.getElementById('send-btn');
    const dropZ   = shadow.getElementById('drop-zone');
    const fileInp = shadow.getElementById('file-inp');
    const fileCard= shadow.getElementById('file-card');
    const fcIcon  = shadow.getElementById('fc-icon');
    const fcName  = shadow.getElementById('fc-name');
    const fcSize  = shadow.getElementById('fc-size');
    const fcRm    = shadow.getElementById('fc-rm');
    const upInstr = shadow.getElementById('up-instr');
    const extBtn  = shadow.getElementById('ext-btn');
    const upStatus= shadow.getElementById('up-status');
    const fldsLst = shadow.getElementById('flds-list');

    let isOpen = false;
    let currentFile = null;

    // ── Sidebar toggle ──────────────────────────────────────────────────────
    function openSidebar() {
      isOpen = true;
      panel.classList.add('open');
      toggle.classList.add('open');
      checkHost();
      refreshFields();
    }
    function closeSidebar() {
      isOpen = false;
      panel.classList.remove('open');
      toggle.classList.remove('open');
    }

    _toggleSidebar = () => isOpen ? closeSidebar() : openSidebar();

    toggle.addEventListener('click', _toggleSidebar);
    closeB.addEventListener('click', closeSidebar);

    // ── Tabs ────────────────────────────────────────────────────────────────
    shadow.querySelectorAll('.tab').forEach(btn => {
      btn.addEventListener('click', () => {
        shadow.querySelectorAll('.tab').forEach(b => b.classList.toggle('active', b === btn));
        shadow.querySelectorAll('.panel').forEach(p => p.classList.toggle('active', p.id === `p-${btn.dataset.tab}`));
        if (btn.dataset.tab === 'fields') refreshFields();
      });
    });

    // ── Native host status ──────────────────────────────────────────────────
    function checkHost() {
      sDot.className = 'dot';
      sTxt.textContent = 'Connecting…';
      chrome.runtime.sendMessage({ type: 'CHECK_NATIVE_HOST' }, res => {
        if (chrome.runtime.lastError || !res) {
          sDot.className = 'dot err';
          sTxt.textContent = 'Native host not found — run install.sh';
          return;
        }
        sDot.className = res.available ? 'dot ok' : 'dot err';
        sTxt.textContent = res.available
          ? 'Connected · antigravity CLI ready'
          : 'Native host not installed — see README';
      });
    }

    // ── Chat ────────────────────────────────────────────────────────────────
    function addMsg(cls, text) {
      const el = document.createElement('div');
      el.className = `msg ${cls}`;
      el.textContent = text;
      msgs.appendChild(el);
      msgs.scrollTop = msgs.scrollHeight;
      return el;
    }

    function showResults(parent, results) {
      if (!results.length) return;
      const div = document.createElement('div');
      div.className = 'fill-results';
      results.forEach(r => {
        const row = document.createElement('div');
        row.className = `ri ${r.success ? 'ok' : 'fail'}`;
        const val = (typeof r.value === 'object' ? JSON.stringify(r.value) : String(r.value)).substring(0, 35);
        row.textContent = `${r.success ? '✓' : '✗'} ${r.name}: ${val}`;
        div.appendChild(row);
      });
      parent.appendChild(div);
      msgs.scrollTop = msgs.scrollHeight;
    }

    function doChat() {
      const text = chatIn.value.trim();
      if (!text) return;

      addMsg('u', text);
      chatIn.value = '';
      chatIn.style.height = 'auto';
      sendBtn.disabled = true;
      toggle.classList.add('busy');

      const thinking = addMsg('a', '⏳ Thinking…');

      runAIFill(text)
        .then(({ results }) => {
          thinking.remove();
          const ok = results.filter(r => r.success).length;
          const m = addMsg('a', `✓ Filled ${ok} field${ok !== 1 ? 's' : ''}`);
          showResults(m, results);
        })
        .catch(err => { thinking.remove(); addMsg('err', `❌ ${err.message}`); })
        .finally(() => { sendBtn.disabled = false; toggle.classList.remove('busy'); });
    }

    sendBtn.addEventListener('click', doChat);
    chatIn.addEventListener('keydown', e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); doChat(); } });
    chatIn.addEventListener('input', () => {
      chatIn.style.height = 'auto';
      chatIn.style.height = Math.min(chatIn.scrollHeight, 120) + 'px';
    });

    // ── File upload ─────────────────────────────────────────────────────────
    const FILE_ICONS = { pdf:'📕', jpg:'🖼️', jpeg:'🖼️', png:'🖼️', gif:'🖼️', csv:'📊', xlsx:'📊', xls:'📊', txt:'📝', docx:'📝', doc:'📝' };

    function setFile(file) {
      currentFile = file;
      const ext = file.name.split('.').pop().toLowerCase();
      fcIcon.textContent = FILE_ICONS[ext] || '📄';
      fcName.textContent = file.name;
      const kb = file.size / 1024;
      fcSize.textContent = kb < 1024 ? `${kb.toFixed(1)} KB` : `${(kb/1024).toFixed(1)} MB`;
      fileCard.classList.add('show');
      extBtn.disabled = false;
      upStatus.textContent = '';
    }

    function clearFile() {
      currentFile = null;
      fileCard.classList.remove('show');
      fileInp.value = '';
      extBtn.disabled = true;
      upStatus.textContent = '';
    }

    dropZ.addEventListener('click', () => fileInp.click());
    dropZ.addEventListener('dragover', e => { e.preventDefault(); dropZ.classList.add('over'); });
    dropZ.addEventListener('dragleave', () => dropZ.classList.remove('over'));
    dropZ.addEventListener('drop', e => { e.preventDefault(); dropZ.classList.remove('over'); if (e.dataTransfer.files[0]) setFile(e.dataTransfer.files[0]); });
    fileInp.addEventListener('change', () => { if (fileInp.files[0]) setFile(fileInp.files[0]); });
    fcRm.addEventListener('click', clearFile);

    extBtn.addEventListener('click', async () => {
      if (!currentFile) return;
      extBtn.disabled = true;
      upStatus.textContent = '⏳ Reading file…';
      toggle.classList.add('busy');

      try {
        const { content, type } = await readFile(currentFile);
        upStatus.textContent = '🤖 Analyzing with AI…';
        const result = await runFileProcess(content, currentFile.name, type, upInstr.value.trim());

        const ok = result.results.filter(r => r.success).length;
        upStatus.textContent = `✅ Filled ${ok} field${ok !== 1 ? 's' : ''}`;

        // Switch to chat tab and show results
        shadow.querySelectorAll('.tab').forEach(b => b.classList.toggle('active', b.dataset.tab === 'chat'));
        shadow.querySelectorAll('.panel').forEach(p => p.classList.toggle('active', p.id === 'p-chat'));

        addMsg('sys', `📎 Processed: ${currentFile.name}`);
        const m = addMsg('a', `✓ Extracted data from file, filled ${ok} fields`);
        showResults(m, result.results);
      } catch (err) {
        upStatus.textContent = `❌ ${err.message}`;
      } finally {
        extBtn.disabled = !currentFile;
        toggle.classList.remove('busy');
      }
    });

    function readFile(file) {
      return new Promise((resolve, reject) => {
        const reader = new FileReader();
        const isImage = file.type.startsWith('image/');
        const isPDF = file.type === 'application/pdf';

        reader.onerror = () => reject(new Error('Failed to read file'));
        reader.onload = () => resolve({
          content: reader.result,
          type: isImage ? 'image' : isPDF ? 'pdf' : 'text',
        });

        if (isImage || isPDF) { reader.readAsDataURL(file); }
        else { reader.readAsText(file, 'UTF-8'); }
      });
    }

    // ── Fields tab ──────────────────────────────────────────────────────────
    function refreshFields() {
      const fields = getFormFields();
      if (!fields.length) {
        fldsLst.innerHTML = '<div class="flds-empty">No form fields found on this page.</div>';
        return;
      }

      fldsLst.innerHTML = `<div class="flds-hdr">${fields.length} field${fields.length !== 1 ? 's' : ''} detected</div>`;
      fields.forEach(f => {
        const el = document.createElement('div');
        el.className = 'fi';
        const badges = [`<span class="badge badge-type">${f.type}</span>`];
        if (f.required) badges.push('<span class="badge badge-req">required</span>');
        if (f.options?.length) badges.push(`<span class="badge badge-type">${f.options.length} opts</span>`);
        el.innerHTML = `
          <div class="fi-name">${f.name}</div>
          <div class="fi-label">${f.label}</div>
          <div class="fi-meta">${badges.join('')}</div>`;
        fldsLst.appendChild(el);
      });
    }

    // Kick off host check after short delay (service worker may be cold-starting)
    setTimeout(checkHost, 800);
  }

  // ─── Bootstrap ───────────────────────────────────────────────────────────────

  function mount() {
    const host = document.createElement('div');
    host.id = 'bizdocs-ai-host';
    Object.assign(host.style, {
      position: 'fixed', top: '0', left: '0',
      width: '100vw', height: '100vh',
      zIndex: '2147483647', pointerEvents: 'none',
    });
    document.body.appendChild(host);

    const shadow = host.attachShadow({ mode: 'open' });
    shadow.innerHTML = buildSidebarHTML();

    initUI(shadow);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', mount);
  } else {
    mount();
  }

  // Allow popup button to toggle the sidebar
  chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
    if (msg.type === 'TOGGLE_ASSISTANT') {
      if (_toggleSidebar) {
        _toggleSidebar();
        sendResponse({ ok: true });
      } else {
        sendResponse({ ok: false, error: 'Panel not mounted yet' });
      }
    }
    return true;
  });
})();
