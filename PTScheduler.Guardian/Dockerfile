FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PTScheduler.Guardian/PTScheduler.Guardian.csproj PTScheduler.Guardian/
RUN dotnet restore PTScheduler.Guardian/PTScheduler.Guardian.csproj

COPY PTScheduler.Guardian/ PTScheduler.Guardian/
RUN dotnet publish PTScheduler.Guardian/PTScheduler.Guardian.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update && apt-get install -y --no-install-recommends \
    curl ca-certificates gnupg bash git && \
    install -m 0755 -d /etc/apt/keyrings && \
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc && \
    chmod a+r /etc/apt/keyrings/docker.asc && \
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" > /etc/apt/sources.list.d/docker.list && \
    apt-get update && apt-get install -y --no-install-recommends docker-ce-cli && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:9090
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 9090

ENTRYPOINT ["dotnet", "PTScheduler.Guardian.dll"]
