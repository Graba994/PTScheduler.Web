FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PTScheduler.Portal/PTScheduler.Portal.csproj PTScheduler.Portal/
RUN dotnet restore PTScheduler.Portal/PTScheduler.Portal.csproj

COPY PTScheduler.Portal/ PTScheduler.Portal/
RUN dotnet publish PTScheduler.Portal/PTScheduler.Portal.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8081
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8081

ENTRYPOINT ["dotnet", "PTScheduler.Portal.dll"]
