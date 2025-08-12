# SQL Server Schema Migration Generator

## Overview

The SQL Server Schema Migration Generator automatically creates migration scripts based on Git-tracked schema changes. It analyzes differences between commits and generates appropriate DDL statements to migrate databases from one state to another.

## Features

- **Automatic Migration Generation**: Detects schema changes from Git history and creates migration scripts
- **Migration Script Splitting**: Intelligently splits complex migrations into organized segments by database object
- **Change Detection**: Supports schemas, filegroups, tables, columns, indexes, constraints, views, stored procedures, functions, and triggers
- **Rename Detection**: Intelligently detects renamed objects based on content similarity
- **Dependency Resolution**: Orders operations correctly to handle dependencies
- **Migration History**: Tracks applied migrations in the database
- **Git Integration**: Uses Git for change tracking and version control

## How It Works

1. The generator analyzes uncommitted changes in the Git repository
2. Identifies schema modifications by parsing SQL files
3. Generates migration scripts and splits them into organized segments
4. Saves split migration files in the `z_migrations/[timestamp]_[actor]_[description]/` folder
5. Commits the schema changes to Git

## Directory Structure

```
servers/
└── [server-name]/
    └── [database-name]/
        ├── schemas/               # Schema definitions
        ├── filegroups/            # Filegroup definitions
        ├── Tables/
        ├── Views/
        ├── StoredProcedures/
        ├── Functions/
        └── z_migrations/          # Migration scripts (split into segments)
            └── [timestamp]_[actor]_[description]/
                ├── manifest.json
                ├── 001_schema_sys_SchemaName.sql
                ├── 002_filegroup_sys_FilegroupName.sql
                ├── 003_table_dbo_TableName.sql
                └── ...
```

## Migration File Naming

Migration folders follow this pattern:
```
_YYYYMMDD_HHMMSS_[actor]_[description]/
```

Example: `_20240115_143022_john_2tables_1indexes/`

Within each migration folder, files are named:
```
[sequence]_[objectType]_[schema]_[objectName].sql
```

Example: `001_table_dbo_Customer.sql`

## Usage

### Command Line

```bash
dotnet run -- <outputPath> <targetServer> <targetDatabase>
```

### Parameters

- `outputPath`: Root directory containing the schema files
- `targetServer`: Target SQL Server instance name
- `targetDatabase`: Target database name

### Optional Environment Variables

- `GITHUB_ACTOR`: Actor name for migration attribution (defaults to system username)

## Migration Script Structure

### Forward Migration
```sql
-- Migration metadata
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Check if already applied
IF EXISTS (SELECT 1 FROM [dbo].[DatabaseMigrationHistory] WHERE [MigrationId] = '...')
BEGIN
    PRINT 'Migration already applied. Skipping.';
    RETURN;
END

-- Schema changes
-- ... DDL statements ...

-- Record migration
INSERT INTO [dbo].[DatabaseMigrationHistory] ...

COMMIT TRANSACTION;
```

### Migration Splitting

Complex migrations are automatically split into organized segments:
- Each database object's operations are grouped together
- Execution order is preserved through numbered prefixes
- A manifest.json file describes the migration structure
- No reverse migrations are generated (use database snapshots or backups for rollback)

COMMIT TRANSACTION;
```

## Supported Operations

- **Tables**: CREATE, DROP, ALTER
- **Columns**: ADD, DROP, ALTER, RENAME
- **Indexes**: CREATE, DROP, ALTER
- **Constraints**: ADD, DROP (PK, FK, DEFAULT, CHECK)
- **Views**: CREATE, DROP, ALTER
- **Stored Procedures**: CREATE, DROP, ALTER
- **Functions**: CREATE, DROP, ALTER
- **Triggers**: CREATE, DROP, ALTER
- **Extended Properties**: Column descriptions

## Reverse Migrations

See [ReverseMigrations.md](Documentation/ReverseMigrations.md) for detailed information about rollback scripts.

## Best Practices

1. Always review generated migrations before applying them
2. Test migrations in a non-production environment first
3. Keep the Git repository clean - commit regularly
4. Use meaningful actor names for attribution
5. Document complex schema changes in commit messages

## Limitations

- Requires Git for change tracking
- Cannot detect certain complex refactoring patterns
- Data migrations must be handled separately
- Some DDL operations may require manual adjustment

## Troubleshooting

### No Changes Detected
- Ensure files are tracked in Git
- Check that changes are uncommitted
- Verify the correct target path

### Migration Generation Fails
- Check for syntax errors in SQL files
- Ensure proper Git repository initialization
- Verify file permissions

### Rename Detection Issues
- Renames are detected based on content similarity
- Very different content may not be recognized as a rename
- Manual adjustment may be needed

## Development

### Running Tests
```bash
dotnet test SqlServer.Schema.Migration.Generator.Tests
```

### Project Structure
- `Generation/`: DDL generation logic
- `Parsing/`: Schema change detection
- `GitIntegration/`: Git operations
- `Validation/`: Migration validation (currently disabled)

*Collaboration by Claude*