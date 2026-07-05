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

COPY --from=build /app/publish .

# HTTP only inside container — terminate TLS at reverse proxy (Nginx/Traefik)
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "PTScheduler.Web.dll"]
