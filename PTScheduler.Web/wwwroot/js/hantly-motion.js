// Ruch maskotki Hantly (animowany hantelek z MascotArt). Otoczka maskotki:
//   <span class="hm-fx" data-hm-motion="normal|calm|lively|off" data-hm-fx="look,tricks,hover,talk,doze,party"> <span class="hm-live …">…</span> </span>
// - look: oczy idą za kursorem (albo palcem),
// - tricks: co jakiś czas sztuczka w spoczynku (podskok, kręcenie się, „+1” z hantlą, rozglądanie się, przeciąganie, shimmy),
// - hover: po najechaniu prostuje się, czasem wypuszcza serduszko,
// - doze: po kilku minutach bezczynności ziewa i drzemie, budzi się, gdy wrócisz,
// - talk / party i reakcje na wydarzenia (stonks, trophy, coins, calendar, sweat, magnifier, sun, chart, tag, ring, warmup):
//   wywołuje strona (PTHmMotion.trick(el, 'stonks')) albo atrybut data-hm-react="stonks" — wtedy raz dziennie po pojawieniu się.
// Klasy dokładamy otoczce, bo Blazor przepisuje klasy samego .hm-live. Przy „ogranicz ruch” nic się nie dzieje.
// Ten sam plik jest w Portalu (wwwroot/hantly-motion.js) i w aplikacji trenera (wwwroot/js/hantly-motion.js) — test pilnuje, żeby były identyczne.
window.PTHmMotion = (function () {
    var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    var TRICKS = { wiggle: 900, hop: 950, spin: 900, curl: 1500, look: 1700, stretch: 1200, heart: 1300, talk: 1600, party: 1400, yawn: 1800,
        stonks: 2800, trophy: 2000, coins: 1900, calendar: 1900, sweat: 1900, magnifier: 2200, sun: 2400, chart: 2000, tag: 2000, ring: 1800, warmup: 1800 };
    var IDLE_POOL = ['wiggle', 'hop', 'spin', 'curl', 'look', 'stretch'];
    var DOZE_MS = { calm: 120000, normal: 180000, lively: 300000 };
    var hosts = [];
    var pointer = null, frame = 0, lastMove = 0, lastActivity = Date.now();

    function fx(el) { return (el.getAttribute('data-hm-fx') || '').split(',').map(function (s) { return s.trim(); }); }
    function has(el, f) { return fx(el).indexOf(f) >= 0; }
    function motion(el) { return el.getAttribute('data-hm-motion') || 'normal'; }
    function off(el) { return reduce || motion(el) === 'off'; }
    function live(el) { return el.querySelector('.hm-live'); }
    function resting(el) {
        var l = live(el);
        // Bez nakładania się: w trakcie innej sztuczki albo reakcji nie zaczynamy następnej.
        return !!l && l.classList.contains('st-idle') && !el.classList.contains('is-dozing') && !/(^|\s)tr-/.test(el.className);
    }
    function inView(el) {
        var r = el.getBoundingClientRect();
        return r.width > 0 && r.bottom > 0 && r.right > 0 && r.top < window.innerHeight && r.left < window.innerWidth;
    }

    function trick(el, name, ms) {
        if (!el || off(el)) return;
        var cls = 'tr-' + name, key = '__hmT' + name;
        el.classList.remove(cls); void el.offsetWidth; el.classList.add(cls);
        clearTimeout(el[key]);
        el[key] = setTimeout(function () { el.classList.remove(cls); }, ms || TRICKS[name] || 1200);
    }

    function schedule(el) {
        clearTimeout(el.__hm.timer);
        var m = motion(el);
        var range = m === 'calm' ? [20000, 35000] : m === 'lively' ? [3500, 7000] : [6000, 12000];
        // Pierwsza sztuczka szybko po pojawieniu się na ekranie — od razu widać, że żyje.
        var wait = el.__hm.first ? range[0] + Math.random() * (range[1] - range[0]) : 2500 + Math.random() * 2500;
        el.__hm.first = true;
        el.__hm.timer = setTimeout(function () {
            if (!document.contains(el)) return;
            if (!off(el) && has(el, 'tricks') && resting(el) && !document.hidden && inView(el)) {
                var pick = IDLE_POOL[Math.floor(Math.random() * IDLE_POOL.length)];
                if (pick === el.__hm.last) pick = IDLE_POOL[(IDLE_POOL.indexOf(pick) + 1) % IDLE_POOL.length];
                el.__hm.last = pick;
                trick(el, pick);
            }
            schedule(el);
        }, wait);
    }

    // Oczy za kursorem: wektor od środka maskotki, przycięty do −1…1; po chwili bez ruchu wracają na środek.
    function look() {
        frame = 0;
        var now = Date.now();
        hosts.forEach(function (el) {
            if (off(el) || !has(el, 'look') || el.classList.contains('is-dozing')) { el.style.removeProperty('--hm-lx'); el.style.removeProperty('--hm-ly'); return; }
            if (!pointer || now - lastMove > 5000 || !inView(el)) { el.style.setProperty('--hm-lx', 0); el.style.setProperty('--hm-ly', 0); return; }
            var r = el.getBoundingClientRect();
            var dx = (pointer.x - (r.left + r.width / 2)) / 220, dy = (pointer.y - (r.top + r.height * .45)) / 220;
            el.style.setProperty('--hm-lx', Math.max(-1, Math.min(1, dx)).toFixed(2));
            el.style.setProperty('--hm-ly', Math.max(-1, Math.min(1, dy)).toFixed(2));
        });
    }

    function activity() {
        lastActivity = Date.now();
        hosts.forEach(function (el) {
            if (el.classList.contains('is-dozing')) { el.classList.remove('is-dozing'); trick(el, 'stretch'); }
        });
    }

    function dozeCheck() {
        var now = Date.now();
        hosts.forEach(function (el) {
            if (off(el) || !has(el, 'doze') || !resting(el) || el.__hm.yawning) return;
            if (now - lastActivity < (DOZE_MS[motion(el)] || DOZE_MS.normal)) return;
            el.__hm.yawning = true;
            trick(el, 'yawn');
            setTimeout(function () {
                el.__hm.yawning = false;
                if (Date.now() - lastActivity >= 1500) el.classList.add('is-dozing');
            }, 1800);
        });
    }

    // Reakcja z atrybutu (np. karta „Na dziś”): raz dziennie, gdy maskotka jest na ekranie i w spoczynku.
    function reactOnce(el) {
        var name = el.getAttribute('data-hm-react');
        if (!name || off(el) || !has(el, 'react') || !TRICKS[name]) return;
        var key = 'hm-react:' + name + ':' + new Date().toISOString().slice(0, 10);
        try { if (localStorage.getItem(key)) return; } catch (e) { }
        var tries = 0;
        (function wait() {
            if (!document.contains(el) || tries++ > 40) return;
            if (!resting(el) || !inView(el) || document.hidden) { setTimeout(wait, 500); return; }
            try { localStorage.setItem(key, '1'); } catch (e) { }
            trick(el, name);
        })();
    }

    function attach(el) {
        if (el.__hm) return;
        el.__hm = {};
        hosts.push(el);
        setTimeout(function () { reactOnce(el); }, 1200);
        el.addEventListener('pointerenter', function (e) {
            if (e.pointerType === 'touch' || off(el) || !has(el, 'hover')) return;
            el.classList.add('is-hover');
            if (Math.random() < .4) trick(el, 'heart');
        });
        el.addEventListener('pointerleave', function () { el.classList.remove('is-hover'); });
        schedule(el);
    }

    function scan() {
        hosts = hosts.filter(function (el) { return document.contains(el); });
        document.querySelectorAll('.hm-fx[data-hm-motion]').forEach(attach);
    }

    if (!reduce) {
        window.addEventListener('pointermove', function (e) {
            pointer = { x: e.clientX, y: e.clientY }; lastMove = Date.now();
            if (!frame) frame = requestAnimationFrame(look);
        }, { passive: true });
        ['pointerdown', 'keydown', 'scroll', 'touchstart'].forEach(function (t) { window.addEventListener(t, activity, { passive: true, capture: true }); });
        window.addEventListener('pointermove', function () { if (Date.now() - lastActivity > 1000) activity(); }, { passive: true });
        setInterval(function () { look(); dozeCheck(); }, 5000);
        var pending = 0;
        new MutationObserver(function () {
            if (pending) return;
            pending = setTimeout(function () { pending = 0; scan(); }, 150);
        }).observe(document.documentElement, { childList: true, subtree: true });
        if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', scan); else scan();
    }

    return {
        // Jednorazowa sztuczka: wiggle, hop, spin, curl, look, stretch, heart, talk, party, yawn.
        trick: function (el, name, ms) { if (el && el.closest) { el = el.classList && el.classList.contains('hm-fx') ? el : (el.closest('.hm-fx') || el.querySelector('.hm-fx')); } trick(el, name, ms); },
        scan: scan
    };
})();
