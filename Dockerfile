# NexaFlow.Api Dockerfile — production-oriented multi-stage build.
# Section 36: reproducible local / integration environment.
# Section 40: run as a non-root user.

# ---------------------------------------------------------------
# Stage 1 — build
# ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

WORKDIR /src

# Copy the central package management + build props first so the restore step is cached.
# Layer ordering matters: changing source code should NOT invalidate the restore cache.
COPY Directory.Build.props Directory.Packages.props .editorconfig global.json ./
COPY NexaFlow.slnx ./

# Copy project files only — restores can be cached
COPY src/NexaFlow.Domain/ ./src/NexaFlow.Domain/
COPY src/NexaFlow.Application/ ./src/NexaFlow.Application/
COPY src/NexaFlow.Infrastructure/ ./src/NexaFlow.Infrastructure/
COPY src/NexaFlow.Api/ ./src/NexaFlow.Api/

# Restore as a distinct layer
RUN dotnet restore NexaFlow.slnx --nologo

# Now copy everything else (configurations, source files)
COPY . ./

# Build + publish a self-contained ASP.NET Core app
RUN dotnet publish src/NexaFlow.Api/NexaFlow.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    --nologo \
    /p:UseAppHost=false

# ---------------------------------------------------------------
# Stage 2 — runtime image
# ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime

# Section 40 — never run as root in production.
# Create a dedicated unprivileged user for the process.
RUN addgroup -S nexaflow && adduser -S nexaflow -G nexaflow

# Section 33 — healthcheck via curl + the live endpoint.
# The runtime image is alpine-based and does not ship curl by default.
RUN apk add --no-cache curl

WORKDIR /app
COPY --from=build /app/publish ./

# Set ASP.NET Core defaults for production
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_PRINT_TELEMETRY_MESSAGE=false \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true

USER nexaflow

EXPOSE 8080

# Composite health check: live endpoint, 30s startup grace, 5s interval.
HEALTHCHECK --interval=5s --timeout=3s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "NexaFlow.Api.dll"]
