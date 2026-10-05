# AgriGuard API (ASP.NET Core, .NET 10). Multi-stage: the SDK image builds, the smaller ASP.NET
# runtime image runs. Build from this folder:  docker build -t agriguard-api backend
# All configuration (connection string, JWT key, agent key, CORS…) arrives as environment
# variables at run time; nothing secret is baked into the image.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files alone, so code edits do not invalidate the package layer.
COPY src/AgriGuard.Domain/AgriGuard.Domain.csproj src/AgriGuard.Domain/
COPY src/AgriGuard.Application/AgriGuard.Application.csproj src/AgriGuard.Application/
COPY src/AgriGuard.Infrastructure/AgriGuard.Infrastructure.csproj src/AgriGuard.Infrastructure/
COPY src/AgriGuard.Api/AgriGuard.Api.csproj src/AgriGuard.Api/
RUN dotnet restore src/AgriGuard.Api/AgriGuard.Api.csproj

COPY src/ src/
RUN dotnet publish src/AgriGuard.Api/AgriGuard.Api.csproj -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Npgsql probes GSSAPI (Kerberos) encryption on every new connection and logs an error when the
# library is missing; installing it keeps the logs clean and the fallback quick.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

# The image's built-in non-root user.
USER $APP_UID

# Render tells the service which port to bind through PORT; elsewhere the container uses 8080.
# Local development (dotnet run on http://localhost:5000) does not use this image at all.
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-8080} exec dotnet AgriGuard.Api.dll"]
