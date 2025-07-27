# Rename Detection Feature

## Overview

The SQL Server Schema Migration Generator now includes intelligent rename detection that identifies when database objects are renamed rather than dropped and recreated. This feature generates more efficient and safer migration scripts using SQL Server's `sp_rename` procedure.

## Supported Object Types

- **Columns**: Detects when a column is renamed within the same table
- **Indexes**: Detects when an index is renamed on the same table
- **Constraints**: Detects when constraints (FK, PK, CHECK, DEFAULT) are renamed
- **Triggers**: Detects when triggers are renamed on the same table

## How It Works

The rename detection algorithm:

1. Groups schema changes by object type
2. Compares deleted objects with added objects
3. Identifies renames when:
   - Objects are in the same schema and table
   - Definitions are identical except for the name
   - For columns: data type, nullability, defaults, and identity specifications match

## Generated SQL

Instead of DROP/CREATE statements, the system generates appropriate `sp_rename` calls:

```sql
-- Column rename
EXEC sp_rename '[dbo].[Customer].[EmailAddress]', 'Email', 'COLUMN';

-- Index rename
EXEC sp_rename '[dbo].[Customer].[IDX_Customer_Email]', 'IX_Customer_Email', 'INDEX';

-- Constraint rename
EXEC sp_rename '[dbo].[FK_Customer_Country]', 'FK_Customer_CountryId', 'OBJECT';

-- Trigger rename
EXEC sp_rename '[dbo].[trg_CustomerAudit]', 'trg_Customer_Audit', 'OBJECT';
```

## Benefits

1. **Data Preservation**: Renames preserve data, avoiding data loss from DROP/CREATE operations
2. **Performance**: Rename operations are much faster than recreating objects
3. **Referential Integrity**: Maintains foreign key relationships without disruption
4. **Minimal Locking**: Reduces table locking compared to DROP/CREATE operations

## Detection Rules

### Columns
- Must be in the same table
- Data type must match exactly (including precision/scale)
- Nullability must match
- Default values must match
- Identity specifications must match

### Indexes
- Must be on the same table
- Column list and order must match
- Index type (CLUSTERED/NONCLUSTERED) must match
- Uniqueness must match
- Included columns must match

### Constraints
- Must be in the same schema
- Constraint definition must match exactly
- For foreign keys: referenced table and columns must match
- For check constraints: condition must match

### Triggers
- Must be on the same table
- Trigger events (INSERT/UPDATE/DELETE) must match
- Trigger timing (BEFORE/AFTER) must match
- Trigger body must match

## Non-Rename Scenarios

The following are NOT detected as renames:

- Changes across different tables or schemas
- Columns with different data types or nullability
- Indexes with different column lists or types
- Constraints with different definitions
- Objects with any structural differences beyond the name

## Migration Script Order

Rename operations are processed first in migration scripts, before:
1. DROP operations
2. ALTER operations  
3. CREATE operations

This ensures rename operations don't conflict with other schema changes.

## Example Usage

When the system detects:
```sql
-- Deleted
[EmailAddress] NVARCHAR(100) NOT NULL

-- Added
[Email] NVARCHAR(100) NOT NULL
```

It generates:
```sql
EXEC sp_rename '[dbo].[Customer].[EmailAddress]', 'Email', 'COLUMN';
```

Instead of:
```sql
ALTER TABLE [dbo].[Customer] DROP COLUMN [EmailAddress];
ALTER TABLE [dbo].[Customer] ADD [Email] NVARCHAR(100) NOT NULL;
```