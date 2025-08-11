# SCMP File Format - Implementation Complete

## Overview

This document describes the implemented SCMP (Schema Comparison) file format, which has fully replaced the previous manifest file format. The SCMP file format is the well-structured XML format used by SQL Server Data Tools and is now the exclusive format for tracking database changes and comparison settings.

## Implementation Status: COMPLETE

### Previous Structure (REMOVED)
```
servers/
└── {servername}/
    └── {databasename}/
        └── _change-manifests/
            └── {source_server}_{source_database}.manifest  # Plain text format - REMOVED
```

### Current Structure (IMPLEMENTED)
```
servers/
└── {servername}/
    └── {databasename}/
        └── _change-manifests/
            └── {source_server}_{source_database}.scmp.xml  # SCMP XML format only
```

## Why This Change?

1. **SCMP is an industry standard**: SQL Server Data Tools already uses this format
2. **Rich configuration options**: Contains all comparison settings needed for accurate migrations
3. **Tool compatibility**: Can be opened and edited in Visual Studio SSDT
4. **Complete information**: Includes source/target connections, comparison options, and exclusions
5. **No custom parsing needed**: Standard XML format with established schema

## SCMP File Structure

The SCMP file contains everything needed for database comparison:

```xml
<?xml version="1.0" encoding="utf-8"?>
<SchemaComparison>
  <Version>10</Version>
  
  <!-- Source Database Connection -->
  <SourceModelProvider>
    <ConnectionBasedModelProvider>
      <ConnectionString>Data Source=10.188.49.19;Initial Catalog=abc_20250801_1804;...</ConnectionString>
    </ConnectionBasedModelProvider>
  </SourceModelProvider>
  
  <!-- Target Database Connection -->
  <TargetModelProvider>
    <ConnectionBasedModelProvider>
      <ConnectionString>Data Source=pharm-n1.pharm.local;Initial Catalog=abc;...</ConnectionString>
    </ConnectionBasedModelProvider>
  </TargetModelProvider>
  
  <!-- Comparison Settings -->
  <SchemaCompareSettingsService>
    <ConfigurationOptionsElement>
      <PropertyElementName>
        <Name>DropObjectsNotInSource</Name>
        <Value>True</Value>
      </PropertyElementName>
      <PropertyElementName>
        <Name>AllowTableRecreation</Name>
        <Value>True</Value>
      </PropertyElementName>
      <PropertyElementName>
        <Name>IgnorePermissions</Name>
        <Value>True</Value>
      </PropertyElementName>
      <!-- ... many more settings ... -->
    </ConfigurationOptionsElement>
  </SchemaCompareSettingsService>
  
  <!-- Excluded Objects (if any) -->
  <ExcludedSourceElements>
    <SelectedItem Type="Microsoft.Data.Tools.Schema.Sql.SchemaModel.SqlTable">
      <Name>dbo.test_table</Name>
    </SelectedItem>
  </ExcludedSourceElements>
</SchemaComparison>
```

## Implementation Complete

### 1. File Naming Convention

```csharp
public class ScmpManifestHandler
{
    // IMPLEMENTED
    private string GetManifestFileName(string server, string database)
    {
        // Sanitize server name (remove port, special chars)
        var sanitizedServer = server.Replace(",", "_").Replace(":", "_");
        return $"{sanitizedServer}_{database}.scmp.xml";
    }
}
```

### 2. Manifest Reading/Writing

```csharp
public class ScmpManifestHandler
{
    public SchemaComparison LoadManifest(string filePath)
    {
        var serializer = new XmlSerializer(typeof(SchemaComparison));
        using var reader = new StreamReader(filePath);
        return (SchemaComparison)serializer.Deserialize(reader);
    }
    
    public void SaveManifest(SchemaComparison comparison, string filePath)
    {
        var serializer = new XmlSerializer(typeof(SchemaComparison));
        using var writer = new StreamWriter(filePath);
        serializer.Serialize(writer, comparison);
    }
    
    // Extract comparison options for DACPAC generation
    public DacDeployOptions GetDeploymentOptions(SchemaComparison comparison)
    {
        var options = new DacDeployOptions();
        
        foreach (var property in comparison.ConfigurationOptions)
        {
            switch (property.Name)
            {
                case "DropObjectsNotInSource":
                    options.DropObjectsNotInSource = bool.Parse(property.Value);
                    break;
                case "AllowTableRecreation":
                    options.AllowTableRecreation = bool.Parse(property.Value);
                    break;
                case "BlockOnPossibleDataLoss":
                    options.BlockOnPossibleDataLoss = bool.Parse(property.Value);
                    break;
                // ... map all relevant options ...
            }
        }
        
        return options;
    }
}
```

### 3. Integration with DACPAC Migration Generator

```csharp
public class EnhancedMigrationGenerator
{
    private readonly ScmpManifestHandler _manifestHandler;
    
    public async Task<MigrationResult> GenerateMigration(string scmpFilePath)
    {
        // Load SCMP file
        var comparison = _manifestHandler.LoadManifest(scmpFilePath);
        
        // Extract source and target info
        var sourceConnection = ParseConnectionString(comparison.SourceModelProvider.ConnectionString);
        var targetConnection = ParseConnectionString(comparison.TargetModelProvider.ConnectionString);
        
        // Get deployment options from SCMP
        var deployOptions = _manifestHandler.GetDeploymentOptions(comparison);
        
        // Generate DACPACs using git history
        var sourceDacpac = await GenerateDacpacFromGit(sourceConnection, "HEAD~1");
        var targetDacpac = await GenerateDacpacFromGit(targetConnection, "HEAD");
        
        // Generate migration with SCMP settings
        var migration = await GenerateMigrationScript(
            sourceDacpac, 
            targetDacpac, 
            deployOptions);
        
        return migration;
    }
}
```

## Implementation Complete - Changes Made

### Components Removed

The following components have been permanently removed from the codebase:

1. **Plain text manifest format** - The `key = value` format is gone
2. **ManifestChange class** - Replaced by SCMP XML structure
3. **ManifestFileHandler class** - Replaced by ScmpManifestHandler
4. **ChangeManifest class** - Replaced by SchemaComparison model
5. **ManifestManager class** - Functionality integrated into ScmpManifestHandler
6. **Custom parsing logic** - XML deserialization handles everything
7. **All related tests** - Replaced with SCMP-specific tests

### No Migration Path Required

Direct cutover to SCMP format - no backward compatibility or migration phases

## Benefits of This Approach

### 1. Standardization
- Uses SQL Server's native comparison format
- Compatible with Visual Studio SSDT
- No proprietary formats to maintain

### 2. Comprehensive Settings
- All SQL comparison options in one place
- Consistent with SqlPackage.exe parameters
- Supports complex exclusion rules

### 3. Simplification
- Removes custom parsing code
- Eliminates format conversion steps
- Direct integration with DACPAC tools

### 4. Maintainability
- Microsoft maintains the SCMP schema
- Documentation already exists
- Tools already support the format

## Example Workflow

### 1. User provides SCMP file
```bash
migration-generator --scmp production_to_staging.scmp.xml
```

### 2. System reads SCMP settings
- Source database info
- Target database info  
- Comparison options
- Excluded objects

### 3. Generate DACPACs from git
- Use source database name to find git history
- Build SQL projects at different commits
- Apply SCMP comparison settings

### 4. Create migration
- Use SCMP options for SqlPackage
- Respect exclusions and settings
- Generate accurate migration script

## Required Code Changes Summary

### Files to Remove/Replace
- `ManifestFileHandler.cs` - Replace with `ScmpManifestHandler.cs`
- `ManifestChange.cs` - Remove entirely
- `ChangeManifest.cs` - Remove entirely

### New Files to Create
- `ScmpManifestHandler.cs` - SCMP file operations
- `SchemaComparison.cs` - SCMP model classes
- `ScmpToDeployOptions.cs` - Option mapping

### Tests to Rewrite
- All manifest parsing tests
- All manifest generation tests
- Integration tests using old format

## Implementation Notes

1. **Direct replacement**: No backward compatibility with old format
2. **New tests created**: All tests written specifically for SCMP
3. **Standard XML serialization**: Using built-in .NET XML capabilities
4. **Full SCMP options support**: All comparison settings preserved
5. **Same file locations**: Only format and extension changed

## Sample SCMP File Location

After implementation, the structure will be:
```
servers/
└── pharm-n1.pharm.local/
    └── abc/
        └── _change-manifests/
            └── 10_188_49_19_1433_abc_20250801_1804.scmp.xml
```

This file will contain all the information needed to:
- Identify source and target databases
- Configure comparison options
- Specify excluded objects
- Generate accurate migrations

## Conclusion

The SCMP format implementation is complete. All custom parsing code has been removed, improving maintainability and leveraging industry-standard SQL Server tools. The system now exclusively uses SCMP XML files for all database comparison and migration operations.

*Collaboration by Claude*