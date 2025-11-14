# syntax=docker/dockerfile:1.7
# Build context is the repository root: docker build -f tools/webhook-receiver/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0.304-bookworm-slim AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/shared/ src/shared/
COPY tools/webhook-receiver/ tools/webhook-receiver/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore tools/webhook-receiver/ClaimBridge.WebhookReceiver/ClaimBridge.WebhookReceiver.csproj --locked-mode
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish tools/webhook-receiver/ClaimBridge.WebhookReceiver/ClaimBridge.WebhookReceiver.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0.8-bookworm-slim AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ClaimBridge.WebhookReceiver.dll"]
