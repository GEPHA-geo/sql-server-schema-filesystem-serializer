# SQL Server Compare Project

## Purpose
This project compares SQL Server database schemas and generates migration scripts to sync them. It consists of:

1. **SqlServerStructureGenerator** - Extracts SQL Server database schemas using SMO
2. **SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner** - Extracts database schema using DACPAC format and generates migrations
3. **SqlServer.Schema.Migration.Generator** - Detects schema changes using Git and generates migration SQL scripts

## Key Components
- Uses DACPAC (Data-tier Application Component) format for database extraction
- Tracks schema changes using Git
- Generates incremental migration scripts with timestamps and actor tracking
- Organizes output by server/database hierarchy: `servers/{server}/{database}/`

## Tech Stack
- .NET 9.0
- Microsoft.SqlServer.DacFx for DACPAC operations
- SQL Server Management Objects (SMO)
- Git for change tracking

*Collaboration by Claude*