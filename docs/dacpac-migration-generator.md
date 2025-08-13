# DACPAC-Based Migration Generator

## Overview

This document describes an enhanced approach to SQL Server migration generation that leverages DACPAC (Data-tier Application Package) technology instead of text-based diff parsing. The system builds complete database states from git filesystem snapshots, generates DACPACs, and uses SQL Server's native comparison tools to create migration scripts.

## Problem Statement

Current text-based migration generators face challenges with:
- Complex dependency resolution
- Table recreation patterns
- Rename detection
- Operation ordering
- Circular dependencies
- Schema-bound objects

## Solution Architecture

### High-Level Flow

```
Git Repository State A          Git Repository State B
         ↓                                ↓
   Extract SQL Files              Extract SQL Files
         ↓                                ↓
Generate SourceOrganized.sqlproj   Generate TargetOrganized.sqlproj
         ↓                                ↓
   Build Source DACPAC              Build Target DACPAC
         ↓                                ↓
         └──────────────┬─────────────────┘
                        ↓
              SqlPackage.Script()
                        ↓
                 Migration Content
                        ↓
              MigrationScriptSplitter
                        ↓
            Split Migration Files
```

### Key Components

#### 1. Git State Extractor
Retrieves complete database schema from git at specific commits/refs:
- Checks out files from `servers/{server}/{database}/schemas/`
- Preserves folder structure
- Handles both historical and current states

#### 2. SQL Project Generator
Creates SDK-style SQL projects (.sqlproj) dynamically:
- Generates SDK-style project file using Microsoft.Build.Sql SDK
- Maintains proper build order
- Handles special files (schemas.sql, filegroups.sql)
- Sets appropriate build properties
- Automatically manages exclusion files for problematic dependencies

##### Exclusion File System
The system automatically manages `.dacpac-exclusions.json` files in each database schema directory:
- **Automatic Usage**: If exclusion file exists, it's automatically applied
- **Automatic Regeneration**: If build fails, exclusions are regenerated
- **Smart Iteration**: First iteration excludes SQL71561 errors, subsequent iterations only exclude cascading dependencies
- **Detailed Tracking**: Each exclusion includes the specific reason for exclusion

Example exclusion file structure:
```json
{
  "version": "1.0",
  "generated": "2025-01-13T10:00:00Z",
  "lastSuccessfulBuild": "2025-01-13T10:05:00Z",
  "exclusions": [
    {
      "file": "schemas/dbo/Views/vw_ComplexView.sql",
      "reason": "SQL71561: Contains unresolved reference to external database [OtherDB].[dbo].[ExternalTable]",
      "excludedOn": "2025-01-13T10:00:00Z",
      "iteration": 1,
      "errorCode": "SQL71561"
    }
  ]
}
```

#### 3. DACPAC Builder
Compiles SQL projects into DACPACs:
- Uses dotnet build with Microsoft.Build.Sql SDK
- Validates schema during build
- Reports compilation errors
- Produces portable schema packages

#### 4. Migration Script Generator
Compares DACPACs and generates migration scripts:
- Uses SqlPackage.exe or DacServices API
- Handles all operation types automatically
- Ensures correct execution order
- Includes safety checks and rollback points

## Implementation Details

### Core Classes

```csharp
public class DacpacMigrationGenerator
{
    private readonly IGitService _gitService;
    // Direct SQL project generation within this class
    private readonly IDacpacBuilder _dacpacBuilder;
    private readonly IMigrationScriptGenerator _scriptGenerator;
    
    public async Task<MigrationResult> GenerateMigrationAsync(
        string sourceRef, 
        string targetRef,
        MigrationOptions options)
    {
        // Step 1: Extract source state from git
        var sourceFiles = await _gitService.GetFilesAtRefAsync(
            sourceRef, 
            options.SchemaPath);
        
        // Step 2: Generate source SQL project
        var sourceProjectPath = await GenerateSdkProjectAsync(
            sourceFiles, 
            "SourceDatabase",
            options.TempDirectory);
        
        // Step 3: Build source DACPAC
        var sourceDacpac = await _dacpacBuilder.BuildAsync(sourceProjectPath);
        
        // Step 4: Extract target state from git
        var targetFiles = await _gitService.GetFilesAtRefAsync(
            targetRef, 
            options.SchemaPath);
        
        // Step 5: Generate target SQL project
        var targetProjectPath = await GenerateSdkProjectAsync(
            targetFiles, 
            "TargetDatabase",
            options.TempDirectory);
        
        // Step 6: Build target DACPAC
        var targetDacpac = await _dacpacBuilder.BuildAsync(targetProjectPath);
        
        // Step 7: Generate migration script
        var migrationScript = await _scriptGenerator.GenerateScriptAsync(
            sourceDacpac, 
            targetDacpac,
            options.ScriptOptions);
        
        return new MigrationResult
        {
            Script = migrationScript,
            SourceDacpac = sourceDacpac,
            TargetDacpac = targetDacpac,
            Metadata = GenerateMetadata(sourceRef, targetRef)
        };
    }
}
```

### SQL Project Generation

```csharp
public class SqlProjectGenerator : ISqlProjectGenerator
{
    public async Task<string> GenerateProjectAsync(
        IEnumerable<GitFile> files, 
        string projectName,
        string outputDirectory)
    {
        var projectDir = Path.Combine(outputDirectory, projectName);
        Directory.CreateDirectory(projectDir);
        
        // Copy SQL files maintaining structure
        foreach (var file in files)
        {
            var targetPath = Path.Combine(projectDir, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            await File.WriteAllTextAsync(targetPath, file.Content);
        }
        
        // Generate .sqlproj file
        var projectFile = GenerateProjectXml(files, projectName);
        var projectPath = Path.Combine(projectDir, $"{projectName}.sqlproj");
        await File.WriteAllTextAsync(projectPath, projectFile);
        
        return projectPath;
    }
    
    private string GenerateProjectXml(IEnumerable<GitFile> files, string projectName)
    {
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Project DefaultTargets=""Build"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <Import Project=""$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props""/>
  <PropertyGroup>
    <Configuration>Debug</Configuration>
    <Platform>AnyCPU</Platform>
    <Name>{projectName}</Name>
    <SchemaVersion>2.0</SchemaVersion>
    <ProjectVersion>4.1</ProjectVersion>
    <DSP>Microsoft.Data.Tools.Schema.Sql.Sql150DatabaseSchemaProvider</DSP>
    <OutputType>Database</OutputType>
    <RootPath />
    <RootNamespace>{projectName}</RootNamespace>
    <AssemblyName>{projectName}</AssemblyName>
    <ModelCollation>1033, CI</ModelCollation>
    <TargetDatabaseSet>True</TargetDatabaseSet>
  </PropertyGroup>
  <Import Project=""$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\SSDT\Microsoft.Data.Tools.Schema.SqlTasks.targets""/>
  <ItemGroup>
    {string.Join("\n    ", files.Select(f => $@"<Build Include=""{f.RelativePath}"" />"))}
  </ItemGroup>
</Project>";
    }
}
```

### Migration Script Generation Options

```csharp
public class ScriptGenerationOptions
{
    public bool DropObjectsNotInSource { get; set; } = true;
    public bool IgnorePermissions { get; set; } = true;
    public bool IgnoreUsers { get; set; } = true;
    public bool IncludeTransactionalScripts { get; set; } = true;
    public bool GenerateSmartDefaults { get; set; } = true;
    public bool BlockOnPossibleDataLoss { get; set} = false;
    public bool BackupDatabaseBeforeChanges { get; set; } = true;
    public bool VerifyDeployment { get; set; } = true;
}
```

## Process Flow

### 1. Initialization
```bash
migration-generator generate --source=HEAD~1 --target=HEAD
```

### 2. Git State Extraction
- Checkout files at source commit
- Checkout files at target commit
- Preserve directory structure

### 3. Project Generation
- Create temporary directories
- Generate .sqlproj files
- Copy SQL files into project structure

### 4. DACPAC Compilation
```bash
msbuild SourceDatabase.sqlproj /p:Configuration=Release
msbuild TargetDatabase.sqlproj /p:Configuration=Release
```

### 5. Migration Generation
```bash
sqlpackage /Action:Script \
  /SourceFile:Source.dacpac \
  /TargetFile:Target.dacpac \
  /OutputPath:migration.sql \
  /p:DropObjectsNotInSource=true
```

### 6. Output Structure
```
migrations/
└── 20250811_150000_migration/
    ├── source/
    │   ├── SourceDatabase.sqlproj
    │   ├── schemas/...
    │   └── bin/Release/SourceDatabase.dacpac
    ├── target/
    │   ├── TargetDatabase.sqlproj
    │   ├── schemas/...
    │   └── bin/Release/TargetDatabase.dacpac
    ├── migration.sql
    └── metadata.json
```

## Integration with Existing Tools

### 1. Preserve Existing Features
- Rename detection (for documentation)
- Migration validation
- Reverse migration generation
- Git integration

### 2. Enhanced Capabilities
- Automatic dependency resolution
- Complex refactoring support
- Schema validation at build time
- Comprehensive error reporting

### 3. Backward Compatibility
- Support both old and new generation methods
- Migration format remains unchanged
- Existing migrations remain valid

## Benefits

### 1. Accuracy
- **SQL Server Native Intelligence**: Leverages SQL Server's built-in understanding of dependencies
- **Correct Operation Ordering**: Automatically handles complex dependency chains
- **Table Recreation Handling**: Properly detects and handles tmp_ms_xx patterns
- **Circular Dependency Resolution**: SQL Server tools handle these automatically

### 2. Completeness
- **All Object Types**: Handles tables, views, procedures, functions, triggers, etc.
- **All Operation Types**: CREATE, ALTER, DROP, RENAME
- **Constraint Management**: Properly orders constraint operations
- **Index Operations**: Handles clustered/non-clustered index dependencies

### 3. Reliability
- **Build-Time Validation**: Errors caught during DACPAC compilation
- **No Manual Parsing**: Eliminates regex-based SQL parsing errors
- **Tested Tools**: Uses Microsoft's production-tested tools

### 4. Maintainability
- **Simpler Codebase**: Removes complex parsing logic
- **Fewer Edge Cases**: SQL Server handles edge cases
- **Better Error Messages**: Compilation provides clear error messages

## Usage Examples

### Basic Migration
```csharp
var generator = new DacpacMigrationGenerator();
var result = await generator.GenerateMigrationAsync(
    sourceRef: "main~1",
    targetRef: "main",
    options: new MigrationOptions
    {
        SchemaPath = "servers/prod-server/mydb/schemas",
        TempDirectory = "./temp"
    });

await File.WriteAllTextAsync("migration.sql", result.Script);
```

### With Custom Options
```csharp
var options = new MigrationOptions
{
    SchemaPath = "servers/prod-server/mydb/schemas",
    TempDirectory = "./temp",
    ScriptOptions = new ScriptGenerationOptions
    {
        DropObjectsNotInSource = false,  // Keep objects not in source
        BlockOnPossibleDataLoss = true,  // Prevent data loss
        IncludeTransactionalScripts = true,  // Wrap in transaction
        GenerateSmartDefaults = true  // Add defaults for NOT NULL columns
    }
};

var result = await generator.GenerateMigrationAsync("v1.0", "v2.0", options);
```

### CI/CD Integration
```yaml
- name: Generate Migration
  run: |
    dotnet run --project SqlServer.Schema.Migration.Generator -- \
      generate-dacpac \
      --source=${{ github.event.before }} \
      --target=${{ github.sha }} \
      --output=./migrations
```

## Error Handling

### Build Errors
```csharp
try
{
    var dacpac = await _dacpacBuilder.BuildAsync(projectPath);
}
catch (DacpacBuildException ex)
{
    Console.WriteLine($"Build failed: {ex.Message}");
    foreach (var error in ex.BuildErrors)
    {
        Console.WriteLine($"  {error.File}({error.Line}): {error.Message}");
    }
}
```

### Migration Generation Errors
```csharp
catch (MigrationGenerationException ex)
{
    Console.WriteLine($"Migration generation failed: {ex.Message}");
    if (ex.HasDataLossWarning)
    {
        Console.WriteLine("WARNING: This migration may cause data loss");
    }
}
```

## Performance Considerations

### Caching
- Cache built DACPACs for common commits
- Reuse SQL projects when possible
- Implement incremental builds

### Parallel Processing
- Build source and target DACPACs in parallel
- Process multiple migrations concurrently
- Batch validation operations

### Resource Management
```csharp
public class TempDirectoryManager : IDisposable
{
    private readonly string _tempPath;
    
    public TempDirectoryManager()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);
    }
    
    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, recursive: true);
        }
    }
}
```

## Future Enhancements

### 1. Incremental Migrations
- Detect unchanged schemas
- Skip redundant DACPAC builds
- Cache intermediate results

### 2. Schema Drift Detection
- Compare actual database with expected state
- Generate drift correction scripts
- Alert on unauthorized changes

### 3. Multi-Database Support
- Handle cross-database references
- Generate coordinated migrations
- Support distributed transactions

### 4. Advanced Scenarios
- Blue-green deployments
- Zero-downtime migrations
- Partial schema updates

## Conclusion

The DACPAC-based migration generator provides a robust, reliable, and maintainable solution for SQL Server schema migrations. By leveraging SQL Server's native tools, it eliminates complex parsing logic while providing accurate, complete migration scripts.

*Collaboration by Claude*