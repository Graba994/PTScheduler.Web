FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PTScheduler.Web.slnx ./
COPY PTScheduler.Domain/PTScheduler.Domain.csproj PTScheduler.Domain/
COPY PTScheduler.Application/PTScheduler.Application.csproj PTScheduler.Application/
COPY PTScheduler.Infrastructure/PTScheduler.Infrastructure.csproj PTScheduler.Infrastructure/
COPY PTScheduler.Web/PTScheduler.Web.csproj PTScheduler.Web/
RUN dotnet restore PTScheduler.Web/PTScheduler.Web.csproj

COPY . .
RUN dotnet publish PTScheduler.Web/PTScheduler.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

# The aspnet:10.0 base image now ships a non-root "app" user. Reuse it by
# forcing UID 10001 (existing volumes rely on it); fall back to creating the
# user on older base images that don't have it.
RUN (usermod -u 10001 app 2>/dev/null || useradd -u 10001 -m -s /usr/sbin/nologin app) \
    && mkdir -p /app/data /app/wwwroot/uploads \
    && chown -R app:app /app
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/ || exit 1

ENTRYPOINT ["dotnet", "PTScheduler.Web.dll"]
