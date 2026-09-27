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
