import React from "react";
import { Composition, Still, staticFile } from "remotion";
import { sceneLayout } from "./common";
import { FPS, Timeline } from "./types";
import { AdShort } from "./ad/AdShort";
import { PromoPost, PromoPostProps } from "./ad/PromoPost";

type AdProps = { episodeId: string; timeline?: Timeline; assetBase?: string };

/** Kompozycje Studia reklam Hantly: film 9:16 („Ad”) i grafika posta 4:5 („Post”). */
export const RemotionRoot: React.FC = () => (
  <>
    {/* Reklama 9:16 – podgląd w `npm run studio`: public/episodes/ad-demo */}
    <Composition
      id="Ad"
      component={AdShort as React.FC<AdProps>}
      width={1080}
      height={1920}
      fps={FPS}
      durationInFrames={300}
      defaultProps={{ episodeId: "ad-demo" } as AdProps}
      calculateMetadata={async ({ props }) => {
        if (props.timeline) return { durationInFrames: sceneLayout(props.timeline).total };
        const base = `episodes/${props.episodeId}/`;
        const tl = (await (await fetch(staticFile(base + "timeline.json"))).json()) as Timeline;
        const ep = await fetch(staticFile(base + "episode.json")).then((r) => (r.ok ? r.json() : {}), () => ({}));
        const timeline = { ...tl, ...ep, audio: tl.audio } as Timeline;
        return { durationInFrames: sceneLayout(timeline).total, props: { ...props, timeline } };
      }}
    />
    {/* Grafika posta 4:5 */}
    <Still
      id="Post"
      width={1080}
      height={1350}
      component={PromoPost as React.FC<PromoPostProps>}
      defaultProps={{
        headline: "Koniec z zapisami na WhatsAppie o 23:00",
        sub: "Klienci sami rezerwują wolne terminy, a Ty masz wieczór dla siebie.",
        bullets: ["Rezerwacje 24/7 bez pisania", "Przypomnienia przed treningiem", "Pakiety i płatności w jednym"],
        screen: { title: "Mój grafik", subtitle: "Wtorek, 14 maja", rows: ["8:00 Anna – trening", "9:30 Marek – pakiet 4/10", "17:00 wolny termin"] },
      } as PromoPostProps}
    />
  </>
);
