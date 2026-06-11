# =============================================================================
# Etisalat SaaS Callback Service - Docker Build
# Author: imarouf
# .NET 10 Multi-Stage Build for Production
# =============================================================================

# -----------------------------------------------------------------------------
# Stage 1: Base runtime image
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8443

# Create non-root user for security
RUN addgroup -g 1000 appgroup && \
    adduser -u 1000 -G appgroup -s /bin/sh -D appuser

# Install curl for health checks
RUN apk add --no-cache curl

# -----------------------------------------------------------------------------
# Stage 2: Build
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copy only the project file first for better layer caching
COPY ["EtisalatSaasCallback.csproj", "."]
RUN dotnet restore "EtisalatSaasCallback.csproj"

# Copy source code and build
COPY . .
RUN dotnet build "EtisalatSaasCallback.csproj" -c Release -o /app/build --no-restore

# -----------------------------------------------------------------------------
# Stage 3: Publish
# -----------------------------------------------------------------------------
FROM build AS publish
RUN dotnet publish "EtisalatSaasCallback.csproj" -c Release -o /app/publish \
    --no-restore \
    /p:UseAppHost=false \
    /p:PublishTrimmed=false

# -----------------------------------------------------------------------------
# Stage 4: Final production image
# -----------------------------------------------------------------------------
FROM base AS final
WORKDIR /app

# Copy published application
COPY --from=publish /app/publish .

# Create logs directory
RUN mkdir -p /app/logs && chown -R appuser:appgroup /app/logs

# Switch to non-root user
USER appuser

# Environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/api/health || exit 1

# Labels
LABEL maintainer="imarouf" \
      version="1.0" \
      description="Etisalat SaaS Callback Service - ISV Provisioning Status API" \
      org.opencontainers.image.source="https://github.com/imarouf/EtisalatSaasCallback"

ENTRYPOINT ["dotnet", "EtisalatSaasCallback.dll"]
