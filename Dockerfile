# Multi-stage build for SQL Server Schema Tools
# Combines DACPAC Runner and Exclusion Manager in a single image

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files first for better caching
COPY *.sln ./
COPY Directory.Build.props ./
COPY VERSION ./

# Copy project files
COPY SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner/*.csproj ./SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner/
COPY SqlServer.Schema.FileSystem.Serializer.Dacpac.Core/*.csproj ./SqlServer.Schema.FileSystem.Serializer.Dacpac.Core/
COPY SqlServer.Schema.Migration.Generator/*.csproj ./SqlServer.Schema.Migration.Generator/
COPY SqlServer.Schema.Exclusion.Manager/*.csproj ./SqlServer.Schema.Exclusion.Manager/

# Restore dependencies
RUN dotnet restore

# Copy everything else
COPY . .

# Build and publish both tools
RUN dotnet publish SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner \
    -c Release \
    -o /app/dacpac \
    --no-restore

RUN dotnet publish SqlServer.Schema.Exclusion.Manager \
    -c Release \
    -o /app/exclusion-manager \
    --no-restore

# Runtime stage - use the base image with git
FROM ghcr.io/gepha-geo/dotnet-runtime-git:9.0

# Install additional runtime dependencies if needed
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    libicu-dev \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy both applications
COPY --from=build /app/dacpac .
COPY --from=build /app/exclusion-manager ./exclusion-manager

# Create wrapper script for Exclusion Manager
RUN echo '#!/bin/bash' > /usr/local/bin/exclusion-manager && \
    echo 'exec dotnet /app/exclusion-manager/SqlServer.Schema.Exclusion.Manager.dll "$@"' >> /usr/local/bin/exclusion-manager && \
    chmod +x /usr/local/bin/exclusion-manager

# Set up Git configuration for container usage
RUN git config --global user.email "docker@sqlserver-migrator.local" && \
    git config --global user.name "SQL Server Schema Migrator" && \
    git config --global init.defaultBranch main && \
    git config --global --add safe.directory '*'

# Default working directory for operations
WORKDIR /workspace

# Labels
LABEL org.opencontainers.image.description="SQL Server Schema Migrator with DACPAC Runner and Exclusion Manager"
LABEL org.opencontainers.image.source="https://github.com/GEPHA-geo/sql-server-schema-filesystem-serializer"
LABEL org.opencontainers.image.title="sqlserver-schema-migrator"
LABEL org.opencontainers.image.authors="GEPHA"

# Default entrypoint is the DACPAC Runner for backward compatibility
ENTRYPOINT ["dotnet", "/app/SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.dll"]