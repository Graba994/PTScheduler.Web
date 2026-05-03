const CACHE = 'ptscheduler-v2';
const PRECACHE = ['/', '/offline.html', '/favicon.png', '/manifest.json'];

self.addEventListener('install', e => {
    e.waitUntil(
        caches.open(CACHE).then(c => c.addAll(PRECACHE))
    );
    self.skipWaiting();
});

self.addEventListener('activate', e => {
    e.waitUntil(
        caches.keys().then(keys =>
            Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))
        )
    );
    self.clients.claim();
});

self.addEventListener('fetch', e => {
    const { request } = e;
    const url = new URL(request.url);

    // Skip non-GET, cross-origin, SignalR negotiation, and Blazor hot-reload
    if (request.method !== 'GET') return;
    if (url.origin !== self.location.origin) return;
    if (url.pathname.startsWith('/_blazor')) return;
    if (url.pathname.startsWith('/negotiate')) return;

    // Navigation requests: network-first, fallback to offline page
    if (request.mode === 'navigate') {
        e.respondWith(
            fetch(request)
                .then(res => {
                    const clone = res.clone();
                    caches.open(CACHE).then(c => c.put(request, clone));
                    return res;
                })
                .catch(() => caches.match('/offline.html'))
        );
        return;
    }

    // Static assets: cache-first for known extensions, network-first for everything else
    const isStatic = /\.(css|js|png|jpg|jpeg|gif|svg|ico|woff2?|ttf)(\?.*)?$/.test(url.pathname);
    if (isStatic) {
        e.respondWith(
            caches.match(request).then(cached => {
                if (cached) return cached;
                return fetch(request).then(res => {
                    const clone = res.clone();
                    caches.open(CACHE).then(c => c.put(request, clone));
                    return res;
                });
            })
        );
        return;
    }

    // Everything else: network-first, cache fallback
    e.respondWith(
        fetch(request)
            .then(res => {
                const clone = res.clone();
                caches.open(CACHE).then(c => c.put(request, clone));
                return res;
            })
            .catch(() => caches.match(request))
    );
});

// Notify clients when a new SW is waiting
self.addEventListener('message', e => {
    if (e.data === 'skipWaiting') self.skipWaiting();
});
