# SQL Project Generation for DACPAC-Based Migration - Detailed Implementation Guide

## Purpose
This document provides a detailed, step-by-step guide for implementing the SQL project generation component of the DACPAC-based migration system. It's designed to help another Claude Code instance understand and implement the complete process.

## Context and Prerequisites

### Repository Structure You'll Be Working With
```
SQL_Server_Compare/
├── servers/                     # SQL files organized by server/database
│   └── {servername}/
│       └── {databasename}/
│           └── schemas/
│               ├── dbo/
│               │   ├── Tables/
│               │   │   └── {TableName}/
│               │   │       ├── TBL_{TableName}.sql
│               │   │       ├── FK_{constraints}.sql
│               │   │       ├── IDX_{indexes}.sql
│               │   │       └── ...
│               │   ├── Views/
│               │   ├── StoredProcedures/
│               │   └── Functions/
│               └── {other_schemas}/
```

### What You Need to Build
Two temporary SQL Server Database Projects (.sqlproj) that can be compiled into DACPACs:
1. **SourceProject**: Represents database state at an earlier git commit
2. **TargetProject**: Represents database state at a later git commit

## Step-by-Step Implementation

### Step 1: Extract Files from Git at Specific Commits

```csharp
public class GitFileExtractor
{
    /// <summary>
    /// Extracts all SQL files from a specific git commit
    /// </summary>
    /// <param name="gitRef">Commit hash, branch name, or tag (e.g., "HEAD~1", "main", "abc123")</param>
    /// <param name="basePath">Path to schemas folder (e.g., "servers/prod/mydb/schemas")</param>
    /// <returns>List of SQL files with their relative paths and content</returns>
    public async Task<List<SqlFile>> ExtractFilesAtCommit(string gitRef, string basePath)
    {
        var files = new List<SqlFile>();
        
        // Use LibGit2Sharp or git commands to get files
        // Example using git show command:
        var gitCommand = $"git ls-tree -r --name-only {gitRef} -- {basePath}";
        var fileList = await ExecuteGitCommand(gitCommand);
        
        foreach (var filePath in fileList)
        {
            // Get file content at specific commit
            var content = await GetFileContentAtCommit(gitRef, filePath);
            
            // Calculate relative path from basePath
            var relativePath = GetRelativePath(basePath, filePath);
            
            files.Add(new SqlFile
            {
                FullPath = filePath,
                RelativePath = relativePath,
                Content = content,
                Schema = ExtractSchemaFromPath(relativePath),
                ObjectType = ExtractObjectTypeFromPath(relativePath),
                ObjectName = ExtractObjectNameFromPath(relativePath)
            });
        }
        
        return files;
    }
    
    private async Task<string> GetFileContentAtCommit(string gitRef, string filePath)
    {
        // Get file content using: git show {gitRef}:{filePath}
        var command = $"git show {gitRef}:{filePath}";
        return await ExecuteGitCommand(command);
    }
}

public class SqlFile
{
    public string FullPath { get; set; }      // Full git path
    public string RelativePath { get; set; }  // Path relative to schemas folder
    public string Content { get; set; }       // SQL file content
    public string Schema { get; set; }        // e.g., "dbo", "new"
    public string ObjectType { get; set; }    // e.g., "Tables", "Views"
    public string ObjectName { get; set; }    // e.g., "Customer", "GetOrders"
}
```

### Step 2: Organize Files for SQL Project

```csharp
public class SqlProjectOrganizer
{
    /// <summary>
    /// CRITICAL: Files must be organized in the correct build order
    /// </summary>
    public ProjectStructure OrganizeFiles(List<SqlFile> files)
    {
        var structure = new ProjectStructure();
        
        // IMPORTANT: Build order matters!
        // 1. Schemas first (CREATE SCHEMA statements)
        structure.SchemaFiles = files
            .Where(f => f.RelativePath.EndsWith("/schema.sql") || 
                       f.RelativePath.Contains("/Schemas.sql"))
            .OrderBy(f => f.Schema == "dbo" ? 0 : 1) // dbo first
            .ToList();
        
        // 2. Filegroups and database settings
        structure.DatabaseFiles = files
            .Where(f => f.RelativePath.Contains("/Database/") ||
                       f.RelativePath.Contains("Filegroups.sql"))
            .ToList();
        
        // 3. Tables (must be before views/procedures that reference them)
        structure.TableFiles = files
            .Where(f => f.ObjectType == "Tables")
            .GroupBy(f => f.ObjectName)
            .Select(g => new TableFileGroup
            {
                TableName = g.Key,
                // CRITICAL: Order within table matters!
                Files = g.OrderBy(f => GetTableFileOrder(f)).ToList()
            })
            .ToList();
        
        // 4. Functions (may be referenced by views)
        structure.FunctionFiles = files
            .Where(f => f.ObjectType == "Functions")
            .ToList();
        
        // 5. Views (may reference tables and functions)
        structure.ViewFiles = files
            .Where(f => f.ObjectType == "Views")
            .ToList();
        
        // 6. Stored Procedures (may reference everything)
        structure.StoredProcedureFiles = files
            .Where(f => f.ObjectType == "StoredProcedures")
            .ToList();
        
        // 7. Triggers (must be after tables)
        structure.TriggerFiles = files
            .Where(f => f.RelativePath.Contains("/TR_") || 
                       f.RelativePath.Contains("/trg_"))
            .ToList();
        
        return structure;
    }
    
    private int GetTableFileOrder(SqlFile file)
    {
        // CRITICAL: Order for table files
        if (file.RelativePath.Contains("/TBL_")) return 1;  // Table definition first
        if (file.RelativePath.Contains("/PK_")) return 2;   // Primary key
        if (file.RelativePath.Contains("/DF_")) return 3;   // Defaults
        if (file.RelativePath.Contains("/CK_")) return 4;   // Check constraints
        if (file.RelativePath.Contains("/IDX_")) return 5;  // Indexes
        if (file.RelativePath.Contains("/FK_")) return 6;   // Foreign keys (last!)
        if (file.RelativePath.Contains("/EP_")) return 7;   // Extended properties
        return 99;
    }
}

public class ProjectStructure
{
    public List<SqlFile> SchemaFiles { get; set; }
    public List<SqlFile> DatabaseFiles { get; set; }
    public List<TableFileGroup> TableFiles { get; set; }
    public List<SqlFile> FunctionFiles { get; set; }
    public List<SqlFile> ViewFiles { get; set; }
    public List<SqlFile> StoredProcedureFiles { get; set; }
    public List<SqlFile> TriggerFiles { get; set; }
}

public class TableFileGroup
{
    public string TableName { get; set; }
    public List<SqlFile> Files { get; set; }
}
```

### Step 3: Generate the SQL Project File (.sqlproj)

The system uses SDK-style SQL projects with Microsoft.Build.Sql SDK:

```csharp
/// <summary>
/// Creates a simple SDK-style SQL project file
/// Uses Microsoft.Build.Sql SDK for modern DACPAC building
/// </summary>
void CreateSdkStyleSqlProject(string projectPath, string projectName, string? referenceDacpacPath = null)
{
    // Minimal SDK-style SQL project with just essential properties
    var projectContent = $@"<Project Sdk=""Microsoft.Build.Sql/0.2.0-preview"">
      <PropertyGroup>
        <Name>{projectName}</Name>
        <EnableStaticCodeAnalysis>false</EnableStaticCodeAnalysis>
        <DSP>Microsoft.Data.Tools.Schema.Sql.Sql150DatabaseSchemaProvider</DSP>
        <TreatTSqlWarningsAsErrors>false</TreatTSqlWarningsAsErrors>
        <SuppressTSqlWarnings>71502;71562;71558;71561</SuppressTSqlWarnings>
        <!-- Skip model validation entirely -->
        <SkipModelValidation>true</SkipModelValidation>
        <SuppressModelValidation>true</SuppressModelValidation>
        <SuppressMissingDependenciesErrors>true</SuppressMissingDependenciesErrors>
        <!-- Still produce a DACPAC -->
        <TargetDatabaseSet>true</TargetDatabaseSet>
      </PropertyGroup>
      
      <ItemGroup>
        <Build Include=""**\*.sql"" />
      </ItemGroup>
    </Project>";
    
    File.WriteAllText(projectPath, projectContent);
}csharp
public class SqlProjectGenerator
{
    /// <summary>
    /// Generates a complete .sqlproj file that can be built by MSBuild
    /// </summary>
    public async Task<string> GenerateProject(
        ProjectStructure structure,
        string projectName,
        string outputDirectory)
    {
        // Create project directory
        var projectDir = Path.Combine(outputDirectory, projectName);
        Directory.CreateDirectory(projectDir);
        
        // Copy all SQL files to project directory maintaining structure
        var copiedFiles = await CopyFilesToProjectDirectory(structure, projectDir);
        
        // Generate .sqlproj XML
        var projectXml = GenerateProjectXml(projectName, copiedFiles);
        
        // Write .sqlproj file
        var projectPath = Path.Combine(projectDir, $"{projectName}.sqlproj");
        await File.WriteAllTextAsync(projectPath, projectXml);
        
        // IMPORTANT: Create any necessary helper files
        await CreateHelperFiles(projectDir, structure);
        
        return projectPath;
    }
    
    private async Task<List<string>> CopyFilesToProjectDirectory(
        ProjectStructure structure,
        string projectDir)
    {
        var copiedFiles = new List<string>();
        
        // Helper function to copy and track files
        async Task CopyFile(SqlFile sqlFile, string subPath = null)
        {
            var relativePath = subPath ?? sqlFile.RelativePath;
            var targetPath = Path.Combine(projectDir, relativePath);
            
            // Create directory if needed
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            
            // Write file content
            await File.WriteAllTextAsync(targetPath, sqlFile.Content);
            
            // Track for .sqlproj
            copiedFiles.Add(relativePath);
        }
        
        // Copy files in build order
        
        // 1. Schemas
        foreach (var file in structure.SchemaFiles)
        {
            await CopyFile(file);
        }
        
        // 2. Database settings
        foreach (var file in structure.DatabaseFiles)
        {
            await CopyFile(file);
        }
        
        // 3. Tables (maintaining internal order)
        foreach (var tableGroup in structure.TableFiles)
        {
            foreach (var file in tableGroup.Files)
            {
                await CopyFile(file);
            }
        }
        
        // 4. Functions
        foreach (var file in structure.FunctionFiles)
        {
            await CopyFile(file);
        }
        
        // 5. Views
        foreach (var file in structure.ViewFiles)
        {
            await CopyFile(file);
        }
        
        // 6. Stored Procedures
        foreach (var file in structure.StoredProcedureFiles)
        {
            await CopyFile(file);
        }
        
        // 7. Triggers
        foreach (var file in structure.TriggerFiles)
        {
            await CopyFile(file);
        }
        
        return copiedFiles;
    }
    
    private string GenerateProjectXml(string projectName, List<string> files)
    {
        // CRITICAL: This XML structure is required for SQL Server Database Projects
        var xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Project DefaultTargets=""Build"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"" ToolsVersion=""4.0"">
  <PropertyGroup>
    <Configuration Condition="" '$(Configuration)' == '' "">Debug</Configuration>
    <Platform Condition="" '$(Platform)' == '' "">AnyCPU</Platform>
    <Name>{projectName}</Name>
    <SchemaVersion>2.0</SchemaVersion>
    <ProjectVersion>4.1</ProjectVersion>
    <ProjectGuid>{{{Guid.NewGuid()}}}</ProjectGuid>
    <DSP>Microsoft.Data.Tools.Schema.Sql.Sql150DatabaseSchemaProvider</DSP>
    <OutputType>Database</OutputType>
    <RootPath />
    <RootNamespace>{projectName}</RootNamespace>
    <AssemblyName>{projectName}</AssemblyName>
    <ModelCollation>1033,CI</ModelCollation>
    <DefaultFileStructure>BySchemaAndSchemaType</DefaultFileStructure>
    <DeployToDatabase>True</DeployToDatabase>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
    <TargetLanguage>CS</TargetLanguage>
    <AppDesignerFolder>Properties</AppDesignerFolder>
    <SqlServerVerification>False</SqlServerVerification>
    <IncludeCompositeObjects>True</IncludeCompositeObjects>
    <TargetDatabaseSet>True</TargetDatabaseSet>
    <DefaultCollation>SQL_Latin1_General_CP1_CI_AS</DefaultCollation>
    <DefaultFilegroup>PRIMARY</DefaultFilegroup>
  </PropertyGroup>
  <PropertyGroup Condition="" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' "">
    <OutputPath>bin\Release\</OutputPath>
    <BuildScriptName>$(MSBuildProjectName).sql</BuildScriptName>
    <TreatWarningsAsErrors>False</TreatWarningsAsErrors>
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
    <DefineDebug>false</DefineDebug>
    <DefineTrace>false</DefineTrace>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
  </PropertyGroup>
  <PropertyGroup Condition="" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' "">
    <OutputPath>bin\Debug\</OutputPath>
    <BuildScriptName>$(MSBuildProjectName).sql</BuildScriptName>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <DebugSymbols>true</DebugSymbols>
    <DebugType>full</DebugType>
    <Optimize>false</Optimize>
    <DefineDebug>true</DefineDebug>
    <DefineTrace>true</DefineTrace>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
  </PropertyGroup>
  <PropertyGroup>
    <VisualStudioVersion Condition=""'$(VisualStudioVersion)' == ''"">11.0</VisualStudioVersion>
    <SSDTExists Condition=""Exists('$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\SSDT\Microsoft.Data.Tools.Schema.SqlTasks.targets')"">True</SSDTExists>
    <VisualStudioVersion Condition=""'$(SSDTExists)' == ''"">11.0</VisualStudioVersion>
  </PropertyGroup>
  <Import Condition=""'$(SQLDBExtensionsRefPath)' != ''"" Project=""$(SQLDBExtensionsRefPath)\Microsoft.Data.Tools.Schema.SqlTasks.targets"" />
  <Import Condition=""'$(SQLDBExtensionsRefPath)' == ''"" Project=""$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\SSDT\Microsoft.Data.Tools.Schema.SqlTasks.targets"" />
  <ItemGroup>
    <Folder Include=""Properties"" />
{string.Join("\n", files.Select(f => Path.GetDirectoryName(f))
    .Distinct()
    .Where(d => !string.IsNullOrEmpty(d))
    .Select(d => $@"    <Folder Include=""{d}"" />"))}
  </ItemGroup>
  <ItemGroup>
{string.Join("\n", files.Select(f => $@"    <Build Include=""{f.Replace('/', '\\')}"" />"))}
  </ItemGroup>
</Project>";
        
        return xml;
    }
    
    private async Task CreateHelperFiles(string projectDir, ProjectStructure structure)
    {
        // Create a master schema file if individual schema files don't exist
        if (!structure.SchemaFiles.Any())
        {
            var schemas = structure.TableFiles
                .SelectMany(t => t.Files)
                .Select(f => f.Schema)
                .Distinct()
                .Where(s => !string.IsNullOrEmpty(s) && s != "dbo");
            
            if (schemas.Any())
            {
                var schemaScript = string.Join("\nGO\n", 
                    schemas.Select(s => $"CREATE SCHEMA [{s}];"));
                
                await File.WriteAllTextAsync(
                    Path.Combine(projectDir, "Schemas.sql"),
                    schemaScript);
            }
        }
    }
}
```

### Step 4: Build the SQL Project to Generate DACPAC

```csharp
public class DacpacBuilder
{
    /// <summary>
    /// Builds a SQL project to produce a DACPAC file
    /// </summary>
    public async Task<DacpacBuildResult> BuildProject(string projectPath)
    {
        var projectDir = Path.GetDirectoryName(projectPath);
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        
        // Option 1: Use MSBuild directly (recommended)
        var buildResult = await BuildUsingMSBuild(projectPath);
        
        if (!buildResult.Success)
        {
            // Option 2: Fallback to DacServices API
            buildResult = await BuildUsingDacServices(projectPath);
        }
        
        return buildResult;
    }
    
    private async Task<DacpacBuildResult> BuildUsingMSBuild(string projectPath)
    {
        var result = new DacpacBuildResult();
        
        try
        {
            // Find MSBuild (try multiple locations)
            var msbuildPath = FindMSBuild();
            
            // Build command
            var arguments = $"\"{projectPath}\" /p:Configuration=Release /p:Platform=AnyCPU";
            
            var processInfo = new ProcessStartInfo
            {
                FileName = msbuildPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(processInfo);
            var output = await process.StandardOutput.ReadToEndAsync();
            var errors = await process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();
            
            result.Success = process.ExitCode == 0;
            result.Output = output;
            result.Errors = errors;
            
            if (result.Success)
            {
                // Find the generated DACPAC
                var projectDir = Path.GetDirectoryName(projectPath);
                var projectName = Path.GetFileNameWithoutExtension(projectPath);
                result.DacpacPath = Path.Combine(projectDir, "bin", "Release", $"{projectName}.dacpac");
                
                if (!File.Exists(result.DacpacPath))
                {
                    // Try Debug folder
                    result.DacpacPath = Path.Combine(projectDir, "bin", "Debug", $"{projectName}.dacpac");
                }
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors = ex.Message;
        }
        
        return result;
    }
    
    private async Task<DacpacBuildResult> BuildUsingDacServices(string projectPath)
    {
        var result = new DacpacBuildResult();
        
        try
        {
            // Load all SQL files from project
            var sqlFiles = await LoadSqlFilesFromProject(projectPath);
            
            // Create TSqlModel and add objects
            var model = new TSqlModel(SqlServerVersion.Sql150, new TSqlModelOptions());
            
            foreach (var file in sqlFiles)
            {
                model.AddObjects(file.Content);
            }
            
            // Build DACPAC
            var projectDir = Path.GetDirectoryName(projectPath);
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var dacpacPath = Path.Combine(projectDir, "bin", "Release", $"{projectName}.dacpac");
            
            Directory.CreateDirectory(Path.GetDirectoryName(dacpacPath));
            
            DacPackageExtensions.BuildPackage(
                dacpacPath,
                model,
                new PackageMetadata
                {
                    Name = projectName,
                    Version = "1.0.0.0"
                });
            
            result.Success = true;
            result.DacpacPath = dacpacPath;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors = ex.Message;
        }
        
        return result;
    }
    
    private string FindMSBuild()
    {
        // Try common MSBuild locations
        var possiblePaths = new[]
        {
            @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe"
        };
        
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }
        
        // Try to find using vswhere
        var vswherePath = @"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe";
        if (File.Exists(vswherePath))
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = vswherePath,
                Arguments = "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(processInfo);
            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!string.IsNullOrEmpty(output) && File.Exists(output))
                return output;
        }
        
        throw new FileNotFoundException("MSBuild not found");
    }
}

public class DacpacBuildResult
{
    public bool Success { get; set; }
    public string DacpacPath { get; set; }
    public string Output { get; set; }
    public string Errors { get; set; }
}
```

### Step 5: Complete Workflow Integration

```csharp
public class SqlProjectWorkflow
{
    private readonly GitFileExtractor _gitExtractor;
    private readonly SqlProjectOrganizer _organizer;
    private readonly SqlProjectGenerator _generator;
    private readonly DacpacBuilder _builder;
    
    /// <summary>
    /// Complete workflow to generate DACPACs for comparison
    /// </summary>
    public async Task<DacpacPair> GenerateDacpacPair(
        string sourceGitRef,
        string targetGitRef,
        string schemaBasePath,
        string workingDirectory)
    {
        // Create temp directory for this operation
        var tempDir = Path.Combine(workingDirectory, $"migration_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(tempDir);
        
        try
        {
            // Generate source DACPAC
            Console.WriteLine($"Generating source DACPAC from git ref: {sourceGitRef}");
            var sourceDacpac = await GenerateSingleDacpac(
                sourceGitRef,
                schemaBasePath,
                Path.Combine(tempDir, "source"),
                "SourceDatabase");
            
            // Generate target DACPAC
            Console.WriteLine($"Generating target DACPAC from git ref: {targetGitRef}");
            var targetDacpac = await GenerateSingleDacpac(
                targetGitRef,
                schemaBasePath,
                Path.Combine(tempDir, "target"),
                "TargetDatabase");
            
            return new DacpacPair
            {
                SourceDacpacPath = sourceDacpac,
                TargetDacpacPath = targetDacpac,
                TempDirectory = tempDir
            };
        }
        catch
        {
            // Clean up on failure
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
            throw;
        }
    }
    
    private async Task<string> GenerateSingleDacpac(
        string gitRef,
        string schemaBasePath,
        string outputDir,
        string projectName)
    {
        // Step 1: Extract files from git
        var files = await _gitExtractor.ExtractFilesAtCommit(gitRef, schemaBasePath);
        Console.WriteLine($"  Extracted {files.Count} SQL files");
        
        // Step 2: Organize files
        var structure = _organizer.OrganizeFiles(files);
        Console.WriteLine($"  Organized into project structure");
        
        // Step 3: Generate SQL project
        var projectPath = await _generator.GenerateProject(structure, projectName, outputDir);
        Console.WriteLine($"  Generated SQL project: {projectPath}");
        
        // Step 4: Build DACPAC
        var buildResult = await _builder.BuildProject(projectPath);
        if (!buildResult.Success)
        {
            throw new Exception($"Failed to build DACPAC: {buildResult.Errors}");
        }
        Console.WriteLine($"  Built DACPAC: {buildResult.DacpacPath}");
        
        return buildResult.DacpacPath;
    }
}

public class DacpacPair
{
    public string SourceDacpacPath { get; set; }
    public string TargetDacpacPath { get; set; }
    public string TempDirectory { get; set; }
}
```

## Common Issues and Solutions

### Issue 1: Build Order Problems
**Symptom**: "Invalid object name" or "Could not find object" errors during build

**Solution**: Ensure files are added to the project in the correct order:
1. Schemas first
2. Tables before views/procedures
3. Foreign keys after all tables
4. Functions before views that use them

### Issue 2: Cross-Database References
**Symptom**: "Could not resolve object reference" for objects like `[OtherDB].[dbo].[Table]`

**Solution**: 
```csharp
// Remove cross-database references or add database references
content = Regex.Replace(content, @"\[[\w]+\]\.\[dbo\]\.", "[dbo].");
```

### Issue 3: SQLCMD Variables
**Symptom**: "Could not resolve $(DatabaseName)" errors

**Solution**:
```csharp
// Replace SQLCMD variables with actual values
content = content.Replace("$(DatabaseName)", "TempDatabase");
```

### Issue 4: Missing Schemas
**Symptom**: "Cannot find the schema" errors

**Solution**: Ensure schema creation scripts are included and processed first

## Testing Your Implementation

```csharp
[Test]
public async Task TestDacpacGeneration()
{
    var workflow = new SqlProjectWorkflow();
    
    // Test with two git commits
    var result = await workflow.GenerateDacpacPair(
        sourceGitRef: "HEAD~1",
        targetGitRef: "HEAD",
        schemaBasePath: "servers/prod/mydb/schemas",
        workingDirectory: "./temp"
    );
    
    // Verify DACPACs were created
    Assert.That(File.Exists(result.SourceDacpacPath));
    Assert.That(File.Exists(result.TargetDacpacPath));
    
    // Verify DACPACs are valid
    using var sourcePkg = DacPackage.Load(result.SourceDacpacPath);
    using var targetPkg = DacPackage.Load(result.TargetDacpacPath);
    
    Assert.That(sourcePkg.Name, Is.EqualTo("SourceDatabase"));
    Assert.That(targetPkg.Name, Is.EqualTo("TargetDatabase"));
}
```

## Integration with Migration Generator

Once you have both DACPACs, generate the migration script:

```csharp
public async Task<string> GenerateMigrationScript(DacpacPair dacpacs)
{
    var scriptPath = Path.Combine(dacpacs.TempDirectory, "migration.sql");
    
    // Use SqlPackage.exe
    var arguments = $"/Action:Script " +
                   $"/SourceFile:\"{dacpacs.SourceDacpacPath}\" " +
                   $"/TargetFile:\"{dacpacs.TargetDacpacPath}\" " +
                   $"/OutputPath:\"{scriptPath}\" " +
                   $"/p:DropObjectsNotInSource=true " +
                   $"/p:IgnorePermissions=true";
    
    await ExecuteProcess("sqlpackage.exe", arguments);
    
    return await File.ReadAllTextAsync(scriptPath);
}
```

## Key Points for Fellow Claude Agent

1. **File Organization is Critical**: The order files are added to the project matters for build success
2. **Schema Extraction**: You're working with files already organized in git, not a live database
3. **Two Separate Projects**: You need two independent SQL projects for source and target states
4. **Temporary Files**: Everything happens in temp directories that should be cleaned up
5. **Error Handling**: Build failures are common - provide detailed error messages
6. **Cross-Database References**: May need to be handled or stripped
7. **Git Integration**: Use git commands or LibGit2Sharp to get files at specific commits

## Required NuGet Packages

```xml
<PackageReference Include="Microsoft.SqlServer.DacFx" Version="162.0.0" />
<PackageReference Include="LibGit2Sharp" Version="0.27.0" />
<PackageReference Include="Microsoft.Build" Version="17.0.0" />
```

Good luck with your implementation! The key is maintaining proper file organization and build order throughout the process.

*Collaboration by Claude*