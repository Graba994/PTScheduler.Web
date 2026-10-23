// Ciche wznawianie połączenia z serwerem (Blazor Server).
// Telefon usypia PWA albo kartę przeglądarki i zrywa połączenie. Zamiast okna „Łączę z serwerem…”:
//  - Blazor łączy się sam od razu po powrocie do aplikacji (szybkie próby ustawione w blazor-start.js),
//  - gdy serwer nie pamięta już sesji, wznawiamy ją (Blazor.resumeCircuit) albo po cichu przeładowujemy stronę,
//  - cienki pasek u góry pojawia się dopiero, gdy łączenie trwa dłużej niż 1,5 s,
//  - „Brak internetu” — tylko gdy połączenia naprawdę nie ma.
const modal = document.getElementById("components-reconnect-modal");
const bar = document.getElementById("pt-reconnect-bar");
const pill = document.getElementById("pt-offline-pill");

let down = false;
let failed = false;
let busy = false;
let barTimer = 0;
let pillTimer = 0;

modal.addEventListener("components-reconnect-state-changed", event => {
    switch (event.detail.state) {
        case "show":
        case "retrying":
            connectionLost();
            break;
        case "hide":
            connectionRestored();
            break;
        case "failed":
            // Blazor przestał próbować — próbujemy dalej sami, bez okna.
            failed = true;
            showPillAfter(0);
            break;
        case "rejected":
        case "paused":
            resumeOrReload();
            break;
        case "resume-failed":
            location.reload();
            break;
    }
});

function connectionLost() {
    if (down) return;
    down = true;
    clearTimeout(barTimer);
    barTimer = setTimeout(() => down && bar.classList.add("on"), 1500);
    showPillAfter(navigator.onLine ? 12000 : 3000);
}

function showPillAfter(ms) {
    clearTimeout(pillTimer);
    pillTimer = setTimeout(() => down && pill.classList.add("on"), ms);
}

function connectionRestored() {
    down = false;
    failed = false;
    clearTimeout(barTimer);
    clearTimeout(pillTimer);
    bar.classList.remove("on");
    pill.classList.remove("on");
}

// Serwer nie zna już tej sesji (dłuższa przerwa, aktualizacja): wznowienie zapisanego stanu albo ciche przeładowanie.
async function resumeOrReload() {
    if (busy) return;
    busy = true;
    try {
        if (await Blazor.resumeCircuit()) {
            busy = false;
            connectionRestored();
            return;
        }
    } catch {
        // wznowienie niemożliwe — przeładowanie niżej
    }
    location.reload();
}

// Po wyczerpaniu prób Blazora: kolejna próba, gdy aplikacja wraca na ekran albo wraca internet.
async function reconnectNow() {
    if (!failed || busy || document.visibilityState !== "visible") return;
    busy = true;
    try {
        const ok = await Blazor.reconnect(); // true — połączono, false — serwer nie zna już sesji
        busy = false;
        if (ok) connectionRestored();
        else await resumeOrReload();
    } catch {
        busy = false; // serwer dalej niedostępny — spróbujemy przy następnej okazji
    }
}

document.addEventListener("visibilitychange", reconnectNow);
window.addEventListener("online", reconnectNow);
window.addEventListener("focus", reconnectNow);
setInterval(reconnectNow, 10000);
