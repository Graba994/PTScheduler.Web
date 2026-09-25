// Drobne usprawnienia formularzy logowania/rejestracji (strony SSR, bez interaktywności Blazora).
// Delegacja zdarzeń na document, więc działa też po nawigacji wzmocnionej.
(function () {
    // Przycisk „pokaż hasło”: <button data-toggle-password="id-pola">
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-toggle-password]');
        if (!btn) return;
        var input = document.getElementById(btn.getAttribute('data-toggle-password'));
        if (!input) return;
        var show = input.type === 'password';
        input.type = show ? 'text' : 'password';
        var label = show ? 'Ukryj hasło' : 'Pokaż hasło';
        btn.setAttribute('aria-label', label);
        btn.title = label;
        var icon = btn.querySelector('i');
        if (icon) icon.className = show ? 'bi bi-eye-slash' : 'bi bi-eye';
        input.focus();
    });

    // Blokada podwójnego wysłania: <form data-busy-on-submit> + <button type=submit data-busy-text="…">
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-busy-on-submit')) return;
        // Przycisk klucza dostępu sam przechwytuje wysłanie (WebAuthn) — nie ruszamy go.
        if (e.defaultPrevented) return;
        if (form.dataset.busy === '1') { e.preventDefault(); return; }
        form.dataset.busy = '1';
        var btn = form.querySelector('button[type=submit][data-busy-text]');
        if (!btn) return;
        btn.dataset.idleHtml = btn.innerHTML;
        // Po bieżącym zdarzeniu, żeby przeglądarka zdążyła wysłać formularz z tym przyciskiem.
        setTimeout(function () {
            btn.disabled = true;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2" aria-hidden="true"></span>' + btn.getAttribute('data-busy-text');
        }, 0);
    });

    // Powrót „wstecz” z pamięci podręcznej przeglądarki — odblokuj formularze.
    window.addEventListener('pageshow', function () {
        document.querySelectorAll('form[data-busy-on-submit]').forEach(function (form) {
            delete form.dataset.busy;
            var btn = form.querySelector('button[type=submit][data-busy-text]');
            if (btn && btn.dataset.idleHtml) {
                btn.disabled = false;
                btn.innerHTML = btn.dataset.idleHtml;
            }
        });
    });
})();
