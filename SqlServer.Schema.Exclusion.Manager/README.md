# SQL Server Schema Exclusion Manager

This tool manages change manifests and exclusion comments for the SQL Server Schema Filesystem Serializer project.

## Purpose

The Exclusion Manager works alongside the existing DACPAC Runner to:
1. Create manifest files that track database changes
2. Update exclusion comments in serialized files based on manifest
3. Support selective exclusion of database changes from migrations

## Usage

### Normal Mode - Create/Update Manifest

Run after DACPAC Runner to create or update the manifest:

```bash
dotnet run --project SqlServer.Schema.Exclusion.Manager -- \
  --output-path "." \
  --target-server "prod" \
  --target-database "MyDB"
```

This will:
- Detect all changes via git diff
- Create/update the manifest file
- Add exclusion comments to serialized files

### Update Comments Mode - GitHub Workflow

Used by GitHub workflow when manifest is edited:

```bash
dotnet run --project SqlServer.Schema.Exclusion.Manager -- \
  --output-path "." \
  --target-server "prod" \
  --target-database "MyDB" \
  --update-exclusion-comments
```

This will:
- Read the existing manifest
- Update exclusion comments in serialized files
- Update migration scripts to comment/uncomment excluded changes

## Manifest File Format

The tool creates manifest files at:
`servers/{server}/{database}/_change-manifests/{server}_{database}.manifest`

Example:
```
DATABASE: MyDB /
SERVER: prod /
GENERATED: 2024-01-15T10:30:00Z /
COMMIT: abc123def /

=== INCLUDED CHANGES ===
dbo.Products.Price - Column type changed from MONEY to DECIMAL(19,4) /
dbo.Orders.Status - Column added /

=== EXCLUDED CHANGES ===
dbo.Orders.LastModified - Column type changed from DATETIME to DATETIME2 /
```

## Integration with DACPAC Runner

1. DACPAC Runner executes (no changes)
2. Exclusion Manager runs to create manifest
3. User edits manifest to exclude changes
4. GitHub workflow runs Exclusion Manager with `--update-exclusion-comments`
5. Files are updated automatically

## Dependencies

- .NET 9.0
- LibGit2Sharp (for git operations)
- System.CommandLine (for CLI)