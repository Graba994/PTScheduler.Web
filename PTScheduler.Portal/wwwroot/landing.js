// Strona główna, szablon „Nowa” (HomeLanding.razor):
//  - podgląd aplikacji w hero: wpisana nazwa i kolor od razu na telefonie (bez wpisywania — przykładowe marki po kolei),
//  - sceny (HomeScene.razor): oś czasu z data-at / data-count / data-type / kursorem („-” chowa kursor), grają tylko na ekranie,
//  - filmy z aplikacji grają tylko, gdy są na ekranie (i nie przy „ogranicz ruch”),
//  - oś „dzień z Hantly”: aktywny krok wg przewijania, obok przyklejony ekran,
//  - zakładki funkcji (klawiatura: strzałki, Home, End),
//  - wejście elementów przy przewijaniu.
// Działa po zwykłym wczytaniu i po nawigacji Blazora (enhancedload).
(function () {
    var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    var observers = [];
    var syncScenes = function () { };

    function cleanup() {
        observers.forEach(function (o) { o.disconnect(); });
        observers = [];
        document.querySelectorAll('[data-ld-scene]').forEach(function (sc) { if (sc.__ld) stop(sc); });
        syncScenes = function () { };
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
            syncScenes();
        }
        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (e) { if (e.isIntersecting) activate(e.target.getAttribute('data-ld-step')); });
        }, { rootMargin: '-45% 0px -45% 0px', threshold: 0 });
        steps.forEach(function (s) { io.observe(s); });
        observers.push(io);
    }

    // ── Podgląd aplikacji w hero ──
    function monogram(name) {
        var words = name.split(/\s+/).filter(function (w) { return w && /[\p{L}\p{N}]/u.test(w[0]); });
        if (!words.length) return 'PT';
        if (words.length === 1) return words[0].length > 1 ? words[0][0].toUpperCase() + words[0][1].toLowerCase() : words[0][0].toUpperCase();
        return (words[0][0] + words[1][0]).toUpperCase();
    }

    function live(root) {
        var box = root.querySelector('[data-ld-live]');
        if (!box || box.__ldLive) return; box.__ldLive = 1;
        var input = box.querySelector('input[name="nazwa"]');
        var phone = box.querySelector('.ldl-phone');
        var radios = Array.prototype.slice.call(box.querySelectorAll('input[name="kolor"]'));
        var nameEl = box.querySelector('[data-ldl-name]'), monoEl = box.querySelector('[data-ldl-mono]');
        var samples = [['Kasia Fit Studio', 0], ['Strefa Ruchu', 1], ['Marek Trener', 2], ['Joga z Olą', 3], ['Power Gym', 4]];
        var touched = false, timer = null;

        function color(r) {
            phone.style.setProperty('--app', r.getAttribute('data-hex'));
            phone.style.setProperty('--app2', r.getAttribute('data-hex2'));
            box.style.setProperty('--ldl-c', r.getAttribute('data-hex'));
        }
        function show(name) {
            nameEl.textContent = name;
            monoEl.textContent = monogram(name);
            monoEl.classList.remove('ldl-flash'); void monoEl.offsetWidth; monoEl.classList.add('ldl-flash');
        }
        function user() {
            touched = true; clearTimeout(timer);
            show(input.value.trim() || 'Twoje studio');
        }
        input.addEventListener('input', user);
        input.addEventListener('focus', function () { touched = true; clearTimeout(timer); });
        radios.forEach(function (r) {
            r.addEventListener('change', function () { touched = true; clearTimeout(timer); color(r); });
        });
        // Bez wpisywania: przykładowe marki i kolory po kolei, żeby było widać, co się zmienia.
        if (reduce) return;
        var i = 0;
        function next() {
            if (touched) return;
            i = (i + 1) % samples.length;
            var sm = samples[i], r = radios[sm[1] % radios.length];
            show(sm[0]); color(r); r.checked = true;
            timer = setTimeout(next, 2800);
        }
        timer = setTimeout(next, 2400);
    }

    // ── Sceny ──
    function fmt(n) { return String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, '\u00a0'); }

    function scenes(root) {
        var list = Array.prototype.slice.call(root.querySelectorAll('[data-ld-scene]'));
        if (!list.length) return;
        list.forEach(function (sc) {
            if (sc.__ld) return;
            sc.__ld = { timers: [] };
            sc.querySelectorAll('[data-type]').forEach(function (el) { el.__text = el.textContent.trim(); });
            sc.querySelectorAll('[data-count]').forEach(function (el) { var p = el.getAttribute('data-count').split(':'); el.__from = +p[0]; el.__to = +p[1]; });
        });
        if (reduce || !('IntersectionObserver' in window)) { list.forEach(finalState); return; }

        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (e) { e.target.__ld.vis = e.isIntersecting; });
            sync();
        }, { threshold: 0.35 });
        list.forEach(function (sc) { io.observe(sc); });
        observers.push(io);
        syncScenes = sync;

        function sync() {
            list.forEach(function (sc) {
                var item = sc.closest('[data-ld-stage]');
                var want = sc.__ld.vis && (!item || item.classList.contains('is-active'));
                if (want && !sc.__ld.run) play(sc);
                else if (!want && sc.__ld.run) stop(sc);
            });
        }
    }

    function later(sc, ms, fn) { sc.__ld.timers.push(setTimeout(fn, ms)); }
    function stop(sc) { sc.__ld.run = false; sc.__ld.timers.forEach(clearTimeout); sc.__ld.timers.forEach(clearInterval); sc.__ld.timers = []; }

    function reset(sc) {
        sc.querySelectorAll('[data-at]').forEach(function (el) { el.classList.remove('on', 'typing'); });
        sc.querySelectorAll('[data-type]').forEach(function (el) { el.textContent = ''; });
        sc.querySelectorAll('[data-count]').forEach(function (el) { el.textContent = fmt(el.__from); });
        var cur = sc.querySelector('.sc-cur');
        if (cur && cur.offsetParent) {
            var b = cur.offsetParent;
            cur.classList.remove('show', 'click'); cur.style.transition = 'none';
            cur.style.transform = 'translate(' + b.offsetWidth * .78 + 'px, ' + b.offsetHeight * .96 + 'px)';
            void cur.offsetWidth; cur.style.transition = '';
        }
    }

    function finalState(sc) {
        sc.classList.add('sc-final');
        sc.querySelectorAll('[data-at]').forEach(function (el) { el.classList.add('on'); });
        sc.querySelectorAll('[data-type]').forEach(function (el) { el.textContent = el.__text; });
        sc.querySelectorAll('[data-count]').forEach(function (el) { el.textContent = fmt(el.__to); });
    }

    function count(sc, el) {
        var t0 = performance.now(), from = el.__from, to = el.__to, dur = 900;
        (function step(now) {
            if (!sc.__ld.run) return;
            var k = Math.min(1, (now - t0) / dur), e = 1 - Math.pow(1 - k, 3);
            el.textContent = fmt(from + (to - from) * e);
            if (k < 1) requestAnimationFrame(step);
        })(t0);
    }

    function type(sc, el) {
        var text = el.__text, i = 0;
        el.classList.add('typing');
        var id = setInterval(function () {
            i += 2; el.textContent = text.slice(0, i);
            if (i >= text.length) { clearInterval(id); el.classList.remove('typing'); }
        }, 32);
        sc.__ld.timers.push(id);
    }

    function moveCursor(sc, cur, step) {
        if (step === '-') { cur.classList.remove('show'); return; }
        var click = step.slice(-1) === '!', name = click ? step.slice(0, -1) : step;
        var target = sc.querySelector('[data-target="' + name + '"]');
        if (!target) return;
        var box = cur.offsetParent.getBoundingClientRect(), r = target.getBoundingClientRect();
        var x = r.left - box.left + r.width / 2, y = r.top - box.top + r.height / 2;
        cur.classList.add('show');
        cur.style.transform = 'translate(' + x + 'px, ' + y + 'px)';
        if (click) {
            cur.classList.remove('click'); void cur.offsetWidth; cur.classList.add('click');
            target.classList.add('hit');
            later(sc, 180, function () { target.classList.remove('hit'); });
        }
    }

    function play(sc) {
        stop(sc);
        sc.__ld.run = true;
        reset(sc);
        sc.querySelectorAll('[data-at]').forEach(function (el) {
            later(sc, +el.getAttribute('data-at'), function () {
                el.classList.add('on');
                if (el.__from !== undefined) count(sc, el);
                if (el.__text !== undefined) type(sc, el);
            });
        });
        var cur = sc.querySelector('.sc-cur');
        if (cur && cur.getAttribute('data-moves')) {
            cur.getAttribute('data-moves').split(';').forEach(function (m) {
                var p = m.split('>');
                later(sc, +p[0], function () { moveCursor(sc, cur, p[1]); });
            });
        }
        if (sc.getAttribute('data-loop') === '1') {
            later(sc, +sc.getAttribute('data-len') + 2600, function () { if (sc.__ld.run) play(sc); });
        }
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

    // „Wyślij mi link”: nazwa studia i kolor z kreatora na żywo jadą razem z e-mailem (link otworzy kreator z nimi).
    function later(root) {
        root.querySelectorAll('form[data-ld-later]').forEach(function (form) {
            if (form.__ld) return;
            form.__ld = true;
            form.addEventListener('submit', function () {
                var name = root.querySelector('#ldl-name');
                var color = root.querySelector('input[name="kolor"]:checked');
                if (name) form.elements.nazwa.value = name.value.trim();
                if (color) form.elements.kolor.value = color.value;
            });
        });
    }

    // Cennik na telefonie to przewijane karty — startujemy od wyróżnionego planu.
    function snap(root) {
        root.querySelectorAll('[data-ld-snap]').forEach(function (track) {
            if (track.scrollWidth <= track.clientWidth + 4) return;
            var featured = track.querySelector('.featured');
            if (!featured) return;
            track.scrollLeft = featured.offsetLeft - (track.clientWidth - featured.offsetWidth) / 2;
        });
    }

    function init() {
        cleanup();
        var root = document.querySelector('.ld');
        if (!root) return;
        document.documentElement.classList.add('ld-js');
        live(root); scenes(root); videos(root); story(root); tabs(root); reveal(root); later(root); snap(root);
    }

    document.addEventListener('DOMContentLoaded', function () {
        init();
        if (window.Blazor && Blazor.addEventListener) Blazor.addEventListener('enhancedload', init);
    });
})();
