# DACPAC Extraction Commands and Configuration

## Working Command for DACPAC Extraction

```powershell
dotnet run --project .\SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner\SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.csproj `
      --source-connection "Server=10.188.49.19;Database=abc_20250801_1804;User Id=sa_abc_dev;Password=Vr9t#nE8%P068@iZ;TrustServerCertificate=true" `
      --target-server "pharm-n1.pharm.local" `
      --target-database "abc" `
      --output-path "C:\Users\petre.chitashvili\repos\gepha\db_comparison"
```

## Linux/WSL Version

```bash
dotnet run --project /mnt/c/Users/petre.chitashvili/repos/SQL_Server_Compare/SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner/SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.csproj \
      --source-connection "Server=10.188.49.19;Database=abc_20250801_1804;User Id=sa_abc_dev;Password=Vr9t#nE8%P068@iZ;TrustServerCertificate=true" \
      --target-server "pharm-n1.pharm.local" \
      --target-database "abc" \
      --output-path "/mnt/c/Users/petre.chitashvili/repos/gepha/db_comparison"
```

## Key Configuration Changes Made

### DacExtractOptions
- `IgnoreExtendedProperties = false` - Include extended properties  
- `IgnorePermissions = false` - Include permissions (GRANT/REVOKE/DENY)
- `IgnoreUserLoginMappings = false` - Include user-to-login mappings

### DacDeployOptions  
- `IgnoreAuthorizer = true` - Don't include AUTHORIZATION in CREATE statements (avoids user dependencies like StokUser)
- `IgnorePermissions = false` - Include permissions
- `IgnoreRoleMembership = false` - Include role memberships
- `IgnoreLoginSids = true` - Ignore login SIDs (server-specific)

## SQL74502 Error Handling
- Added message handler to log but continue on SQL74502 errors (users that can't be recreated)
- Catch block to retry or continue despite these errors

## Important Notes
- The source database is `abc_20250801_1804` on server `10.188.49.19` (not the local pharm-n1.pharm.local)
- Uses SQL authentication with sa_abc_dev user
- The IgnoreAuthorizer option is critical to avoid dependencies on users like StokUser in schema definitions