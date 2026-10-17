// Trwały bufor trwającego treningu po stronie przeglądarki.
// Blazor Server gubi stan komponentu przy zerwaniu połączenia / uśpieniu
// telefonu / zamknięciu karty — dlatego postęp treningu zapisujemy do
// localStorage przy KAŻDEJ zmianie. localStorage jest zapisywany na dysk
// synchronicznie, więc przetrwa wyłączenie telefonu, zamknięcie przeglądarki
// i zanik sieci. Sesja trwa aż do jawnego zakończenia (wtedy Blazor woła clear).
window.PTWorkout = {
    save: function (key, json) {
        try { localStorage.setItem(key, json); return true; }
        catch (e) { return false; }
    },
    load: function (key) {
        try { return localStorage.getItem(key); }
        catch (e) { return null; }
    },
    clear: function (key) {
        try { localStorage.removeItem(key); } catch (e) { }
    }
};
