using System.Diagnostics;
using System.Text;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using SqlServer.Schema.Exclusion.Manager.Core.Models;
using SqlServer.Schema.Exclusion.Manager.Core.Services;

namespace SqlServer.Schema.Migration.Generator;

/// <summary>
/// Generates migrations by comparing DACPACs built from file system at different points
/// </summary>
public class DacpacMigrationGenerator
{
    readonly string _tempBasePath;
    readonly ScmpToDeployOptions _optionsMapper = new();

    public DacpacMigrationGenerator()
    {
        _tempBasePath = Path.Combine(Path.GetTempPath(), "DacpacMigrations");
        Directory.CreateDirectory(_tempBasePath);
    }

    /// <summary>
    /// Generates migration by comparing committed vs uncommitted state using git worktrees
    /// </summary>
    public async Task<MigrationGenerationResult> GenerateMigrationAsync(
        string outputPath,
        string targetServer, 
        string targetDatabase,
        string migrationsPath,
        SchemaComparison? scmpComparison = null,
        string? actor = null,
        bool validateMigration = true,
        string? connectionString = null)
    {
        var result = new MigrationGenerationResult();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var tempDir = Path.Combine(_tempBasePath, $"migration_{timestamp}");
        
        // Worktree path for committed state
        string? committedWorktreePath = null;
        
        try
        {
            Directory.CreateDirectory(tempDir);
            
            // Path to the database schema files (relative to repo root)
            var schemaRelativePath = Path.Combine("servers", targetServer, targetDatabase);
            
            Console.WriteLine("=== Generating DACPAC-based Migration ===");
            Console.WriteLine("Comparing committed state vs uncommitted changes...");
            
            // Check if we have any commits
            var hasCommits = await HasAnyCommit(outputPath);
            
            if (hasCommits)
            {
                // Step 1: Create worktree for last committed state (source)
                Console.WriteLine("Creating worktree for last committed state (HEAD)...");
                committedWorktreePath = Path.Combine(tempDir, "committed-worktree");
                await CreateWorktree(outputPath, committedWorktreePath, "HEAD");
                
                // Build source DACPAC from committed state
                var committedSchemaPath = Path.Combine(committedWorktreePath, schemaRelativePath);
                string sourceDacpacPath;
                
                if (Directory.Exists(committedSchemaPath))
                {
                    Console.WriteLine("Building source DACPAC from committed state...");
                    sourceDacpacPath = await BuildDacpacFromFileSystem(
                        committedSchemaPath,
                        Path.Combine(tempDir, "source-build"),
                        "SourceDatabase");
                    
                    if (string.IsNullOrEmpty(sourceDacpacPath))
                    {
                        // If build fails, use empty DACPAC
                        Console.WriteLine("Failed to build from committed state, using empty source...");
                        sourceDacpacPath = await CreateEmptyDacpac(
                            Path.Combine(tempDir, "source-build"),
                            "SourceDatabase");
                    }
                }
                else
                {
                    // Schema didn't exist in committed state
                    Console.WriteLine("Schema didn't exist in committed state, using empty source...");
                    sourceDacpacPath = await CreateEmptyDacpac(
                        Path.Combine(tempDir, "source-build"),
                        "SourceDatabase");
                }
                
                // Step 2: Build target DACPAC from current working directory (uncommitted state)
                var currentSchemaPath = Path.Combine(outputPath, schemaRelativePath);
                Console.WriteLine("Building target DACPAC from current uncommitted state...");
                var targetDacpacPath = await BuildDacpacFromFileSystem(
                    currentSchemaPath,
                    Path.Combine(tempDir, "target-build"),
                    "TargetDatabase");
                
                if (string.IsNullOrEmpty(targetDacpacPath))
                {
                    result.Success = false;
                    result.Error = "Failed to build target DACPAC from current state";
                    return result;
                }
                
                // Generate migration
                result = await GenerateMigrationFromDacpacs(
                    sourceDacpacPath,
                    targetDacpacPath,
                    tempDir,
                    migrationsPath,
                    scmpComparison,
                    actor,
                    timestamp);
            }
            else
            {
                // No commits yet - compare empty to current
                Console.WriteLine("No commits found, creating initial migration...");
                
                var sourceDacpacPath = await CreateEmptyDacpac(
                    Path.Combine(tempDir, "source-build"),
                    "SourceDatabase");
                
                var currentSchemaPath = Path.Combine(outputPath, schemaRelativePath);
                var targetDacpacPath = await BuildDacpacFromFileSystem(
                    currentSchemaPath,
                    Path.Combine(tempDir, "target-build"),
                    "TargetDatabase");
                
                if (string.IsNullOrEmpty(targetDacpacPath))
                {
                    result.Success = false;
                    result.Error = "Failed to build target DACPAC";
                    return result;
                }
                
                result = await GenerateMigrationFromDacpacs(
                    sourceDacpacPath,
                    targetDacpacPath,
                    tempDir,
                    migrationsPath,
                    scmpComparison,
                    actor,
                    timestamp);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = $"Migration generation failed: {ex.Message}";
            return result;
        }
        finally
        {
            // Clean up worktree
            if (!string.IsNullOrEmpty(committedWorktreePath))
            {
                try
                {
                    await RemoveWorktree(outputPath, committedWorktreePath);
                }
                catch { /* Ignore cleanup errors */ }
            }
            
            // Clean up temp directory
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }
    
    /// <summary>
    /// Generates migration scripts from two DACPACs
    /// </summary>
    async Task<MigrationGenerationResult> GenerateMigrationFromDacpacs(
        string sourceDacpacPath,
        string targetDacpacPath,
        string tempDir,
        string migrationsPath,
        SchemaComparison? scmpComparison,
        string? actor,
        string timestamp)
    {
        var result = new MigrationGenerationResult();
        
        try
        {
            // Compare DACPACs using SqlPackage
            Console.WriteLine("Comparing DACPACs to generate migration script...");
            var migrationScript = await CompareDacpacs(
                sourceDacpacPath,
                targetDacpacPath,
                tempDir,
                scmpComparison);
            
            if (string.IsNullOrEmpty(migrationScript))
            {
                result.Success = false;
                result.Error = "No changes detected between source and target";
                result.HasChanges = false;
                return result;
            }
            
            // Generate reverse migration
            Console.WriteLine("Generating reverse migration script...");
            var reverseMigrationScript = await CompareDacpacs(
                targetDacpacPath,
                sourceDacpacPath,
                tempDir,
                scmpComparison,
                isReverse: true);
            
            // Save migration files
            var description = ExtractDescription(migrationScript);
            var sanitizedActor = SanitizeForFilename(actor ?? "system");
            var filename = $"_{timestamp}_{sanitizedActor}_{description}.sql";
            
            var migrationFilePath = Path.Combine(migrationsPath, filename);
            await File.WriteAllTextAsync(migrationFilePath, migrationScript);
            Console.WriteLine($"✓ Generated migration: {filename}");
            
            // Save reverse migration
            var reverseMigrationsPath = Path.Combine(Path.GetDirectoryName(migrationsPath)!, "z_migrations_reverse");
            Directory.CreateDirectory(reverseMigrationsPath);
            
            var reverseFilename = $"reverse_{filename}";
            var reverseMigrationPath = Path.Combine(reverseMigrationsPath, reverseFilename);
            await File.WriteAllTextAsync(reverseMigrationPath, reverseMigrationScript);
            Console.WriteLine($"✓ Generated reverse migration: {reverseFilename}");
            
            result.Success = true;
            result.MigrationPath = migrationFilePath;
            result.ReverseMigrationPath = reverseMigrationPath;
            result.HasChanges = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }
        
        return result;
    }
    
    /// <summary>
    /// Creates a git worktree at the specified commit
    /// </summary>
    async Task CreateWorktree(string repoPath, string worktreePath, string commit)
    {
        var command = $"worktree add \"{worktreePath}\" {commit}";
        await ExecuteGitCommand(command, repoPath);
    }
    
    /// <summary>
    /// Removes a git worktree
    /// </summary>
    async Task RemoveWorktree(string repoPath, string worktreePath)
    {
        var command = $"worktree remove \"{worktreePath}\" --force";
        await ExecuteGitCommand(command, repoPath);
    }
    
    /// <summary>
    /// Checks if repository has any commits
    /// </summary>
    async Task<bool> HasAnyCommit(string repoPath)
    {
        try
        {
            var result = await ExecuteGitCommand("rev-parse HEAD", repoPath);
            return !string.IsNullOrEmpty(result);
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Builds a DACPAC from the file system
    /// </summary>
    async Task<string> BuildDacpacFromFileSystem(string schemaPath, string outputDir, string projectName)
    {
        try
        {
            Directory.CreateDirectory(outputDir);
            
            // Create SQL project from file system
            var projectBuilder = new SqlProjectBuilder();
            var projectPath = await projectBuilder.CreateSqlProject(schemaPath, outputDir, projectName);
            
            if (string.IsNullOrEmpty(projectPath))
                return string.Empty;
            
            // Build the project to generate DACPAC
            var dacpacPath = await BuildSqlProject(projectPath);
            return dacpacPath ?? string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error building DACPAC: {ex.Message}");
            return string.Empty;
        }
    }
    
    /// <summary>
    /// Creates an empty DACPAC for initial migration
    /// </summary>
    async Task<string> CreateEmptyDacpac(string outputDir, string projectName)
    {
        return await Task.Run(() =>
        {
            Directory.CreateDirectory(outputDir);
            
            var dacpacPath = Path.Combine(outputDir, $"{projectName}.dacpac");
            
            // Create empty DACPAC using DacPackageExtensions
            var model = new TSqlModel(SqlServerVersion.Sql150, new TSqlModelOptions());
            
            DacPackageExtensions.BuildPackage(
                dacpacPath,
                model,
                new PackageMetadata 
                { 
                    Name = projectName,
                    Version = "1.0.0.0"
                });
            
            return dacpacPath;
        });
    }
    
    /// <summary>
    /// Compares two DACPACs using SqlPackage and returns the migration script
    /// </summary>
    async Task<string> CompareDacpacs(
        string sourceDacpac,
        string targetDacpac,
        string outputDir,
        SchemaComparison? scmpComparison,
        bool isReverse = false)
    {
        var scriptPath = Path.Combine(outputDir, isReverse ? "reverse_migration.sql" : "migration.sql");
        
        try
        {
            // Build SqlPackage arguments
            var arguments = new StringBuilder();
            arguments.Append($"/Action:Script ");
            arguments.Append($"/SourceFile:\"{sourceDacpac}\" ");
            arguments.Append($"/TargetFile:\"{targetDacpac}\" ");
            arguments.Append($"/OutputPath:\"{scriptPath}\" ");
            
            // Apply SCMP options if provided
            if (scmpComparison != null)
            {
                var deployOptions = _optionsMapper.MapOptions(scmpComparison);
                
                // Add key options as SqlPackage parameters
                arguments.Append($"/p:DropObjectsNotInSource={deployOptions.DropObjectsNotInSource} ");
                arguments.Append($"/p:BlockOnPossibleDataLoss={deployOptions.BlockOnPossibleDataLoss} ");
                arguments.Append($"/p:IgnorePermissions={deployOptions.IgnorePermissions} ");
                arguments.Append($"/p:IgnoreRoleMembership={deployOptions.IgnoreRoleMembership} ");
                arguments.Append($"/p:IgnoreUserSettingsObjects={deployOptions.IgnoreUserSettingsObjects} ");
                arguments.Append($"/p:IgnoreLoginSids={deployOptions.IgnoreLoginSids} ");
                arguments.Append($"/p:IgnoreExtendedProperties={deployOptions.IgnoreExtendedProperties} ");
                arguments.Append($"/p:IgnoreWhitespace={deployOptions.IgnoreWhitespace} ");
                arguments.Append($"/p:IgnoreKeywordCasing={deployOptions.IgnoreKeywordCasing} ");
                arguments.Append($"/p:IgnoreSemicolonBetweenStatements={deployOptions.IgnoreSemicolonBetweenStatements} ");
                arguments.Append($"/p:IgnoreComments={deployOptions.IgnoreComments} ");
                arguments.Append($"/p:GenerateSmartDefaults={deployOptions.GenerateSmartDefaults} ");
                arguments.Append($"/p:IncludeCompositeObjects={deployOptions.IncludeCompositeObjects} ");
                arguments.Append($"/p:IncludeTransactionalScripts={deployOptions.IncludeTransactionalScripts} ");
            }
            else
            {
                // Default conservative options
                arguments.Append("/p:DropObjectsNotInSource=false ");
                arguments.Append("/p:BlockOnPossibleDataLoss=true ");
                arguments.Append("/p:IgnorePermissions=true ");
                arguments.Append("/p:IgnoreRoleMembership=true ");
                arguments.Append("/p:IgnoreUserSettingsObjects=true ");
                arguments.Append("/p:IgnoreLoginSids=true ");
            }
            
            // Find and execute SqlPackage
            var sqlPackagePath = FindSqlPackage();
            if (string.IsNullOrEmpty(sqlPackagePath))
            {
                throw new FileNotFoundException("SqlPackage.exe not found. Please install SQL Server Data Tools.");
            }
            
            var processInfo = new ProcessStartInfo
            {
                FileName = sqlPackagePath,
                Arguments = arguments.ToString(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = outputDir
            };
            
            using var process = Process.Start(processInfo);
            if (process == null)
                throw new InvalidOperationException("Failed to start SqlPackage process");
            
            var output = await process.StandardOutput.ReadToEndAsync();
            var errors = await process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0)
            {
                Console.WriteLine($"SqlPackage error: {errors}");
                if (!string.IsNullOrEmpty(output))
                    Console.WriteLine($"SqlPackage output: {output}");
                return string.Empty;
            }
            
            // Read and return the generated script
            if (File.Exists(scriptPath))
            {
                var script = await File.ReadAllTextAsync(scriptPath);
                
                // Check if script has actual changes
                if (string.IsNullOrWhiteSpace(script) || 
                    script.Contains("No schema differences detected") ||
                    !ContainsSchemaChanges(script))
                {
                    return string.Empty;
                }
                
                return script;
            }
            
            return string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error comparing DACPACs: {ex.Message}");
            return string.Empty;
        }
    }
    
    /// <summary>
    /// Builds a SQL project using DacServices API
    /// </summary>
    async Task<string?> BuildSqlProject(string projectPath)
    {
        return await Task.Run(() =>
        {
            var projectDir = Path.GetDirectoryName(projectPath)!;
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            
            try
            {
                // Load all SQL files from project
                var sqlFiles = Directory.GetFiles(projectDir, "*.sql", SearchOption.AllDirectories)
                    .OrderBy(f => GetSqlFileOrder(f))
                    .ToList();
                
                if (!sqlFiles.Any())
                {
                    Console.WriteLine("No SQL files found in project");
                    return null;
                }
                
                // Create TSqlModel and add all SQL files
                var model = new TSqlModel(SqlServerVersion.Sql150, new TSqlModelOptions());
                
                foreach (var sqlFile in sqlFiles)
                {
                    try
                    {
                        var content = File.ReadAllText(sqlFile);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            model.AddObjects(content);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Warning: Could not add {Path.GetFileName(sqlFile)}: {ex.Message}");
                    }
                }
                
                // Build DACPAC
                var dacpacPath = Path.Combine(projectDir, $"{projectName}.dacpac");
                
                DacPackageExtensions.BuildPackage(
                    dacpacPath,
                    model,
                    new PackageMetadata 
                    { 
                        Name = projectName,
                        Version = "1.0.0.0"
                    });
                
                Console.WriteLine($"  ✓ Built DACPAC: {Path.GetFileName(dacpacPath)}");
                return dacpacPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error building SQL project: {ex.Message}");
                return null;
            }
        });
    }
    
    /// <summary>
    /// Determines the order for processing SQL files
    /// </summary>
    int GetSqlFileOrder(string filePath)
    {
        var fileName = Path.GetFileName(filePath).ToLower();
        
        // Process in dependency order
        if (fileName.Contains("schema")) return 1;
        if (fileName.Contains("type")) return 2;
        if (fileName.Contains("table") || fileName.StartsWith("tbl_")) return 3;
        if (fileName.Contains("function") || fileName.StartsWith("fn_")) return 4;
        if (fileName.Contains("view") || fileName.StartsWith("vw_")) return 5;
        if (fileName.Contains("procedure") || fileName.StartsWith("sp_")) return 6;
        if (fileName.Contains("trigger") || fileName.StartsWith("tr_")) return 7;
        if (fileName.Contains("index") || fileName.StartsWith("idx_")) return 8;
        if (fileName.Contains("constraint") || fileName.StartsWith("fk_") || fileName.StartsWith("ck_")) return 9;
        
        return 10;
    }
    
    /// <summary>
    /// Finds SqlPackage.exe on the system
    /// </summary>
    string? FindSqlPackage()
    {
        // First check if sqlpackage is in PATH (common in CI/CD)
        var sqlPackageInPath = ExecuteCommand("where", "sqlpackage").Result;
        if (!string.IsNullOrEmpty(sqlPackageInPath))
        {
            var lines = sqlPackageInPath.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0 && File.Exists(lines[0].Trim()))
                return lines[0].Trim();
        }
        
        // Check common installation paths
        var possiblePaths = new[]
        {
            @"C:\Program Files\Microsoft SQL Server\160\DAC\bin\SqlPackage.exe",
            @"C:\Program Files\Microsoft SQL Server\150\DAC\bin\SqlPackage.exe",
            @"C:\Program Files (x86)\Microsoft SQL Server\160\DAC\bin\SqlPackage.exe",
            @"C:\Program Files (x86)\Microsoft SQL Server\150\DAC\bin\SqlPackage.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe"
        };
        
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }
        
        // Check if running in WSL/Linux
        if (Environment.OSVersion.Platform == PlatformID.Unix)
        {
            var result = ExecuteCommand("which", "sqlpackage").Result;
            if (!string.IsNullOrEmpty(result))
                return result.Trim();
        }
        
        return null;
    }
    
    /// <summary>
    /// Executes a git command and returns the output
    /// </summary>
    async Task<string> ExecuteGitCommand(string command, string workingDirectory)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = command.Replace("git ", ""),
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(processInfo);
            if (process == null)
                return string.Empty;
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            return process.ExitCode == 0 ? output.Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
    
    /// <summary>
    /// Executes a command and returns output
    /// </summary>
    async Task<string> ExecuteCommand(string command, string arguments)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(processInfo);
            if (process == null)
                return string.Empty;
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }
    
    /// <summary>
    /// Checks if a script contains actual schema changes
    /// </summary>
    bool ContainsSchemaChanges(string script)
    {
        // Check for actual DDL statements
        var ddlKeywords = new[]
        {
            "CREATE TABLE", "ALTER TABLE", "DROP TABLE",
            "CREATE VIEW", "ALTER VIEW", "DROP VIEW",
            "CREATE PROCEDURE", "ALTER PROCEDURE", "DROP PROCEDURE",
            "CREATE FUNCTION", "ALTER FUNCTION", "DROP FUNCTION",
            "CREATE INDEX", "DROP INDEX",
            "CREATE TRIGGER", "ALTER TRIGGER", "DROP TRIGGER",
            "CREATE SCHEMA", "DROP SCHEMA",
            "ADD CONSTRAINT", "DROP CONSTRAINT"
        };
        
        var upperScript = script.ToUpper();
        return ddlKeywords.Any(keyword => upperScript.Contains(keyword));
    }
    
    /// <summary>
    /// Extracts a description from the migration script
    /// </summary>
    string ExtractDescription(string migrationScript)
    {
        var lines = migrationScript.Split('\n').Take(100);
        
        var tables = 0;
        var views = 0;
        var procedures = 0;
        var functions = 0;
        var other = 0;
        
        foreach (var line in lines)
        {
            var upper = line.ToUpper();
            if (upper.Contains("TABLE")) tables++;
            else if (upper.Contains("VIEW")) views++;
            else if (upper.Contains("PROCEDURE")) procedures++;
            else if (upper.Contains("FUNCTION")) functions++;
            else if (upper.Contains("CREATE") || upper.Contains("ALTER") || upper.Contains("DROP")) other++;
        }
        
        var parts = new List<string>();
        if (tables > 0) parts.Add($"{tables}_tables");
        if (views > 0) parts.Add($"{views}_views");
        if (procedures > 0) parts.Add($"{procedures}_procedures");
        if (functions > 0) parts.Add($"{functions}_functions");
        if (other > 0) parts.Add($"{other}_other");
        
        return parts.Any() ? string.Join("_", parts) : "schema_changes";
    }
    
    /// <summary>
    /// Sanitizes a string for use in filenames
    /// </summary>
    string SanitizeForFilename(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder();
        
        foreach (var c in input)
        {
            if (!invalid.Contains(c))
                sanitized.Append(c);
            else
                sanitized.Append('_');
        }
        
        return sanitized.ToString();
    }
}

/// <summary>
/// Result of migration generation
/// </summary>
public class MigrationGenerationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? MigrationPath { get; set; }
    public string? ReverseMigrationPath { get; set; }
    public bool HasChanges { get; set; }
}