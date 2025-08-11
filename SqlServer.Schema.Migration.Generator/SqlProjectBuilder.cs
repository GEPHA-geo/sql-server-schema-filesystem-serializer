using System.Text;
using System.Xml;

namespace SqlServer.Schema.Migration.Generator;

/// <summary>
/// Builds SQL Server Database Projects (.sqlproj) from file system structure
/// </summary>
public class SqlProjectBuilder
{
    /// <summary>
    /// Creates a SQL Server Database Project from a directory of SQL files
    /// </summary>
    public async Task<string> CreateSqlProject(string schemaPath, string outputDir, string projectName)
    {
        if (!Directory.Exists(schemaPath))
            throw new DirectoryNotFoundException($"Schema path not found: {schemaPath}");
        
        // Create project directory
        var projectDir = Path.Combine(outputDir, projectName);
        Directory.CreateDirectory(projectDir);
        
        // Collect all SQL files
        var sqlFiles = CollectSqlFiles(schemaPath);
        
        if (!sqlFiles.Any())
        {
            Console.WriteLine($"No SQL files found in {schemaPath}");
            return string.Empty;
        }
        
        Console.WriteLine($"  Found {sqlFiles.Count} SQL files");
        
        // Copy files to project directory maintaining structure
        var copiedFiles = await CopyFilesToProject(sqlFiles, schemaPath, projectDir);
        
        // Generate .sqlproj file
        var projectPath = Path.Combine(projectDir, $"{projectName}.sqlproj");
        await GenerateProjectFile(projectPath, projectName, copiedFiles);
        
        Console.WriteLine($"  Created SQL project: {projectPath}");
        return projectPath;
    }
    
    /// <summary>
    /// Collects all SQL files from the schema directory in correct build order
    /// </summary>
    List<SqlFileInfo> CollectSqlFiles(string schemaPath)
    {
        var files = new List<SqlFileInfo>();
        
        // Get all SQL files
        var allFiles = Directory.GetFiles(schemaPath, "*.sql", SearchOption.AllDirectories);
        
        foreach (var file in allFiles)
        {
            // Skip migration files
            if (file.Contains("z_migrations") || file.Contains("_change-manifests"))
                continue;
            
            var relativePath = Path.GetRelativePath(schemaPath, file);
            var fileInfo = new SqlFileInfo
            {
                FullPath = file,
                RelativePath = relativePath,
                BuildOrder = GetBuildOrder(relativePath)
            };
            
            files.Add(fileInfo);
        }
        
        // Sort by build order
        return files.OrderBy(f => f.BuildOrder).ThenBy(f => f.RelativePath).ToList();
    }
    
    /// <summary>
    /// Determines the build order for a SQL file based on its type
    /// </summary>
    int GetBuildOrder(string relativePath)
    {
        var path = relativePath.ToLower();
        var fileName = Path.GetFileName(path);
        
        // Critical order for successful builds
        if (path.Contains("schemas") || fileName.Contains("schema.sql")) return 100;
        if (path.Contains("types") || fileName.StartsWith("type_")) return 200;
        if (path.Contains("sequences")) return 250;
        
        // Tables and their components
        if (path.Contains("tables"))
        {
            if (fileName.StartsWith("tbl_") || fileName.Contains("table")) return 300;
            if (fileName.StartsWith("pk_")) return 310;
            if (fileName.StartsWith("df_") || fileName.Contains("default")) return 320;
            if (fileName.StartsWith("ck_") || fileName.Contains("check")) return 330;
            if (fileName.StartsWith("uq_") || fileName.Contains("unique")) return 340;
            if (fileName.StartsWith("idx_") || fileName.Contains("index")) return 350;
            if (fileName.StartsWith("fk_") || fileName.Contains("foreign")) return 360; // FK last!
            if (fileName.StartsWith("ep_") || fileName.Contains("extended")) return 370;
            return 300; // Default for tables
        }
        
        // Functions must be before views/procedures that might use them
        if (path.Contains("functions") || fileName.StartsWith("fn_")) return 400;
        
        // Views can reference tables and functions
        if (path.Contains("views") || fileName.StartsWith("vw_")) return 500;
        
        // Stored procedures can reference everything
        if (path.Contains("stored-procedures") || path.Contains("procedures") || fileName.StartsWith("sp_")) return 600;
        
        // Triggers must be after tables
        if (path.Contains("triggers") || fileName.StartsWith("tr_") || fileName.StartsWith("trg_")) return 700;
        
        // Synonyms
        if (path.Contains("synonyms")) return 800;
        
        // Everything else
        return 900;
    }
    
    /// <summary>
    /// Copies SQL files to project directory
    /// </summary>
    async Task<List<string>> CopyFilesToProject(List<SqlFileInfo> files, string sourceRoot, string projectDir)
    {
        var copiedFiles = new List<string>();
        
        foreach (var file in files)
        {
            try
            {
                var targetPath = Path.Combine(projectDir, file.RelativePath);
                var targetDir = Path.GetDirectoryName(targetPath);
                
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);
                
                var content = await File.ReadAllTextAsync(file.FullPath);
                
                // Clean up content for DACPAC compilation
                content = CleanSqlContent(content);
                
                await File.WriteAllTextAsync(targetPath, content);
                copiedFiles.Add(file.RelativePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Could not copy {file.RelativePath}: {ex.Message}");
            }
        }
        
        return copiedFiles;
    }
    
    /// <summary>
    /// Cleans SQL content for DACPAC compilation
    /// </summary>
    string CleanSqlContent(string content)
    {
        // Remove EXCLUDED comments if present
        if (content.StartsWith("-- EXCLUDED:"))
        {
            var lines = content.Split('\n');
            var cleanedLines = lines.SkipWhile(l => l.StartsWith("--")).ToArray();
            content = string.Join('\n', cleanedLines);
        }
        
        // Remove USE statements (not allowed in DACPAC)
        content = System.Text.RegularExpressions.Regex.Replace(
            content, 
            @"^\s*USE\s+\[.*?\]\s*;?\s*$", 
            "", 
            System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        // Remove GO statements (handled by DACPAC builder)
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"^\s*GO\s*$",
            "",
            System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        return content;
    }
    
    /// <summary>
    /// Generates the .sqlproj XML file
    /// </summary>
    async Task GenerateProjectFile(string projectPath, string projectName, List<string> files)
    {
        var xml = new StringBuilder();
        xml.AppendLine(@"<?xml version=""1.0"" encoding=""utf-8""?>");
        xml.AppendLine(@"<Project DefaultTargets=""Build"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"" ToolsVersion=""4.0"">");
        
        // Project properties
        xml.AppendLine(@"  <PropertyGroup>");
        xml.AppendLine(@"    <Configuration Condition="" '$(Configuration)' == '' "">Debug</Configuration>");
        xml.AppendLine(@"    <Platform Condition="" '$(Platform)' == '' "">AnyCPU</Platform>");
        xml.AppendLine($@"    <Name>{projectName}</Name>");
        xml.AppendLine(@"    <SchemaVersion>2.0</SchemaVersion>");
        xml.AppendLine(@"    <ProjectVersion>4.1</ProjectVersion>");
        xml.AppendLine($@"    <ProjectGuid>{{{Guid.NewGuid()}}}</ProjectGuid>");
        xml.AppendLine(@"    <DSP>Microsoft.Data.Tools.Schema.Sql.Sql150DatabaseSchemaProvider</DSP>");
        xml.AppendLine(@"    <OutputType>Database</OutputType>");
        xml.AppendLine(@"    <RootPath />");
        xml.AppendLine($@"    <RootNamespace>{projectName}</RootNamespace>");
        xml.AppendLine($@"    <AssemblyName>{projectName}</AssemblyName>");
        xml.AppendLine(@"    <ModelCollation>1033,CI</ModelCollation>");
        xml.AppendLine(@"    <DefaultFileStructure>BySchemaAndSchemaType</DefaultFileStructure>");
        xml.AppendLine(@"    <DeployToDatabase>True</DeployToDatabase>");
        xml.AppendLine(@"    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>");
        xml.AppendLine(@"    <TargetLanguage>CS</TargetLanguage>");
        xml.AppendLine(@"    <AppDesignerFolder>Properties</AppDesignerFolder>");
        xml.AppendLine(@"    <SqlServerVerification>False</SqlServerVerification>");
        xml.AppendLine(@"    <IncludeCompositeObjects>True</IncludeCompositeObjects>");
        xml.AppendLine(@"    <TargetDatabaseSet>True</TargetDatabaseSet>");
        xml.AppendLine(@"    <DefaultCollation>SQL_Latin1_General_CP1_CI_AS</DefaultCollation>");
        xml.AppendLine(@"    <DefaultFilegroup>PRIMARY</DefaultFilegroup>");
        xml.AppendLine(@"    <TreatTSqlWarningsAsErrors>False</TreatTSqlWarningsAsErrors>");
        xml.AppendLine(@"    <SuppressTSqlWarnings>71502,71562</SuppressTSqlWarnings>");
        xml.AppendLine(@"  </PropertyGroup>");
        
        // Configuration properties
        xml.AppendLine(@"  <PropertyGroup Condition="" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' "">");
        xml.AppendLine(@"    <OutputPath>bin\Debug\</OutputPath>");
        xml.AppendLine(@"    <BuildScriptName>$(MSBuildProjectName).sql</BuildScriptName>");
        xml.AppendLine(@"    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>");
        xml.AppendLine(@"    <DebugSymbols>true</DebugSymbols>");
        xml.AppendLine(@"    <DebugType>full</DebugType>");
        xml.AppendLine(@"    <Optimize>false</Optimize>");
        xml.AppendLine(@"    <DefineDebug>true</DefineDebug>");
        xml.AppendLine(@"    <DefineTrace>true</DefineTrace>");
        xml.AppendLine(@"    <ErrorReport>prompt</ErrorReport>");
        xml.AppendLine(@"    <WarningLevel>4</WarningLevel>");
        xml.AppendLine(@"  </PropertyGroup>");
        
        xml.AppendLine(@"  <PropertyGroup Condition="" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' "">");
        xml.AppendLine(@"    <OutputPath>bin\Release\</OutputPath>");
        xml.AppendLine(@"    <BuildScriptName>$(MSBuildProjectName).sql</BuildScriptName>");
        xml.AppendLine(@"    <TreatWarningsAsErrors>False</TreatWarningsAsErrors>");
        xml.AppendLine(@"    <DebugType>pdbonly</DebugType>");
        xml.AppendLine(@"    <Optimize>true</Optimize>");
        xml.AppendLine(@"    <DefineDebug>false</DefineDebug>");
        xml.AppendLine(@"    <DefineTrace>false</DefineTrace>");
        xml.AppendLine(@"    <ErrorReport>prompt</ErrorReport>");
        xml.AppendLine(@"    <WarningLevel>4</WarningLevel>");
        xml.AppendLine(@"  </PropertyGroup>");
        
        // Import targets
        xml.AppendLine(@"  <PropertyGroup>");
        xml.AppendLine(@"    <VisualStudioVersion Condition=""'$(VisualStudioVersion)' == ''"">11.0</VisualStudioVersion>");
        xml.AppendLine(@"    <SSDTExists Condition=""Exists('$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\SSDT\Microsoft.Data.Tools.Schema.SqlTasks.targets')"">True</SSDTExists>");
        xml.AppendLine(@"    <VisualStudioVersion Condition=""'$(SSDTExists)' == ''"">11.0</VisualStudioVersion>");
        xml.AppendLine(@"  </PropertyGroup>");
        xml.AppendLine(@"  <Import Condition=""'$(SQLDBExtensionsRefPath)' != ''"" Project=""$(SQLDBExtensionsRefPath)\Microsoft.Data.Tools.Schema.SqlTasks.targets"" />");
        xml.AppendLine(@"  <Import Condition=""'$(SQLDBExtensionsRefPath)' == ''"" Project=""$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\SSDT\Microsoft.Data.Tools.Schema.SqlTasks.targets"" />");
        
        // Add folders
        xml.AppendLine(@"  <ItemGroup>");
        xml.AppendLine(@"    <Folder Include=""Properties"" />");
        
        var folders = files
            .Select(f => Path.GetDirectoryName(f)?.Replace('/', '\\'))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .OrderBy(d => d);
        
        foreach (var folder in folders)
        {
            xml.AppendLine($@"    <Folder Include=""{folder}"" />");
        }
        xml.AppendLine(@"  </ItemGroup>");
        
        // Add SQL files as Build items
        xml.AppendLine(@"  <ItemGroup>");
        foreach (var file in files)
        {
            var filePath = file.Replace('/', '\\');
            xml.AppendLine($@"    <Build Include=""{filePath}"" />");
        }
        xml.AppendLine(@"  </ItemGroup>");
        
        xml.AppendLine(@"</Project>");
        
        await File.WriteAllTextAsync(projectPath, xml.ToString());
    }
    
    /// <summary>
    /// Information about a SQL file for build ordering
    /// </summary>
    class SqlFileInfo
    {
        public string FullPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public int BuildOrder { get; set; }
    }
}