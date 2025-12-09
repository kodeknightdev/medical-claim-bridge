# syntax=docker/dockerfile:1.7
# Build context is the repository root: docker build -f src/services/providers/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0.304-bookworm-slim AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/shared/ src/shared/
COPY src/services/providers/ src/services/providers/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore src/services/providers/ClaimBridge.Providers/ClaimBridge.Providers.csproj --locked-mode
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet publish src/services/providers/ClaimBridge.Providers/ClaimBridge.Providers.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0.8-bookworm-slim AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ClaimBridge.Providers.dll"]
