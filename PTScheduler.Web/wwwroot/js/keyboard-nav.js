// Document-level keyboard shortcut bridge for Blazor pages.
// Pages register a .NET callback for a small set of "command" keys
// (ArrowLeft/ArrowRight/Escape). The bridge:
//   - Ignores events while the user is typing in a form field (input,
//     textarea, select, contenteditable) so we never steal typing.
//   - Ignores events with Ctrl/Alt/Meta modifiers so browser shortcuts
//     (Cmd+Left "Back", Alt+Left, etc.) keep working.
//   - Allows only one registered listener at a time — re-registering
//     replaces the previous one.

window.PTKeyboard = (() => {
    let handler = null;

    const FORWARDED_KEYS = new Set(['ArrowLeft', 'ArrowRight', 'Escape']);

    function isTypingTarget(el) {
        if (!el) return false;
        if (el.isContentEditable) return true;
        const tag = (el.tagName || '').toLowerCase();
        return tag === 'input' || tag === 'textarea' || tag === 'select';
    }

    function listen(dotnetRef, methodName) {
        unlisten(); // make registration idempotent — re-entering the page replaces

        handler = (e) => {
            if (!FORWARDED_KEYS.has(e.key)) return;
            if (e.ctrlKey || e.altKey || e.metaKey) return;
            if (isTypingTarget(e.target)) return;

            e.preventDefault();
            dotnetRef.invokeMethodAsync(methodName, e.key);
        };
        document.addEventListener('keydown', handler);
    }

    function unlisten() {
        if (handler) {
            document.removeEventListener('keydown', handler);
            handler = null;
        }
    }

    return { listen, unlisten };
})();

// Menu „+” (QuickAdd): zamknij po wyborze i po kliknięciu obok.
document.addEventListener('click', e => {
    document.querySelectorAll('details.qa[open]').forEach(d => {
        if (!d.contains(e.target) || e.target.closest('.qa-menu a')) d.removeAttribute('open');
    });
});
document.addEventListener('keydown', e => {
    if (e.key === 'Escape') document.querySelectorAll('details.qa[open]').forEach(d => d.removeAttribute('open'));
});

// Profil klienta na telefonie: karty przeglądu zwinięte do nagłówka (z najważniejszą liczbą), rozwijane dotknięciem.
document.addEventListener('click', function (e) {
    if (window.innerWidth >= 768) return;
    var head = e.target.closest('.cp-overview .cp-card-title, .cp-overview .cp-card-head');
    if (!head || e.target.closest('a, button, input, select, textarea, label')) return;
    var card = head.closest('.cp-card');
    if (!card) return;
    card.classList.toggle('is-open');
    var title = card.querySelector('.cp-card-title');
    if (title) title.setAttribute('aria-expanded', card.classList.contains('is-open') ? 'true' : 'false');
});

// Przewinięcie do sekcji, która może dopiero się wczytywać (np. karta ładująca dane po zmianie zakładki).
window.PTScrollTo = function (id) {
    let tries = 0;
    (function attempt() {
        const el = document.getElementById(id);
        if (el) {
            el.classList.add('pt-flash');
            el.scrollIntoView({ behavior: 'smooth', block: 'start' });
            setTimeout(() => el.classList.remove('pt-flash'), 1600);
        } else if (tries++ < 30) {
            setTimeout(attempt, 100);
        }
    })();
};
