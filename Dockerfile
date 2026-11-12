FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for layer caching (restore only re-runs when .csproj changes)
COPY PTScheduler.Domain/PTScheduler.Domain.csproj         PTScheduler.Domain/
COPY PTScheduler.Application/PTScheduler.Application.csproj PTScheduler.Application/
COPY PTScheduler.Infrastructure/PTScheduler.Infrastructure.csproj PTScheduler.Infrastructure/
COPY PTScheduler.Web/PTScheduler.Web.csproj               PTScheduler.Web/

RUN dotnet restore PTScheduler.Web/PTScheduler.Web.csproj

# Copy everything and publish
COPY PTScheduler.Domain/       PTScheduler.Domain/
COPY PTScheduler.Application/  PTScheduler.Application/
COPY PTScheduler.Infrastructure/ PTScheduler.Infrastructure/
COPY PTScheduler.Web/          PTScheduler.Web/

RUN dotnet publish PTScheduler.Web/PTScheduler.Web.csproj \
    -c Release -o /app/publish

# ── Runtime image ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# tzdata jest wymagane przez TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"),
# którego używa IAppClock do wyznaczania zegara ściennego studia.
# Bez tej bazy aplikacja wstanie, ale spadnie na UTC i godziny przypomnień
# będą przesunięte — z komunikatem Error w logu.
RUN apt-get update \
 && apt-get install -y --no-install-recommends tzdata \
 && rm -rf /var/lib/apt/lists/*

# UWAGA: celowo NIE ustawiamy ENV TZ. Kontener ma zostać w UTC.
# Część kolumn to nadal timestamptz odczytywany w trybie legacy Npgsql, który
# konwertuje odczyt na strefę maszyny — ustawienie TZ przesunęłoby te wartości.
# Strefę studia niesie APP_TIMEZONE i obsługuje wyłącznie IAppClock.
ENV APP_TIMEZONE=Europe/Warsaw

COPY --from=build /app/publish .

# HTTP only inside container — terminate TLS at reverse proxy (Nginx/Traefik)
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Wersja obrazu — przekazywana przez portal/Guardian jako --build-arg przy budowie.
# Dzięki temu apka zna commit/czas builda i pokazuje je w panelu admina.
# Domyślnie "unknown", gdy zbudowano bez argumentów.
ARG BUILD_COMMIT=unknown
ARG BUILD_TIME=unknown
ARG BUILD_BRANCH=unknown
ENV BUILD_COMMIT=$BUILD_COMMIT
ENV BUILD_TIME=$BUILD_TIME
ENV BUILD_BRANCH=$BUILD_BRANCH

EXPOSE 8080

VOLUME /app/wwwroot/branding

ENTRYPOINT ["dotnet", "PTScheduler.Web.dll"]
