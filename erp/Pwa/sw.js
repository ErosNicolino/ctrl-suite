'use strict';
const VERSION = '2026.11.11.1';
const CACHE = 'cacp-shell-' + VERSION;
const SHELL = ['/app/', '/app/index.html', '/app/app.css', '/app/app.js', '/app/operational.js', '/app/actions.js', '/app/order-editor.js', '/app/order-finance.js', '/app/sale-editor.js', '/app/tools.js', '/app/manifest.webmanifest', '/app/brand.png', '/app/logotipo_2026.svg', '/app/icon-192.png', '/app/icon-512.png', '/app/icon-maskable-512.png', '/app/apple-touch-icon.png'];
self.addEventListener('install', event => event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(SHELL))));
self.addEventListener('activate', event => event.waitUntil((async () => {
  for (const key of await caches.keys()) if (key.startsWith('cacp-shell-') && key !== CACHE) await caches.delete(key);
  await self.clients.claim();
})()));
self.addEventListener('message', event => { if (event.data === 'ACTIVATE_UPDATE') self.skipWaiting(); });
self.addEventListener('fetch', event => {
  const request = event.request, url = new URL(request.url);
  // Exact public asset allowlist. API, auth, PDF and product photos are never intercepted/cached.
  if (request.method !== 'GET' || url.origin !== self.location.origin || url.search || !SHELL.includes(url.pathname)) return;
  event.respondWith((async () => {
    const cached = await caches.open(CACHE);
    if (request.mode === 'navigate') {
      try { return await fetch(request, {cache:'no-store'}); }
      catch { return await cached.match('/app/'); }
    }
    // Versioned shell stays coherent until a user accepts the waiting worker.
    return await cached.match(url.pathname) || fetch(request, {cache:'no-store'});
  })());
});
