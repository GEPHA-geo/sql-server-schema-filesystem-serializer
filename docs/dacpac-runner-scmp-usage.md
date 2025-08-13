# DACPAC Runner - SCMP File Usage

The DACPAC Runner now supports using SCMP (Schema Comparison) files as the primary source of configuration for database schema extraction and comparison.

## Key Features

1. **Automatic Connection Extraction**: Extracts source and target database connections from the SCMP file
2. **Password Support**: Supports providing passwords separately for security
3. **Deployment Options**: Uses SCMP configuration options for deployment settings
4. **Exclusion Management**: Applies exclusions defined in the SCMP file to generated SQL files

## Command Line Usage

### Basic Usage with SCMP File

```bash
dotnet run --project SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner \
  --scmp "/path/to/comparison.scmp" \
  --output-path "/path/to/output" \
  --source-password "YourPassword"
```

### Full Example

```bash
dotnet run --project SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner \
  --scmp "/mnt/c/Users/petre.chitashvili/repos/SQL_Server_Compare/mamuka_production.scmp" \
  --output-path "/mnt/c/Users/petre.chitashvili/repos/gepha/db_comparison" \
  --source-password "Vr9t#nE8%P068@iZ"
```

### Override SCMP Settings

You can still override specific settings from the SCMP file by providing them explicitly:

```bash
dotnet run --project SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner \
  --scmp "/path/to/comparison.scmp" \
  --target-server "different-server" \
  --target-database "different-db" \
  --output-path "/path/to/output" \
  --source-password "YourPassword"
```

## Parameters

- `--scmp`: Path to the SCMP XML file containing comparison settings
- `--source-password`: Password for source database (required for SQL authentication)
- `--output-path`: Directory where schema files will be generated (required)
- `--source-connection`: Override source connection from SCMP (optional)
- `--target-server`: Override target server from SCMP (optional)
- `--target-database`: Override target database from SCMP (optional)
- `--commit-message`: Custom git commit message (optional)
- `--skip-exclusion-manager`: Skip applying SCMP exclusions (optional)

## How It Works

1. **Load SCMP File**: The runner loads the SCMP XML file and deserializes it into a C# object structure
2. **Extract Connections**: 
   - Source connection string is extracted from `SourceModelProvider.ConnectionBasedModelProvider`
   - Target server and database are extracted from `TargetModelProvider`
   - If a password is provided, it's added to the source connection string
3. **Apply Deployment Options**: The SCMP configuration options are mapped to DacDeployOptions:
   - DropObjectsNotInSource
   - BlockOnPossibleDataLoss
   - IgnorePermissions
   - And many more...
4. **Generate Schema**: The DACPAC is extracted and deployment script is generated using the SCMP options
5. **Apply Exclusions**: Objects marked as excluded in the SCMP file are:
   - Identified by parsing ExcludedSourceElements and ExcludedTargetElements
   - Located in the generated SQL files
   - Marked with exclusion comments at the top of the file

## Exclusion Format

When an object is excluded via SCMP, the corresponding SQL file is modified:

```sql
-- EXCLUDED: dbo.TableName
-- This object is excluded from deployment based on SCMP configuration
-- Remove this comment to include the object in deployments

CREATE TABLE [dbo].[TableName] (
    -- table definition
)
```

## Benefits

1. **Single Source of Truth**: Use the same SCMP file used in Visual Studio SSDT
2. **Security**: Passwords are not stored in SCMP files
3. **Flexibility**: Override specific settings when needed
4. **Consistency**: Same deployment options as Visual Studio schema compare
5. **Automation**: Fully automated exclusion management

## Migration from Legacy Manifest Format

The system has been fully migrated from the legacy `.manifest` text format to the SCMP XML format. All manifest-related functionality now uses SCMP files exclusively.