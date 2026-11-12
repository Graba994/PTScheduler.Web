window.PTExport = {
    downloadCsv: function (fileName, bytes) {
        var mime = fileName.endsWith('.pdf') ? 'application/pdf'
            : fileName.endsWith('.zip') ? 'application/zip'
            : fileName.endsWith('.ics') ? 'text/calendar;charset=utf-8;'
            : 'text/csv;charset=utf-8;';
        var blob = new Blob([new Uint8Array(bytes)], { type: mime });
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }
};

// Porównanie zdjęć sylwetki: dwa zdjęcia obok siebie z datami na jednym obrazie — do zapisania albo udostępnienia.
window.PTPhotoCompare = {
    _load: function (src) {
        return new Promise(function (resolve, reject) {
            var img = new Image();
            img.onload = function () { resolve(img); };
            img.onerror = reject;
            img.src = src;
        });
    },
    _render: async function (beforeUrl, afterUrl, beforeLabel, afterLabel, middle) {
        var imgs = await Promise.all([this._load(beforeUrl), this._load(afterUrl)]);
        var colW = 720, pad = 16, footer = 96;
        // Wspólna wysokość: zdjęcia skalujemy do szerokości kolumny, bierzemy wyższe (maks. 1080).
        var h = Math.min(1080, Math.max.apply(null, imgs.map(function (i) { return Math.round(i.naturalHeight * colW / i.naturalWidth); })));
        var canvas = document.createElement('canvas');
        canvas.width = colW * 2 + pad * 3;
        canvas.height = h + pad * 2 + footer;
        var ctx = canvas.getContext('2d');
        ctx.fillStyle = '#111827';
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        imgs.forEach(function (img, i) {
            var x = pad + i * (colW + pad);
            var scale = Math.min(colW / img.naturalWidth, h / img.naturalHeight);
            var w = img.naturalWidth * scale, ih = img.naturalHeight * scale;
            ctx.fillStyle = '#1f2937';
            ctx.fillRect(x, pad, colW, h);
            ctx.drawImage(img, x + (colW - w) / 2, pad + (h - ih) / 2, w, ih);
        });
        var y = pad * 2 + h;
        ctx.textBaseline = 'middle';
        ctx.fillStyle = '#ffffff';
        ctx.font = '600 30px system-ui, -apple-system, "Segoe UI", sans-serif';
        ctx.textAlign = 'left';
        ctx.fillText(beforeLabel, pad + 8, y + footer / 2 - 6);
        ctx.textAlign = 'right';
        ctx.fillText(afterLabel, canvas.width - pad - 8, y + footer / 2 - 6);
        if (middle) {
            ctx.textAlign = 'center';
            ctx.fillStyle = '#93c5fd';
            ctx.font = '700 34px system-ui, -apple-system, "Segoe UI", sans-serif';
            ctx.fillText(middle, canvas.width / 2, y + footer / 2 - 6);
        }
        return await new Promise(function (resolve) { canvas.toBlob(resolve, 'image/jpeg', 0.9); });
    },
    // mode: 'share' (systemowe „Udostępnij”, jeśli telefon pozwala) albo 'save'. Zwraca: shared | saved | cancelled | error.
    build: async function (beforeUrl, afterUrl, beforeLabel, afterLabel, middle, fileName, mode) {
        try {
            var blob = await this._render(beforeUrl, afterUrl, beforeLabel, afterLabel, middle);
            var file = new File([blob], fileName, { type: 'image/jpeg' });
            if (mode === 'share' && navigator.canShare && navigator.canShare({ files: [file] })) {
                try { await navigator.share({ files: [file], title: 'Przed i po' }); return 'shared'; }
                catch (e) { return e && e.name === 'AbortError' ? 'cancelled' : 'error'; }
            }
            var url = URL.createObjectURL(blob);
            var a = document.createElement('a');
            a.href = url; a.download = fileName;
            document.body.appendChild(a); a.click(); document.body.removeChild(a);
            setTimeout(function () { URL.revokeObjectURL(url); }, 2000);
            return 'saved';
        } catch (e) { return 'error'; }
    }
};
