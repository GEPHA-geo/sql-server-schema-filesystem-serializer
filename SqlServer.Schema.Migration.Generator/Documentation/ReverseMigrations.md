# Reverse Migrations

## Overview

The SQL Server Migration Generator automatically creates reverse migration scripts alongside forward migration scripts. These reverse scripts allow manual rollback of applied migrations when needed.

## How It Works

When a migration is generated in the `z_migrations` folder, a corresponding reverse migration script is created in the `z_migrations_reverse` folder with the same filename. The reverse script contains SQL statements that undo the changes made by the forward migration.

### Directory Structure
```
servers/
└── [server-name]/
    └── [database-name]/
        ├── z_migrations/
        │   └── _20240115_120000_user_2tables_1indexes.sql
        └── z_migrations_reverse/
            └── _20240115_120000_user_2tables_1indexes.sql
```

## Reverse Operations

The reverse migration generator creates inverse operations for each type of change:

### Table Operations
- **CREATE TABLE** → **DROP TABLE**
- **DROP TABLE** → **CREATE TABLE** (using original definition)
- **ALTER TABLE** → Handled by column-level changes

### Column Operations
- **ADD COLUMN** → **DROP COLUMN**
- **DROP COLUMN** → **ADD COLUMN** (using original definition)
- **ALTER COLUMN** → **ALTER COLUMN** (using original definition)

### Index Operations
- **CREATE INDEX** → **DROP INDEX**
- **DROP INDEX** → **CREATE INDEX** (using original definition)
- **ALTER INDEX** → Recreate with original definition

### Other Objects
- **Views, Procedures, Functions, Triggers**: Similar pattern of DROP/CREATE inversions
- **Constraints**: DROP CONSTRAINT ↔ ADD CONSTRAINT
- **Renames**: Reverse the rename direction (new name → old name)

## Important Notes

### Manual Execution Only
- Reverse migrations are **NOT** automatically executed
- They are **NOT** tracked in the `DatabaseMigrationHistory` table
- They must be reviewed and executed manually when needed

### Operation Order
Reverse migrations execute operations in reverse order to ensure proper dependency handling:
1. Drop created objects (tables, columns, indexes)
2. Restore modified objects to original state
3. Recreate dropped objects
4. Reverse any rename operations

### Transaction Safety
All reverse migrations are wrapped in transactions:
```sql
SET XACT_ABORT ON;
BEGIN TRANSACTION;
-- Reverse operations here
COMMIT TRANSACTION;
```

## Usage

To rollback a migration:

1. Locate the reverse migration script in `z_migrations_reverse/`
2. Review the script carefully to ensure it's appropriate for your situation
3. Execute the script manually against your database
4. Optionally, remove the migration record from `DatabaseMigrationHistory`:
   ```sql
   DELETE FROM [dbo].[DatabaseMigrationHistory]
   WHERE [MigrationId] = 'migration_id_here';
   ```

## Limitations

1. **Extended Properties**: Reverse operations for extended properties (like column descriptions) are not fully automated and may require manual adjustment
2. **Data Loss**: Dropping columns or tables in forward migrations means data cannot be recovered in reverse migrations
3. **Complex Modifications**: Some complex schema changes may not perfectly reverse
4. **Identity Columns**: IDENTITY properties cannot be added back via ALTER COLUMN

## Best Practices

1. Always review reverse migration scripts before execution
2. Test reverse migrations in a non-production environment first
3. Keep backups before applying any migrations (forward or reverse)
4. Document any manual adjustments needed for complex reversals
5. Consider the impact on data when planning migrations

## Example

Forward Migration:
```sql
-- Add new column
ALTER TABLE [dbo].[Users] ADD [LastLogin] DATETIME2 NULL;
GO

-- Create new index
CREATE INDEX [IX_Users_LastLogin] ON [dbo].[Users] ([LastLogin]);
GO
```

Reverse Migration:
```sql
-- Reversing CREATE operations (DROP)
DROP INDEX [IX_Users_LastLogin] ON [dbo].[Users];
GO

-- Reversing CREATE operations (DROP)
ALTER TABLE [dbo].[Users] DROP COLUMN [LastLogin];
GO
```