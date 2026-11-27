// Tabele z klasą .table-stack: na telefonie każdy wiersz to karta „etykieta: wartość”.
// Etykiety biorą się z nagłówków kolumn (data-label), więc strony nie muszą ich powtarzać.
// Blazor przerysowuje tabele, dlatego obserwujemy zmiany w dokumencie.
(function () {
    'use strict';
    function label(table) {
        const heads = [];
        table.querySelectorAll(':scope > thead > tr:first-child > th').forEach(th => {
            const span = parseInt(th.getAttribute('colspan') || '1', 10);
            for (let i = 0; i < span; i++) heads.push(th.textContent.trim());
        });
        if (!heads.length) return;
        table.querySelectorAll(':scope > tbody > tr').forEach(tr => {
            let col = 0;
            for (const td of tr.children) {
                const text = heads[col] || '';
                if (!td.hasAttribute('data-label') || td.getAttribute('data-label') !== text) td.setAttribute('data-label', text);
                // Długi tekst (opis, szczegóły): etykieta nad wartością zamiast poszarpanego wyrównania do prawej.
                const long = td.textContent.trim().length > 42 && !td.querySelector('button, .btn');
                if (td.classList.contains('stack-long') !== long) td.classList.toggle('stack-long', long);
                col += parseInt(td.getAttribute('colspan') || '1', 10);
            }
        });
    }
    let pending = false;
    function run() {
        pending = false;
        document.querySelectorAll('table.table-stack').forEach(label);
    }
    function schedule() {
        if (pending) return;
        pending = true;
        requestAnimationFrame(run);
    }
    new MutationObserver(schedule).observe(document.documentElement, { childList: true, subtree: true });
    document.addEventListener('DOMContentLoaded', schedule);
    schedule();
})();
