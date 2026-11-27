import React from "react";
import { AbsoluteFill } from "remotion";
import { EMOJI, fontFamily } from "../common";
import { Hantelek } from "./Hantelek";
import { PhoneMock } from "./PhoneMock";

/**
 * Grafika posta promocyjnego 4:5 (1080×1350): nagłówek, korzyści, telefon z prawdziwym zrzutem
 * (albo makietą z wierszy), Hantelek i pigułka CTA. Teksty przychodzą z Portalu (Studio reklam).
 */
export type PromoPostProps = {
  headline: string;
  sub?: string;
  bullets?: string[];
  screen?: { title?: string; subtitle?: string; rows?: string[]; image?: string };
  url?: string;
  brand?: string;
  accent?: string;
  background?: [string, string];
  brandName?: string;
  cta?: string;
};

const row = (s: string) => {
  const [label, value] = s.split(/\s[–—-]\s/, 2);
  return { label: label.trim(), value: value?.trim() };
};

export const PromoPost: React.FC<PromoPostProps> = ({
  headline, sub, bullets = [], screen, url = "hantly.pl", brand = "#7C3AED", accent = "#06D6A0",
  background = ["#110E1B", "#5B21B6"], brandName = "Hantly", cta,
}) => {
  const hSize = Math.max(64, Math.min(96, 1500 / Math.max(10, headline.length)));
  return (
    <AbsoluteFill style={{ background: `linear-gradient(160deg, ${background[0]} 0%, ${background[1]} 100%)`, fontFamily, overflow: "hidden" }}>
      {/* poświata i kropki w tle */}
      <div style={{ position: "absolute", right: -220, top: 260, width: 760, height: 760, borderRadius: "50%",
        background: `radial-gradient(circle, ${accent}55 0%, transparent 65%)` }} />
      <svg width={1080} height={1350} style={{ position: "absolute", inset: 0 }}>
        {Array.from({ length: 60 }).map((_, i) => (
          <circle key={i} cx={(i * 197) % 1080} cy={(i * 331) % 1350} r={i % 3 === 0 ? 4 : 2.5} fill="#ffffff" opacity={0.08} />
        ))}
      </svg>

      {/* marka */}
      <div style={{ position: "absolute", left: 70, top: 64, display: "flex", alignItems: "center", gap: 16 }}>
        <div style={{ width: 54, height: 54, borderRadius: 16, background: accent, display: "flex", alignItems: "center",
          justifyContent: "center", fontSize: 34, fontWeight: 900, color: background[0] }}>{brandName[0]}</div>
        <span style={{ color: "#fff", fontSize: 40, fontWeight: 900, letterSpacing: -0.5 }}>{brandName}</span>
      </div>

      {/* nagłówek */}
      <div style={{ position: "absolute", left: 70, top: 160, width: 940 }}>
        <div style={{ color: "#fff", fontSize: hSize, fontWeight: 900, lineHeight: 1.04, letterSpacing: -1.5 }}>{headline}</div>
        {sub && <div style={{ color: "#ffffffcc", fontSize: 38, fontWeight: 600, lineHeight: 1.25, marginTop: 22, width: 900 }}>{sub}</div>}
      </div>

      {/* korzyści */}
      <div style={{ position: "absolute", left: 70, top: 470, width: 560, display: "flex", flexDirection: "column", gap: 22 }}>
        {bullets.slice(0, 3).map((b, i) => (
          <div key={i} style={{ display: "flex", alignItems: "center", gap: 18, background: "rgba(255,255,255,0.08)",
            border: "2px solid rgba(255,255,255,0.14)", borderRadius: 24, padding: "18px 22px" }}>
            <div style={{ flex: "0 0 auto", width: 46, height: 46, borderRadius: 23, background: accent, color: background[0],
              display: "flex", alignItems: "center", justifyContent: "center", fontSize: 30, fontWeight: 900 }}>✓</div>
            <span style={{ color: "#fff", fontSize: Math.min(34, 900 / Math.max(14, b.length)), fontWeight: 800, lineHeight: 1.15 }}>{b}</span>
          </div>
        ))}
      </div>

      {/* telefon + maskotka */}
      <svg viewBox="0 0 1080 1350" width={1080} height={1350} style={{ position: "absolute", inset: 0 }}>
        <PhoneMock x={840} y={860} scale={0.8} rotate={6} frame={0} brand={brand}
          image={screen?.image} title={screen?.title ?? brandName} subtitle={screen?.subtitle} rows={(screen?.rows ?? []).map(row)} />
        {/* asystent AI z aplikacji (Hantelek) */}
        <g transform="translate(130 930)">
          <Hantelek mood="hello" frame={24} size={340} id="hk-post" />
        </g>
      </svg>

      {/* wezwanie do działania */}
      <div style={{ position: "absolute", left: 70, bottom: 64, display: "flex", alignItems: "center", gap: 14,
        background: accent, color: background[0], borderRadius: 40, padding: "18px 34px", fontSize: 36, fontWeight: 900 }}>
        <span>{cta ?? `Zacznij za darmo · ${url}`}</span>
        <span style={{ fontFamily: EMOJI }}>👉</span>
      </div>
    </AbsoluteFill>
  );
};
