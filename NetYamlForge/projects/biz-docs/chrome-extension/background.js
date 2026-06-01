// Service worker: bridges content scripts to antigravity CLI via native messaging

const NATIVE_HOST = 'com.bizdocs.aiassistant';
let nativePort = null;
const pendingRequests = new Map();
let requestIdCounter = 0;

function connectNative() {
  try {
    nativePort = chrome.runtime.connectNative(NATIVE_HOST);

    nativePort.onMessage.addListener((response) => {
      const { id, ...data } = response;
      const pending = pendingRequests.get(id);
      if (pending) {
        pendingRequests.delete(id);
        clearTimeout(pending.timer);
        pending.resolve(data);
      }
    });

    nativePort.onDisconnect.addListener(() => {
      const err = chrome.runtime.lastError?.message || 'Native host disconnected';
      nativePort = null;
      // Reject all pending requests
      for (const [id, pending] of pendingRequests) {
        clearTimeout(pending.timer);
        pending.reject(new Error(err));
      }
      pendingRequests.clear();
    });

    return true;
  } catch (e) {
    nativePort = null;
    return false;
  }
}

function callNative(type, payload, timeoutMs = 60000) {
  if (!nativePort && !connectNative()) {
    return Promise.reject(new Error(
      'Native host unavailable. Run install.sh to set up the antigravity host.'
    ));
  }

  const id = ++requestIdCounter;

  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      pendingRequests.delete(id);
      reject(new Error('antigravity timed out after 60s'));
    }, timeoutMs);

    pendingRequests.set(id, { resolve, reject, timer });

    try {
      nativePort.postMessage({ id, type, ...payload });
    } catch (e) {
      clearTimeout(timer);
      pendingRequests.delete(id);
      nativePort = null;
      reject(e);
    }
  });
}

function extractJSON(text) {
  // Extract first {...} or [...] block from AI output
  const match = text.match(/\{[\s\S]*\}/);
  if (!match) throw new Error(`No JSON found in AI response: ${text.substring(0, 200)}`);
  return JSON.parse(match[0]);
}

function buildFillPrompt(instruction, fields, pageUrl) {
  const today = new Date().toISOString().split('T')[0];
  const fieldLines = fields.map(f => {
    let line = `- ${f.name} (${f.type})`;
    if (f.label && f.label !== f.name) line += `: "${f.label}"`;
    if (f.options?.length) line += ` [options: ${f.options.map(o => o.value).join(', ')}]`;
    if (f.required) line += ' [required]';
    return line;
  }).join('\n');

  return `You are a form-filling assistant for BizDocs, a business document management system.
Page: ${pageUrl}
Today: ${today}

Form fields:
${fieldLines}

User instruction: "${instruction}"

Return ONLY a JSON object mapping field names to values. Rules:
- null = skip field, do not change
- Dates: ISO format YYYY-MM-DD
- Select fields: use the exact option value string
- Numbers: numeric type not string
- Foreign-key/picker fields (e.g. CustomerId): {"id": "<id>", "display": "<name>"} or just the name string
- Infer reasonable values (e.g. "due in 30 days" = ${new Date(Date.now() + 30*86400000).toISOString().split('T')[0]})

JSON only, no explanation.`;
}

function buildFilePrompt(fileContent, fileName, fileType, instruction, fields) {
  const today = new Date().toISOString().split('T')[0];
  const fieldLines = fields.map(f => {
    let line = `- ${f.name} (${f.type})`;
    if (f.label && f.label !== f.name) line += `: "${f.label}"`;
    return line;
  }).join('\n');

  const contentSection = fileType === 'image' || fileType === 'pdf'
    ? `[Binary file - base64 encoded, ${fileName}]\n${fileContent.substring(0, 500)}...`
    : `Document content:\n${fileContent.substring(0, 4000)}`;

  return `You are a data extraction assistant for BizDocs.
Today: ${today}
File: ${fileName}
${instruction ? `Additional instruction: "${instruction}"` : ''}

Form fields available:
${fieldLines}

${contentSection}

Extract relevant data from the document and map it to the form fields.
Return ONLY a JSON object. Rules:
- null = no data found for this field
- Dates: ISO format YYYY-MM-DD
- Numbers: numeric type

JSON only, no explanation.`;
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.type === 'CHECK_NATIVE_HOST') {
    const ok = connectNative();
    sendResponse({ available: ok });
    return false;
  }

  if (message.type === 'AI_FILL_FORM') {
    const { instruction, fields, pageUrl } = message.data;
    const prompt = buildFillPrompt(instruction, fields, pageUrl);

    callNative('chat', { prompt })
      .then(result => {
        if (!result.success) throw new Error(result.error || 'Native host error');
        const fieldData = extractJSON(result.output);
        sendResponse({ success: true, fieldData });
      })
      .catch(err => sendResponse({ success: false, error: err.message }));

    return true;
  }

  if (message.type === 'AI_PROCESS_FILE') {
    const { fileContent, fileName, fileType, instruction, fields } = message.data;
    const prompt = buildFilePrompt(fileContent, fileName, fileType, instruction, fields);

    callNative('chat', { prompt })
      .then(result => {
        if (!result.success) throw new Error(result.error || 'Native host error');
        const fieldData = extractJSON(result.output);
        sendResponse({ success: true, fieldData });
      })
      .catch(err => sendResponse({ success: false, error: err.message }));

    return true;
  }
});
