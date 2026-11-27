import React from "react";

/**
 * Hantelek – maskotka i asystent AI aplikacji Hantly, przeniesiony 1:1 z PTScheduler.Portal/Services/MascotArt.cs,
 * animowany klatka po klatce (Remotion nie używa animacji CSS): oddech, mruganie, mówienie, podskoki.
 * Jedno źródło prawdy to MascotArt.cs – test HantelekMatchesMascotArtTests pilnuje, żeby kształty i kolory
 * były tu takie same. Po zmianie maskotki w MascotArt popraw ten plik, aż test przejdzie.
 */
export type HantelekMood = "hello" | "happy" | "celebrate" | "think" | "remind" | "sleep";
export const MOODS: HantelekMood[] = ["hello", "happy", "celebrate", "think", "remind", "sleep"];

const INK = "#1E1B4B";

const Plates: React.FC<{ id: string; l: number; r: number }> = ({ id, l, r }) => (
  <>
    <g transform={`rotate(${l} 30 63)`}>
      <rect x="6" y="57" width="10" height="12" rx="3" fill="#64748B" />
      <rect x="13" y="30" width="15" height="66" rx="6" fill={`url(#${id}p)`} />
      <rect x="24" y="37" width="12" height="52" rx="5" fill="#334155" />
      <rect x="16" y="34" width="4" height="22" rx="2" fill="#fff" opacity=".18" />
    </g>
    <g transform={`rotate(${r} 90 63)`}>
      <rect x="104" y="57" width="10" height="12" rx="3" fill="#64748B" />
      <rect x="92" y="30" width="15" height="66" rx="6" fill={`url(#${id}p)`} />
      <rect x="84" y="37" width="12" height="52" rx="5" fill="#334155" />
      <rect x="95" y="34" width="4" height="22" rx="2" fill="#fff" opacity=".18" />
    </g>
  </>
);

const Eyes: React.FC<{ mood: HantelekMood; blink: number }> = ({ mood, blink }) => {
  if (mood === "happy" || mood === "celebrate")
    return (
      <>
        <path d="M46 64 q5 -6 10 0" stroke={INK} strokeWidth={3} fill="none" strokeLinecap="round" />
        <path d="M64 64 q5 -6 10 0" stroke={INK} strokeWidth={3} fill="none" strokeLinecap="round" />
      </>
    );
  if (mood === "sleep")
    return (
      <>
        <path d="M46 64 q5 4 10 0" stroke={INK} strokeWidth={3} fill="none" strokeLinecap="round" />
        <path d="M64 64 q5 4 10 0" stroke={INK} strokeWidth={3} fill="none" strokeLinecap="round" />
      </>
    );
  const [dx, dy] = mood === "think" ? [1.6, -1.6] : [0, 0];
  return (
    <g transform={`translate(0 ${63 * (1 - blink)}) scale(1 ${blink})`}>
      <ellipse cx="51" cy="63" rx="4.6" ry="5.4" fill={INK} />
      <ellipse cx="69" cy="63" rx="4.6" ry="5.4" fill={INK} />
      <circle cx={52.6 + dx} cy={61 + dy} r="1.8" fill="#fff" />
      <circle cx={70.6 + dx} cy={61 + dy} r="1.8" fill="#fff" />
    </g>
  );
};

/** Usta: przy mówieniu (talk>0) otwierają się w rytm głosu. */
const Mouth: React.FC<{ mood: HantelekMood; talk: number }> = ({ mood, talk }) => {
  if (talk > 0.05) {
    const h = 2 + talk * 7;
    return (
      <g>
        <ellipse cx="60" cy="75" rx={5.5 + talk * 1.5} ry={h} fill={INK} />
        <ellipse cx="60" cy={75 + h * 0.55} rx={3.2} ry={Math.max(0.5, h * 0.3)} fill="#F472B6" />
      </g>
    );
  }
  switch (mood) {
    case "hello":
      return (<><path d="M51 72 q9 11 18 0 z" fill={INK} /><path d="M55 76 q5 3 10 0" fill="#F472B6" /></>);
    case "celebrate":
      return (<><ellipse cx="60" cy="76" rx="7" ry="6" fill={INK} /><ellipse cx="60" cy="79" rx="4" ry="2.5" fill="#F472B6" /></>);
    case "think":
      return <path d="M54 75 h12" stroke={INK} strokeWidth={3} strokeLinecap="round" />;
    case "remind":
      return <ellipse cx="60" cy="75" rx="3.4" ry="3.8" fill={INK} />;
    case "sleep":
      return <path d="M56 75 q4 3 8 0" stroke={INK} strokeWidth={2.6} fill="none" strokeLinecap="round" />;
    default:
      return <path d="M52 73 q8 7 16 0" stroke={INK} strokeWidth={3} fill="none" strokeLinecap="round" />;
  }
};

const Extras: React.FC<{ mood: HantelekMood; frame: number }> = ({ mood, frame: f }) => {
  const tw = (o: number) => 0.6 + 0.5 * Math.abs(Math.sin((f + o) / 9));
  switch (mood) {
    case "hello":
      return (
        <g fill="#FBBF24">
          <path transform={`translate(100 22) scale(${tw(0)}) translate(-100 -22)`} d="M100 14 l2.4 5.2 5.2 2.4 -5.2 2.4 -2.4 5.2 -2.4 -5.2 -5.2 -2.4 5.2 -2.4z" />
          <circle cx="88" cy="12" r={2 * tw(8)} />
          <circle cx="20" cy="20" r={1.8 * tw(16)} fill="#A78BFA" />
        </g>
      );
    case "celebrate": {
      const fall = (o: number) => ((f * 1.4 + o) % 60) - 6;
      const cols = ["#F472B6", "#FBBF24", "#06D6A0", "#60A5FA", "#A78BFA"];
      return (
        <g>
          {cols.map((c, i) => (
            <rect key={i} x={14 + i * 21} y={fall(i * 13)} width={5} height={3} rx={1} fill={c}
              transform={`rotate(${(f * 6 + i * 50) % 360} ${16 + i * 21} ${fall(i * 13) + 1.5})`} opacity={0.9} />
          ))}
        </g>
      );
    }
    case "think":
      return (
        <g fill="#CBD5E1">
          <circle cx="92" cy="26" r={3 * tw(0)} />
          <circle cx="100" cy="17" r={4 * tw(5)} />
          <ellipse cx="110" cy="7" rx={7 * tw(10)} ry={5 * tw(10)} />
        </g>
      );
    case "remind": {
      const ring = Math.sin(f / 2.2) * 14 * Math.max(0, Math.sin(f / 20));
      return (
        <g transform={`rotate(${ring} 104 8)`}>
          <path d="M104 8 a6 6 0 0 1 6 6 v5 l2 3 h-16 l2 -3 v-5 a6 6 0 0 1 6 -6z" fill="#FBBF24" />
          <circle cx="104" cy="24.5" r="2.2" fill="#F59E0B" />
        </g>
      );
    }
    case "sleep":
      return (
        <g fill="#94A3B8" fontFamily="Arial, sans-serif" fontWeight={700}>
          <text x="90" y={28 - (f % 60) / 8} fontSize="13" opacity={1 - (f % 60) / 60}>Z</text>
          <text x="101" y="18" fontSize="10">z</text>
        </g>
      );
    default:
      return null;
  }
};

export const Hantelek: React.FC<{
  mood?: HantelekMood; frame: number; talk?: number; size?: number;
  /** 0..1 – podskok/salto radości (np. przy CTA) */ jump?: number;
  style?: React.CSSProperties; id?: string;
}> = ({ mood = "happy", frame: f, talk = 0, size = 300, jump = 0, style, id = "hk" }) => {
  const breath = Math.sin(f / 15) * 0.5 + 0.5;
  const sx = 1 + breath * 0.015 + talk * 0.02, sy = 1 + breath * 0.035 - talk * 0.01;
  const bob =
    mood === "celebrate" ? -Math.abs(Math.sin(f / 5.5)) * 9 :
    mood === "happy" ? Math.sin(f / 12.4) * 3 : 0;
  const tilt =
    mood === "think" ? -6 * Math.max(0, Math.sin(f / 24)) :
    mood === "hello" ? Math.sin(f / 4) * 6 * Math.max(0, Math.sin(f / 18) - 0.4) : 0;
  const plates = Math.sin(f / 15) * 2.5 + (talk > 0.1 ? Math.sin(f / 2.5) * 3 : 0);
  // mruganie co ~4,6 s
  const bp = f % 138;
  const blink = bp > 128 && bp < 134 ? 0.1 : 1;
  const flip = jump > 0 ? Math.sin(Math.min(1, jump) * Math.PI) : 0;
  const rot = jump > 0 ? Math.min(1, jump) * 360 : 0;
  const s = mood !== "sleep" && mood !== "think" ? 1 : 1;
  return (
    <svg viewBox="0 0 120 120" width={size} height={size} style={{ overflow: "visible", ...style }}>
      <defs>
        <linearGradient id={`${id}b`} x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#9F7AEA" /><stop offset="1" stopColor="#6D28D9" /></linearGradient>
        <linearGradient id={`${id}p`} x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#475569" /><stop offset="1" stopColor="#1E293B" /></linearGradient>
        <clipPath id={`${id}c`}><rect x="33" y="38" width="54" height="50" rx="24" /></clipPath>
      </defs>
      <ellipse cx="60" cy="102" rx={36 * (1 - flip * 0.35)} ry="4.5" fill="#0F172A" opacity={0.13 * s} />
      <g transform={`translate(0 ${bob - flip * 34}) rotate(${tilt + rot} 60 ${jump > 0 ? 63 : 100})`}>
        <g transform={`translate(60 100) scale(${sx} ${sy}) translate(-60 -100)`}>
          <Plates id={id} l={-plates} r={plates} />
          <rect x="33" y="38" width="54" height="50" rx="24" fill={`url(#${id}b)`} />
          <g clipPath={`url(#${id}c)`}>
            <rect x="30" y="44" width="60" height="8" fill="#06D6A0" />
            <rect x="30" y="44" width="60" height="2.5" fill="#fff" opacity=".35" />
            <rect x={40 + ((f * 1.2) % 160) - 20} y="44" width="6" height="8" fill="#fff" opacity={(f % 133) < 50 ? 0.55 : 0} />
          </g>
          <ellipse cx="47" cy="44" rx="7" ry="3" fill="#fff" opacity=".22" />
          <Eyes mood={mood} blink={blink} />
          {mood !== "think" && (
            <>
              <circle cx="43" cy="72" r="4.5" fill="#F472B6" opacity=".55" />
              <circle cx="77" cy="72" r="4.5" fill="#F472B6" opacity=".55" />
            </>
          )}
          <Mouth mood={mood} talk={talk} />
        </g>
      </g>
      <Extras mood={mood} frame={f} />
    </svg>
  );
};
