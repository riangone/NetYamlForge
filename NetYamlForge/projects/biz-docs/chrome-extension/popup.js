const pageStatus = document.getElementById('page-status');
const nativeStatus = document.getElementById('native-status');
const mainContent = document.getElementById('main-content');
const notBizdocs = document.getElementById('not-bizdocs');
const openBtn = document.getElementById('open-assistant');
const installBtn = document.getElementById('install-guide');

async function init() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  const url = tab?.url || '';

  const isBizdocs = /\/(biz-docs|bizdocs)(\/|$)/i.test(url) ||
                    url.includes('localhost') || url.includes('127.0.0.1');

  if (!isBizdocs) {
    mainContent.style.display = 'none';
    notBizdocs.style.display = 'block';
    return;
  }

  pageStatus.textContent = 'BizDocs';
  pageStatus.className = 'badge badge-ok';

  // Check native host
  chrome.runtime.sendMessage({ type: 'CHECK_NATIVE_HOST' }, (resp) => {
    if (chrome.runtime.lastError || !resp?.available) {
      nativeStatus.textContent = '未安装';
      nativeStatus.className = 'badge badge-err';
    } else {
      nativeStatus.textContent = '已连接';
      nativeStatus.className = 'badge badge-ok';
    }
  });

  openBtn.addEventListener('click', async () => {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    chrome.tabs.sendMessage(tab.id, { type: 'TOGGLE_ASSISTANT' });
    window.close();
  });

  installBtn.addEventListener('click', () => {
    chrome.tabs.create({
      url: chrome.runtime.getURL('install-guide.html')
    });
  });
}

init();
