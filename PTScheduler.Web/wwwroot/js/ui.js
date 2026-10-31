// Drobne pomocniki interfejsu wołane z Blazora.
(function () {
    // Otwarty edytor (np. notatka w „Dziś”): przewiń tak, żeby był widoczny nad dolnym menu, i ustaw kursor w polu.
    window.ptFocusNote = function (id) {
        var el = document.getElementById(id);
        if (!el) return;
        var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        el.scrollIntoView({ behavior: reduce ? 'auto' : 'smooth', block: 'center' });
        var field = el.querySelector('textarea, input');
        if (field) setTimeout(function () { field.focus({ preventScroll: true }); }, reduce ? 0 : 250);
    };
})();
