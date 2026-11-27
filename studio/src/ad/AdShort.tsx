import React from "react";
import { AbsoluteFill, Audio, Img, Sequence, interpolate, spring, staticFile, useCurrentFrame, useVideoConfig } from "remotion";
import { Captions, EMOJI, Flash, Music, fontFamily, sceneLayout, useMouth } from "../common";
import { Timeline } from "../types";
import { Hantelek, HantelekMood, MOODS } from "./Hantelek";

/**
 * Reklama Hantly 9:16 (format "ad"): szybki montaż jak w spocie – hak, prawdziwe ekrany aplikacji
 * (zrzuty z konta demo) w telefonie, asystent AI (Hantelek) w akcji, przed/po, korzyści i mocne CTA.
 * Każda scena: story.template = ad-hook | ad-screen | ad-assistant | ad-compare | ad-benefits | ad-mascot | ad-cta.
 */

type P = Record<string, any>;
type Ctx = {
  p: P; f: number; frames: number; fps: number; accent: string; brand: string;
  talk: number; shot: (file?: string) => string | undefined; size: (file?: string) => { w: number; h: number };
  brandName: string;
};

const W = 1080, H = 1920;
const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;
const mood = (m: unknown, d: HantelekMood): HantelekMood => (MOODS.includes(m as HantelekMood) ? (m as HantelekMood) : d);
const str = (v: unknown, max = 200) => (typeof v === "string" ? v.slice(0, max) : "");
const arr = (v: unknown): any[] => (Array.isArray(v) ? v : []);
const pop = (f: number, fps: number, delay = 0, damping = 12) => spring({ frame: f - delay, fps, config: { damping, stiffness: 170 } });
const outFade = (f: number, frames: number) => interpolate(f, [frames - 7, frames], [1, 0], clamp);

/* ---------------- wspólne klocki ---------------- */

const Headline: React.FC<{ text: string; f: number; fps: number; accent: string; top?: number; size?: number }> = ({ text, f, fps, accent, top = 190, size }) => {
  if (!text) return null;
  // *wyróżnienie* może obejmować kilka słów
  let on = false;
  const words = text.split(" ").map((w) => {
    // „*24/7*,” – gwiazdka może stać przed interpunkcją
    const start = w.startsWith("*"), end = /.\*[.,!?:;…)”"]*$/.test(w);
    if (start) on = true;
    const hi = on;
    if (end) on = false;
    return { w: w.replace(/\*/g, ""), hi };
  });
  const fs = size ?? Math.max(58, Math.min(92, 1500 / Math.max(10, text.length)));
  return (
    <div style={{ position: "absolute", top, left: 60, right: 60, textAlign: "center", fontFamily, fontWeight: 900, fontSize: fs, lineHeight: 1.08, color: "#fff", letterSpacing: -1 }}>
      {words.map(({ w, hi }, i) => {
        const s = pop(f, fps, 2 + i * 2.5, 13);
        return (
          <span key={i} style={{ display: "inline-block", marginRight: fs * 0.25, opacity: s, transform: `translateY(${(1 - s) * 40}px) scale(${0.7 + 0.3 * s})`, color: hi ? accent : "#fff" }}>
            {w}
          </span>
        );
      })}
    </div>
  );
};

/** Telefon z prawdziwym screenem; obraz przewija się w dół (scroll 0..1), wjeżdża obrotem 3D. */
const Phone: React.FC<{ src?: string; size: { w: number; h: number }; f: number; fps: number; frames: number; x: number; y: number; width?: number; scroll?: number; tilt?: number; zoom?: { y: number; at: number } }> = ({
  src, size, f, fps, frames, x, y, width = 500, scroll = 0, tilt = 0, zoom,
}) => {
  const enter = pop(f, fps, 0, 15);
  const screenH = width * 2.16;
  const imgH = size.w > 0 ? (size.h / size.w) * width : screenH;
  const maxScroll = Math.max(0, imgH - screenH);
  const sc = interpolate(f, [fps * 0.6, frames - fps * 0.4], [0, Math.min(1, scroll)], { ...clamp, easing: (t) => t * t * (3 - 2 * t) }) * maxScroll;
  const ry = (1 - enter) * -55 + Math.sin(f / 40) * 3 + tilt;
  const z = zoom ? interpolate(f, [zoom.at * frames, zoom.at * frames + fps * 0.5], [1, 1.35], { ...clamp, easing: (t) => 1 - (1 - t) ** 3 }) : 1;
  return (
    <div style={{ position: "absolute", left: x - width / 2 - 18, top: y - screenH / 2 - 18, perspective: 1800, transform: `scale(${z})`, transformOrigin: `50% ${zoom ? zoom.y * 100 : 50}%` }}>
      <div style={{
        width: width + 36, height: screenH + 36, borderRadius: 74, background: "linear-gradient(145deg,#2A2540,#0D0B16)", padding: 18,
        boxShadow: "0 60px 120px rgba(0,0,0,.55), 0 0 0 3px rgba(255,255,255,.08) inset",
        transform: `rotateY(${ry}deg) rotateX(${(1 - enter) * 12}deg) translateY(${(1 - enter) * 300}px)`, opacity: Math.min(1, enter * 1.5),
      }}>
        <div style={{ width, height: screenH, borderRadius: 56, overflow: "hidden", background: "#F7F7FB", position: "relative" }}>
          {/* plansza pod spodem – widoczna, gdy scena nie ma zrzutu */}
          <div style={{ position: "absolute", inset: 0, display: "flex", alignItems: "center", justifyContent: "center", fontFamily, fontSize: 40, color: "#7C3AED", fontWeight: 900 }}>Hantly</div>
          {src && (
            <Img src={src} style={{ width, position: "absolute", top: -sc, left: 0, background: "#F7F7FB" }} />
          )}
          <div style={{ position: "absolute", top: 14, left: width / 2 - 70, width: 140, height: 34, borderRadius: 20, background: "#0D0B16" }} />
          <div style={{ position: "absolute", inset: 0, background: "linear-gradient(115deg, rgba(255,255,255,.16) 0%, rgba(255,255,255,0) 32%)" }} />
        </div>
      </div>
    </div>
  );
};

/** Dymek z opisem przy telefonie (wskazuje miejsce na ekranie) */
const Callout: React.FC<{ text: string; f: number; fps: number; delay: number; x: number; y: number; side: "left" | "right"; accent: string }> = ({ text, f, fps, delay, x, y, side, accent }) => {
  const s = pop(f, fps, delay, 11);
  if (s <= 0.01) return null;
  const dir = side === "right" ? 1 : -1;
  return (
    <div style={{
      position: "absolute", top: y - 50, [side === "right" ? "left" : "right"]: side === "right" ? x : W - x, maxWidth: 430,
      transform: `translateX(${(1 - s) * 60 * dir}px) scale(${0.6 + 0.4 * s})`, opacity: s, transformOrigin: side === "right" ? "0 50%" : "100% 50%",
      display: "flex", alignItems: "center", gap: 14, flexDirection: side === "right" ? "row" : "row-reverse",
    }}>
      <div style={{ width: 22, height: 22, borderRadius: 11, background: accent, boxShadow: `0 0 0 ${8 + Math.sin(f / 5) * 4}px ${accent}44`, flexShrink: 0 }} />
      <div style={{ background: "#fff", color: "#1A1625", fontFamily, fontWeight: 800, fontSize: 38, lineHeight: 1.15, padding: "18px 24px", borderRadius: 24, boxShadow: "0 18px 40px rgba(0,0,0,.35)", borderBottom: `6px solid ${accent}` }}>
        {text}
      </div>
    </div>
  );
};

const Bubble: React.FC<{ text: string; f: number; fps: number; x: number; y: number; width?: number; tail?: "left" | "right" | "down"; delay?: number }> = ({ text, f, fps, x, y, width = 640, tail = "down", delay = 4 }) => {
  const s = pop(f, fps, delay, 12);
  if (!text || s <= 0.01) return null;
  return (
    <div style={{ position: "absolute", left: x - width / 2, top: y, width, transform: `scale(${0.5 + 0.5 * s})`, opacity: s, transformOrigin: "50% 100%" }}>
      <div style={{ background: "#fff", color: "#1A1625", fontFamily, fontWeight: 800, fontSize: 46, lineHeight: 1.2, padding: "28px 34px", borderRadius: 40, textAlign: "center", boxShadow: "0 24px 60px rgba(0,0,0,.4)" }}>
        {text}
      </div>
      {tail === "down" && <div style={{ position: "absolute", left: width / 2 - 22, bottom: -26, width: 0, height: 0, borderLeft: "22px solid transparent", borderRight: "22px solid transparent", borderTop: "30px solid #fff" }} />}
    </div>
  );
};

/* ---------------- ujęcia ---------------- */

const AdHook: React.FC<Ctx> = ({ p, f, fps, frames, accent, talk }) => {
  const msgs = arr(p.messages).map((m) => str(m, 60)).filter(Boolean).slice(0, 5);
  const big = str(p.big, 24);
  const bigAt = msgs.length ? Math.min(frames * 0.55, msgs.length * 7 + 8) : 2;
  const s = pop(f, fps, bigAt, 9);
  const shake = f > bigAt && f < bigAt + 8 ? Math.sin(f * 3) * 10 : 0;
  return (
    <AbsoluteFill>
      {msgs.map((m, i) => {
        const a = pop(f, fps, i * 7, 13);
        const dim = interpolate(f, [bigAt, bigAt + 6], [1, 0.35], clamp);
        return (
          <div key={i} style={{
            position: "absolute", top: 300 + i * 118, left: i % 2 ? 300 : 80, maxWidth: 680, opacity: a * dim,
            transform: `translateX(${(1 - a) * (i % 2 ? 80 : -80)}px) rotate(${i % 2 ? 2 : -2}deg)`,
            background: "#fff", borderRadius: 34, padding: "20px 30px", fontFamily, fontWeight: 700, fontSize: 40, color: "#1A1625",
            boxShadow: "0 16px 40px rgba(0,0,0,.35)", display: "flex", gap: 14, alignItems: "center",
          }}>
            <span style={{ fontFamily: EMOJI, fontSize: 38 }}>💬</span>{m}
          </div>
        );
      })}
      {big && (
        <div style={{ position: "absolute", top: msgs.length ? 330 + msgs.length * 118 : 520, left: 0, right: 0, textAlign: "center", transform: `translateX(${shake}px) scale(${0.3 + 0.7 * s}) rotate(${(1 - s) * -8}deg)`, opacity: s }}>
          <span style={{ fontFamily, fontWeight: 900, fontSize: big.length > 10 ? 150 : 210, color: accent, letterSpacing: -4, textShadow: "0 14px 0 rgba(0,0,0,.35), 0 0 60px rgba(6,214,160,.35)" }}>{big}</span>
        </div>
      )}
      <Headline text={str(p.line, 70)} f={f - bigAt - 4} fps={fps} accent={accent} top={msgs.length ? 600 + msgs.length * 118 : 820} size={72} />
      <div style={{ position: "absolute", right: 40, bottom: 380, opacity: pop(f, fps, bigAt + 6) }}>
        <Hantelek mood={mood(p.mood, "think")} frame={f} talk={talk} size={260} id="hk-hook" />
      </div>
    </AbsoluteFill>
  );
};

/** Pozycje dymków przy telefonie: kolejność jak w scenariuszu, ale co najmniej 150 px odstępu (żeby nie nachodziły). */
export const calloutYs = (callouts: any[], phoneY: number, width: number) => {
  const top = phoneY - width * 1.08, h = width * 2.16, GAP = 150;
  const ys = callouts.map((c, i) => top + Math.max(0.08, Math.min(0.92, Number(c?.y) || 0.25 + i * 0.25)) * h);
  const order = ys.map((y, i) => [y, i] as const).sort((a, b) => a[0] - b[0]);
  const out = [...ys];
  let prev = -Infinity;
  for (const [y, i] of order) { out[i] = Math.max(y, prev + GAP); prev = out[i]; }
  // nie wychodź pod telefon – w razie czego przesuń całość w górę
  const over = Math.max(0, prev - (top + h - 40));
  return out.map((y) => y - over);
};

const AdScreen: React.FC<Ctx> =({ p, f, fps, frames, accent, talk, shot, size }) => {
  const side: "left" | "right" = p.side === "left" ? "left" : "right";
  const callouts = arr(p.callouts).slice(0, 3);
  const phoneX = callouts.length ? (side === "right" ? 380 : 700) : 540;
  const phoneY = 900;
  const width = callouts.length ? 470 : 540;
  return (
    <AbsoluteFill>
      <Headline text={str(p.caption, 44)} f={f} fps={fps} accent={accent} top={150} size={70} />
      <Phone src={shot(p.screen)} size={size(p.screen)} f={f} fps={fps} frames={frames} x={phoneX} y={phoneY} width={width}
        scroll={typeof p.scroll === "number" ? p.scroll : 0} tilt={side === "right" ? 6 : -6} />
      {calloutYs(callouts, phoneY, width).map((y, i) => {
        const c = callouts[i];
        const edge = side === "right" ? phoneX + width / 2 - 30 : phoneX - width / 2 + 30;
        return <Callout key={i} text={str(c?.text, 30)} f={f} fps={fps} delay={Math.round(frames * (0.22 + i * 0.2))} x={edge} y={y} side={side} accent={accent} />;
      })}
      <div style={{ position: "absolute", [side === "right" ? "right" : "left"]: 30, top: 1210, opacity: pop(f, fps, 10) }}>
        <Hantelek mood={mood(p.mood, "happy")} frame={f} talk={talk} size={230} id={`hk-s`} />
      </div>
    </AbsoluteFill>
  );
};

/** Asystent AI w akcji: czat z Hantelkiem (pisze, podpowiada, układa wiadomość). */
const AdAssistant: React.FC<Ctx> = ({ p, f, fps, frames, accent, talk, brandName }) => {
  const msgs = arr(p.messages).slice(0, 4).map((m) => ({ from: m?.from === "me" ? "me" : "ai", text: str(m?.text, 120) }));
  const step = (frames * 0.8) / Math.max(1, msgs.length);
  const card = pop(f, fps, 0, 15);
  return (
    <AbsoluteFill>
      <Headline text={str(p.title, 40) || `Asystent ${brandName}`} f={f} fps={fps} accent={accent} top={150} size={70} />
      <div style={{
        position: "absolute", left: 70, right: 70, top: 380, height: 900, borderRadius: 48, background: "#F7F6FC",
        boxShadow: "0 50px 100px rgba(0,0,0,.5)", overflow: "hidden", transform: `translateY(${(1 - card) * 200}px) scale(${0.9 + 0.1 * card})`, opacity: card,
      }}>
        <div style={{ height: 130, background: "linear-gradient(90deg,#6D28D9,#8B5CF6)", display: "flex", alignItems: "center", gap: 18, padding: "0 30px" }}>
          <div style={{ width: 96, height: 96, borderRadius: 48, background: "#fff", display: "flex", alignItems: "center", justifyContent: "center" }}>
            <Hantelek mood="happy" frame={f} talk={talk} size={88} id="hk-av" />
          </div>
          <div style={{ fontFamily, color: "#fff" }}>
            <div style={{ fontWeight: 900, fontSize: 40 }}>{str(p.agent, 30) || "Asystent"}</div>
            <div style={{ fontWeight: 600, fontSize: 26, opacity: 0.85 }}>● online · AI</div>
          </div>
        </div>
        <div style={{ padding: 30, display: "flex", flexDirection: "column", gap: 22 }}>
          {msgs.map((m, i) => {
            const at = 8 + i * step;
            const typing = m.from === "ai" && f >= at && f < at + fps * 0.6;
            const s = pop(f, fps, m.from === "ai" ? at + fps * 0.6 : at, 13);
            if (f < at) return null;
            const me = m.from === "me";
            return (
              <div key={i} style={{ alignSelf: me ? "flex-end" : "flex-start", maxWidth: "82%" }}>
                {typing ? (
                  <div style={{ background: "#fff", borderRadius: 30, padding: "22px 30px", display: "flex", gap: 10, boxShadow: "0 6px 16px rgba(0,0,0,.08)" }}>
                    {[0, 1, 2].map((d) => <div key={d} style={{ width: 16, height: 16, borderRadius: 8, background: "#8B5CF6", opacity: 0.4 + 0.6 * Math.abs(Math.sin((f + d * 4) / 4)) }} />)}
                  </div>
                ) : (
                  <div style={{
                    opacity: s, transform: `translateY(${(1 - s) * 30}px)`, background: me ? accent : "#fff", color: "#1A1625",
                    borderRadius: me ? "34px 34px 8px 34px" : "34px 34px 34px 8px", padding: "22px 28px", fontFamily, fontWeight: 700, fontSize: 36, lineHeight: 1.25,
                    boxShadow: "0 6px 16px rgba(0,0,0,.08)", border: me ? "none" : "2px solid #ECE8F7",
                  }}>{m.text}</div>
                )}
              </div>
            );
          })}
        </div>
      </div>
      <div style={{ position: "absolute", right: 30, top: 1180, opacity: pop(f, fps, 6) }}>
        <Hantelek mood={mood(p.mood, "hello")} frame={f} talk={talk} size={240} id="hk-as" />
      </div>
    </AbsoluteFill>
  );
};

const AdCompare: React.FC<Ctx> = ({ p, f, fps, frames, accent, talk }) => {
  const before = arr(p.before).map((x) => str(x, 34)).slice(0, 3);
  const after = arr(p.after).map((x) => str(x, 34)).slice(0, 3);
  const swap = Math.round(frames * 0.45);
  const row = (t: string, i: number, good: boolean, start: number) => {
    const s = pop(f, fps, start + i * 5, 13);
    const strike = !good ? interpolate(f, [swap - 6, swap + 4], [0, 100], clamp) : 0;
    return (
      <div key={`${good}${i}`} style={{ display: "flex", alignItems: "center", gap: 22, opacity: s * (good ? 1 : interpolate(f, [swap, swap + 8], [1, 0.45], clamp)), transform: `translateX(${(1 - s) * -60}px)` }}>
        <div style={{ width: 70, height: 70, borderRadius: 35, background: good ? accent : "#EF4444", display: "flex", alignItems: "center", justifyContent: "center", fontFamily, fontWeight: 900, fontSize: 44, color: good ? "#110E1B" : "#fff", flexShrink: 0 }}>{good ? "✓" : "✕"}</div>
        <div style={{ position: "relative", fontFamily, fontWeight: 800, fontSize: 46, color: "#fff" }}>
          {t}
          {!good && <div style={{ position: "absolute", left: 0, top: "52%", height: 6, width: `${strike}%`, background: "#EF4444", borderRadius: 3 }} />}
        </div>
      </div>
    );
  };
  return (
    <AbsoluteFill>
      <div style={{ position: "absolute", top: 260, left: 80, right: 80, display: "flex", flexDirection: "column", gap: 34 }}>
        <div style={{ fontFamily, fontWeight: 900, fontSize: 44, color: "#FCA5A5", letterSpacing: 3 }}>{str(p.beforeLabel, 20) || "BEZ HANTLY"}</div>
        {before.map((t, i) => row(t, i, false, 2))}
        <div style={{ height: 40 }} />
        <div style={{ fontFamily, fontWeight: 900, fontSize: 44, color: accent, letterSpacing: 3, opacity: pop(f, fps, swap) }}>{str(p.afterLabel, 20) || "Z HANTLY"}</div>
        {after.map((t, i) => row(t, i, true, swap + 4))}
      </div>
      <div style={{ position: "absolute", right: 40, top: 1150, opacity: pop(f, fps, swap) }}>
        <Hantelek mood={f > swap ? "celebrate" : "think"} frame={f} talk={talk} size={240} id="hk-cmp" />
      </div>
    </AbsoluteFill>
  );
};

const AdBenefits: React.FC<Ctx> = ({ p, f, fps, frames, accent, talk }) => {
  const items = arr(p.items).map((x) => str(x, 40)).slice(0, 4);
  return (
    <AbsoluteFill>
      <Headline text={str(p.title, 40)} f={f} fps={fps} accent={accent} top={200} />
      <div style={{ position: "absolute", top: 520, left: 80, right: 80, display: "flex", flexDirection: "column", gap: 30 }}>
        {items.map((t, i) => {
          const s = pop(f, fps, Math.round(frames * 0.12) + i * Math.round(frames * 0.15), 12);
          return (
            <div key={i} style={{ display: "flex", alignItems: "center", gap: 24, background: "rgba(255,255,255,.1)", border: "2px solid rgba(255,255,255,.18)", borderRadius: 30, padding: "26px 30px", opacity: s, transform: `translateX(${(1 - s) * 120}px)` }}>
              <div style={{ width: 64, height: 64, borderRadius: 20, background: accent, display: "flex", alignItems: "center", justifyContent: "center", fontFamily, fontWeight: 900, fontSize: 40, color: "#110E1B", transform: `scale(${0.6 + 0.4 * s}) rotate(${(1 - s) * -90}deg)` }}>✓</div>
              <div style={{ fontFamily, fontWeight: 800, fontSize: 46, color: "#fff" }}>{t}</div>
            </div>
          );
        })}
      </div>
      <div style={{ position: "absolute", left: 40, top: 1170, opacity: pop(f, fps, 4) }}>
        <Hantelek mood={mood(p.mood, "happy")} frame={f} talk={talk} size={230} id="hk-ben" />
      </div>
    </AbsoluteFill>
  );
};

const AdMascot: React.FC<Ctx> = ({ p, f, fps, talk }) => {
  const s = pop(f, fps, 0, 10);
  return (
    <AbsoluteFill>
      <Bubble text={str(p.bubble, 90)} f={f} fps={fps} x={540} y={300} width={820} delay={8} />
      <div style={{ position: "absolute", left: 540 - 300, top: 620, transform: `scale(${s}) translateY(${(1 - s) * 200}px)` }}>
        <Hantelek mood={mood(p.mood, "hello")} frame={f} talk={talk} size={600} id="hk-big" />
      </div>
      <Headline text={str(p.name, 30)} f={f - 10} fps={fps} accent="#fff" top={1250} size={60} />
    </AbsoluteFill>
  );
};

const AdCta: React.FC<Ctx> = ({ p, f, fps, frames, accent, talk, brandName }) => {
  const logo = pop(f, fps, 0, 11);
  const btn = pop(f, fps, 14, 9);
  const pulse = 1 + Math.max(0, Math.sin((f - 20) / 6)) * 0.04;
  const jump = interpolate(f, [fps * 0.3, fps * 1.3], [0, 1], clamp);
  return (
    <AbsoluteFill>
      {/* konfetti */}
      {Array.from({ length: 26 }).map((_, i) => {
        const x = (i * 197) % W, sp = 6 + (i % 5) * 2.4, y = -60 + (f * sp + i * 83) % (H + 120);
        return <div key={i} style={{ position: "absolute", left: x, top: y, width: 18, height: 30, borderRadius: 4, background: ["#06D6A0", "#FBBF24", "#F472B6", "#60A5FA", "#A78BFA"][i % 5], transform: `rotate(${f * (4 + i % 3) + i * 40}deg)`, opacity: f < frames ? 0.85 : 0 }} />;
      })}
      <div style={{ position: "absolute", left: 340, top: 170, transform: `scale(${logo})` }}>
        <Hantelek mood="celebrate" frame={f} talk={talk} size={400} jump={jump < 1 ? jump : 0} id="hk-cta" />
      </div>
      <div style={{ position: "absolute", top: 600, left: 0, right: 0, textAlign: "center", fontFamily, fontWeight: 900, fontSize: 150, color: "#fff", letterSpacing: -5, transform: `scale(${logo})` }}>
        {brandName.toLowerCase()}<span style={{ color: accent }}>.</span>
      </div>
      <Headline text={str(p.headline, 40)} f={f - 6} fps={fps} accent={accent} top={800} size={74} />
      {str(p.price, 12) && (
        <div style={{ position: "absolute", top: 990, left: 0, right: 0, display: "flex", justifyContent: "center", opacity: pop(f, fps, 10), transform: `rotate(-3deg) scale(${pop(f, fps, 10, 8)})` }}>
          <div style={{ background: "#FBBF24", color: "#110E1B", fontFamily, fontWeight: 900, fontSize: 64, padding: "10px 40px", borderRadius: 24 }}>{str(p.price, 12)}</div>
        </div>
      )}
      <div style={{ position: "absolute", top: 1130, left: 0, right: 0, display: "flex", justifyContent: "center" }}>
        <div style={{
          transform: `scale(${btn * pulse})`, background: accent, color: "#110E1B", fontFamily, fontWeight: 900, fontSize: 58, padding: "34px 70px", borderRadius: 80,
          boxShadow: `0 0 0 ${10 + Math.sin(f / 5) * 6}px ${accent}40, 0 30px 60px rgba(0,0,0,.4)`,
        }}>
          {str(p.button, 26) || "Zacznij za darmo"} →
        </div>
      </div>
      <div style={{ position: "absolute", top: 1330, left: 0, right: 0, textAlign: "center", fontFamily, fontWeight: 700, fontSize: 40, color: "#E9E4FF", opacity: pop(f, fps, 22) }}>
        {str(p.note, 60)}
      </div>
    </AbsoluteFill>
  );
};

export const AD_TEMPLATES: Record<string, React.FC<Ctx>> = {
  "ad-hook": AdHook, "ad-screen": AdScreen, "ad-assistant": AdAssistant, "ad-compare": AdCompare,
  "ad-benefits": AdBenefits, "ad-mascot": AdMascot, "ad-cta": AdCta,
};

/* ---------------- tło, marka, dźwięki ---------------- */

const AdBackground: React.FC<{ colors: [string, string]; accent: string }> = ({ colors, accent }) => {
  const f = useCurrentFrame();
  const b = (i: number) => ({ x: 540 + Math.sin(f / (70 + i * 23) + i * 2) * 420, y: 960 + Math.cos(f / (90 + i * 17) + i) * 700 });
  return (
    <AbsoluteFill style={{ background: `linear-gradient(160deg, ${colors[0]} 0%, ${colors[1]} 100%)` }}>
      {[0, 1, 2].map((i) => (
        <div key={i} style={{ position: "absolute", left: b(i).x - 380, top: b(i).y - 380, width: 760, height: 760, borderRadius: "50%", background: [accent, "#8B5CF6", "#F472B6"][i], opacity: 0.16, filter: "blur(120px)" }} />
      ))}
      <AbsoluteFill style={{ backgroundImage: "radial-gradient(rgba(255,255,255,.07) 2px, transparent 2px)", backgroundSize: "44px 44px" }} />
    </AbsoluteFill>
  );
};

const BrandBar: React.FC<{ name: string; url: string; accent: string }> = ({ name, url, accent }) => {
  const f = useCurrentFrame();
  return (
    <div style={{ position: "absolute", top: 60, left: 50, display: "flex", alignItems: "center", gap: 12, fontFamily, color: "#fff" }}>
      <Hantelek mood="happy" frame={f} size={70} id="hk-bar" />
      <span style={{ fontWeight: 900, fontSize: 40 }}>{name}</span>
      {url && <span style={{ fontWeight: 700, fontSize: 28, color: accent, marginLeft: 8 }}>{url}</span>}
    </div>
  );
};

const adCues = (tpl: string, p: P, index: number, frames: number) => {
  const at = (r: number) => Math.round(frames * r);
  const c: { key: string; at: number; volume: number }[] = [];
  if (index > 0) c.push({ key: "whoosh", at: 0, volume: 0.35 });
  if (tpl === "ad-hook") arr(p.messages).slice(0, 5).forEach((_, i) => c.push({ key: "ding", at: i * 7, volume: 0.22 }));
  if (tpl === "ad-hook") c.push({ key: "stamp", at: arr(p.messages).length ? Math.min(at(0.55), arr(p.messages).length * 7 + 8) : 2, volume: 0.5 });
  if (tpl === "ad-screen") arr(p.callouts).slice(0, 3).forEach((_, i) => c.push({ key: "pop", at: at(0.22 + i * 0.2), volume: 0.3 }));
  if (tpl === "ad-assistant") arr(p.messages).slice(0, 4).forEach((_, i) => c.push({ key: "pop", at: Math.round(8 + i * (frames * 0.8) / Math.max(1, arr(p.messages).length)) + 18, volume: 0.25 }));
  if (tpl === "ad-compare") c.push({ key: "sparkle", at: at(0.45), volume: 0.35 });
  if (tpl === "ad-benefits") arr(p.items).slice(0, 4).forEach((_, i) => c.push({ key: "pop", at: at(0.12 + i * 0.15), volume: 0.26 }));
  if (tpl === "ad-cta") c.push({ key: "confetti", at: 2, volume: 0.45 }, { key: "ding", at: 16, volume: 0.35 });
  return c.filter((x) => x.at >= 0 && x.at < frames);
};

/* ---------------- kompozycja ---------------- */

const SceneShot: React.FC<{ tpl: string; p: P; frames: number; audioFile: string; words: Timeline["audio"][number]["words"]; base: Omit<Ctx, "p" | "f" | "frames" | "fps" | "talk"> }> = ({ tpl, p, frames, audioFile, words, base }) => {
  const f = useCurrentFrame();
  const { fps } = useVideoConfig();
  const talk = useMouth(audioFile, words);
  const T = AD_TEMPLATES[tpl] ?? AdMascot;
  const enter = spring({ frame: f, fps, config: { damping: 18, stiffness: 140 } });
  const out = outFade(f, frames);
  return (
    <AbsoluteFill style={{ opacity: out, transform: `translateX(${(1 - enter) * 140}px) scale(${1.06 - 0.06 * enter + interpolate(f, [0, frames], [0, 0.025])})` }}>
      <T {...base} p={p} f={f} frames={frames} fps={fps} talk={talk} />
    </AbsoluteFill>
  );
};

export const AdShort: React.FC<{ episodeId: string; timeline?: Timeline; assetBase?: string }> = ({ episodeId, timeline, assetBase }) => {
  if (!timeline) return null;
  const t = timeline;
  const layout = sceneLayout(t);
  const accent = t.theme.accent;
  const root = assetBase ? assetBase.replace(/episodes\/[^/]+\/$/, "") : staticFile("");
  const libraryBase = root + "library/";
  const shotsBase = `${root}sources/${t.screensDir ?? "hantly"}/screens/`;
  const brandName = t.channel?.name ?? "Hantly";
  const base = {
    accent, brand: "#7C3AED", brandName,
    // tylko zrzuty z listy odcinka (Portal wpisuje tam wyłącznie istniejące pliki) – brakujący plik wywróciłby render
    shot: (file?: string) => (file && /^[A-Za-z0-9._-]+$/.test(file) && t.screens?.[file] ? shotsBase + file : undefined),
    size: (file?: string) => (file && t.screens?.[file]) || { w: 780, h: 1688 },
  };
  const sfx = t.sound?.sfx ?? [];
  return (
    <AbsoluteFill style={{ backgroundColor: "#110E1B" }}>
      <AdBackground colors={t.theme.background} accent={accent} />
      {t.scenes.map((scene, i) => {
        const { from, frames } = layout.items[i];
        const audio = t.audio[i];
        const file = assetBase ? assetBase + audio.file : staticFile(`episodes/${episodeId}/${audio.file}`);
        const p = (scene.story ?? { template: "ad-mascot" }) as P;
        return (
          <Sequence key={i} from={from} durationInFrames={frames} name={`${i} ${p.template}`}>
            <Audio src={file} />
            {adCues(String(p.template), p, i, frames).filter((c) => sfx.includes(c.key)).map((c, k) => (
              <Sequence key={k} from={c.at} durationInFrames={Math.max(1, Math.min(90, frames - c.at))} layout="none">
                <Audio src={`${libraryBase}sfx-${c.key}.mp3`} volume={c.volume} />
              </Sequence>
            ))}
            <SceneShot tpl={String(p.template)} p={p} frames={frames} audioFile={file} words={audio.words} base={base} />
            {p.template !== "ad-cta" && <Captions words={audio.words} accent={accent} top={1500} size={54} />}
          </Sequence>
        );
      })}
      <Music file={t.sound?.music} volume={(t.sound?.musicVolume ?? 0.09) * 1.6} total={layout.total} libraryBase={libraryBase} />
      <BrandBar name={brandName} url={(t.website ?? "").replace(/^https?:\/\//, "").replace(/\/$/, "")} accent={accent} />
      <Flash layout={layout} />
    </AbsoluteFill>
  );
};
