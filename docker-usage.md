# Docker Usage Guide for SQL Server Schema Migrator

## Quick Start

Pull and run the latest image:

```bash
docker run --rm -v $(pwd)/output:/output \
  ghcr.io/gepha-geo/sqlserver-schema-migrator:latest \
  "Server=dev-server;Database=DevDB;User Id=sa;Password=mypassword;TrustServerCertificate=true" \
  "Server=prod-server;Database=ProductionDB;User Id=sa;Password=mypassword;TrustServerCertificate=true" \
  "/output"
```

## Available Tags

- `latest` - Latest stable version from main branch
- `v1.0.0`, `v1.0`, `v1` - Semantic versioning tags
- `latest-arm64` - ARM64 architecture build
- `pr-123` - Pull request builds (for testing)

## Usage Examples

### Basic Extraction

```bash
# Create output directory
mkdir -p ./db_schemas

# Run extraction
docker run --rm \
  -v $(pwd)/db_schemas:/output \
  ghcr.io/gepha-geo/sqlserver-schema-migrator:latest \
  "Server=dev-server,1433;Database=DevDB;User Id=sa;Password=YourPassword;TrustServerCertificate=true" \
  "Server=prod-server,1433;Database=ProductionDB;User Id=sa;Password=YourPassword;TrustServerCertificate=true" \
  "/output"
```

### Using Environment Variables

```bash
# Set connection strings as environment variables
export SOURCE_DB="Server=dev-server,1433;Database=DevDB;User Id=sa;Password=YourPassword;TrustServerCertificate=true"
export TARGET_DB="Server=prod-server,1433;Database=ProductionDB;User Id=sa;Password=YourPassword;TrustServerCertificate=true"

# Run with environment variables
docker run --rm \
  -v $(pwd)/output:/output \
  ghcr.io/gepha-geo/sqlserver-schema-migrator:latest \
  "$SOURCE_DB" \
  "$TARGET_DB" \
  "/output"
```

### Docker Compose Example

Create a `docker-compose.yml`:

```yaml
version: '3.8'

services:
  schema-extractor:
    image: ghcr.io/gepha-geo/sqlserver-schema-migrator:latest
    volumes:
      - ./schemas:/output
    command: ["${SOURCE_CONNECTION}", "${TARGET_CONNECTION}", "/output"]
    environment:
      - SOURCE_CONNECTION=${SOURCE_DB}
      - TARGET_CONNECTION=${TARGET_DB}
```

Run with:
```bash
SOURCE_DB="dev-connection-string" TARGET_DB="prod-connection-string" docker-compose run --rm schema-extractor
```

### GitHub Actions Usage

```yaml
- name: Extract Database Schema
  run: |
    docker run --rm \
      -v ${{ github.workspace }}/output:/output \
      ghcr.io/gepha-geo/sqlserver-schema-migrator:latest \
      "${{ secrets.SOURCE_DB_CONNECTION }}" \
      "${{ secrets.TARGET_DB_CONNECTION }}" \
      "/output"
```

## Volume Mounts

The container expects an output directory to be mounted at `/output`:

- `-v $(pwd)/output:/output` - Mounts local `./output` directory
- The extracted files will be organized in hierarchical structure: `/output/servers/{target-server}/{target-database}/schemas/`

## Security Notes

1. **Never include passwords in commands** - Use environment variables or secrets
2. **Use read-only mounts when possible** - Add `:ro` to mount options if only reading
3. **Run as non-root** - The container runs as non-root user by default

## Troubleshooting

### Permission Issues

If you encounter permission issues with the output files:

```bash
# Run with current user's UID/GID
docker run --rm \
  --user $(id -u):$(id -g) \
  -v $(pwd)/output:/output \
  ghcr.io/gepha-geo/sqlserver-schema-migrator:latest \
  "source-connection-string" "target-connection-string" "/output"
```

### Network Issues

If connecting to SQL Server on localhost:

```bash
# Use host network mode
docker run --rm \
  --network host \
  -v $(pwd)/output:/output \
  ghcr.io/gepha-geo/sqlserver-schema-migrator:latest \
  "Server=localhost;..." "Server=prod-server;..." "/output"
```

### Debug Output

To see more detailed output:

```bash
# The application outputs to console by default
docker run --rm \
  -v $(pwd)/output:/output \
  ghcr.io/gepha-geo/sqlserver-schema-migrator:latest \
  "source-connection-string" "target-connection-string" "/output"
```

## Building Locally

If you want to build the image locally:

```bash
# Clone the repository
git clone https://github.com/GEPHA-geo/sql-server-schema-filesystem-serializer.git
cd sql-server-schema-filesystem-serializer

# Build using .NET SDK
dotnet publish SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner \
  --os linux \
  --arch x64 \
  -c Release \
  /t:PublishContainer \
  -p:ContainerRepository=my-local-image
```

## Multi-Architecture Support

The image is built for both AMD64 and ARM64 architectures. Docker will automatically pull the correct architecture for your system.

## Version Information

To check the version of the tool in the container:

```bash
docker run --rm ghcr.io/gepha-geo/sqlserver-schema-migrator:latest --version
```

*Note: Version command support will be added in a future release*