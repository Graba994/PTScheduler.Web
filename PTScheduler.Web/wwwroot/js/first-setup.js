// Konfigurator „Pierwsze ustawienia” (/witaj): zapamiętany krok (wraca się w to samo miejsce) i kod QR z adresem aplikacji.
window.ptFirstSetup = {
    load: function (key) { try { return localStorage.getItem(key) || ''; } catch (e) { return ''; } },
    save: function (key, value) { try { localStorage.setItem(key, value); } catch (e) { } },
    qr: function (el, text) {
        if (!el) return;
        function draw() {
            if (typeof qrcode !== 'function') return;
            var qr = qrcode(0, 'M');
            qr.addData(text);
            qr.make();
            el.innerHTML = qr.createSvgTag({ cellSize: 4, margin: 2, scalable: true });
        }
        if (typeof qrcode === 'function') { draw(); return; }
        var s = document.createElement('script');
        s.src = '/lib/qrcode-generator/qrcode.js';
        s.onload = draw;
        document.head.appendChild(s);
    }
};
