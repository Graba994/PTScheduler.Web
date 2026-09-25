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

// ── Klucze dostępu (odcisk palca / Face ID) ─────────────────────────────────
// Stan „na tym urządzeniu” trzymamy w localStorage: serwer nie wie, na którym
// urządzeniu leży który klucz. Wszystko w try/catch — tryb prywatny, zablokowane dane.
window.PTPasskey = (function () {
    var KEY = 'pts.passkey';
    var DISMISS_DAYS = 30;

    function get() { try { return localStorage.getItem(KEY) || ''; } catch (e) { return ''; } }
    function set(v) { try { localStorage.setItem(KEY, v); } catch (e) { } }

    function supported() {
        return typeof navigator.credentials !== 'undefined' &&
            typeof window.PublicKeyCredential !== 'undefined' &&
            typeof PublicKeyCredential.parseCreationOptionsFromJSON === 'function' &&
            typeof PublicKeyCredential.parseRequestOptionsFromJSON === 'function';
    }

    function isStandalone() {
        return window.navigator.standalone === true || window.matchMedia('(display-mode: standalone)').matches;
    }

    return {
        supported: supported,
        isEnrolledHere: function () { return get() === 'enrolled'; },
        markEnrolled: function () { set('enrolled'); },
        dismiss: function () { set('dismissed:' + Date.now()); },
        // Propozycja po zalogowaniu: tylko telefon/PWA z czytnikiem biometrii, bez klucza tutaj,
        // nie odrzucona w ostatnich 30 dniach i nie w trakcie baneru instalacji PWA.
        shouldOffer: async function () {
            if (!supported()) return false;
            var s = get();
            if (s === 'enrolled') return false;
            if (s.indexOf('dismissed:') === 0 && Date.now() - parseInt(s.slice(10), 10) < DISMISS_DAYS * 864e5) return false;
            if (!(isStandalone() || window.matchMedia('(pointer: coarse)').matches)) return false;
            if (document.querySelector('.pwa-banner')) return false;
            // Raz na sesję przeglądarki — nie wraca przy każdej zmianie strony.
            try { if (sessionStorage.getItem(KEY + '.shown')) return false; } catch (e) { }
            var ok = false;
            try { ok = await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable(); }
            catch (e) { return false; }
            if (ok) { try { sessionStorage.setItem(KEY + '.shown', '1'); } catch (e) { } }
            return ok;
        }
    };
})();

(function () {
    // Strona logowania: jeśli to urządzenie ma klucz, najpierw „Zaloguj odciskiem palca”,
    // formularz hasła schowany pod „Zaloguj hasłem”.
    function applyLoginMode() {
        if (document.querySelector('[data-passkey-enrolled]')) window.PTPasskey.markEnrolled();

        var card = document.querySelector('[data-login-card]');
        if (!card || card.dataset.modeApplied === '1') return;
        card.dataset.modeApplied = '1';
        if (!window.PTPasskey.supported() || !window.PTPasskey.isEnrolledHere()) return;
        card.classList.add('passkey-first');
        // Po błędzie (np. klucz anulowany) od razu pokaż też hasło.
        if (card.hasAttribute('data-login-error')) card.classList.add('show-password');
    }

    document.addEventListener('click', function (e) {
        if (e.target.closest('[data-show-password-login]')) {
            var card = document.querySelector('[data-login-card]');
            if (card) {
                card.classList.add('show-password');
                var email = card.querySelector('input[type=email]');
                if (email) email.focus();
            }
        }
        if (e.target.closest('[data-passkey-dismiss]')) window.PTPasskey.dismiss();
    });

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', applyLoginMode);
    else applyLoginMode();
    if (window.Blazor && typeof window.Blazor.addEventListener === 'function')
        window.Blazor.addEventListener('enhancedload', applyLoginMode);
})();
