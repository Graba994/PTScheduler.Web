// Wyszukiwarka ustawień (jak w ustawieniach Androida): filtruje gotową listę wyników
// już przy pisaniu — bez zapytań do serwera. Działa też na stronach renderowanych statycznie.
(function () {
    function norm(s) {
        return (s || '').toLowerCase().replace(/ł/g, 'l').normalize('NFD').replace(/[̀-ͯ]/g, '');
    }

    function filter(input) {
        var box = input.closest('[data-ss]');
        if (!box) return;
        var results = box.querySelector('.ss-results');
        // Polska odmiana: „haslo” ma znaleźć „hasła”, „platnosc” — „płatności” (szukamy po rdzeniu).
        var words = norm(input.value).split(/\s+/).filter(Boolean).map(function (w) {
            return w.length >= 5 ? w.slice(0, Math.max(4, w.length - 2)) : w;
        });
        var scored = [];
        box.querySelectorAll('.ss-item').forEach(function (a) {
            var text = a.getAttribute('data-text') || '';
            var title = norm(a.querySelector('b') ? a.querySelector('b').textContent : '');
            var ok = words.length > 0 && words.every(function (w) { return text.indexOf(w) >= 0; });
            a.hidden = true;
            a.classList.remove('ss-active');
            if (ok) scored.push({ a: a, score: words.filter(function (w) { return title.indexOf(w) >= 0; }).length });
        });
        // Najpierw trafienia w nazwie ustawienia, potem w opisie.
        scored.sort(function (x, y) { return y.score - x.score; });
        var shown = Math.min(scored.length, 8);
        scored.slice(0, 8).forEach(function (s) { s.a.hidden = false; results.insertBefore(s.a, box.querySelector('.ss-empty')); });
        var first = box.querySelector('.ss-item:not([hidden])');
        if (first) first.classList.add('ss-active');
        box.querySelector('.ss-empty').hidden = shown > 0 || words.length === 0;
        results.hidden = words.length === 0;
        box.classList.toggle('ss-open', words.length > 0);
    }

    function key(e, input) {
        var box = input.closest('[data-ss]');
        var items = Array.prototype.slice.call(box.querySelectorAll('.ss-item:not([hidden])'));
        var cur = items.findIndex(function (a) { return a.classList.contains('ss-active'); });
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault();
            if (!items.length) return;
            var next = e.key === 'ArrowDown' ? Math.min(cur + 1, items.length - 1) : Math.max(cur - 1, 0);
            items.forEach(function (a) { a.classList.remove('ss-active'); });
            items[next].classList.add('ss-active');
            items[next].scrollIntoView({ block: 'nearest' });
        } else if (e.key === 'Enter') {
            var target = items[cur >= 0 ? cur : 0];
            if (target) { e.preventDefault(); location.href = target.getAttribute('href'); }
        } else if (e.key === 'Escape') {
            input.value = '';
            filter(input);
        }
    }

    // Skrót „/” — od razu do wyszukiwarki (gdy nie piszesz w innym polu).
    document.addEventListener('keydown', function (e) {
        if (e.key !== '/' || e.ctrlKey || e.metaKey) return;
        var t = e.target;
        if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return;
        var input = document.querySelector('[data-ss] input');
        if (input && input.offsetParent !== null) { e.preventDefault(); input.focus(); }
    });

    window.PTSettingsSearch = { filter: filter, key: key };
})();
