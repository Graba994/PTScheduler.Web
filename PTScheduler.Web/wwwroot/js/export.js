window.PTExport = {
    downloadCsv: function (fileName, bytes) {
        var mime = fileName.endsWith('.pdf') ? 'application/pdf' : 'text/csv;charset=utf-8;';
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
