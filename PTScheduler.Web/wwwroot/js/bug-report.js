// „Zgłoś błąd”: od startu strony pamięta kilka ostatnich błędów z przeglądarki, po kliknięciu robi zrzut
// widocznej części ekranu (modern-screenshot — ładowane dopiero przy pierwszym zgłoszeniu) i otwiera okno
// zgłoszenia (BugReporter.razor). Na podglądzie można zaznaczyć miejsce palcem albo myszką.
window.ptBug = (function () {
    const errors = [];
    const remember = (kind, msg) => {
        try {
            const text = String(msg ?? '').replace(/\s+/g, ' ').trim().slice(0, 400);
            if (!text) return;
            errors.push(new Date().toTimeString().slice(0, 8) + ' ' + kind + ': ' + text);
            if (errors.length > 12) errors.shift();
        } catch { /* nigdy nie przeszkadzamy stronie */ }
    };
    window.addEventListener('error', e => remember('error', (e.message || 'błąd zasobu') + (e.filename ? ' (' + e.filename.split('/').pop() + ':' + e.lineno + ')' : '')));
    window.addEventListener('unhandledrejection', e => remember('promise', e.reason && (e.reason.stack || e.reason.message) || e.reason));
    const consoleError = console.error;
    console.error = function (...args) {
        remember('console', args.map(a => a instanceof Error ? (a.stack || a.message) : typeof a === 'string' ? a : (() => { try { return JSON.stringify(a); } catch { return String(a); } })()).join(' '));
        return consoleError.apply(this, args);
    };

    let dotnet = null, libLoad = null, busy = false;
    let base = null;            // pełny zrzut (canvas)
    let view = null, marks = null, host = null;

    const frames = n => new Promise(r => { const step = () => (n-- > 0 ? requestAnimationFrame(step) : r()); step(); });
    const lib = () => window.modernScreenshot ? Promise.resolve(window.modernScreenshot) : (libLoad ??= new Promise((ok, fail) => {
        const s = document.createElement('script');
        s.src = '/lib/modern-screenshot/modern-screenshot.js';
        s.onload = () => ok(window.modernScreenshot);
        s.onerror = () => { libLoad = null; fail(new Error('Nie wczytano modułu zrzutu ekranu.')); };
        document.head.appendChild(s);
    }));

    // Zrzut tego, co widać: cała strona przesunięta o przewinięcie i przycięta do okna.
    async function capture() {
        const ms = await lib();
        const w = document.documentElement.clientWidth || window.innerWidth, h = window.innerHeight;
        const x = window.scrollX, y = window.scrollY;
        // Renderujemy tylko do dolnej krawędzi okna; płótno nie może przekroczyć ok. 16 Mpx (limit Safari na iOS).
        const tall = y + h;
        const scale = Math.max(0.25, Math.min(window.devicePixelRatio || 1, 2, 1600 / w, Math.sqrt(16e6 / ((w + x) * tall))));
        // Ikony Bootstrap są tylko w ::before, więc biblioteka nie uznaje ich czcionki za używaną i ikony
        // znikają ze zrzutu. Niewidoczny znak z tą czcionką sprawia, że zostaje dołączona.
        const iconFont = document.createElement('span');
        iconFont.textContent = '\uF285';
        iconFont.style.cssText = 'font-family:bootstrap-icons;position:absolute;left:0;top:0;opacity:0;font-size:2px;pointer-events:none';
        document.body.appendChild(iconFont);
        const full = await ms.domToCanvas(document.documentElement, {
            scale,
            width: w + x,
            height: tall,
            backgroundColor: getComputedStyle(document.body).backgroundColor || '#ffffff',
            timeout: 2500,
            filter: visibleOnly(h),
            fetch: { placeholderImage: 'data:image/gif;base64,R0lGODlhAQABAAAAACw=' }
        }).finally(() => iconFont.remove());
        const out = document.createElement('canvas');
        out.width = Math.round(w * scale);
        out.height = Math.round(h * scale);
        out.getContext('2d').drawImage(full, Math.round(x * scale), Math.round(y * scale), out.width, out.height, 0, 0, out.width, out.height);
        return out;
    }

    // Pomijamy to, czego i tak nie widać: ukryte elementy i wszystko poniżej dolnej krawędzi okna
    // (ich usunięcie nie przesuwa niczego wyżej). Na długich listach zrzut jest przez to kilka razy szybszy.
    function visibleOnly(viewportHeight) {
        const limit = viewportHeight + 40;
        return el => {
            if (!(el instanceof Element)) return true;
            if (el.hasAttribute('data-bug-ignore') || el.tagName === 'SCRIPT' || el.tagName === 'NOSCRIPT') return false;
            if (el === document.body || !document.body.contains(el)) return true;
            if (el.getClientRects().length === 0) return getComputedStyle(el).display !== 'none';
            return el.getBoundingClientRect().top <= limit;
        };
    }

    // Kontekst bez wrażliwych parametrów adresu (kody, tokeny, hasła).
    function context() {
        let url = location.pathname;
        try {
            const p = new URLSearchParams(location.search);
            for (const k of [...p.keys()]) if (/token|code|secret|pass|key|^t$/i.test(k)) p.set(k, '…');
            const q = p.toString();
            if (q && !location.pathname.toLowerCase().startsWith('/account')) url += '?' + q;
        } catch { /* zostaje sama ścieżka */ }
        return {
            url,
            title: document.title,
            viewport: window.innerWidth + '×' + window.innerHeight + ' @' + (Math.round((window.devicePixelRatio || 1) * 100) / 100) + 'x',
            userAgent: navigator.userAgent,
            errors: errors.join('\n')
        };
    }

    function draw() {
        if (!view || !base) return;
        const g = view.getContext('2d');
        g.clearRect(0, 0, view.width, view.height);
        g.drawImage(base, 0, 0, view.width, view.height);
        g.drawImage(marks, 0, 0, view.width, view.height);
    }

    function onKey(e) {
        if (e.key === 'Escape' && dotnet && document.querySelector('.bugr-modal')) dotnet.invokeMethodAsync('Close');
    }
    document.addEventListener('keydown', onKey);

    return {
        register(ref) { dotnet = ref; },

        async open() {
            if (busy) return;
            busy = true;
            const cursor = document.documentElement.style.cursor;
            document.documentElement.style.cursor = 'progress';
            // Od razu widać, że coś się dzieje (dymek nie trafia na zrzut — data-bug-ignore).
            const toast = document.createElement('div');
            toast.className = 'bugr-toast';
            toast.setAttribute('data-bug-ignore', '');
            toast.setAttribute('role', 'status');
            toast.innerHTML = '<span class="spinner-border spinner-border-sm"></span>Robię zrzut ekranu…';
            try {
                for (let i = 0; i < 30 && !dotnet; i++) await new Promise(r => setTimeout(r, 100));
                if (!dotnet) { alert('Okno zgłoszenia jeszcze się wczytuje — spróbuj za chwilę.'); return; }
                await frames(2);                       // menu, z którego kliknięto, zdąży się zamknąć
                await new Promise(r => setTimeout(r, 80));
                document.body.appendChild(toast);
                try { base = await capture(); } catch (err) { base = null; remember('zrzut', err && err.message); }
                marks = null;
                await dotnet.invokeMethodAsync('Open', !!base);
            } finally {
                toast.remove();
                document.documentElement.style.cursor = cursor;
                busy = false;
            }
        },

        context,

        // Podgląd z możliwością zaznaczania (czerwony pisak) w elemencie z okna zgłoszenia.
        mount(el) {
            host = el;
            if (!base || !el) return;
            el.innerHTML = '';
            marks = document.createElement('canvas');
            marks.width = base.width; marks.height = base.height;
            view = document.createElement('canvas');
            view.width = base.width; view.height = base.height;
            view.setAttribute('aria-label', 'Zrzut ekranu — możesz na nim zaznaczyć miejsce');
            el.appendChild(view);
            draw();
            const pen = marks.getContext('2d');
            pen.strokeStyle = '#ef4444'; pen.lineCap = 'round'; pen.lineJoin = 'round';
            pen.lineWidth = Math.max(4, base.width / 180);
            let last = null;
            const at = e => { const r = view.getBoundingClientRect(); return [(e.clientX - r.left) * base.width / r.width, (e.clientY - r.top) * base.height / r.height]; };
            view.addEventListener('pointerdown', e => { view.setPointerCapture(e.pointerId); last = at(e); e.preventDefault(); });
            view.addEventListener('pointermove', e => {
                if (!last) return;
                const p = at(e);
                pen.beginPath(); pen.moveTo(last[0], last[1]); pen.lineTo(p[0], p[1]); pen.stroke();
                last = p; draw();
            });
            const end = () => { last = null; };
            view.addEventListener('pointerup', end);
            view.addEventListener('pointercancel', end);
        },

        clearMarks() {
            if (marks) marks.getContext('2d').clearRect(0, 0, marks.width, marks.height);
            draw();
        },

        // Zrzut z zaznaczeniami jako JPEG (Uint8Array → strumień do .NET); mniejsza jakość, gdy plik wyjdzie duży.
        async image() {
            if (!base) return null;
            const out = document.createElement('canvas');
            out.width = base.width; out.height = base.height;
            const g = out.getContext('2d');
            g.drawImage(base, 0, 0);
            if (marks) g.drawImage(marks, 0, 0);
            for (const q of [0.82, 0.65, 0.5]) {
                const blob = await new Promise(r => out.toBlob(r, 'image/jpeg', q));
                if (blob && (blob.size < 2_400_000 || q === 0.5)) return new Uint8Array(await blob.arrayBuffer());
            }
            return null;
        },

        focus(selector) { const el = document.querySelector(selector); if (el) el.focus(); },

        reset() { base = null; marks = null; view = null; if (host) host.innerHTML = ''; host = null; }
    };
})();
