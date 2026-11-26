// Minimal service worker: caches the app shell for fast start-up. API and hub calls are never cached.
const CACHE = 'lca-shell-v1'

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(CACHE).then((cache) => cache.addAll(['/', '/manifest.webmanifest', '/icon.svg'])))
  self.skipWaiting()
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim()),
  )
})

self.addEventListener('fetch', (event) => {
  const request = event.request
  const url = new URL(request.url)
  if (request.method !== 'GET' || url.origin !== self.location.origin) return

  // Network first, falling back to cache (navigation falls back to the cached shell).
  event.respondWith(
    fetch(request)
      .then((response) => {
        if (response.ok && (request.mode === 'navigate' || url.pathname.startsWith('/assets/'))) {
          const copy = response.clone()
          void caches.open(CACHE).then((cache) => cache.put(request.mode === 'navigate' ? '/' : request, copy))
        }
        return response
      })
      .catch(() => caches.match(request.mode === 'navigate' ? '/' : request).then((r) => r ?? Response.error())),
  )
})
