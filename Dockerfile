# syntax=docker/dockerfile:1

# Story 2.8: everything comes up with one command. One image, one origin (AD-12) — the page is
# built where Node lives, the API is published where the SDK lives, and what ships is the
# runtime image with the two outputs beside each other: no SDK, no npm, no sources.


# --- the page -----------------------------------------------------------------------------
FROM node:26-alpine AS web

WORKDIR /build/web

# The manifests on their own first, so `npm ci` stays cached until a dependency really moves.
COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./

# `npm run build` type-checks and then bundles, so a type error fails the image instead of
# shipping a page that only CI would have caught.
RUN npm run build


# --- the API ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api

WORKDIR /build

# The SDK pin and the two project files alone, so restore is a layer of its own.
COPY global.json ./
COPY src/FitFix.Core/FitFix.Core.csproj src/FitFix.Core/
COPY src/FitFix.Api/FitFix.Api.csproj src/FitFix.Api/
RUN dotnet restore src/FitFix.Api/FitFix.Api.csproj

COPY src/FitFix.Core/ src/FitFix.Core/
COPY src/FitFix.Api/ src/FitFix.Api/

# Framework-dependent, and no runtime identifier named: the runtime image below carries the
# framework, which is what lets a publish from this glibc SDK run on the musl image.
RUN dotnet publish src/FitFix.Api/FitFix.Api.csproj --configuration Release --no-restore --output /publish


# --- what ships ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final

WORKDIR /app

COPY --from=api /publish/ ./

# wwwroot is the web root by convention (Microsoft.NET.Sdk.Web) and the only place the service
# looks for a page. Without it every path answers ROUTE_NOT_FOUND, which is exactly what a
# checkout nobody built the frontend in does.
COPY --from=web /build/web/dist/ ./wwwroot/

# One port for the page and for /api/* alike (AD-12); the compose file publishes this one.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# The image's own unprivileged user. Nothing in the container is ever written — an upload is
# held in memory (AD-3) and no result is kept (AD-11) — so nothing needs to be owned either,
# and compose mounts the filesystem read-only.
USER $APP_UID

# What /health exists for (Story 2.1). wget and not curl: the runtime image has busybox and no
# curl, and a GET rather than a spider's HEAD, because /health is mapped for GET alone.
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --quiet --output-document=/dev/null http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "FitFix.Api.dll"]
