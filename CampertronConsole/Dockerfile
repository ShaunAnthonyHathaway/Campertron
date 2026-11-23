#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.
# Multi-architecture: the SDK stage always runs on the build machine's platform ($BUILDPLATFORM) and
# publishes for the target architecture ($TARGETARCH: amd64 or arm64), so building linux/arm64 on an
# amd64 host needs no emulation. The final stage contains no RUN instruction.
# The base is the ASP.NET Core runtime image (decision D85): the console hosts the web UI (CampertronConsole --web, D80), so
# it needs the Microsoft.AspNetCore.App shared framework. Web mode is opt-in: the image's default is still the headless
# monitoring, and nothing listens unless the container is started with --web (for example Compose: command: ["--web"],
# ports: ["8790:8790"]). EXPOSE only documents the web UI's port. The image has no HEALTHCHECK, because its default mode
# serves nothing; Compose can use ["CMD", "dotnet", "CampertronConsole.dll", "--healthcheck"] for a web-mode container.

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS base
USER app
WORKDIR /app
EXPOSE 8790

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
ARG VERSION=0.0.0-local
WORKDIR /src
COPY ["CampertronConsole/CampertronConsole.csproj", "CampertronConsole/"]
COPY ["CampertronLibrary/CampertronLibrary.csproj", "CampertronLibrary/"]
COPY ["CampertronWeb/CampertronWeb.csproj", "CampertronWeb/"]
RUN dotnet restore "./CampertronConsole/./CampertronConsole.csproj" -a $TARGETARCH
COPY . .
WORKDIR "/src/CampertronConsole"
RUN dotnet build "./CampertronConsole.csproj" -c $BUILD_CONFIGURATION -a $TARGETARCH --no-restore -o /app/build -p:Version=$VERSION

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
ARG VERSION=0.0.0-local
RUN dotnet publish "./CampertronConsole.csproj" -c $BUILD_CONFIGURATION -a $TARGETARCH --no-restore -o /app/publish -p:UseAppHost=false -p:Version=$VERSION

FROM base AS final
ARG VERSION=0.0.0-local
ARG REVISION=unknown
LABEL org.opencontainers.image.title="Campertron console" \
      org.opencontainers.image.description="Polls recreation.gov for campsite availability and notifies you of new matches by email or ntfy (headless, outputTo: Email or Ntfy)." \
      org.opencontainers.image.source="https://github.com/ShaunAnthonyHathaway/Campertron" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version="$VERSION" \
      org.opencontainers.image.revision="$REVISION"
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CampertronConsole.dll"]
