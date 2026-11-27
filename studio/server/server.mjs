// Renderer Studia reklam Hantly: wewnętrzny serwis HTTP (tylko sieć Dockera „ptscheduler”, bez portu na zewnątrz).
//   GET  /health                                                                      {ok, rendering, progress, commit}
//   POST /tts            {slug, voice, credentials, scenes:[{index,text,regenerate}]} -> scene-N.mp3 + timeline.json
//   POST /preview-voice  {voice, credentials, text}                                 -> audio/mpeg
//   POST /render         {slug}                                                      -> video.mp4
//   POST /stills         {slug}                                                      -> klatki kontrolne (JPEG base64)
//   POST /post           {props}                                                     -> grafika posta 4:5 (PNG base64)
//   POST /screens        {dir, entries:{trainer?,client?} | startUrl, hideEmails?, pages} -> zrzuty aplikacji
//   GET  /files/episodes/<slug>/<file>, /files/library/<file>, /files/sources/<dir>/screens/<file>  (dla Chromium w trakcie renderu)
// Klucze API przychodzą w każdym żądaniu od Portalu – renderer niczego nie przechowuje.
// Dane: DATA_DIR (/data) = udział „hantly-studio” na serwerze, ten sam, który Portal ma w /opt/ptscheduler/studio.
import http from "node:http";
import fs from "node:fs/promises";
import { createReadStream } from "node:fs";
import path from "node:path";
import { synthesize } from "./tts.mjs";
import { captureScreens } from "./screens.mjs";

const PORT = Number(process.env.PORT ?? 3000);
const DATA = path.resolve(process.env.DATA_DIR ?? "/data");
const EPISODES = path.join(DATA, "episodes");
const CONCURRENCY = Number(process.env.RENDER_CONCURRENCY ?? 2);
const SELF = `http://127.0.0.1:${PORT}`;

const log = (...a) => console.log(new Date().toISOString(), ...a);
const safe = (name) => {
  if (!/^[A-Za-z0-9._-]+$/.test(name) || name === "." || name === "..") throw httpError(400, `Niedozwolona nazwa: ${name}`);
  return name;
};
const httpError = (status, message) => Object.assign(new Error(message), { status });
const episodeDir = (slug) => path.join(EPISODES, safe(slug));

const COMMIT = (process.env.RENDERER_BUILD_COMMIT ?? "").slice(0, 7) || null;
const compositionOf = (episode) => {
  if ((episode.format ?? "ad") !== "ad") throw httpError(400, `Nieznany format filmu: ${episode.format}`);
  return "Ad";
};

/* ---------------- Remotion (bundle raz, przy starcie) ---------------- */
let bundlePromise;
const getBundle = () =>
  (bundlePromise ??= (async () => {
    const { bundle } = await import("@remotion/bundler");
    log("Bundluję kompozycję…");
    const url = await bundle({ entryPoint: path.resolve(import.meta.dirname, "..", "src", "index.ts") });
    log("Bundle gotowy");
    return url;
  })().catch((e) => {
    bundlePromise = undefined;
    throw e;
  }));

// Jedno ciężkie zadanie naraz (render, klatki, post, zrzuty) – serwer ma 4 rdzenie i dzieli je z Portalem i studiami.
let renderChain = Promise.resolve();
const state = { rendering: null, progress: 0, busy: null, queued: 0, voicing: 0 };

/** Kolejka ciężkich zadań: jedno naraz. „busy” w /health mówi Guardianowi, że teraz nie wolno podmieniać kontenera. */
function enqueue(name, fn) {
  state.queued++;
  const job = renderChain.then(async () => {
    state.queued--;
    state.busy = name;
    try { return await fn(); } finally { state.busy = null; }
  });
  renderChain = job.catch(() => {});
  return job;
}

async function render(slug) {
  const dir = episodeDir(slug);
  const episode = JSON.parse(await fs.readFile(path.join(dir, "episode.json"), "utf8"));
  const timeline = JSON.parse(await fs.readFile(path.join(dir, "timeline.json"), "utf8"));
  if (!Array.isArray(timeline.audio) || timeline.audio.length !== episode.scenes.length)
    throw httpError(409, "Głos nie pasuje do scen – najpierw wygeneruj głos.");

  const { selectComposition, renderMedia } = await import("@remotion/renderer");
  const serveUrl = await getBundle();
  const inputProps = {
    episodeId: slug,
    timeline: { ...episode, audio: timeline.audio },
    assetBase: `${SELF}/files/episodes/${slug}/`,
  };
  const compositionId = compositionOf(episode);
  const composition = await selectComposition({ serveUrl, id: compositionId, inputProps });
  const tmp = path.join(dir, `video.tmp.mp4`);
  const started = Date.now();
  state.rendering = slug;
  state.progress = 0;
  let logged = 0;
  try {
    await renderMedia({
      composition,
      serveUrl,
      codec: "h264",
      crf: 20,
      outputLocation: tmp,
      inputProps,
      concurrency: CONCURRENCY,
      onProgress: ({ progress }) => {
        const p = Math.round(progress * 100);
        if (p >= logged + 10) { logged = p; log(`render ${slug}: ${p}%`); }
        state.progress = p;
      },
    });
  } finally {
    state.rendering = null;
  }
  await fs.rename(tmp, path.join(dir, "video.mp4"));
  const seconds = composition.durationInFrames / composition.fps;
  log(`render ${slug} gotowy: ${seconds.toFixed(1)} s filmu w ${((Date.now() - started) / 1000).toFixed(0)} s`);
  return { durationSeconds: seconds, renderSeconds: (Date.now() - started) / 1000 };
}


/* ---------------- Klatki kontrolne (dla kontroli jakości przez AI) ---------------- */
const TAIL_MS = 380, OUTRO_HOLD = 45, FPS = 30;
function sceneFrames(audio) {
  let cursor = 0;
  return audio.map((a, i) => {
    const last = a.words?.at(-1)?.endMs ?? a.durationMs;
    let frames = Math.ceil((Math.min(a.durationMs, last + TAIL_MS) / 1000) * FPS);
    if (i === audio.length - 1) frames += OUTRO_HOLD;
    const item = { from: cursor, frames };
    cursor += frames;
    return item;
  });
}

async function stills(slug) {
  const dir = episodeDir(slug);
  const episode = JSON.parse(await fs.readFile(path.join(dir, "episode.json"), "utf8"));
  const timeline = JSON.parse(await fs.readFile(path.join(dir, "timeline.json"), "utf8"));
  const { selectComposition, renderStill } = await import("@remotion/renderer");
  const serveUrl = await getBundle();
  const inputProps = { episodeId: slug, timeline: { ...episode, audio: timeline.audio }, assetBase: `${SELF}/files/episodes/${slug}/` };
  const id = compositionOf(episode);
  const composition = await selectComposition({ serveUrl, id, inputProps });
  const out = [];
  const tmp = path.join(dir, "_still.jpg");
  for (const [i, s] of sceneFrames(timeline.audio).entries()) {
    for (const ratio of [0.35, 0.8]) {
      const frame = Math.min(composition.durationInFrames - 1, s.from + Math.round(s.frames * ratio));
      await renderStill({ composition, serveUrl, inputProps, frame, output: tmp, imageFormat: "jpeg", jpegQuality: 78, scale: 0.5 });
      out.push({ scene: i, ratio, frame, jpeg: (await fs.readFile(tmp)).toString("base64") });
    }
  }
  await fs.rm(tmp, { force: true });
  return { stills: out };
}

/** Grafika posta promocyjnego (Still "Post", PNG 1080×1350) – props od panelu, wynik base64 */
async function post(props) {
  const { selectComposition, renderStill } = await import("@remotion/renderer");
  const serveUrl = await getBundle();
  if (typeof props.screenImage === "string" && /^sources\/[A-Za-z0-9._-]+\/screens\/[A-Za-z0-9._-]+\.png$/.test(props.screenImage))
    props = { ...props, screen: { ...(props.screen ?? {}), image: `${SELF}/files/${props.screenImage}` } };
  const composition = await selectComposition({ serveUrl, id: "Post", inputProps: props });
  const tmp = path.join(DATA, `_post-${Date.now()}.png`);
  try {
    await renderStill({ composition, serveUrl, inputProps: props, frame: 0, output: tmp, imageFormat: "png" });
    return { png: (await fs.readFile(tmp)).toString("base64") };
  } finally {
    await fs.rm(tmp, { force: true });
  }
}

/* ---------------- TTS ---------------- */
async function tts({ slug, voice, credentials, scenes }) {
  const dir = episodeDir(slug);
  await fs.mkdir(dir, { recursive: true });
  let previous = [];
  try {
    previous = JSON.parse(await fs.readFile(path.join(dir, "timeline.json"), "utf8")).audio ?? [];
  } catch {}

  const audio = [];
  for (const s of scenes) {
    const file = `scene-${s.index}.mp3`;
    const prev = previous[s.index];
    const exists = await fs.access(path.join(dir, file)).then(() => true, () => false);
    if (!s.regenerate && prev && exists) {
      audio[s.index] = prev;
      continue;
    }
    const r = await synthesize(voice, credentials, s.text);
    await fs.writeFile(path.join(dir, file), r.audio);
    audio[s.index] = { file, durationMs: r.durationMs, words: r.words };
    log(`tts ${slug} scena ${s.index}: ${(r.durationMs / 1000).toFixed(1)} s`);
  }
  const ordered = scenes.map((s) => audio[s.index]);
  await fs.writeFile(path.join(dir, "timeline.json"), JSON.stringify({ audio: ordered }, null, 2));
  return { scenes: ordered.map((a) => ({ durationMs: a.durationMs, words: a.words.length })) };
}

/* ---------------- HTTP ---------------- */
const readJson = async (req) => {
  const chunks = [];
  for await (const c of req) chunks.push(c);
  return JSON.parse(Buffer.concat(chunks).toString("utf8") || "{}");
};
const send = (res, status, body, type = "application/json") => {
  res.writeHead(status, { "content-type": type });
  res.end(type === "application/json" ? JSON.stringify(body) : body);
};

const MIME = { ".mp3": "audio/mpeg", ".mp4": "video/mp4", ".json": "application/json", ".png": "image/png" };

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, SELF);
  try {
    if (req.method === "GET" && url.pathname === "/health") return send(res, 200, { ok: true, ...state, commit: COMMIT });

    if (req.method === "GET" && url.pathname.startsWith("/files/library/")) {
      const file = safe(decodeURIComponent(url.pathname.split("/")[3] ?? ""));
      const p = path.join(DATA, "library", file);
      const st = await fs.stat(p).catch(() => null);
      if (!st) return send(res, 404, { error: "brak pliku" });
      const ext = path.extname(p).toLowerCase();
      const mime = { ".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".webp": "image/webp", ".svg": "image/svg+xml" }[ext] ?? "application/octet-stream";
      res.writeHead(200, { "content-type": mime, "content-length": st.size, "access-control-allow-origin": "*" });
      return createReadStream(p).pipe(res);
    }

    if (req.method === "GET" && url.pathname.startsWith("/files/sources/")) {
      const [, , , dir, sub, file] = url.pathname.split("/");
      if (sub !== "screens") return send(res, 404, { error: "brak pliku" });
      const p = path.join(DATA, "sources", safe(decodeURIComponent(dir ?? "")), "screens", safe(decodeURIComponent(file ?? "")));
      const st = await fs.stat(p).catch(() => null);
      if (!st) return send(res, 404, { error: "brak pliku" });
      res.writeHead(200, { "content-type": "image/png", "content-length": st.size, "access-control-allow-origin": "*", "cache-control": "no-store" });
      return createReadStream(p).pipe(res);
    }

    if (req.method === "GET" && url.pathname.startsWith("/files/episodes/")) {
      const [, , , slug, file] = url.pathname.split("/");
      const p = path.join(episodeDir(decodeURIComponent(slug)), safe(decodeURIComponent(file)));
      const st = await fs.stat(p).catch(() => null);
      if (!st) return send(res, 404, { error: "brak pliku" });
      res.writeHead(200, {
        "content-type": MIME[path.extname(p)] ?? "application/octet-stream",
        "content-length": st.size,
        "access-control-allow-origin": "*",
        "cache-control": "no-store",
      });
      return createReadStream(p).pipe(res);
    }

    if (req.method === "POST" && url.pathname === "/tts") {
      const body = await readJson(req);
      state.voicing++; // głos nie czeka w kolejce renderu (to tylko zapytania do TTS), ale też blokuje podmianę kontenera
      try { return send(res, 200, await tts(body)); } finally { state.voicing--; }
    }

    if (req.method === "POST" && url.pathname === "/preview-voice") {
      const { voice, credentials, text } = await readJson(req);
      const r = await synthesize(voice, credentials, (text ?? "").slice(0, 400));
      return send(res, 200, r.audio, "audio/mpeg");
    }

    if (req.method === "POST" && url.pathname === "/stills") {
      const { slug } = await readJson(req);
      return send(res, 200, await enqueue("stills", () => stills(slug)));
    }

    if (req.method === "POST" && url.pathname === "/post") {
      const { props } = await readJson(req);
      return send(res, 200, await enqueue("post", () => post(props ?? {})));
    }

    if (req.method === "POST" && url.pathname === "/screens") {
      const body = await readJson(req);
      safe(body.dir ?? "");
      const urls = [body.startUrl, body.entries?.trainer, body.entries?.client].filter(Boolean);
      if (!urls.length || !urls.every((u) => /^https?:\/\//.test(u))) throw httpError(400, "Brak adresu wejścia do aplikacji");
      return send(res, 200, await enqueue("screens", () => captureScreens(DATA, body, log)));
    }

    if (req.method === "POST" && url.pathname === "/render") {
      const { slug } = await readJson(req);
      return send(res, 200, await enqueue("render", () => render(slug)));
    }

    send(res, 404, { error: "nie ma takiej ścieżki" });
  } catch (e) {
    log("BŁĄD", url.pathname, e.message);
    if (!res.headersSent) send(res, e.status ?? 500, { error: e.message });
    else res.end();
  }
});

server.requestTimeout = 0; // render trwa kilka minut
server.headersTimeout = 60_000;
server.listen(PORT, () => {
  log(`Renderer nasłuchuje na :${PORT}, dane: ${DATA}`);
  getBundle().catch((e) => log("Bundle nieudany:", e.message));
});
