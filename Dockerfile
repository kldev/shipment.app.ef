# The one host of Shipment: the Blazor front and the REST API over the application services (ADR 0002, 0013). No Node:
# wwwroot/css/app.css is committed.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG CONFIGURATION=Release
# The Husky target must not run in the image (no .git, no tools).
ENV HUSKY=0
WORKDIR /src

# 1) Only what restore needs - cached until dependencies change. Keep the list the closure of Shipment.Api's references.
COPY global.json Directory.Build.props Directory.Packages.props nuget.config ./
COPY apps/Shipment.Api/Shipment.Api.csproj apps/Shipment.Api/
COPY libs/Shipment.Application/Shipment.Application.csproj libs/Shipment.Application/
COPY libs/Shipment.Domain/Shipment.Domain.csproj libs/Shipment.Domain/
COPY libs/Shipment.Geo/Shipment.Geo.csproj libs/Shipment.Geo/
COPY libs/Shipment.Messaging.Contracts/Shipment.Messaging.Contracts.csproj libs/Shipment.Messaging.Contracts/
COPY libs/Shipment.Observability/Shipment.Observability.csproj libs/Shipment.Observability/
COPY libs/Shipment.Observability.AspNetCore/Shipment.Observability.AspNetCore.csproj libs/Shipment.Observability.AspNetCore/
COPY libs/Shipment.Persistence/Shipment.Persistence.csproj libs/Shipment.Persistence/
RUN dotnet restore apps/Shipment.Api/Shipment.Api.csproj

# 2) Sources and publish.
COPY libs/ libs/
COPY apps/ apps/
RUN dotnet publish apps/Shipment.Api/Shipment.Api.csproj \
    --configuration $CONFIGURATION \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false \
    /p:SkipTailwind=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# The sessions' keys (DataProtection__KeysPath): a named volume takes this owner on its first use.
RUN mkdir /keys && chown $APP_UID /keys
WORKDIR /app
COPY --from=build --chown=$APP_UID /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER $APP_UID

# No curl in the aspnet image: the app checks its own /health (ContainerHealthCheck).
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD ["dotnet", "Shipment.Api.dll", "healthcheck"]

ENTRYPOINT ["dotnet", "Shipment.Api.dll"]
