# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Central package management + restore inputs
COPY ["Directory.Packages.props", "./"]
COPY ["nuget.config", "./"]
COPY ["UrbanX.sln", "./"]

# Project files for layer-cached restore (UrbanX.Catalog.API dependency graph)
COPY ["src/ServiceDefaults/UrbanX.ServiceDefaults/UrbanX.ServiceDefaults.csproj", "src/ServiceDefaults/UrbanX.ServiceDefaults/"]
COPY ["src/Shared/UrbanX.Shared/UrbanX.Shared.csproj", "src/Shared/UrbanX.Shared/"]
COPY ["src/Shared/UrbanX.Shared.EventBus/UrbanX.Shared.EventBus.csproj", "src/Shared/UrbanX.Shared.EventBus/"]
COPY ["src/Shared/UrbanX.Shared.Idempotent/UrbanX.Shared.Idempotent.csproj", "src/Shared/UrbanX.Shared.Idempotent/"]
COPY ["src/Shared/UrbanX.Shared.Kafka/UrbanX.Shared.Kafka.csproj", "src/Shared/UrbanX.Shared.Kafka/"]
COPY ["src/Shared/UrbanX.Shared.MediatR/UrbanX.Shared.MediatR.csproj", "src/Shared/UrbanX.Shared.MediatR/"]
COPY ["src/Shared/UrbanX.Shared.Security/UrbanX.Shared.Security.csproj", "src/Shared/UrbanX.Shared.Security/"]
COPY ["src/Services/Catalog/UrbanX.Catalog.Domain/UrbanX.Catalog.Domain.csproj", "src/Services/Catalog/UrbanX.Catalog.Domain/"]
COPY ["src/Services/Catalog/UrbanX.Catalog.Application/UrbanX.Catalog.Application.csproj", "src/Services/Catalog/UrbanX.Catalog.Application/"]
COPY ["src/Services/Catalog/UrbanX.Catalog.Persistence/UrbanX.Catalog.Persistence.csproj", "src/Services/Catalog/UrbanX.Catalog.Persistence/"]
COPY ["src/Services/Catalog/UrbanX.Catalog.API/UrbanX.Catalog.API.csproj", "src/Services/Catalog/UrbanX.Catalog.API/"]

RUN dotnet restore "src/Services/Catalog/UrbanX.Catalog.API/UrbanX.Catalog.API.csproj"

COPY . .
WORKDIR "/src/src/Services/Catalog/UrbanX.Catalog.API"
RUN dotnet publish "UrbanX.Catalog.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd -r -g 1001 urbanx && useradd -r -u 1001 -g urbanx urbanx

COPY --from=build --chown=urbanx:urbanx /app/publish .

USER 1001

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl --fail http://localhost:8080/alive || exit 1

ENTRYPOINT ["dotnet", "UrbanX.Catalog.API.dll"]
