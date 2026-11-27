// Robot od zrzutów aplikacji Hantly – do reklam z prawdziwymi ekranami.
// Wejście bez hasła: Portal wydaje jednorazowe linki SSO na konto demo (trener i klient, dane fikcyjne),
// robot otwiera je i od razu jest zalogowany (tryb "sso"). Tryb "public" = strony publiczne bez logowania.
// RODO: na zrzutach wyłącznie dane demo; e-maile kont podane w hideEmails są podmieniane przed zrzutem.
import fs from "node:fs/promises";
import path from "node:path";

const CHROME = process.env.CHROME_PATH
  ?? "/app/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell";
const HIDE_CSS = `.demo-bar,.admin-role-banner,.pwa-banner,#blazor-error-ui,#components-reconnect-modal,.toast-container,[class*="cookie"]{display:none!important}
*{caret-color:transparent!important;scroll-behavior:auto!important}`;
const UA = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1";

const nameOf = (p) => (p.replace(/^\/+|\/+$/g, "").replace(/\{[^}]+\}/g, "id").replace(/[^A-Za-z0-9]+/g, "-").toLowerCase() || "start");
const patternRe = (p) =>
  new RegExp("^" + p.split(/(\{[^}]+\})/).map((part) =>
    part.startsWith("{") ? (part.includes(":int") ? "\\d+" : "[^/?#]+") : part.replace(/[.*+?^$()|[\]\\]/g, "\\$&")).join("") + "$");
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const fail = (msg) => Object.assign(new Error(msg), { status: 422 });

async function preparePage(browser, width, height) {
  const ctx = await browser.createBrowserContext();
  const page = await ctx.newPage();
  await page.setUserAgent(UA);
  await page.setExtraHTTPHeaders({ "Accept-Language": "pl-PL,pl;q=0.9" });
  await page.setViewport({ width, height, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  await page.emulateMediaFeatures([
    { name: "prefers-reduced-motion", value: "reduce" },
    { name: "prefers-color-scheme", value: "light" },
  ]);
  return { ctx, page };
}

async function settle(page) {
  await page.waitForNetworkIdle({ idleTime: 600, timeout: 15000 }).catch(() => {});
  await page.addStyleTag({ content: HIDE_CSS }).catch(() => {});
  await sleep(1400); // Blazor: dorysowanie po połączeniu obwodu
}

/** Wejście do aplikacji dla roli; zwraca origin, pod którym robot dalej chodzi po trasach. */
async function enter(page, { mode, startUrl, entries }, role, log) {
  if (mode === "public") return new URL(startUrl).origin; // strony publiczne (np. cennik), bez logowania
  const url = entries?.[role];
  if (!url) throw fail(`Brak linku wejścia dla widoku ${role === "client" ? "klienta" : "trenera"}.`);
  await page.goto(url, { waitUntil: "domcontentloaded", timeout: 30000 });
  await settle(page);
  const now = new URL(page.url());
  if (/\/Account\/(Login|AccessDenied|Lockout)/i.test(now.pathname) || /\/demo\/start/i.test(now.pathname))
    throw fail("Link wejścia na konto demo nie zadziałał (wygasł albo został już użyty) – spróbuj jeszcze raz.");
  log(`screeny: wejście (${role === "client" ? "klient" : "trener"}) → ${now.origin}`);
  return now.origin;
}

/** Pliki widoku klienta kończą się na „-klient” (także „-klient-2”), reszta to widok trenera. */
export const roleOfFile = (file) => (/-klient(-\d+)?\.png$/.test(file) ? "client" : "trainer");

/**
 * pages: [{path, as?: "client"}] – ścieżki z {param} (np. /clients/{Id:int}) są rozwiązywane
 * z linków znalezionych na wcześniej odwiedzonych stronach.
 * role: "trainer" | "client" – tylko jeden widok; podmieniane są wyłącznie jego pliki (drugi widok zostaje).
 * Portal woła osobno dla trenera i klienta, żeby każdy jednorazowy link (ważny 2 min) był świeży.
 */
export async function captureScreens(DATA, opts, log) {
  const { dir, mode = "sso", startUrl, entries, pages = [], width = 390, height = 844, maxHeight = 3 } = opts;
  const only = opts.role === "client" || opts.role === "trainer" ? opts.role : null;
  const hideEmails = (Array.isArray(opts.hideEmails) ? opts.hideEmails : []).filter((e) => typeof e === "string" && e.includes("@"));
  const puppeteer = (await import("puppeteer-core")).default;
  const outDir = path.join(DATA, "sources", dir, "screens");
  const tmpDir = outDir + ".tmp";
  await fs.rm(tmpDir, { recursive: true, force: true });
  await fs.mkdir(tmpDir, { recursive: true });
  const browser = await puppeteer.launch({
    executablePath: CHROME, headless: true,
    args: ["--no-sandbox", "--lang=pl-PL", "--hide-scrollbars"],
  });
  const shots = [], skipped = [];
  try {
    for (const role of only ? [only] : ["trainer", "client"]) {
      const list = pages.filter((p) => (p.as === "client" ? "client" : "trainer") === role);
      if (!list.length) continue;
      if (mode === "sso" && !entries?.[role]) {
        skipped.push(...list.map((p) => ({ path: p.path, reason: `brak wejścia dla widoku ${role === "client" ? "klienta" : "trenera"}` })));
        continue;
      }
      const { ctx, page } = await preparePage(browser, width, height);
      try {
        const origin = await enter(page, { mode, startUrl, entries }, role, log);
        const links = new Set();
        const collect = async () => {
          const hrefs = await page.$$eval("a[href]", (as) => as.map((a) => new URL(a.href, location.href).pathname)).catch(() => []);
          hrefs.forEach((h) => links.add(h));
        };
        await collect();
        // najpierw zwykłe ścieżki (zbierają linki), potem te z parametrem
        const ordered = [...list.filter((p) => !p.path.includes("{")), ...list.filter((p) => p.path.includes("{"))];
        for (const item of ordered) {
          let target = item.path;
          if (target.includes("{")) {
            const re = patternRe(target);
            target = [...links].find((h) => re.test(h));
            if (!target) { skipped.push({ path: item.path, reason: "brak linku do przykładowej strony" }); continue; }
          }
          if (shots.some((s) => s.path === target)) continue; // ta sama strona z innego wzorca
          try {
            const resp = await page.goto(origin + target, { waitUntil: "domcontentloaded", timeout: 30000 });
            if (resp && resp.status() >= 400) { skipped.push({ path: item.path, reason: `HTTP ${resp.status()}` }); continue; }
            await settle(page);
            const final = new URL(page.url());
            if (/\/Account\/(Login|AccessDenied)|\/not-found/i.test(final.pathname) || (target !== "/" && final.pathname === "/")) {
              skipped.push({ path: item.path, reason: `przekierowanie na ${final.pathname}` });
              continue;
            }
            await collect();
            await page.evaluate(() => window.scrollTo(0, 0));
            const full = await page.evaluate(() => Math.max(document.documentElement.scrollHeight, document.body.scrollHeight));
            const h = Math.min(Math.max(full, height), height * maxHeight);
            const stem = nameOf(item.path) + (role === "client" ? "-klient" : "");
            let file = stem;
            for (let n = 2; shots.some((s) => s.file === file + ".png"); n++) file = stem + "-" + n;
            file += ".png";
            // prywatność: e-maile kont nigdzie na zrzucie (także w polach formularzy)
            if (hideEmails.length) await page.evaluate((mails) => {
              const esc = (m) => m.replace(/[^a-z0-9]/gi, (c) => "\\" + c);
              const re = new RegExp(mails.map(esc).join("|"), "gi");
              const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
              for (let n = w.nextNode(); n; n = w.nextNode()) {
                const v = n.nodeValue.replace(re, "trener@hantly.pl");
                if (v !== n.nodeValue) n.nodeValue = v;
              }
              document.querySelectorAll("input").forEach((i) => { i.value = i.value.replace(re, "trener@hantly.pl"); });
            }, hideEmails);
            const thumb = Buffer.from(await page.screenshot({ type: "jpeg", quality: 55, clip: { x: 0, y: 0, width, height } })).toString("base64");
            // wysoki screen: okno tak wysokie jak strona – elementy fixed (dolne menu, schowane panele) zostają na swoich miejscach
            if (h > height) {
              await page.setViewport({ width, height: h, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
              await sleep(700);
            }
            await page.screenshot({ path: path.join(tmpDir, file), type: "png", clip: { x: 0, y: 0, width, height: h } });
            if (h > height) await page.setViewport({ width, height, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
            const info = await page.evaluate(() => ({
              title: document.title,
              text: (document.querySelector("main") ?? document.body).innerText.replace(/\s+/g, " ").slice(0, 500),
            }));
            shots.push({ file, path: target, pattern: item.path, as: role, w: width * 2, h: h * 2, title: info.title, text: info.text, thumb });
            log(`screeny: ${target} → ${file} (${h}px)`);
          } catch (e) {
            skipped.push({ path: item.path, reason: String(e.message).slice(0, 120) });
          }
        }
      } finally {
        await ctx.close().catch(() => {});
      }
    }
  } finally {
    await browser.close().catch(() => {});
  }
  if (!shots.length) {
    await fs.rm(tmpDir, { recursive: true, force: true });
    throw fail("Nie udało się zrobić żadnego screena: " + skipped.slice(0, 3).map((s) => `${s.path}: ${s.reason}`).join("; "));
  }
  if (!only) {
    await fs.rm(outDir, { recursive: true, force: true });
    await fs.rename(tmpDir, outDir);
    return { shots, skipped };
  }
  // jeden widok: usuń jego stare pliki, przenieś nowe, pliki drugiego widoku zostają
  await fs.mkdir(outDir, { recursive: true });
  for (const f of await fs.readdir(outDir)) if (f.endsWith(".png") && roleOfFile(f) === only) await fs.rm(path.join(outDir, f), { force: true });
  for (const f of await fs.readdir(tmpDir)) await fs.rename(path.join(tmpDir, f), path.join(outDir, f));
  await fs.rm(tmpDir, { recursive: true, force: true });
  return { shots, skipped };
}
