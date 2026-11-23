# Hantly – renderer Studia reklam (Remotion + Chromium + TTS + robot od zrzutów).
# Tylko sieć wewnętrzna „ptscheduler”, bez portu na zewnątrz. Dane: /data (udział hantly-studio).
# Budowany z katalogu głównego repo (tak buduje go Guardian i deploy/update-renderer.sh):
#   docker build -f studio/Dockerfile -t hantly-renderer:latest .
# Bez funkcji BuildKit – Guardian buduje klasycznym „docker build”.
FROM node:22-bookworm-slim

# Zależności Chromium (wg dokumentacji Remotion) + czcionki z emoji i polskimi znakami
RUN apt-get update && apt-get install -y --no-install-recommends \
      ca-certificates curl \
      libnss3 libdbus-1-3 libatk1.0-0 libgbm-dev libasound2 libxrandr2 libxkbcommon-dev \
      libxfixes3 libxcomposite1 libxdamage1 libatk-bridge2.0-0 libpango-1.0-0 libcairo2 libcups2 \
      fonts-noto-color-emoji fonts-noto-core fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY studio/package.json studio/package-lock.json ./
RUN npm ci --no-audit --no-fund --ignore-scripts
# Chrome Headless Shell dla Remotion (i robota od zrzutów) – pobierany przy budowie, nie w czasie renderu
RUN npx remotion browser ensure

COPY studio/tsconfig.json ./
COPY studio/src ./src
COPY studio/server ./server

ARG BUILD_COMMIT=unknown
ENV NODE_ENV=production PORT=3000 DATA_DIR=/data RENDER_CONCURRENCY=2 TZ=Europe/Warsaw \
    RENDERER_BUILD_COMMIT=${BUILD_COMMIT}
EXPOSE 3000
HEALTHCHECK --interval=30s --timeout=5s --start-period=90s --retries=3 CMD curl -fs http://127.0.0.1:3000/health || exit 1
CMD ["node", "server/server.mjs"]
