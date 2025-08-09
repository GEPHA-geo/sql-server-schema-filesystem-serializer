# Example Usage

## Scenario

You've made database changes and want to exclude some environment-specific changes from migrations.

## Step 1: Run DACPAC Runner (existing tool)

```bash
dotnet run --project SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner -- \
  --source-connection "Server=localhost;Database=MyDB;Trusted_Connection=true" \
  --output-path "." \
  --target-server "prod" \
  --target-database "MyDB"
```

This creates serialized files and migration scripts as usual.

## Step 2: Run Exclusion Manager to create manifest

```bash
dotnet run --project SqlServer.Schema.Exclusion.Manager -- \
  --output-path "." \
  --target-server "prod" \
  --target-database "MyDB"
```

This creates: `servers/prod/MyDB/_change-manifests/prod_MyDB.manifest`

## Step 3: Edit manifest to exclude changes

Original manifest:
```
DATABASE: MyDB /
SERVER: prod /
GENERATED: 2024-01-15T10:30:00Z /
COMMIT: abc123def /

=== INCLUDED CHANGES ===
dbo.Products.Price - Column type changed from MONEY to DECIMAL(19,4) /
dbo.Orders.LastModified - Column type changed from DATETIME to DATETIME2 /
dbo.Orders.UpdatedBy - Column added /

=== EXCLUDED CHANGES ===
```

Edit to exclude environment-specific changes:
```
DATABASE: MyDB /
SERVER: prod /
GENERATED: 2024-01-15T10:30:00Z /
COMMIT: abc123def /

=== INCLUDED CHANGES ===
dbo.Products.Price - Column type changed from MONEY to DECIMAL(19,4) /

=== EXCLUDED CHANGES ===
dbo.Orders.LastModified - Column type changed from DATETIME to DATETIME2 /
dbo.Orders.UpdatedBy - Column added /
```

## Step 4: Commit and push

```bash
git add .
git commit -m "Exclude environment-specific columns from migration"
git push
```

## Step 5: GitHub workflow runs automatically

When the workflow detects manifest changes, it runs:

```bash
dotnet run --project SqlServer.Schema.Exclusion.Manager -- \
  --output-path "." \
  --target-server "prod" \
  --target-database "MyDB" \
  --update-exclusion-comments
```

This updates:
1. Serialized files (adds exclusion comments)
2. Migration scripts (comments out excluded changes)

## Result

### Updated serialized file (Orders.sql):
```sql
CREATE TABLE [dbo].[Orders] (
    [OrderID] INT IDENTITY(1,1) NOT NULL,
    -- MIGRATION EXCLUDED: Column type changed from DATETIME to DATETIME2
    -- This change is NOT included in current migration
    -- See: _change-manifests/prod_MyDB.manifest
    [LastModified] DATETIME2 NOT NULL,
    -- MIGRATION EXCLUDED: Column added
    -- This change is NOT included in current migration
    -- See: _change-manifests/prod_MyDB.manifest
    [UpdatedBy] NVARCHAR(100) NULL,
    ...
)
```

### Updated migration script:
```sql
-- Modify Products table
ALTER TABLE [dbo].[Products] 
ALTER COLUMN [Price] DECIMAL(19,4) NOT NULL;
GO

-- EXCLUDED: dbo.Orders.LastModified - Column type changed from DATETIME to DATETIME2
-- Source: _change-manifests/prod_MyDB.manifest
/*
ALTER TABLE [dbo].[Orders] 
ALTER COLUMN [LastModified] DATETIME2 NOT NULL;
GO
*/

-- EXCLUDED: dbo.Orders.UpdatedBy - Column added
-- Source: _change-manifests/prod_MyDB.manifest
/*
ALTER TABLE [dbo].[Orders] 
ADD [UpdatedBy] NVARCHAR(100) NULL;
GO
*/
```