const CACHE = 'ptscheduler-v7';
const PRECACHE = [
    '/',
    '/offline.html',
    '/favicon.png',
    '/manifest.webmanifest',
    '/icons/icon-192.png',
    '/icons/icon-512.png',
];

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

    // Skip non-GET and cross-origin
    if (request.method !== 'GET') return;
    if (url.origin !== self.location.origin) return;

    // Skip Blazor circuit (SignalR hub + negotiation) — must always be network
    if (url.pathname.startsWith('/_blazor')) return;
    if (url.pathname.includes('/negotiate')) return;
    if (url.pathname.startsWith('/_framework/blazor')) return;

    // Navigation requests (HTML pages): network-first, fallback to offline page
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

    // Blazor framework static assets (fingerprinted URLs like blazor.web.hwax.js)
    // and other _framework/* files — cache-first (they are content-hashed)
    if (url.pathname.startsWith('/_framework/') || url.pathname.startsWith('/_content/')) {
        e.respondWith(
            caches.match(request).then(cached => {
                if (cached) return cached;
                return fetch(request).then(res => {
                    if (res.ok) {
                        const clone = res.clone();
                        caches.open(CACHE).then(c => c.put(request, clone));
                    }
                    return res;
                });
            })
        );
        return;
    }

    // Static assets with known extensions — stale-while-revalidate: szybko z cache, a w tle
    // pobieramy nową wersję. Pliki bez odcisku w nazwie (/js/*.js, /branding/logo.png,
    // manifest) przy cache-first zostawały na telefonach w starej wersji na zawsze.
    const isStatic = /\.(css|js|png|jpg|jpeg|gif|svg|ico|woff2?|ttf|webmanifest)(\?.*)?$/.test(url.pathname);
    if (isStatic) {
        e.respondWith(
            caches.open(CACHE).then(cache =>
                cache.match(request).then(cached => {
                    const network = fetch(request).then(res => {
                        if (res.ok) cache.put(request, res.clone());
                        return res;
                    });
                    if (cached) {
                        e.waitUntil(network.catch(() => {}));
                        return cached;
                    }
                    return network;
                })
            )
        );
        return;
    }

    // Everything else (API calls etc.) — network-first, cache fallback
    e.respondWith(
        fetch(request)
            .then(res => {
                if (res.ok) {
                    const clone = res.clone();
                    caches.open(CACHE).then(c => c.put(request, clone));
                }
                return res;
            })
            .catch(() => caches.match(request))
    );
});

// Web Push notifications
self.addEventListener('push', e => {
    let data = { title: 'Powiadomienie', body: '' };
    try { data = e.data.json(); } catch (_) {}
    e.waitUntil(
        self.registration.showNotification(data.title || 'Powiadomienie', {
            body: data.body || '',
            icon: data.icon || '/icons/icon-192.png',
            badge: '/icons/icon-96.png',
            // Przyciski (np. „Akceptuj”, gotowe odpowiedzi) — szczegóły akcji w data.actions.
            actions: Array.isArray(data.actions) ? data.actions.slice(0, 2) : [],
            data: { url: data.url || '/app', actions: data.actionData || {} }
        })
    );
});

self.addEventListener('notificationclick', e => {
    e.notification.close();
    const act = e.action ? e.notification.data?.actions?.[e.action] : null;
    // Przycisk z tokenem: wykonaj w tle (bez otwierania aplikacji) i pokaż krótkie potwierdzenie.
    if (act && act.token) {
        e.waitUntil((async () => {
            try {
                const res = await fetch('/api/push/act', {
                    method: 'POST', credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ token: act.token })
                });
                const j = res.ok ? await res.json() : { ok: false, message: 'Zaloguj się, żeby dokończyć.', url: e.notification.data?.url };
                await self.registration.showNotification(j.ok ? '✓ Gotowe' : 'Nie udało się', {
                    body: j.message || '', icon: '/icons/icon-192.png', badge: '/icons/icon-96.png',
                    data: { url: j.url || e.notification.data?.url || '/app' }
                });
            } catch (_) {
                await clients.openWindow(new URL(e.notification.data?.url || '/app', self.location.origin).href);
            }
        })());
        return;
    }
    const url = new URL((act && act.url) || e.notification.data?.url || '/app', self.location.origin).href;
    e.waitUntil((async () => {
        const list = await clients.matchAll({ type: 'window', includeUncontrolled: true });
        for (const client of list) {
            if (!client.url.startsWith(self.location.origin)) continue;
            try {
                await client.focus();
                // navigate() działa tylko dla okien kontrolowanych przez SW — w razie błędu otwieramy nowe.
                if ('navigate' in client) { await client.navigate(url); return; }
            } catch (_) { }
        }
        await clients.openWindow(url);
    })());
});

// Allow app shell to trigger SW update (e.g. after push.ps1 deploy)
self.addEventListener('message', e => {
    if (e.data === 'skipWaiting') self.skipWaiting();
});
