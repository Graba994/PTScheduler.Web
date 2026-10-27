// Konfigurator „Pierwsze ustawienia” (/witaj): zapamiętany krok (wraca się w to samo miejsce) i kod QR z adresem aplikacji.
window.ptFirstSetup = {
    load: function (key) { try { return localStorage.getItem(key) || ''; } catch (e) { return ''; } },
    save: function (key, value) { try { localStorage.setItem(key, value); } catch (e) { } },
    qr: function (el, text) {
        if (!el) return;
        function draw() {
            if (typeof qrcode !== 'function') return;
            var qr = qrcode(0, 'M');
            qr.addData(text);
            qr.make();
            el.innerHTML = qr.createSvgTag({ cellSize: 4, margin: 2, scalable: true });
        }
        if (typeof qrcode === 'function') { draw(); return; }
        var s = document.createElement('script');
        s.src = '/lib/qrcode-generator/qrcode.js';
        s.onload = draw;
        document.head.appendChild(s);
    },
    // Strona główna w telefonie z podglądu: po załadowaniu raz płynnie zjeżdża w dół i wraca na górę,
    // żeby było widać całą wizytówkę. Pierwszy dotyk albo kółko myszy oddaje przewijanie użytkownikowi.
    tour: function (frame) {
        try {
            var win = frame && frame.contentWindow;
            var doc = win && win.document;
            if (!doc || !doc.documentElement) return;
            // Podgląd ogląda trener, nie gość — pasek o cookies tylko zasłaniałby dół „telefonu”.
            var st = doc.createElement('style');
            st.textContent = '.cookie-banner{display:none!important}';
            (doc.head || doc.documentElement).appendChild(st);
            if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
            if (frame._ptToured) return;
            frame._ptToured = true;
            var stopped = false;
            var stop = function () { stopped = true; };
            ['wheel', 'touchstart', 'mousedown', 'keydown'].forEach(function (ev) { doc.addEventListener(ev, stop, { passive: true, once: true }); });
            var max = Math.max(0, doc.documentElement.scrollHeight - win.innerHeight);
            if (max < 50) return;
            var target = Math.min(max, win.innerHeight * 6);
            var start = null, down = 5200, pause = 700, up = 1400;
            var ease = function (t) { return t < .5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2; };
            var step = function (ts) {
                if (stopped) return;
                if (start === null) start = ts;
                var e = ts - start, y;
                if (e < down) y = target * ease(e / down);
                else if (e < down + pause) y = target;
                else if (e < down + pause + up) y = target * (1 - ease((e - down - pause) / up));
                else { win.scrollTo(0, 0); return; }
                win.scrollTo(0, y);
                win.requestAnimationFrame(step);
            };
            setTimeout(function () { if (!stopped) win.requestAnimationFrame(step); }, 900);
        } catch (e) { }
    },
    // Ruch maskotki wybrany w „Pierwszych ustawieniach” — ten sam zapis co w dymku asystenta (PTHantly).
    setMotion: function (value) {
        try {
            var raw = window.PTHantly ? window.PTHantly.load() : '';
            var prefs = raw ? JSON.parse(raw) : {};
            prefs.Motion = value || '';
            if (window.PTHantly) window.PTHantly.save(JSON.stringify(prefs));
        } catch (e) { }
    },
    getMotion: function () {
        try {
            var raw = window.PTHantly ? window.PTHantly.load() : '';
            return raw ? (JSON.parse(raw).Motion || '') : '';
        } catch (e) { return ''; }
    }
};
