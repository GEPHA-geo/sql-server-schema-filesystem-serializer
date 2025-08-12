# Migration Script Splitting

## Overview

This document describes the migration script splitting feature for the SQL Server Compare tool. This feature takes generated migration SQL scripts and intelligently splits them into organized, logical segments based on database objects, making complex migrations easier to understand and review.

### Purpose

When migrations involve multiple database objects with complex interdependencies (such as table recreations with temporary tables, foreign key drops and recreations, index modifications), the resulting SQL script can be hundreds or thousands of lines long. The migration script splitter:

- Groups all related operations for each database object into a single file
- Maintains the critical execution order through numbered file prefixes
- Makes it easy to understand what changes are happening to each object
- Allows reconstruction of the original migration script when needed

### Key Benefits

1. **Clarity**: Each file contains all operations for a single database object
2. **Reviewability**: Easier to review changes in pull requests
3. **Traceability**: Clear understanding of what operations affect each object
4. **Maintainability**: Simpler to debug issues with specific objects
5. **Direct Execution**: No intermediate migration.sql file - splits are created directly

## Architecture

### Object Grouping Logic

The splitter identifies and groups SQL operations based on the database object they affect:

#### Table Operations
When a table is modified (especially when recreated), all of the following operations are grouped together in execution order:
- Foreign key drops (referencing this table)
- Index drops
- Table drop (if being recreated)
- Temporary table creation (`tmp_ms_xx_` pattern)
- Data migration to temporary table
- Old table cleanup
- Temporary table rename to final name
- Primary key creation
- Check constraint creation
- Index creation
- Foreign key recreation
- Extended properties

#### View/Procedure/Function Operations
- DROP IF EXISTS statements
- CREATE or ALTER statements
- Permission grants
- Extended properties

#### Schema Operations
- CREATE SCHEMA statements
- Schema-level permissions
- Schema ownership changes

#### Filegroup Operations
- ALTER DATABASE ADD FILEGROUP statements
- Filegroup property modifications
- File additions to filegroups

#### Special Patterns

##### Table Recreation Pattern (tmp_ms_xx)
SQL Server uses a pattern where tables are recreated using temporary tables with names like `tmp_ms_xx_TableName`. The splitter recognizes this pattern and ensures all operations are kept together:

```sql
-- All of these stay together in one file:
CREATE TABLE [dbo].[tmp_ms_xx_Customer] ...
INSERT INTO [dbo].[tmp_ms_xx_Customer] ...
DROP TABLE [dbo].[Customer]
EXEC sp_rename '[dbo].[tmp_ms_xx_Customer]', 'Customer'
```

##### Foreign Key Dependencies
Foreign keys that reference a table being modified are grouped with that table's operations to maintain referential integrity during the migration.

## Output Structure

### Folder Organization

```
z_migrations/
└── 20250812_123456_john_update_customer_schema/
    ├── manifest.json                    # Metadata and execution order
    ├── 001_schema_sys_NewSchema.sql    # Schema creation
    ├── 002_filegroup_sys_FG_Data.sql   # Filegroup creation
    ├── 003_table_dbo_Customer.sql      # Table operations
    ├── 004_table_dbo_Orders.sql        # Table operations
    ├── 005_view_dbo_vw_CustomerOrders.sql
    ├── 006_procedure_dbo_sp_GetCustomers.sql
    └── 007_function_dbo_fn_CalculateTotal.sql
```

Note: The original migration.sql file is no longer kept. The split files are created directly from the migration content.

### File Naming Convention

Files are named with the pattern: `{sequence}_{objectType}_{schema}_{objectName}.sql`

- **sequence**: Three-digit number indicating execution order (001, 002, etc.)
- **objectType**: Type of database object (schema, filegroup, table, view, procedure, function, trigger, etc.)
- **schema**: Database schema (typically dbo)
- **objectName**: Name of the database object

### Manifest File Format

The `manifest.json` file contains metadata about the migration and its segments:

```json
{
  "version": "1.0",
  "timestamp": "20250812_123456",
  "actor": "john",
  "description": "update_customer_schema",
  "originalScript": "migration.sql",
  "totalSegments": 5,
  "executionOrder": [
    {
      "sequence": 1,
      "filename": "001_table_dbo_Customer.sql",
      "objectType": "table",
      "schema": "dbo",
      "objectName": "Customer",
      "operations": [
        "DROP CONSTRAINT FK_Orders_Customer",
        "CREATE TABLE tmp_ms_xx_Customer",
        "INSERT INTO tmp_ms_xx_Customer",
        "DROP TABLE Customer",
        "RENAME tmp_ms_xx_Customer TO Customer",
        "CREATE PRIMARY KEY PK_Customer",
        "CREATE INDEX IX_Customer_Email",
        "CREATE CONSTRAINT FK_Orders_Customer"
      ],
      "lineCount": 45,
      "hasDataModification": true
    },
    {
      "sequence": 2,
      "filename": "002_table_dbo_Orders.sql",
      "objectType": "table",
      "schema": "dbo",
      "objectName": "Orders",
      "operations": [
        "ALTER TABLE ADD COLUMN",
        "CREATE INDEX IX_Orders_OrderDate"
      ],
      "lineCount": 12,
      "hasDataModification": false
    }
  ],
  "summary": {
    "tablesModified": 2,
    "viewsCreated": 1,
    "proceduresAltered": 1,
    "functionsCreated": 1,
    "totalOperations": 12
  }
}
```

## Implementation Details

### Key Classes

#### MigrationScriptSplitter

The main class responsible for parsing and splitting migration scripts.

```csharp
public class MigrationScriptSplitter
{
    public async Task SplitMigrationScript(
        string migrationScriptPath,
        string outputDirectory);
        
    private List<ObjectScriptGroup> ParseAndGroupByObject(string script);
    
    private ObjectInfo IdentifyObject(string statement);
    
    private List<ObjectScriptGroup> SortByDependencies(
        List<ObjectScriptGroup> groups);
        
    private async Task GenerateManifest(
        string outputDirectory, 
        List<ObjectScriptGroup> groups);
}
```

### Parsing Strategy

1. **Statement Splitting**: Split the script on GO statements while preserving statement integrity
2. **Object Identification**: Parse each statement to identify the affected object
3. **Grouping**: Group statements by object, handling special cases like tmp_ms_xx patterns
4. **Dependency Ordering**: Sort groups based on object dependencies
5. **File Generation**: Write each group to its appropriately named file

### Object Identification Patterns

The splitter uses regex patterns to identify objects and operations:

```csharp
// Table operations
@"CREATE\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?"
@"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?"
@"DROP\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?"

// Temporary table pattern
@"tmp_ms_xx_(\w+)"

// Foreign key operations
@"CONSTRAINT\s+\[?(\w+)\]?\s+FOREIGN\s+KEY.*REFERENCES\s+\[?(\w+)\]?\.\[?(\w+)\]?"

// Index operations
@"CREATE\s+(?:UNIQUE\s+)?(?:CLUSTERED\s+)?(?:NONCLUSTERED\s+)?INDEX\s+\[?(\w+)\]?\s+ON\s+\[?(\w+)\]?\.\[?(\w+)\]?"

// Schema operations
@"CREATE\s+SCHEMA\s+\[?(\w+)\]?"

// Filegroup operations
@"ALTER\s+DATABASE.*?ADD\s+FILEGROUP\s+\[?(\w+)\]?"
```

## Usage Example

### Input Migration Script

```sql
-- Drop foreign key before table modification
ALTER TABLE [dbo].[Orders] DROP CONSTRAINT [FK_Orders_Customer]
GO

-- Recreate Customer table with new structure
CREATE TABLE [dbo].[tmp_ms_xx_Customer] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Email] NVARCHAR(255) NOT NULL,
    [CreatedDate] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
)
GO

-- Migrate data
SET IDENTITY_INSERT [dbo].[tmp_ms_xx_Customer] ON
INSERT INTO [dbo].[tmp_ms_xx_Customer] ([Id], [Name], [Email], [CreatedDate])
SELECT [Id], [Name], 'unknown@example.com', GETUTCDATE() FROM [dbo].[Customer]
SET IDENTITY_INSERT [dbo].[tmp_ms_xx_Customer] OFF
GO

-- Replace old table
DROP TABLE [dbo].[Customer]
GO
EXEC sp_rename '[dbo].[tmp_ms_xx_Customer]', 'Customer'
GO

-- Add constraints
ALTER TABLE [dbo].[Customer] ADD CONSTRAINT [PK_Customer] PRIMARY KEY ([Id])
GO
CREATE INDEX [IX_Customer_Email] ON [dbo].[Customer] ([Email])
GO

-- Restore foreign key
ALTER TABLE [dbo].[Orders] ADD CONSTRAINT [FK_Orders_Customer] 
    FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([Id])
GO

-- Update stored procedure
ALTER PROCEDURE [dbo].[sp_GetCustomers]
AS
BEGIN
    SELECT Id, Name, Email, CreatedDate FROM Customer
END
GO
```

### Output Structure

```
z_migrations/20250812_123456_john_update_schema/
├── manifest.json
├── 001_table_dbo_Customer.sql (contains all Customer operations)
└── 002_procedure_dbo_sp_GetCustomers.sql
```

Note: No `changes/` subdirectory or original `migration.sql` file - split files are placed directly in the migration folder.

### 001_table_dbo_Customer.sql Content

```sql
-- Migration Segment: Table dbo.Customer
-- Generated: 2025-08-12 12:34:56
-- This file contains all operations related to table [dbo].[Customer]

-- Drop foreign key before table modification
ALTER TABLE [dbo].[Orders] DROP CONSTRAINT [FK_Orders_Customer]
GO

-- Recreate Customer table with new structure
CREATE TABLE [dbo].[tmp_ms_xx_Customer] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Email] NVARCHAR(255) NOT NULL,
    [CreatedDate] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
)
GO

-- Migrate data
SET IDENTITY_INSERT [dbo].[tmp_ms_xx_Customer] ON
INSERT INTO [dbo].[tmp_ms_xx_Customer] ([Id], [Name], [Email], [CreatedDate])
SELECT [Id], [Name], 'unknown@example.com', GETUTCDATE() FROM [dbo].[Customer]
SET IDENTITY_INSERT [dbo].[tmp_ms_xx_Customer] OFF
GO

-- Replace old table
DROP TABLE [dbo].[Customer]
GO
EXEC sp_rename '[dbo].[tmp_ms_xx_Customer]', 'Customer'
GO

-- Add constraints
ALTER TABLE [dbo].[Customer] ADD CONSTRAINT [PK_Customer] PRIMARY KEY ([Id])
GO
CREATE INDEX [IX_Customer_Email] ON [dbo].[Customer] ([Email])
GO

-- Restore foreign key
ALTER TABLE [dbo].[Orders] ADD CONSTRAINT [FK_Orders_Customer] 
    FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([Id])
GO
```

## Integration with Existing System

### Modification to DacpacMigrationGenerator

The splitter is integrated into the migration generation process:

```csharp
// In GenerateMigrationFromDacpacs method:
// Compare DACPACs returns the actual SQL content now
var migrationScript = await CompareDacpacs(
    sourceDacpacPath,
    targetDacpacPath,
    tempDir,
    scmpComparison);

// Create migration directory structure
var migrationDir = Path.Combine(migrationsPath, migrationDirName);
Directory.CreateDirectory(migrationDir);

// Create a temporary file for the splitter to process
var tempMigrationPath = Path.Combine(tempDir, "temp_migration.sql");
await File.WriteAllTextAsync(tempMigrationPath, migrationScript);

// Split the migration script into organized segments
var splitter = new MigrationScriptSplitter();
await splitter.SplitMigrationScript(tempMigrationPath, migrationDir);

// No original migration.sql is kept - only the split files
// No reverse migrations are generated anymore
```

### Reconstruction Capability

The system can reconstruct the original migration script from the segments:

```csharp
public async Task<string> ReconstructMigration(string segmentsDirectory)
{
    var manifestPath = Path.Combine(segmentsDirectory, "manifest.json");
    var manifest = JsonSerializer.Deserialize<MigrationManifest>(
        await File.ReadAllTextAsync(manifestPath));
    
    var scriptBuilder = new StringBuilder();
    foreach (var segment in manifest.ExecutionOrder)
    {
        var segmentPath = Path.Combine(segmentsDirectory, "changes", segment.Filename);
        scriptBuilder.AppendLine(await File.ReadAllTextAsync(segmentPath));
    }
    
    return scriptBuilder.ToString();
}
```

## Edge Cases and Special Handling

### 1. Circular Dependencies
When objects have circular dependencies, the splitter maintains the original script order to ensure successful execution.

### 2. Dynamic SQL
Dynamic SQL statements that build object names at runtime are kept in their original context.

### 3. Transactions
Transaction boundaries (BEGIN TRAN/COMMIT) are preserved within the relevant object file.

### 4. Cross-Schema References
Objects that reference multiple schemas are grouped based on the primary object being modified.

### 5. System Objects
System-level objects like schemas and filegroups are identified with the `sys` schema prefix and ordered appropriately:
- Schemas: `001_schema_sys_SchemaName.sql`
- Filegroups: `002_filegroup_sys_FilegroupName.sql`

## Testing Strategy

### Unit Tests
1. **Parsing Tests**: Verify correct identification of objects and operations
2. **Grouping Tests**: Ensure related operations stay together
3. **Ordering Tests**: Validate dependency-based ordering
4. **Pattern Tests**: Test tmp_ms_xx pattern recognition

### Integration Tests
1. **Round-trip Tests**: Split and reconstruct scripts, verify identical execution
2. **Complex Migration Tests**: Test with real-world complex migrations
3. **Edge Case Tests**: Test with circular dependencies, dynamic SQL, etc.

### Validation Tests
1. **Execution Order**: Verify segments can be executed in order without errors
2. **Completeness**: Ensure no SQL statements are lost during splitting
3. **Manifest Accuracy**: Validate manifest correctly describes all operations

## Configuration Options

Future enhancement could include configuration options:

```json
{
  "splittingEnabled": true,
  "preserveOriginal": true,
  "includeManifest": true,
  "groupingStrategy": "byObject",  // or "byOperation", "bySchema"
  "addComments": true,
  "generateSummary": true
}
```

## Conclusion

The migration script splitting feature enhances the migration generation process by providing clear, organized, and reviewable migration scripts while maintaining the critical execution order required for successful database updates. This makes complex database migrations more manageable and reduces the risk of errors during deployment.

*Collaboration by Claude*