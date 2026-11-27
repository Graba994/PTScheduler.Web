export type Word = { text: string; startMs: number; endMs: number };

/** Scenorys sceny: ujęcie reklamy (ad-hook, ad-screen, …) + jego parametry. Katalog: src/ad/AdShort.tsx. */
export type Story = { template: string; [key: string]: unknown };

export type Scene = {
  story?: Story;
  kind: "hook" | "fact" | "outro";
  number?: number;
  headline: string;
  keyline?: string;
  icon?: string;
  expression?: string;
  gesture?: string;
  text: string;
};

/** episode.json reklamy + czasy słów z timeline.json (audio). */
export type Timeline = {
  id?: string;
  title: string;
  format?: "ad";
  theme: { accent: string; background: [string, string] };
  channel?: { name: string; tagline?: string };
  /** adres strony produktu (pasek marki) */
  website?: string;
  /** katalog screenów w data/sources/<screensDir>/screens */
  screensDir?: string;
  /** wymiary screenów (do przewijania w telefonie) */
  screens?: Record<string, { w: number; h: number }>;
  sound?: { music?: string | null; musicVolume?: number; sfx?: string[] };
  scenes: Scene[];
  audio: { file: string; durationMs: number; words: Word[] }[];
};

export const FPS = 30;
