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
