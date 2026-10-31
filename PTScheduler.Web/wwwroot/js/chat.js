// Czat: przewijanie listy wiadomości i śledzenie, czy karta jest widoczna
// (wiadomości oznaczamy jako przeczytane tylko, gdy ktoś faktycznie patrzy).
window.ptChat = {
    scrollBottom: function (el) {
        if (el) el.scrollTop = el.scrollHeight;
    },
    isVisible: function () {
        return document.visibilityState === 'visible';
    },
    watchVisibility: function (dotnet) {
        window.ptChat.unwatch();
        const handler = function () {
            if (document.visibilityState === 'visible') dotnet.invokeMethodAsync('OnPageVisible').catch(function () { });
        };
        document.addEventListener('visibilitychange', handler);
        window.ptChat._handler = handler;
    },
    unwatch: function () {
        if (window.ptChat._handler) document.removeEventListener('visibilitychange', window.ptChat._handler);
        window.ptChat._handler = null;
    }
};

// Udostępnianie linku (menu systemowe na telefonie, kopiowanie na komputerze).
window.ptShare = {
    share: async function (title, text, url) {
        if (navigator.share) {
            try { await navigator.share({ title: title, text: text, url: url }); return 'shared'; }
            catch (e) { if (e && e.name === 'AbortError') return 'aborted'; }
        }
        return (await window.ptShare.copy(url)) ? 'copied' : 'failed';
    },
    copy: async function (text) {
        try { await navigator.clipboard.writeText(text); return true; }
        catch (e) { return false; }
    }
};
