// Strona główna, szablon „Nowa” (HomeLanding.razor):
//  - filmy z aplikacji grają tylko, gdy są na ekranie (i nie przy „ogranicz ruch”),
//  - oś „dzień z Hantly”: aktywny krok wg przewijania, obok przyklejony ekran,
//  - zakładki funkcji (klawiatura: strzałki, Home, End),
//  - wejście elementów przy przewijaniu.
// Działa po zwykłym wczytaniu i po nawigacji Blazora (enhancedload).
(function () {
    var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    var observers = [];

    function cleanup() {
        observers.forEach(function (o) { o.disconnect(); });
        observers = [];
    }

    function videos(root) {
        var list = root.querySelectorAll('video[data-ld-video]');
        if (!list.length) return;
        if (!('IntersectionObserver' in window)) { list.forEach(function (v) { v.setAttribute('controls', ''); }); return; }
        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (e) {
                var v = e.target;
                if (e.isIntersecting && !reduce) {
                    if (v.preload !== 'auto') { v.preload = 'auto'; }
                    var p = v.play(); if (p && p.catch) p.catch(function () { });
                } else if (!v.paused) {
                    v.pause();
                }
            });
        }, { threshold: 0.35 });
        list.forEach(function (v) { io.observe(v); });
        observers.push(io);
        if (reduce) list.forEach(function (v) { v.setAttribute('controls', ''); });
    }

    function story(root) {
        var grid = root.querySelector('[data-ld-story]');
        if (!grid || !('IntersectionObserver' in window)) return;
        var steps = grid.querySelectorAll('[data-ld-step]');
        var stage = grid.querySelectorAll('[data-ld-stage]');
        function activate(i) {
            steps.forEach(function (s) { s.classList.toggle('is-active', s.getAttribute('data-ld-step') === String(i)); });
            stage.forEach(function (s) {
                var on = s.getAttribute('data-ld-stage') === String(i);
                s.classList.toggle('is-active', on);
                var v = s.querySelector('video');
                if (v) {
                    if (on && !reduce) { v.preload = 'auto'; try { v.currentTime = 0; } catch (e) { } var p = v.play(); if (p && p.catch) p.catch(function () { }); }
                    else { v.pause(); }
                }
            });
        }
        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (e) { if (e.isIntersecting) activate(e.target.getAttribute('data-ld-step')); });
        }, { rootMargin: '-45% 0px -45% 0px', threshold: 0 });
        steps.forEach(function (s) { io.observe(s); });
        observers.push(io);
    }

    function tabs(root) {
        root.querySelectorAll('[data-ld-tabs]').forEach(function (box) {
            if (box.__ldTabs) return; box.__ldTabs = 1;
            var list = Array.prototype.slice.call(box.querySelectorAll('[role="tab"]'));
            function select(t, focus) {
                list.forEach(function (x) {
                    var on = x === t;
                    x.setAttribute('aria-selected', on ? 'true' : 'false');
                    x.tabIndex = on ? 0 : -1;
                    var panel = document.getElementById(x.getAttribute('aria-controls'));
                    if (panel) panel.hidden = !on;
                });
                if (focus) t.focus();
            }
            list.forEach(function (t, i) {
                t.addEventListener('click', function () { select(t, false); });
                t.addEventListener('keydown', function (e) {
                    var n = e.key === 'ArrowRight' ? i + 1 : e.key === 'ArrowLeft' ? i - 1 : e.key === 'Home' ? 0 : e.key === 'End' ? list.length - 1 : null;
                    if (n === null) return;
                    e.preventDefault();
                    select(list[(n + list.length) % list.length], true);
                });
            });
        });
    }

    function reveal(root) {
        var items = root.querySelectorAll('[data-ld-reveal]:not(.is-in)');
        if (!items.length) return;
        if (!('IntersectionObserver' in window) || reduce) { items.forEach(function (el) { el.classList.add('is-in'); }); return; }
        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (e) { if (e.isIntersecting) { e.target.classList.add('is-in'); io.unobserve(e.target); } });
        }, { rootMargin: '0px 0px -10% 0px' });
        items.forEach(function (el) { io.observe(el); });
        observers.push(io);
    }

    function init() {
        cleanup();
        var root = document.querySelector('.ld');
        if (!root) return;
        document.documentElement.classList.add('ld-js');
        videos(root); story(root); tabs(root); reveal(root);
    }

    document.addEventListener('DOMContentLoaded', function () {
        init();
        if (window.Blazor && Blazor.addEventListener) Blazor.addEventListener('enhancedload', init);
    });
})();
