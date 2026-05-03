# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files first (layer-cached — only re-runs if .csproj changes)
COPY AirportSim.Shared/AirportSim.Shared.csproj   AirportSim.Shared/
COPY AirportSim.Server/AirportSim.Server.csproj   AirportSim.Server/

RUN dotnet restore AirportSim.Server/AirportSim.Server.csproj

# Copy everything else and publish
COPY AirportSim.Shared/   AirportSim.Shared/
COPY AirportSim.Server/   AirportSim.Server/

WORKDIR /src/AirportSim.Server
RUN dotnet publish -c Release -o /app/publish --no-restore

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app/publish .

# Copy airport layout JSON files that the simulation reads at runtime
COPY --from=build /src/AirportSim.Server/*.json ./

EXPOSE 5000

ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Development

ENTRYPOINT ["dotnet", "AirportSim.Server.dll"]