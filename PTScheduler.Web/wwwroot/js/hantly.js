// Asystent-maskotka (HantlyAssistant.razor): zamknięte podpowiedzi i wyciszenie zapamiętujemy w tej przeglądarce.
window.PTHantly = (function () {
    var KEY = 'pt-hantly';
    return {
        load: function () { try { return localStorage.getItem(KEY) || ''; } catch (e) { return ''; } },
        save: function (json) { try { localStorage.setItem(KEY, json); } catch (e) { } }
    };
})();
