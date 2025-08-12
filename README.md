# SQL Server Compare

A comprehensive SQL Server database schema management and migration system that tracks changes, generates migrations, and maintains database structure in Git.

## Overview

SQL Server Compare provides tools for:
- **Schema Extraction**: Extract database schemas to file-based representation
- **Change Tracking**: Track schema changes through Git version control
- **Migration Generation**: Automatically generate migration scripts from schema changes
- **Migration Splitting**: Organize complex migrations into logical segments by database object
- **SCMP Support**: Integration with SQL Server Schema Compare (SCMP) files
- **Exclusion Management**: Manage objects excluded from deployment

## Key Components

### 1. DACPAC Runner
Extracts database schema using Data-tier Application Packages (DACPAC) technology.

```bash
dotnet SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.dll \
  --source-connection "Data Source=server;Initial Catalog=database;..." \
  --target-server "server-name" \
  --target-database "database-name" \
  --output-path "./output" \
  [--scmp "path/to/comparison.scmp"]
```

### 2. Migration Generator
Generates migration scripts by comparing database states.

Features:
- **Automatic Migration Detection**: Identifies changes between commits
- **Migration Script Splitting**: Breaks complex migrations into organized segments
- **Object Grouping**: Groups related operations (table recreation, FK drops/creates, etc.)
- **Dependency Resolution**: Ensures correct execution order

### 3. Migration Script Splitter
Intelligently splits migration scripts into organized segments:

```
z_migrations/
└── 20250812_123456_john_update_schema/
    ├── manifest.json
    ├── 001_schema_sys_NewSchema.sql
    ├── 002_filegroup_sys_FG_Data.sql
    ├── 003_table_dbo_Customer.sql
    └── 004_view_dbo_CustomerView.sql
```

## Directory Structure

```
servers/
└── [server-name]/
    └── [database-name]/
        ├── schemas/               # Schema definitions
        │   ├── dbo/
        │   │   ├── Tables/
        │   │   ├── Views/
        │   │   ├── StoredProcedures/
        │   │   └── Functions/
        │   └── [schema-name].sql  # Schema CREATE statement
        ├── filegroups/            # Filegroup definitions
        └── z_migrations/          # Migration scripts
```

## Supported Database Objects

- **Schemas**: CREATE SCHEMA statements
- **Filegroups**: ALTER DATABASE ADD FILEGROUP statements
- **Tables**: Including all constraints, indexes, and extended properties
- **Views**: Standard and indexed views
- **Stored Procedures**: Including CLR procedures
- **Functions**: Scalar, table-valued, and CLR functions
- **Triggers**: DML and DDL triggers
- **Indexes**: Clustered, non-clustered, unique, filtered
- **Constraints**: Primary keys, foreign keys, check, default
- **Extended Properties**: Descriptions and custom properties

## Migration Script Splitting

Complex migrations are automatically organized into logical segments:

### Benefits
- **Clarity**: Each file contains operations for a single database object
- **Reviewability**: Easier to review in pull requests
- **Traceability**: Clear understanding of changes per object
- **Maintainability**: Simpler debugging of specific objects

### Object Grouping Logic
- **Table Operations**: Groups all related operations including:
  - Foreign key drops/recreations
  - Table recreation with tmp_ms_xx pattern
  - Index operations
  - Constraint operations
  - Extended properties
- **Schema/Filegroup Operations**: System-level changes executed first
- **View/Procedure/Function Operations**: Grouped by object

## SCMP Integration

Supports SQL Server Schema Compare files for:
- Extracting connection information
- Applying comparison options
- Managing excluded objects
- Configuring deployment settings

## Usage Examples

### Extract Database Schema
```bash
dotnet SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.dll \
  --source-connection "Data Source=myserver;Initial Catalog=mydb;Integrated Security=True" \
  --target-server "myserver" \
  --target-database "mydb" \
  --output-path "./db_comparison"
```

### Use SCMP File
```bash
dotnet SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.dll \
  --scmp "./comparisons/production.scmp" \
  --output-path "./db_comparison"
```

### Generate Migration
Migrations are automatically generated when schema extraction detects changes.

## Docker Support

Run in Docker container for consistent environment:

```bash
docker build -t sql-compare .
docker run -v $(pwd):/workspace sql-compare \
  --source-connection "..." \
  --output-path "/workspace/output"
```

## Configuration

### Environment Variables
- `GITHUB_ACTOR`: Set actor name for migration attribution
- `GITHUB_WORKSPACE`: Set workspace for GitHub Actions integration

### SCMP Options
Configure comparison behavior through SCMP files:
- Object exclusions
- Deployment options
- Comparison settings

## Development

### Building
```bash
dotnet build
```

### Testing
```bash
dotnet test
```

### Project Structure
- `SqlServer.Schema.FileSystem.Serializer.Dacpac.Core/`: Core DACPAC functionality
- `SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner/`: CLI runner application
- `SqlServer.Schema.Migration.Generator/`: Migration generation and splitting
- `SqlServer.Schema.Exclusion.Manager/`: Exclusion management
- `docs/`: Documentation

## Documentation

- [Migration Script Splitting](docs/migration-script-splitting.md)
- [DACPAC Migration Generator](docs/dacpac-migration-generator.md)
- [SCMP Integration](docs/dacpac-runner-scmp-usage.md)
- [Docker Usage](docker-usage.md)

## License

[License information here]

## Contributing

[Contributing guidelines here]

*Collaboration by Claude*