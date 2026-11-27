# Renderer Studia reklam (hantly-renderer)

Node 22 + Remotion 4 + Chromium. Robi dla Portalu (Panel → Studio reklam):

- **filmy 9:16** – kompozycja `Ad` (`src/ad/AdShort.tsx`): ujęcia `ad-hook`, `ad-mascot`, `ad-screen`, `ad-assistant`,
  `ad-compare`, `ad-benefits`, `ad-cta`; napisy karaoke, muzyka i efekty z biblioteki (`/data/library`),
- **grafiki postów 4:5** – still `Post` (`src/ad/PromoPost.tsx`),
- **głos** – Azure Speech albo ElevenLabs z czasami słów (`server/tts.mjs`); klucze przychodzą w każdym żądaniu z Portalu,
- **zrzuty aplikacji** – robot na koncie demo przez jednorazowe linki SSO z Portalu (`server/screens.mjs`).

Hantelek (`src/ad/Hantelek.tsx`) to animowana kopia `PTScheduler.Portal/Services/MascotArt.cs` –
test `HantelekMatchesMascotArtTests` pilnuje, żeby kształty i kolory się zgadzały.

## Na serwerze

Kontener `hantly-renderer` w sieci Portalu (`ptscheduler`), **bez portu na zewnątrz**, dane w `/data`
= miejsce na filmy Portalu (`/mnt/user/hantly-studio`, w Portalu `/opt/ptscheduler/studio`).
Buduje i podmienia go Guardian: **Portal → Aktualizacje → Renderer filmów** (pierwszy raz sam go zakłada).
Ręcznie: `REPO_DIR=/mnt/user/appdata/hantly-repo bash deploy/update-renderer.sh`.

API (opis w nagłówku `server/server.mjs`): `/health`, `/tts`, `/preview-voice`, `/render`, `/stills`, `/post`, `/screens`, `/files/...`.

## Lokalnie

```
cd studio
npm install
npm run studio        # podgląd w przeglądarce (public/episodes/ad-demo – cisza i przykładowe napisy)
npm run typecheck
npx remotion render src/index.ts Ad out/demo.mp4
```
