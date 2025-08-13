#!/bin/bash

# DACPAC Runner with SCMP file - Actual command example
# This extracts database schema using configuration from the SCMP file

dotnet run --project ./SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner/SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.csproj \
  --scmp "/mnt/c/Users/petre.chitashvili/repos/SQL_Server_Compare/mamuka_production.scmp" \
  --output-path "/mnt/c/Users/petre.chitashvili/repos/gepha/db_comparison" \
  --source-password "YourPasswordHere"

# Note: Replace "YourPasswordHere" with the actual password for the source database
# The SCMP file will provide:
# - Source connection string (from SourceModelProvider)
# - Target server and database (from TargetModelProvider)
# - Deployment options and exclusions

# Alternative: If you need to override some settings from the SCMP
# dotnet run --project ./SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner/SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.csproj \
#   --scmp "/mnt/c/Users/petre.chitashvili/repos/SQL_Server_Compare/mamuka_production.scmp" \
#   --output-path "/mnt/c/Users/petre.chitashvili/repos/gepha/db_comparison" \
#   --source-password "YourPasswordHere" \
#   --target-database "different_database_name" \
#   --commit-message "Schema update from SCMP"