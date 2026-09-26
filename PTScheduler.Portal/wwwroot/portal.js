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
