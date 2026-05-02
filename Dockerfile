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
    -c Release -o /app/publish --no-restore

# Download Bootstrap Icons and FullCalendar locally so container needs no internet access
RUN apt-get update -qq && apt-get install -y --no-install-recommends wget ca-certificates \
 && mkdir -p /app/publish/wwwroot/lib/bootstrap-icons/fonts \
 && mkdir -p /app/publish/wwwroot/lib/fullcalendar \
 # Bootstrap Icons CSS + font
 && wget -q "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css" \
         -O /app/publish/wwwroot/lib/bootstrap-icons/bootstrap-icons.min.css \
 && wget -q "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/fonts/bootstrap-icons.woff2" \
         -O /app/publish/wwwroot/lib/bootstrap-icons/fonts/bootstrap-icons.woff2 \
 && wget -q "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/fonts/bootstrap-icons.woff" \
         -O /app/publish/wwwroot/lib/bootstrap-icons/fonts/bootstrap-icons.woff \
 # FullCalendar
 && wget -q "https://cdn.jsdelivr.net/npm/fullcalendar@6.1.15/index.global.min.css" \
         -O /app/publish/wwwroot/lib/fullcalendar/fullcalendar.min.css \
 && wget -q "https://cdn.jsdelivr.net/npm/fullcalendar@6.1.15/index.global.min.js" \
         -O /app/publish/wwwroot/lib/fullcalendar/fullcalendar.min.js \
 && wget -q "https://cdn.jsdelivr.net/npm/fullcalendar@6.1.15/locales/pl.global.min.js" \
         -O /app/publish/wwwroot/lib/fullcalendar/pl.global.min.js \
 && apt-get purge -y wget && apt-get autoremove -y && rm -rf /var/lib/apt/lists/*

# ── Runtime image ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# HTTP only inside container — terminate TLS at reverse proxy (Nginx/Traefik)
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "PTScheduler.Web.dll"]
