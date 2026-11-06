# Production API image: multi-stage, non-root, health-checked.
# Build from the repo root:  docker build -f src/Messenger.Api/Dockerfile -t messenger-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Messenger.slnx Directory.Build.props global.json ./
COPY src/ src/
RUN dotnet publish src/Messenger.Api/Messenger.Api.csproj -c Release -o /app/publish --nologo \
    -p:ContinuousIntegrationBuild=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data/media \
    && chown -R app:app /app
USER app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "Messenger.Api.dll"]
