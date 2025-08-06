# Database Change Exclusion System

## TL;DR

**What**: A system to exclude specific database changes from migration scripts without modifying serialization.

**How**: 
1. **Initial generation**: DACPAC Runner → creates serialized files + migration script, then new tool creates manifest + adds comments
2. **Exclude changes**: Edit manifest file → move changes to EXCLUDED section → push to PR
3. **Auto-update**: GitHub workflow runs new tool → updates files with exclusion comments

**Key files**:
- **Manifest**: `change-manifest-{server}-{database}.manifest` - lists included/excluded changes
- **Serialized files**: SQL files with exclusion comments 
- **Migration script**: SQL with excluded changes commented out

**Tools**:
- **DACPAC Runner** (existing): Handles serialization + migration generation (no changes)
- **Manifest & Comment Manager** (new): Creates manifest, processes exclusions, updates comments in files

## Overview

This document describes a system for selectively excluding database changes from migration scripts while maintaining complete version control of the actual database state. The system allows users to control which detected changes are included in migration scripts without affecting the serialized database structure.

## Problem Statement

When serializing database structures for version control and comparison:
- Some changes are environment-specific and shouldn't be migrated
- Certain columns or indexes may need to be excluded from migrations
- Users need visibility into what changes are detected
- Users need control over what changes are included in migrations
- The actual database state must remain accurately represented in version control

Current limitations:
- Once a change is serialized, it automatically appears in migrations
- No way to exclude specific changes without modifying serialization
- No visibility into what changes will be migrated before generation

## Solution Architecture

### Core Concept

The solution introduces a **Change Manifest** - a human-readable text file that:
1. Lists all detected database changes
2. Separates changes into "included" and "excluded" sections
3. Allows users to move changes between sections
4. Persists exclusions across migration regenerations

### Key Principles

- **Serialization remains accurate** - The database structure is always captured as it truly exists
- **Initial serialization respects existing manifests** - If a manifest exists, exclusion comments are added during the first serialization - but not by the dacpac.runner tool, by the tool used to analyze the manifest file and apply knowledge from it to the serialized files; this happens either as the last step of the initial serialization or separately, by the tool which GitHub also uses to apply changes in the manifest file
- **Regeneration never re-serializes** - It only adds/removes comments to existing files
- **Comments are reversible** - Excluded changes are commented out, not deleted, enabling easy re-inclusion
- **Serialized files include exclusion comments** - When changes are excluded from migrations, comments are added to the serialized files indicating the exclusion
- **Filtering happens at migration time** - Exclusions only affect SQL script generation
- **User control** - Simple text file manipulation to control migrations
- **Versioned exclusions** - Change manifests are tracked in version control
- **Toggle-friendly** - Changes can be excluded and re-included multiple times by regenerating

## Change Manifest Specification

### File Format

**Filename**: `change-manifest-{server_name}-{database_name}.manifest`

This naming convention ensures that:
- Each database has its own manifest file
- Migration generator can identify which exclusions to apply
- Multiple databases on the same server have separate exclusion lists
- The `.manifest` extension clearly identifies the file's purpose

**File Content Structure**:
```
DATABASE: {database_name} {marker}
SERVER: {server_name} {marker}
GENERATED: {ISO-8601 timestamp} {marker}
COMMIT: {git_commit_hash} {marker}

=== INCLUDED CHANGES ===
{change_identifier} - {change_description} {marker}
{change_identifier} - {change_description} {marker}
...

=== EXCLUDED CHANGES ===
{change_identifier} - {change_description} {marker}
{change_identifier} - {change_description} {marker}
...
```

**Rotation Marker System**: 
To ensure the manifest always appears in PR diffs, each line ends with a rotation marker (`/` or `\`) that alternates with each regeneration:
- First generation: All lines end with `/`
- Next regeneration (with `--regenerate`): All lines end with `\`
- This pattern continues alternating only when `--regenerate` is run
- **Important**: When users manually edit the manifest, they should preserve the existing markers
- **Only `--regenerate` changes the marker direction** - this ensures PR consistency
- Within a single PR, all manual edits maintain the same marker symbol
- Ensures reviewers always see what's being included/excluded in the PR

### Change Identifiers

Change identifiers uniquely identify each database change:

| Object Type | Identifier Format | Example |
|------------|-------------------|---------|
| Column | `{schema}.{table}.{column}` | `dbo.Orders.CustomerID` |
| Index | `{schema}.{index_name}` | `dbo.IX_Orders_Date` |
| Constraint | `{schema}.{constraint_name}` | `dbo.FK_Orders_Customers` |
| Table | `{schema}.{table}` | `dbo.Orders` |
| View | `{schema}.{view_name}` | `dbo.vw_OrderSummary` |
| Stored Procedure | `{schema}.{procedure_name}` | `dbo.sp_GetOrders` |
| Function | `{schema}.{function_name}` | `dbo.fn_CalculateTotal` |

### Change Descriptions

Descriptions provide human-readable context:
- Column changes: `Column type changed from {old_type} to {new_type}`
- New objects: `{Object_type} added`
- Deleted objects: `{Object_type} removed`
- Modified objects: `{Object_type} definition changed`

### File Location

```
servers/
  {server_name}/
    {database_name}/
      change-manifest-{server_name}-{database_name}.manifest
      schemas/
      z_migrations/
```

## User Workflow

### Initial Migration Generation

**Phase 1: Generate Initial State**
1. User modifies database structure
2. Runs DACPAC Runner (no changes to existing tool):
   - Serializes current database state to files
   - Analyzes git diff to detect changes
   - Creates initial migration script with ALL changes active
3. Runs new Manifest & Comment Manager tool:
   - Creates manifest file with all changes in "INCLUDED" section
   - Adds initial exclusion comments to files (if any exclusions exist)

**Phase 2: Apply Exclusions (Separate Step)**
3. If user wants to exclude changes:
   - User edits manifest file (moves changes to EXCLUDED section)
   - Runs `--update-exclusion-comments`:
     - Updates serialized files with exclusion comments
     - Updates migration script (comments out excluded changes)
4. GitHub workflow runs this automatically when manifest changes in PR

### Excluding Changes

Users have two options for excluding changes:

#### Option A: Permanent Exclusion
1. User opens the `.manifest` file for their database
2. Identifies changes to exclude (e.g., environment-specific columns)
3. Cuts lines from "INCLUDED CHANGES" section
4. Pastes lines under "EXCLUDED CHANGES" section
5. Saves file and commits
6. Future migrations will continue to exclude these changes

#### Option B: Temporary Exclusion (Current Session Only)
1. User opens the `.manifest` file for their database
2. Identifies changes to exclude temporarily
3. **Deletes lines from "INCLUDED CHANGES" section** (does NOT move to excluded)
4. Saves file and commits
5. Current migration excludes these changes
6. **Future migrations will detect these changes again** as they're not in the excluded section

### Applying Manifest Changes

When manifest is modified in a PR:
1. User edits manifest → commits → pushes
2. GitHub workflow runs `--update-exclusion-comments`
3. Updates both serialized files and migration scripts
4. Commits changes back to PR

### Re-including Changes

Same as excluding but in reverse - move from EXCLUDED to INCLUDED section. GitHub workflow handles the rest.

## Implementation Details

### Component Architecture

```
DacpacSerializer/StructureGenerator
  ├── DacpacScriptParser (parses database structure)
  └── FileSystemManager (writes files)

MigrationGenerator
  ├── GitDiffAnalyzer (detects file changes)
  ├── SqlFileChangeDetector (analyzes changes)
  ├── ChangeManifestManager (NEW - handles manifest)
  │   ├── LoadManifest()
  │   ├── SaveManifest()
  │   ├── ApplyExclusions()
  │   └── MergeWithExisting()
  ├── MigrationScriptBuilder (generates SQL)
  └── ReverseMigrationBuilder (generates rollback)
```

### ChangeManifestManager Class

The `ChangeManifestManager` class should handle:
- Loading and saving manifest files
- Applying exclusions to filter changes
- Generating new manifests from detected changes
- Merging with existing manifests while preserving exclusions
- Generating change identifiers (e.g., `dbo.Orders.CustomerID`)
- Implementing the rotation marker system (`/` and `\`) for PR visibility

### Serializer Integration

The serializer focuses solely on accurate database serialization:

The serializer (DacpacScriptParser):
- Serializes the database structure exactly as it exists
- Does NOT handle manifest files or exclusion comments
- Maintains a clean separation of concerns
- All exclusion-related operations are handled by the Migration Generator

### Migration Generator Integration

The Migration Generator handles all manifest-related operations:

The Migration Generator should support the following CLI options:
- `--update-exclusion-comments`: Update exclusion comments in existing serialized files
- `--regenerate`: Regenerate migrations by comparing with origin/main

Key responsibilities:
1. **Manifest Management**:
   - Load existing manifest or create new one
   - Merge new changes with existing manifest preserving exclusions
   - Save manifest with rotation markers for PR visibility

2. **Migration Generation**:
   - Detect changes via git diff analysis
   - Apply exclusions from manifest
   - Generate migration script with excluded changes commented inline
   - Pass both included and excluded changes to script builder

3. **Comment Updates**:
   - Update exclusion comments in serialized files when manifest changes
   - Process all SQL files in the database directory

### Migration Script Generation with Exclusions

The migration script builder includes both included and excluded changes in order, with excluded changes commented out. This makes it easy to toggle individual changes by commenting/uncommenting:

```sql
-- Migration: 20240115_103000_AddOrderColumns.sql
-- Generated: 2024-01-15 10:30:00 UTC
-- Database: ProductionDB
-- Changes: 4 total (2 included, 2 excluded)

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Modify Products table
ALTER TABLE [dbo].[Products] 
ALTER COLUMN [Price] DECIMAL(19,4) NOT NULL;
GO

-- EXCLUDED: dbo.Orders.LastModified - Column type changed from DATETIME to DATETIME2
-- Source: change-manifest-prod-server-ProductionDB.manifest
-- To include: Uncomment the following statement
/*
ALTER TABLE [dbo].[Orders] 
ALTER COLUMN [LastModified] DATETIME2 NOT NULL;
GO
*/

-- Add new column to Orders
ALTER TABLE [dbo].[Orders]
ADD [OrderStatus] VARCHAR(50) NOT NULL DEFAULT 'Pending';
GO

-- EXCLUDED: dbo.Orders.UpdatedBy - Column added
-- Source: change-manifest-prod-server-ProductionDB.manifest
/*
ALTER TABLE [dbo].[Orders] 
ADD [UpdatedBy] NVARCHAR(100) NULL;
GO
*/

-- EXCLUDED: dbo.IX_Orders_Performance - Index added
-- Source: change-manifest-prod-server-ProductionDB.manifest
/*
CREATE NONCLUSTERED INDEX [IX_Orders_Performance] 
ON [dbo].[Orders] ([OrderDate], [Status])
INCLUDE ([CustomerID], [TotalAmount]);
GO
*/

COMMIT TRANSACTION;
PRINT 'Migration applied successfully.';
```

**Key Benefits of Inline Comments**:
- **Dependency Order Preserved**: Changes remain in the correct execution order
- **Easy Individual Toggle**: Uncomment specific changes without affecting others
- **Context Preserved**: Can see the relationship between included and excluded changes
- **Simpler Implementation**: No need to reorder or group changes
- **Manual Override**: DBAs can selectively uncomment changes based on needs

### Tool Architecture

**DACPAC Runner** (existing, unchanged):
- Continues to perform serialization and migration generation
- No changes to this tool

**Manifest & Comment Manager** (new tool):
- Creates manifest file from detected changes
- Updates exclusion comments in serialized files and migration scripts
- Can be run manually or by GitHub workflow
- Handles all manifest-related operations

## GitHub Workflow Integration (Future Implementation)

**Note**: To be implemented separately.

Workflow triggers on manifest file changes:
1. Detects `.manifest` file changes
2. Runs the Manifest & Comment Manager tool 
3. Commits updated files back to PR

This keeps manifest and files in sync automatically.



## Examples

### Example 1: Excluding Environment-Specific Columns

Initial manifest after detecting changes (first generation with `/`):
```
DATABASE: ProductionDB /
SERVER: prod-server /
GENERATED: 2024-01-15T10:30:00Z /
COMMIT: abc123def /

=== INCLUDED CHANGES ===
dbo.Orders.LastModified - Column type changed from DATETIME to DATETIME2 /
dbo.Orders.UpdatedBy - Column added /
dbo.IX_Orders_Performance - Index added /
dbo.Products.Price - Column type changed from MONEY to DECIMAL(19,4) /

=== EXCLUDED CHANGES ===
```

After user manually moves changes to excluded (preserves `/` markers):
```
DATABASE: ProductionDB /
SERVER: prod-server /
GENERATED: 2024-01-15T10:30:00Z /
COMMIT: abc123def /

=== INCLUDED CHANGES ===
dbo.Products.Price - Column type changed from MONEY to DECIMAL(19,4) /

=== EXCLUDED CHANGES ===
dbo.Orders.LastModified - Column type changed from DATETIME to DATETIME2 /
dbo.Orders.UpdatedBy - Column added /
dbo.IX_Orders_Performance - Index added /
```

If later `--regenerate` is run, the markers would flip to `\`:
```
DATABASE: ProductionDB \
SERVER: prod-server \
GENERATED: 2024-01-15T10:50:00Z \
COMMIT: def456ghi \

=== INCLUDED CHANGES ===
dbo.Products.Price - Column type changed from MONEY to DECIMAL(19,4) \

=== EXCLUDED CHANGES ===
dbo.Orders.LastModified - Column type changed from DATETIME to DATETIME2 \
dbo.Orders.UpdatedBy - Column added \
dbo.IX_Orders_Performance - Index added \
```

### Example 2: Temporary Exclusion

Initial manifest:
```
DATABASE: ProductionDB
SERVER: prod-server
GENERATED: 2024-01-15T10:30:00Z
COMMIT: abc123def

=== INCLUDED CHANGES ===
dbo.Orders.LastModified - Column type changed from DATETIME to DATETIME2
dbo.Orders.UpdatedBy - Column added
dbo.IX_Orders_Performance - Index added
dbo.Products.Price - Column type changed from MONEY to DECIMAL(19,4)

=== EXCLUDED CHANGES ===
```

After user deletes lines (temporary exclusion for this migration only):
```
DATABASE: ProductionDB
SERVER: prod-server
GENERATED: 2024-01-15T10:30:00Z
COMMIT: abc123def

=== INCLUDED CHANGES ===
dbo.Products.Price - Column type changed from MONEY to DECIMAL(19,4)

=== EXCLUDED CHANGES ===
```

Next time migration runs, the deleted changes will reappear in INCLUDED section.

### Example 3: Pre-existing Manifest on Initial Run

Scenario: Manifest exists from a previous branch/environment with exclusions

Existing manifest before serialization:
```
DATABASE: ProductionDB
SERVER: prod-server
GENERATED: 2024-01-10T09:00:00Z
COMMIT: xyz789abc

=== INCLUDED CHANGES ===
dbo.Products.Price - Column type changed from MONEY to DECIMAL(19,4)

=== EXCLUDED CHANGES ===
dbo.Orders.LastModified - Column type changed from DATETIME to DATETIME2
dbo.IX_Orders_Performance - Index added
```

When migration generation runs for the first time:
1. Detects the existing manifest
2. Generates migration respecting the exclusions
3. Runs `--update-exclusion-comments` to add exclusion comments to serialized files:
   ```sql
   CREATE TABLE [dbo].[Orders] (
       [OrderID] INT IDENTITY(1,1) NOT NULL,
       -- EXCLUDED FROM MIGRATION: Column type changed from DATETIME to DATETIME2
       -- Reason: Defined in change-manifest-prod-server-ProductionDB.manifest
       [LastModified] DATETIME2 NOT NULL,
       ...
   )
   ```
4. Generated migration only includes the Price change, not LastModified

### Example 4: Handling Renamed Objects

```
=== INCLUDED CHANGES ===
dbo.Customer - Table renamed to dbo.Customers
dbo.IX_Customer_Email - Index renamed to dbo.IX_Customers_Email
dbo.GetCustomer - Stored procedure renamed to dbo.GetCustomers

=== EXCLUDED CHANGES ===
dbo.temp_migration_data - Table added
```

## File System Comments for Excluded Changes

During serialization, when the system detects that changes are excluded (either in the EXCLUDED section or removed from INCLUDED section), it adds comments to the serialized files:

### Example: Table File with Excluded Column Changes
`servers/prod-server/ProductionDB/schemas/dbo/Tables/Orders.sql`:
```sql
CREATE TABLE [dbo].[Orders] (
    [OrderID] INT IDENTITY(1,1) NOT NULL,
    [CustomerID] INT NOT NULL,
    [OrderDate] DATETIME NOT NULL,
    -- MIGRATION EXCLUDED: Column type changed from DATETIME to DATETIME2
    -- This change is NOT included in current migration
    -- See: change-manifest-prod-server-ProductionDB.manifest
    [LastModified] DATETIME2 NOT NULL,
    -- MIGRATION EXCLUDED: New column 
    -- This change is NOT included in current migration
    -- See: change-manifest-prod-server-ProductionDB.manifest
    [UpdatedBy] NVARCHAR(100) NULL,
    CONSTRAINT [PK_Orders] PRIMARY KEY CLUSTERED ([OrderID])
);
```

### Example: Index Directory with Excluded Index
`servers/prod-server/ProductionDB/schemas/dbo/Indexes/Orders/README.md`:
```markdown
## Indexes for Orders table

Current indexes:
- IX_Orders_CustomerID.sql
- IX_Orders_OrderDate.sql

## Excluded from Migration
- IX_Orders_Performance - Performance index not included in current migration
  See: change-manifest-prod-server-ProductionDB.manifest
```

### Implementation Note
The Migration Generator's `--update-exclusion-comments` operation checks the manifest file to determine which changes are excluded and adds appropriate comments. This ensures developers can see at the file level what changes exist but aren't being migrated.

## Benefits

1. **Visibility** - Users see all detected changes before migration
2. **PR Visibility** - Rotation markers ensure manifest always appears in PR diffs
3. **Control** - Simple text editing to control migrations
4. **Persistence** - Exclusions survive across regenerations
5. **Auditability** - Version control tracks exclusion decisions
6. **Flexibility** - Easy to include/exclude changes as needed
7. **Non-invasive** - Doesn't affect database serialization
8. **Transparency** - Excluded changes are visible in migration scripts as comments
9. **Traceability** - Clear connection between manifest file and excluded changes

### PR Diff Visibility

The rotation marker system (`/` and `\`) ensures that:
- Every manifest regeneration changes every line in the file
- The manifest always appears at the top of the PR's changed files list
- Reviewers cannot miss what changes are being included or excluded
- The visual rotation creates a clear indicator that the file was regenerated
- Even if the actual changes remain the same, the markers ensure visibility

## Testing Strategy

Comprehensive test coverage is critical for this system. The following test suites should be implemented:

### Unit Tests for ChangeManifestManager

```csharp
[Fact]
public void GenerateChangeIdentifier_Column_ReturnsCorrectFormat()
{
    // Given: A column change
    // When: GenerateChangeIdentifier is called
    // Then: Returns "schema.table.column" format
}

[Fact]
public void LoadManifest_ValidFile_ParsesCorrectly()
{
    // Given: A valid manifest file with included and excluded changes
    // When: LoadManifest is called
    // Then: Returns manifest with correct sections and changes
}

[Fact]
public void SaveManifest_NewManifest_CreatesFileWithCorrectFormat()
{
    // Given: A new manifest with changes
    // When: SaveManifest is called
    // Then: Creates file with correct filename and content structure
}

[Fact]
public void ApplyExclusions_WithExcludedChanges_FiltersCorrectly()
{
    // Given: List of changes and manifest with exclusions
    // When: ApplyExclusions is called
    // Then: Returns only changes not in exclusion list
}

[Fact]
public void MergeWithExisting_NewChangesDetected_PreservesExistingExclusions()
{
    // Given: Existing manifest with exclusions and new detected changes
    // When: MergeWithExisting is called
    // Then: Adds new changes to included section, preserves exclusions
}
```

### Integration Tests for Migration Generation

```csharp
[Fact]
public void GenerateMigration_WithExcludedChanges_CreatesCommentedSQL()
{
    // Given: Database changes with some excluded via manifest
    // When: Migration is generated
    // Then: Excluded changes appear as commented SQL with explanations
}

[Fact]
public void GenerateMigration_NoManifestExists_CreatesNewManifestWithAllIncluded()
{
    // Given: No existing manifest file
    // When: Migration is generated
    // Then: Creates manifest with all changes in included section
}

[Fact]
public void GenerateMigration_MovedFromExcludedToIncluded_IncludesInMigration()
{
    // Given: Change previously excluded, now moved to included
    // When: Migration is regenerated
    // Then: Change appears in active migration SQL
}

[Fact]
public void GenerateMigration_DeletedFromIncluded_NotInExcluded_ReappearsNextTime()
{
    // Given: Change deleted from included section (not moved to excluded)
    // When: Migration generated again in future
    // Then: Change reappears in included section
    // This tests temporary exclusion behavior
}

[Fact]
public void GenerateMigration_MultipleDatabases_UsesCorrectManifest()
{
    // Given: Multiple databases with different manifest files
    // When: Migration generated for specific database
    // Then: Uses exclusions from correct manifest file
}
```

### End-to-End Tests

```csharp
[Fact]
public void FullWorkflow_ExcludeColumnChange_WorksEndToEnd()
{
    // Scenario: User wants to exclude a column type change
    // Given: Column type changed in database
    // When: 
    //   1. Serialization captures change
    //   2. Migration generator creates manifest
    //   3. User moves change to excluded section
    //   4. Migration regenerated
    // Then: Migration script has change commented out
}

[Fact]
public void FullWorkflow_ReincludeChange_AppearsInMigration()
{
    // Scenario: User wants to reinclude a previously excluded change
    // Given: Change in excluded section of manifest
    // When:
    //   1. User moves change back to included section
    //   2. Migration regenerated with --regenerate flag
    // Then: Change appears in active migration SQL
}

[Fact]
public void FullWorkflow_GitHubWorkflow_RegeneratesOnManifestChange()
{
    // Scenario: PR workflow detects manifest change
    // Given: PR with modified manifest file
    // When: GitHub action runs
    // Then: Deletes old migrations and generates new ones
}
```

### Edge Case Tests

```csharp
[Fact]
public void HandleRenamedObjects_WithExclusions_TracksCorrectly()
{
    // Given: Object renamed and rename is excluded
    // When: Migration generated
    // Then: Both old and new names handled correctly
}

[Fact]
public void HandleDeletedObjects_InExclusions_HandlesGracefully()
{
    // Given: Object in exclusions that no longer exists
    // When: Manifest merged with new changes
    // Then: Removes non-existent exclusion or marks as obsolete
}

[Fact]
public void ConcurrentManifestEdits_MergeConflict_ResolvedCorrectly()
{
    // Given: Two branches modify same manifest
    // When: Branches merged
    // Then: Conflict resolution preserves both sets of changes
}

[Fact]
public void LargeScaleChanges_Performance_HandlesEfficiently()
{
    // Given: Database with 1000+ changes
    // When: Manifest generated and filtered
    // Then: Completes within reasonable time (<5 seconds)
}
```

### Manifest File Format Tests

```csharp
[Fact]
public void ManifestFilename_ContainsServerAndDatabase()
{
    // Given: Server "prod-01" and database "OrdersDB"
    // When: Manifest filename generated
    // Then: Returns "change-manifest-prod-01-OrdersDB.manifest"
}

[Fact]
public void ManifestContent_MaintainsOrder_AfterMultipleUpdates()
{
    // Given: Manifest updated multiple times
    // When: Changes added/removed
    // Then: Sections remain properly formatted and ordered
}

[Fact]
public void InvalidManifestFormat_ErrorHandling_ProvidesHelpfulMessage()
{
    // Given: Corrupted or invalid manifest file
    // When: LoadManifest called
    // Then: Returns clear error message with fix instructions
}
```

### Migration Script Tests

```csharp
[Fact]
public void MigrationScript_Header_IncludesChangeCount()
{
    // Given: 5 total changes (3 included, 2 excluded)
    // When: Migration script generated
    // Then: Header shows "Changes: 5 total (3 included, 2 excluded)"
}

[Fact]
public void MigrationScript_ExcludedSection_IncludesManifestReference()
{
    // Given: Excluded changes
    // When: Migration script generated
    // Then: Each exclusion references specific manifest file
}

[Fact]
public void MigrationScript_CommentedSQL_ValidSyntax()
{
    // Given: Complex SQL changes excluded
    // When: Migration script generated
    // Then: Commented SQL maintains valid syntax when uncommented
}
```

### Test Data Scenarios

Each test should cover realistic scenarios:
- **Simple column type changes**: INT to BIGINT, VARCHAR(50) to VARCHAR(100)
- **Complex object changes**: Stored procedures with multiple modifications
- **Constraint changes**: Foreign keys, check constraints, defaults
- **Index changes**: New indexes, modified indexes, dropped indexes
- **Schema-level changes**: Objects moving between schemas
- **Batch operations**: Multiple related changes that should be excluded together

## Future Enhancements

1. **Pattern-based exclusions** - Support wildcards (e.g., `*.LastModified`)
2. **Exclusion reasons** - Add comments explaining why changes are excluded
3. **Manifest validation** - Ensure excluded changes still exist
4. **Auto-exclusion rules** - Configure patterns for automatic exclusion
5. **Manifest merging** - Handle conflicts when merging branches

## Migration Scenarios

### Scenario 1: Development to Production

1. Developer makes changes in dev environment
2. Serialization captures all changes
3. Developer excludes dev-only objects in manifest
4. Production migration only includes relevant changes

### Scenario 2: Hotfix Branch

1. Create hotfix branch from main
2. Make emergency changes
3. Exclude non-critical changes via manifest
4. Deploy only critical fixes

### Scenario 3: Feature Branch

1. Long-running feature branch accumulates changes
2. Some changes are experimental
3. Exclude experimental changes before merging
4. Clean migration to main branch

## Tool Responsibilities Summary

### DACPAC Runner (Unchanged)
- **Existing functionality**: Serialization + migration generation
- No changes to this tool
- Continues to work exactly as before

### Manifest & Comment Manager (New Tool)
- Creates and manages manifest files
- Updates exclusion comments in both serialized files and migration scripts
- Analyzes git diff to identify changes
- Can be run manually after DACPAC Runner or automatically by GitHub workflow

### Typical Workflow

```bash
# 1. Run DACPAC Runner (unchanged)
dotnet run --project SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner -- \
  --source-connection "..." \
  --output-path "." \
  --target-server "prod" \
  --target-database "MyDB"

# 2. Run Manifest & Comment Manager to create manifest and add comments
dotnet run --project [ManifestCommentManager] -- \
  --output-path "." \
  --target-server "prod" \
  --target-database "MyDB"

# 3. User edits manifest, pushes to PR
# 4. GitHub workflow runs Manifest & Comment Manager automatically
```

## Conclusion

Simple system for excluding database changes from migrations:
- Edit manifest → push to PR → workflow updates files automatically
- Clean tool separation: initial generation vs. exclusion updates
- Version control tracks all exclusion decisions

*Collaboration by Claude*