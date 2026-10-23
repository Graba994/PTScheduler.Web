// Okienko „coś się kończy” (UpsellPopup.razor): pokazujemy, jeśli ta przeglądarka
// nie zamknęła go jeszcze w bieżącym okresie (klucz zmienia się co dzień / co 3 dni).
(function () {
    var PREFIX = 'pt-upsell-';

    function read(key) { try { return localStorage.getItem(PREFIX + key); } catch (e) { return null; } }
    function write(key) { try { localStorage.setItem(PREFIX + key, '1'); } catch (e) { } }

    function init() {
        var pop = document.querySelector('.ups-pop[data-upsell-key]');
        if (!pop || pop.dataset.bound) return;
        pop.dataset.bound = '1';
        var key = pop.getAttribute('data-upsell-key');
        if (read(key)) return;
        setTimeout(function () { pop.hidden = false; }, 1200);
        pop.querySelectorAll('[data-upsell-close]').forEach(function (el) {
            el.addEventListener('click', function () { write(key); pop.hidden = true; });
        });
        pop.addEventListener('click', function (e) { if (e.target === pop) { write(key); pop.hidden = true; } });
    }

    document.addEventListener('DOMContentLoaded', init);
    // Nawigacja Blazora (bez przeładowania strony) podmienia treść — sprawdzamy ponownie.
    var tries = 0;
    (function hook() {
        if (window.Blazor && window.Blazor.addEventListener) { window.Blazor.addEventListener('enhancedload', init); init(); }
        else if (tries++ < 50) setTimeout(hook, 100);
    })();
})();
