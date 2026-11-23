import React from "react";
import { fontFamily } from "../common";

const OUT = "#2B2340";
const SW = 6;
const T: React.CSSProperties = { fontFamily, fontWeight: 900 };

export type ScreenRow = { label: string; value?: string; accent?: string };

/** Makieta telefonu (SVG): prawdziwy zrzut ekranu (image) albo rysowany ekran aplikacji (title + rows). */
export const PhoneMock: React.FC<{
  x: number; y: number; scale?: number; rotate?: number;
  image?: string; title?: string; subtitle?: string; rows?: ScreenRow[];
  brand?: string; reveal?: number; frame: number;
}> = ({ x, y, scale = 1, rotate = 0, image, title, subtitle, rows = [], brand = "#7C3AED", reveal = 1, frame }) => {
  const W = 400, H = 820;
  const clip = `phone-clip-${x}-${y}`;
  return (
    <g transform={`translate(${x} ${y}) rotate(${rotate}) scale(${scale})`}>
      <rect x={-W / 2 - 18} y={-H / 2 - 18} width={W + 36} height={H + 36} rx={64} fill="#15131F" stroke={OUT} strokeWidth={SW} />
      <clipPath id={clip}><rect x={-W / 2} y={-H / 2} width={W} height={H} rx={48} /></clipPath>
      <g clipPath={`url(#${clip})`}>
        <rect x={-W / 2} y={-H / 2} width={W} height={H} fill="#FAFAFF" />
        {image ? (
          <image href={image} x={-W / 2} y={-H / 2} width={W} height={H} preserveAspectRatio="xMidYMin slice" />
        ) : (
          <g>
            <rect x={-W / 2} y={-H / 2} width={W} height={190} fill={brand} />
            <text x={-W / 2 + 30} y={-H / 2 + 110} fill="#fff" fontSize={34} style={T}>{title ?? "Hantly"}</text>
            {subtitle && <text x={-W / 2 + 30} y={-H / 2 + 152} fill="#ffffffcc" fontSize={22} style={{ ...T, fontWeight: 600 }}>{subtitle}</text>}
            {rows.slice(0, 5).map((r, i) => {
              const show = Math.max(0, Math.min(1, reveal * (rows.length + 1) - i));
              const ry = -H / 2 + 230 + i * 112;
              return (
                <g key={i} opacity={show} transform={`translate(${(1 - show) * 40} 0)`}>
                  <rect x={-W / 2 + 22} y={ry} width={W - 44} height={92} rx={20} fill="#fff" stroke="#E8E5F0" strokeWidth={3} />
                  <rect x={-W / 2 + 22} y={ry} width={10} height={92} rx={5} fill={r.accent ?? brand} />
                  <text x={-W / 2 + 48} y={ry + 40} fill="#1A1625" fontSize={Math.min(24, 520 / Math.max(10, r.label.length))} style={{ ...T, fontWeight: 800 }}>{r.label}</text>
                  {r.value && <text x={-W / 2 + 48} y={ry + 72} fill="#5E5872" fontSize={20} style={{ ...T, fontWeight: 600 }}>{r.value}</text>}
                </g>
              );
            })}
          </g>
        )}
      </g>
      <rect x={-60} y={-H / 2 + 10} width={120} height={28} rx={14} fill="#15131F" />
      <ellipse cx={0} cy={H / 2 + 60} rx={W * 0.5} ry={16} fill="#000" opacity={0.18} />
      {/* odblask szkła */}
      <path d={`M${-W / 2 + 20} ${-H / 2 + 40} L${-W / 2 + 120} ${-H / 2 + 40} L${-W / 2 + 20} ${-H / 2 + 260} Z`} fill="#fff" opacity={0.06 + Math.sin(frame / 30) * 0.02} />
    </g>
  );
};
