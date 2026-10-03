FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PTScheduler.Portal/PTScheduler.Portal.csproj PTScheduler.Portal/
RUN dotnet restore PTScheduler.Portal/PTScheduler.Portal.csproj

COPY PTScheduler.Portal/ PTScheduler.Portal/
RUN dotnet publish PTScheduler.Portal/PTScheduler.Portal.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# git — for pulling latest source at upgrade time
# docker-ce-cli + compose plugin — to build tenant images and manage containers
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl ca-certificates gnupg bash openssl git && \
    install -m 0755 -d /etc/apt/keyrings && \
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc && \
    chmod a+r /etc/apt/keyrings/docker.asc && \
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" > /etc/apt/sources.list.d/docker.list && \
    apt-get update && apt-get install -y --no-install-recommends docker-ce-cli docker-compose-plugin && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# Build metadata baked at image build time — /panel/updates compares
# these against the remote to detect when an upgrade is available.
# Pass with --build-arg BUILD_COMMIT=$(git rev-parse HEAD) --build-arg BUILD_TIME=$(date -uIs).
ARG BUILD_COMMIT=unknown
ARG BUILD_TIME=unknown
ARG BUILD_BRANCH=main
ENV PTS_BUILD_COMMIT=$BUILD_COMMIT
ENV PTS_BUILD_TIME=$BUILD_TIME
ENV PTS_BUILD_BRANCH=$BUILD_BRANCH

ENV ASPNETCORE_URLS=http://+:8081
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8081

ENTRYPOINT ["dotnet", "PTScheduler.Portal.dll"]
