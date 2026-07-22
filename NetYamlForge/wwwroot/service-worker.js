// 解析路径参数以支持子目录部署 (PathBase)
const urlParams = new URLSearchParams(self.location.search);
const pathBase = (urlParams.get('pathBase') || '').trim().replace(/\/+$/, '');

const CACHE_NAME = 'netyamlforge-cache-v1';
const OFFLINE_URL = `${pathBase}/offline.html`;

const ASSETS_TO_CACHE = [
  OFFLINE_URL,
  `${pathBase}/manifest.json`,
  `${pathBase}/favicon.ico`,
  `${pathBase}/images/icon-192.png`,
  `${pathBase}/images/icon-512.png`,
  `${pathBase}/images/icon-192-maskable.png`,
  `${pathBase}/images/icon-512-maskable.png`,
  `${pathBase}/css/site.css`,
  `${pathBase}/NetYamlForge.styles.css`,
  `${pathBase}/lib/daisyui/daisyui.min.css`,
  `${pathBase}/lib/tailwindcss/browser.min.js`
];

// 安装阶段：预缓存核心资源
self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => {
      console.log('[Service Worker] Pre-caching offline page and assets dynamically');
      // 逐个缓存，确保即使某个文件（如 CSS 隔离样式包）不存在，核心离线页面和 manifest 依然能缓存成功
      const cachePromises = ASSETS_TO_CACHE.map((url) => {
        return cache.add(url).catch((err) => {
          console.warn(`[Service Worker] Optional asset failed to cache (ignoring): ${url}`, err);
        });
      });
      return Promise.all(cachePromises);
    }).then(() => self.skipWaiting())
  );
});

// 激活阶段：清理旧缓存
self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then((cacheNames) => {
      return Promise.all(
        cacheNames.map((cacheName) => {
          if (cacheName !== CACHE_NAME) {
            console.log('[Service Worker] Deleting old cache:', cacheName);
            return caches.delete(cacheName);
          }
        })
      );
    }).then(() => self.clients.claim())
  );
});

// 接收后端推送的 Web Push 消息并展示系统通知
// Payload 格式（见 PushOutboxPoller.cs）: { "title": "...", "body": "...", "url": "..." }
self.addEventListener('push', (event) => {
  let data = { title: 'NetYamlForge', body: '' };
  if (event.data) {
    try {
      data = event.data.json();
    } catch (err) {
      data.body = event.data.text();
    }
  }

  const title = data.title || 'NetYamlForge';
  const options = {
    body: data.body || '',
    icon: `${pathBase}/images/icon-192.png`,
    badge: `${pathBase}/images/icon-192.png`,
    data: { url: data.url ? (data.url.startsWith('http') ? data.url : `${pathBase}${data.url}`) : `${pathBase}/` }
  };

  event.waitUntil(self.registration.showNotification(title, options));
});

// 通知点击：聚焦已打开的窗口，无则新开一个，并跳转到通知携带的 URL
self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const targetUrl = (event.notification.data && event.notification.data.url) || `${pathBase}/`;

  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
      for (const client of clientList) {
        if (client.url === targetUrl && 'focus' in client) {
          return client.focus();
        }
      }
      if (self.clients.openWindow) {
        return self.clients.openWindow(targetUrl);
      }
    })
  );
});

// 拦截请求并实施缓存策略
self.addEventListener('fetch', (event) => {
  // 仅拦截 GET 请求
  if (event.request.method !== 'GET') {
    return;
  }

  const url = new URL(event.request.url);

  // 忽略一些不应缓存的请求，比如 C# API 请求、后端管理路由等非静态或不宜离线的 API
  if (url.pathname.startsWith('/api/') || url.pathname.includes('/Account/')) {
    return;
  }

  // 仅缓存同源资源，以及特定 CDN 资源 (如 Google Fonts, EasyMDE 等)
  const isAllowedOrigin = url.origin === self.location.origin || 
                          url.origin.includes('unpkg.com') || 
                          url.origin.includes('fonts.googleapis.com') || 
                          url.origin.includes('fonts.gstatic.com');
  
  if (!isAllowedOrigin) {
    return;
  }

  // 网页导航请求 (HTML 页面) - 使用 Network-First 策略
  if (event.request.mode === 'navigate') {
    event.respondWith(
      fetch(event.request)
        .then((response) => {
          // 只有正常的网页响应才缓存
          if (response.status === 200) {
            const responseCopy = response.clone();
            caches.open(CACHE_NAME).then((cache) => {
              cache.put(event.request, responseCopy);
            });
          }
          return response;
        })
        .catch(() => {
          // 如果网络请求失败，尝试从缓存中获取该页
          return caches.match(event.request).then((cachedResponse) => {
            if (cachedResponse) {
              return cachedResponse;
            }
            // 缓存未命中，返回离线 fallback 页面
            return caches.match(OFFLINE_URL);
          });
        })
    );
    return;
  }

  // 静态资源请求 (CSS, JS, 字体, 图片等) - 使用 Stale-While-Revalidate 策略
  event.respondWith(
    caches.match(event.request).then((cachedResponse) => {
      if (cachedResponse) {
        // 命中缓存，后台异步请求更新缓存
        fetch(event.request).then((networkResponse) => {
          if (networkResponse.status === 200) {
            caches.open(CACHE_NAME).then((cache) => {
              cache.put(event.request, networkResponse);
            });
          }
        }).catch(() => {/* 忽略后台更新失败 */});
        
        return cachedResponse;
      }

      // 未命中缓存，正常网络请求并缓存
      return fetch(event.request).then((networkResponse) => {
        if (!networkResponse || networkResponse.status !== 200) {
          return networkResponse;
        }

        const responseToCache = networkResponse.clone();
        caches.open(CACHE_NAME).then((cache) => {
          cache.put(event.request, responseToCache);
        });

        return networkResponse;
      }).catch(() => {
        // 网络请求失败时的回退
        const acceptHeader = event.request.headers.get('accept');
        if (acceptHeader && acceptHeader.includes('image')) {
          // 可以选择返回一个默认的占位图
        }
      });
    })
  );
});
