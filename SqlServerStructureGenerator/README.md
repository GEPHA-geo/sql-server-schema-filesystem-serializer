# SQL Server Structure Generator

A C# application that generates organized SQL scripts from a SQL Server database using SQL Management Objects (SMO).

## Features

- Connects to SQL Server databases and extracts complete schema information
- Generates organized folder structure by schema
- Creates separate SQL files for each database object:
  - Tables (columns and constraints)
  - Primary Keys
  - Indexes
  - Foreign Keys
  - Check Constraints
  - Triggers
  - Views
  - Stored Procedures
  - Functions
- Includes DROP IF EXISTS statements for safe re-running
- Preserves all SQL Server specific features

## Structure

Generated output follows this hierarchy:
```
DatabaseName/
├── SchemaName/
│   ├── Tables/
│   │   └── TableName/
│   │       ├── TableName.sql (table definition)
│   │       ├── PK_TableName.sql
│   │       ├── IX_TableName_Column.sql
│   │       ├── FK_TableName_RefTable.sql
│   │       ├── CK_TableName_Check.sql
│   │       └── TR_TableName_Trigger.sql
│   ├── Views/
│   ├── StoredProcedures/
│   └── Functions/
```

## Usage

```bash
dotnet run -- "connection-string" "output-path"
```

Example:
```bash
dotnet run -- "Server=localhost;Database=MyDB;Integrated Security=true" "C:\Output"
```

## Requirements

- .NET 9.0 or later
- SQL Server (any version supported by SMO)
- Appropriate permissions to read database schema

*Collaboration by Claude*