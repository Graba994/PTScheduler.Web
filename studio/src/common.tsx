import React, { useMemo } from "react";
import { AbsoluteFill, Audio, Easing, interpolate, spring, useCurrentFrame, useVideoConfig } from "remotion";
import { useAudioData, visualizeAudio } from "@remotion/media-utils";
import { loadFont } from "@remotion/google-fonts/Montserrat";
import { FPS, Timeline, Word } from "./types";

/** Wspólne klocki reklamy i posta: czcionka, długości scen, napisy karaoke, błysk, ruch ust, muzyka. */

export const { fontFamily } = loadFont("normal", {
  weights: ["600", "700", "800", "900"],
  subsets: ["latin", "latin-ext"],
});
export const EMOJI = `"Segoe UI Emoji","Noto Color Emoji","Apple Color Emoji",sans-serif`;

const TAIL_MS = 380; // ile ciszy zostawić po ostatnim słowie sceny
const OUTRO_HOLD = 45; // dodatkowe klatki na końcu filmu

/** Długość sceny = koniec ostatniego słowa + 380 ms (ostatnia scena +45 klatek). Ten sam wzór ma server.mjs (klatki kontrolne). */
export const sceneLayout = (t: Pick<Timeline, "audio">) => {
  let cursor = 0;
  const items = t.audio.map((a, i) => {
    const last = a.words.at(-1)?.endMs ?? a.durationMs;
    const ms = Math.min(a.durationMs, last + TAIL_MS);
    let frames = Math.ceil((ms / 1000) * FPS);
    if (i === t.audio.length - 1) frames += OUTRO_HOLD;
    const item = { from: cursor, frames };
    cursor += frames;
    return item;
  });
  return { items, total: Math.max(cursor, 1) };
};

/* ------------------------------ NAPISY ------------------------------ */

const paginate = (words: Word[]) => {
  const pages: Word[][] = [];
  let cur: Word[] = [];
  for (const w of words) {
    const prev = cur.at(-1);
    const chars = cur.reduce((s, x) => s + x.text.length + 1, 0);
    if (cur.length && (cur.length >= 4 || chars + w.text.length > 22 || (prev && w.startMs - prev.endMs > 220))) {
      pages.push(cur);
      cur = [];
    }
    cur.push(w);
  }
  if (cur.length) pages.push(cur);
  return pages;
};

/** Napisy karaoke z czasów słów: po kilka słów, aktualne słowo podświetlone kolorem akcentu. */
export const Captions: React.FC<{ words: Word[]; accent: string; top?: number; size?: number; keywords?: RegExp }> = ({
  words, accent, top = 880, size = 78, keywords,
}) => {
  const f = useCurrentFrame();
  const ms = (f / FPS) * 1000;
  const pages = useMemo(() => paginate(words), [words]);
  if (words.length && ms > words[words.length - 1].endMs + 700) return null;
  const page = pages.find((p, i) => ms >= p[0].startMs - 80 && (i === pages.length - 1 || ms < pages[i + 1][0].startMs - 80));
  if (!page) return null;
  const pageStart = ((page[0].startMs - 80) / 1000) * FPS;
  const appear = spring({ frame: f - pageStart, fps: FPS, config: { damping: 15, stiffness: 200 } });
  return (
    <AbsoluteFill style={{ top, height: 220, alignItems: "center", justifyContent: "center" }}>
      <div style={{ display: "flex", flexWrap: "wrap", justifyContent: "center", gap: "6px 20px", padding: "0 70px", transform: `scale(${0.85 + 0.15 * appear})`, opacity: appear }}>
        {page.map((w, i) => {
          const active = ms >= w.startMs && ms < (page[i + 1]?.startMs ?? w.endMs + 200);
          const said = ms >= w.startMs;
          const key = keywords?.test(w.text) ?? false;
          return (
            <span
              key={i}
              style={{
                fontFamily,
                fontWeight: 900,
                fontSize: size,
                textTransform: "uppercase",
                color: active ? "#1B1B3A" : key ? accent : "#fff",
                background: active ? accent : "transparent",
                borderRadius: 18,
                padding: "0 14px",
                opacity: said ? 1 : 0.55,
                transform: `scale(${active ? 1.08 : 1})`,
                WebkitTextStroke: active ? "0px" : "3px rgba(0,0,0,0.6)",
                paintOrder: "stroke fill",
                textShadow: active ? "none" : "0 6px 14px rgba(0,0,0,0.5)",
              }}
            >
              {w.text}
            </span>
          );
        })}
      </div>
    </AbsoluteFill>
  );
};

/** Krótki błysk przy zmianie sceny. */
export const Flash: React.FC<{ layout: ReturnType<typeof sceneLayout> }> = ({ layout }) => {
  const f = useCurrentFrame();
  const o = layout.items
    .slice(1)
    .map((it) => interpolate(f, [it.from - 2, it.from, it.from + 6], [0, 0.18, 0], {
      extrapolateLeft: "clamp",
      extrapolateRight: "clamp",
      easing: Easing.out(Easing.quad),
    }))
    .reduce((a, b) => Math.max(a, b), 0);
  return <AbsoluteFill style={{ background: "#fff", opacity: o, pointerEvents: "none" }} />;
};

/** Otwarcie ust 0..1 z głośności lektora, tylko w trakcie słów. */
export const useMouth = (audioFile: string, words: Word[]) => {
  const f = useCurrentFrame();
  const { fps } = useVideoConfig();
  const audioData = useAudioData(audioFile);
  if (!audioData) return 0;
  const amp = (fr: number) => {
    if (fr < 0) return 0;
    const v = visualizeAudio({ fps, frame: fr, audioData, numberOfSamples: 32 });
    return v.slice(0, 10).reduce((s, x) => s + x, 0) / 10;
  };
  const a = (amp(f) * 0.65 + amp(f - 1) * 0.35) * 9;
  const ms = (f / fps) * 1000;
  const inWord = words.some((w) => ms >= w.startMs - 40 && ms <= w.endMs + 40);
  return inWord ? Math.min(1, a) : 0;
};

/**
 * Muzyka w tle (cicho, z wejściem i wyjściem). Pliki leżą w bibliotece (data/library),
 * wygenerowane raz (ElevenLabs Music). Brak pliku = brak muzyki.
 */
export const Music: React.FC<{ file?: string | null; volume?: number; total: number; libraryBase: string }> = ({
  file, volume = 0.09, total, libraryBase,
}) => {
  if (!file) return null;
  return (
    <Audio
      src={libraryBase + file}
      loop
      volume={(f) => volume * interpolate(f, [0, 20, total - 40, total], [0, 1, 1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" })}
    />
  );
};
