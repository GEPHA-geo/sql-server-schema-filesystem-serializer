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

        // First, check for schema directories and generate CREATE SCHEMA statements
        var schemasDir = Path.Combine(schemaPath, "schemas");
        if (Directory.Exists(schemasDir))
        {
            var schemaDirs = Directory.GetDirectories(schemasDir);
            foreach (var schemaDir in schemaDirs)
            {
                var schemaName = Path.GetFileName(schemaDir);
                // Skip default 'dbo' schema as it exists by default
                if (!schemaName.Equals("dbo", StringComparison.OrdinalIgnoreCase))
                {
                    // Create a virtual SQL file for schema creation
                    var schemaFile = new SqlFileInfo
                    {
                        FullPath = null, // This will be generated content
                        RelativePath = $"schemas/{schemaName}.sql",
                        BuildOrder = 0, // Schemas must be created first
                        Content = $"CREATE SCHEMA [{schemaName}];\nGO\n",
                        IsGenerated = true
                    };
                    files.Add(schemaFile);
                }
            }
        }

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
                BuildOrder = GetBuildOrder(relativePath),
                IsGenerated = false
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

                string content;
                if (file.IsGenerated && !string.IsNullOrEmpty(file.Content))
                {
                    // Use the generated content directly
                    content = file.Content;
                }
                else if (!string.IsNullOrEmpty(file.FullPath))
                {
                    // Read from actual file
                    content = await File.ReadAllTextAsync(file.FullPath);
                    // Clean up content for DACPAC compilation
                    content = CleanSqlContent(content);
                }
                else
                {
                    // Skip if no content source
                    continue;
                }

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
        // Create minimal sqlproj content with wildcard pattern for SQL files
        var projectContent = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<--bad--Project DefaultTargets=""Build"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"" ToolsVersion=""4.0"">
  <PropertyGroup>
    <Name>{projectName}</Name>
    <ProjectGuid>{{{Guid.NewGuid()}}}</ProjectGuid>
    <DSP>Microsoft.Data.Tools.Schema.Sql.Sql150DatabaseSchemaProvider</DSP>
    <OutputType>Database</OutputType>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
    <TreatTSqlWarningsAsErrors>False</TreatTSqlWarningsAsErrors>
    <SuppressTSqlWarnings>71502,71562,71561,71501,71558</SuppressTSqlWarnings>
    <SkipModelValidation>true</SkipModelValidation>
  </PropertyGroup>
  
  <ItemGroup>
    <Build Include=""**\*.sql"" />
  </ItemGroup>
</Project>";

        await File.WriteAllTextAsync(projectPath, projectContent);
    }

    /// <summary>
    /// Information about a SQL file for build ordering
    /// </summary>
    class SqlFileInfo
    {
        public string? FullPath { get; init; }
        public string RelativePath { get; init; } = string.Empty;
        public int BuildOrder { get; init; }
        public string? Content { get; init; } // For generated content like schemas
        public bool IsGenerated { get; init; } // Indicates if this is generated content
    }
}