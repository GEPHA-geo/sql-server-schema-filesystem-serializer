# Actor Tracking in Migration Files

## Overview

The Migration Generator now includes actor tracking to identify who created each migration. This helps with accountability and debugging in team environments.

## How It Works

1. **Command Line Option**: You can specify the actor using the `--actor` parameter:
   ```bash
   dotnet SqlServer.Schema.Migration.Generator.dll --output-path ./output --database MyDB --actor john_doe
   ```

2. **GitHub Actions Integration**: When running in GitHub Actions, the tool automatically uses the `GITHUB_ACTOR` environment variable if no actor is specified.

3. **Fallback**: If no actor is specified and not running in GitHub Actions, the current system username is used.

## Migration File Naming

Migration files now include the actor name in their filename:
```
20240115_123456_john_doe_2tables_3columns_1indexes.sql
```

Format: `{timestamp}_{actor}_{description}.sql`

## Migration Header

The actor is also recorded in the migration file header:
```sql
-- Migration: 20240115_123456_2tables_3columns_1indexes.sql
-- MigrationId: 20240115_123456_2tables_3columns_1indexes
-- Generated: 2024-01-15 12:34:56 UTC
-- Database: MyDatabase
-- Actor: john_doe
-- Changes: 6 schema modifications
```

## Actor Name Sanitization

Actor names are sanitized for use in filenames:
- Special characters are replaced with underscores
- Spaces, dots, @ symbols, and hyphens become underscores
- Consecutive underscores are collapsed to single underscores
- Names are converted to lowercase
- Names are limited to 50 characters
- If no valid characters remain, "unknown" is used

## Examples

| Input Actor | Sanitized Output |
|-------------|------------------|
| john.doe@company.com | john_doe_company_com |
| Jane Smith | jane_smith |
| user-123 | user_123 |
| ADMIN | admin |
| (empty) | unknown |