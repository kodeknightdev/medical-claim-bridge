# syntax=docker/dockerfile:1.7
# Build context is the repository root: docker build -f src/services/scrubber/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0.304-bookworm-slim AS build
# the protoc bundled with Grpc.Tools crashes on linux/arm64 builders
RUN apt-get update && apt-get install -y --no-install-recommends protobuf-compiler && rm -rf /var/lib/apt/lists/*
ENV PROTOBUF_PROTOC=/usr/bin/protoc
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/shared/ src/shared/
COPY src/services/scrubber/ src/services/scrubber/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore src/services/scrubber/ClaimBridge.Scrubber/ClaimBridge.Scrubber.csproj --locked-mode
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet publish src/services/scrubber/ClaimBridge.Scrubber/ClaimBridge.Scrubber.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0.8-bookworm-slim AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ClaimBridge.Scrubber.dll"]
