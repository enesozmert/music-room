# Multi-stage: the SDK image builds, the runtime image ships. The runtime image contains neither
# the SDK nor the source — ci-cd-containerization. Base images are pinned by tag, never `latest`.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, against the manifests only, so a source-only change does not re-download packages.
COPY Directory.Build.props Directory.Packages.props SonarLint.xml MusicRoom.sln ./
COPY src/corePackages/Core.Application/Core.Application.csproj src/corePackages/Core.Application/
COPY src/corePackages/Core.CrossCuttingConcerns/Core.CrossCuttingConcerns.csproj src/corePackages/Core.CrossCuttingConcerns/
COPY src/corePackages/Core.Persistence/Core.Persistence.csproj src/corePackages/Core.Persistence/
COPY src/corePackages/Core.Security/Core.Security.csproj src/corePackages/Core.Security/
COPY src/MusicRoom/Domain/MusicRoom.Domain.csproj src/MusicRoom/Domain/
COPY src/MusicRoom/Application/MusicRoom.Application.csproj src/MusicRoom/Application/
COPY src/MusicRoom/Persistence/MusicRoom.Persistence.csproj src/MusicRoom/Persistence/
COPY src/MusicRoom/Infrastructure/MusicRoom.Infrastructure.csproj src/MusicRoom/Infrastructure/
COPY src/MusicRoom/WebAPI/MusicRoom.WebAPI.csproj src/MusicRoom/WebAPI/
RUN dotnet restore src/MusicRoom/WebAPI/MusicRoom.WebAPI.csproj

COPY src/ src/
# The OpenAPI document is emitted and committed from a developer build, not from the image.
# Not --no-restore: the publish configuration re-evaluates the package graph, and a restore that
# only saw the manifests is not guaranteed to satisfy it. The layer above still does the download,
# so this resolves from the package cache rather than the network.
RUN dotnet publish src/MusicRoom/WebAPI/MusicRoom.WebAPI.csproj \
        -c Release \
        -o /app/publish \
        -p:OpenApiGenerateDocumentsOnBuild=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The aspnet image ships a non-root `app` user. Use it — never run as root.
USER app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build --chown=app:app /app/publish ./

ENTRYPOINT ["dotnet", "MusicRoom.WebAPI.dll"]
