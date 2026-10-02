// Menu panelu: zwijanie do ikon (komputer, zapamiętane) i wysuwanie (telefon).
(function () {
    var KEY = 'pt-portal-nav-collapsed';
    function apply() {
        var collapsed = false;
        try { collapsed = localStorage.getItem(KEY) === '1'; } catch (e) { }
        document.documentElement.classList.toggle('nav-collapsed', collapsed);
        document.documentElement.classList.remove('nav-open');
    }
    window.ptNav = {
        toggleCollapse: function () {
            var next = !document.documentElement.classList.contains('nav-collapsed');
            document.documentElement.classList.toggle('nav-collapsed', next);
            try { localStorage.setItem(KEY, next ? '1' : '0'); } catch (e) { }
        },
        open: function () { document.documentElement.classList.add('nav-open'); },
        close: function () { document.documentElement.classList.remove('nav-open'); }
    };
    apply();
    document.addEventListener('DOMContentLoaded', function () {
        if (window.Blazor && Blazor.addEventListener) Blazor.addEventListener('enhancedload', apply);
    });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') window.ptNav.close(); });
})();

// Zegar w pasku statusu panelu — liczony w przeglądarce, więc nie obciąża serwera.
(function () {
    var fmtDate = new Intl.DateTimeFormat('pl-PL', { weekday: 'short', day: 'numeric', month: 'short' });
    var fmtTime = new Intl.DateTimeFormat('pl-PL', { hour: '2-digit', minute: '2-digit' });
    function tick() {
        var now = new Date();
        var text = fmtTime.format(now) + ' · ' + fmtDate.format(now);
        document.querySelectorAll('[data-pt-clock]').forEach(function (el) {
            if (el.textContent !== text) el.textContent = text;
        });
    }
    tick();
    setInterval(tick, 5000);
    document.addEventListener('DOMContentLoaded', tick);
    if (window.MutationObserver) {
        new MutationObserver(function () {
            var el = document.querySelector('[data-pt-clock]');
            if (el && !el.textContent) tick();
        }).observe(document.documentElement, { childList: true, subtree: true });
    }
})();

// Podgląd logów: przewijanie na dół (najnowsze wpisy są na końcu), chyba że ktoś czyta starsze.
window.ptLogs = {
    atBottom: function (el) { return !el || el.scrollHeight - el.scrollTop - el.clientHeight < 40; },
    toBottom: function (el) { if (el) el.scrollTop = el.scrollHeight; }
};

// Kalkulator odzyskanego czasu na stronie głównej (te same wzory co w Home.razor).
(function () {
    var pl = new Intl.NumberFormat('pl-PL');
    function set(root, key, text) {
        var el = root.querySelector('[data-calc-out="' + key + '"]');
        if (el) el.textContent = text;
    }
    function update(root) {
        var clients = +root.querySelector('[data-calc="clients"]').value;
        var rate = +root.querySelector('[data-calc="rate"]').value;
        var minutes = +root.getAttribute('data-minutes') || 35;
        var plan = parseFloat((root.getAttribute('data-plan') || '0').replace(',', '.'));
        var hours = clients * minutes / 60;
        var value = Math.round(hours * rate / 10) * 10;
        set(root, 'clients', String(clients));
        set(root, 'rate', pl.format(rate) + ' zł');
        set(root, 'hours', pl.format(Math.round(hours)));
        set(root, 'value', pl.format(value) + ' zł');
        if (plan > 0) set(root, 'ratio', Math.max(1, Math.floor(hours * rate / plan)) + '×');
    }
    document.addEventListener('input', function (e) {
        var root = e.target.closest && e.target.closest('[data-lp-calc]');
        if (root) update(root);
    });
})();

// Kreator „Zbuduj swoją aplikację”: szkic w przeglądarce (bez hasła) i konfetti po publikacji.
(function () {
    var KEY = 'pt-builder-draft';
    window.ptBuilder = {
        save: function (json) { try { localStorage.setItem(KEY, json); } catch (e) { } },
        load: function () { try { return localStorage.getItem(KEY); } catch (e) { return null; } },
        clear: function () { try { localStorage.removeItem(KEY); } catch (e) { } },
        focus: function (sel) {
            setTimeout(function () {
                // Na telefonie podgląd jest nad krokami — po zmianie kroku wracamy na górę, żeby było widać efekt.
                if (window.innerWidth <= 900) window.scrollTo({ top: 0, behavior: 'smooth' });
                var el = document.querySelector(sel); if (el) { el.focus({ preventScroll: true }); }
            }, 60);
        },
        confetti: function (colors) {
            if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
            var c = document.createElement('canvas');
            c.className = 'bd-confetti';
            c.width = window.innerWidth; c.height = window.innerHeight;
            document.body.appendChild(c);
            var ctx = c.getContext('2d');
            var parts = [];
            for (var i = 0; i < 170; i++) {
                parts.push({
                    x: c.width / 2 + (Math.random() - .5) * 240, y: c.height * .45,
                    vx: (Math.random() - .5) * 16, vy: -Math.random() * 17 - 5,
                    w: 6 + Math.random() * 7, h: 9 + Math.random() * 8, r: Math.random() * 6.28,
                    vr: (Math.random() - .5) * .35, color: colors[i % colors.length]
                });
            }
            var start = performance.now();
            (function frame(t) {
                var age = t - start;
                ctx.clearRect(0, 0, c.width, c.height);
                parts.forEach(function (p) {
                    p.vy += .42; p.vx *= .99; p.x += p.vx; p.y += p.vy; p.r += p.vr;
                    ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(p.r);
                    ctx.globalAlpha = Math.max(0, 1 - age / 3200);
                    ctx.fillStyle = p.color; ctx.fillRect(-p.w / 2, -p.h / 2, p.w, p.h * Math.cos(p.r * 2));
                    ctx.restore();
                });
                if (age < 3300) requestAnimationFrame(frame); else c.remove();
            })(start);
        }
    };
})();
