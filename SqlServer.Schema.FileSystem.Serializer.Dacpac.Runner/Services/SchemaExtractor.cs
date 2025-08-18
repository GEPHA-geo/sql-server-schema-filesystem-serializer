using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using Microsoft.SqlServer.Dac;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Core;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Constants;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Models;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Services;

/// <summary>
/// Handles schema extraction from databases and DACPACs
/// </summary>
public class SchemaExtractor
{
    readonly DacpacScriptParser _parser = new();
    readonly FileSystemManager _fileSystemManager = new();
    readonly GitDiffAnalyzer _gitAnalyzer = new();
    readonly GitWorktreeManager _gitManager = new();

    /// <summary>
    /// Extracts a DACPAC from a live database
    /// </summary>
    public async Task<Result<string>> ExtractFromDatabase(
        string connectionString,
        string outputPath,
        string dacpacName,
        string databaseName)
    {
        Console.WriteLine($"Extracting {dacpacName} DACPAC from database...");

        var dacServices = new DacServices(connectionString);
        var extractOptions = new DacExtractOptions
        {
            ExtractAllTableData = false,
            IgnoreExtendedProperties = false,
            IgnorePermissions = false,
            IgnoreUserLoginMappings = true
        };

        dacServices.Extract(
            outputPath,
            databaseName,
            dacpacName,
            new Version(1, 0),
            null,
            null,
            extractOptions);

        Console.WriteLine($"✓ {dacpacName} extracted: {Path.GetFileName(outputPath)}");
        return Result.Success(outputPath);
    }

    /// <summary>
    /// Extracts schema from a DACPAC to the filesystem
    /// </summary>
    public async Task<Result> ExtractToFileSystem(
        DacpacExtractionContext context,
        string sourceDacpacPath)
    {
        Console.WriteLine("Extracting source schema to filesystem...");

        // First clean the target directory
        CleanTargetDirectory(context.TargetOutputPath);

        // Generate deployment script from source DACPAC
        var sourceDacpac = DacPackage.Load(sourceDacpacPath);
        var dacServices = new DacServices(context.SourceConnection.ConnectionString);

        var deployOptions = new DacDeployOptions
        {
            CreateNewDatabase = true,
            IgnoreAuthorizer = true,
            IgnoreExtendedProperties = false,
            IgnorePermissions = false
        };

        var script = dacServices.GenerateDeployScript(
            sourceDacpac,
            context.SourceConnection.Database,
            deployOptions,
            null);

        // Remove AUTHORIZATION clauses from CREATE SCHEMA statements
        script = RemoveAuthorizationClauses(script);

        // Parse and extract to filesystem
        Console.WriteLine($"  Parsing and organizing scripts...");
        Console.WriteLine($"    Output path: {context.OutputPath}");
        Console.WriteLine($"    Target server: {context.TargetConnection.SanitizedServer}");
        Console.WriteLine($"    Target database: {context.TargetConnection.Database}");
        
        _parser.ParseAndOrganizeScripts(
            script,
            context.OutputPath,
            context.TargetConnection.SanitizedServer,
            context.TargetConnection.Database,
            context.SourceConnection.SanitizedServer,
            context.SourceConnection.SanitizedDatabase);

        // Verify files were created and normalize them
        var targetSchemaPath = Path.Combine(
            context.OutputPath,
            DacpacConstants.Directories.Servers,
            context.TargetConnection.SanitizedServer,
            context.TargetConnection.Database);
        
        Console.WriteLine($"  Checking for extracted files in: {targetSchemaPath}");
        if (Directory.Exists(targetSchemaPath))
        {
            var sqlFiles = Directory.GetFiles(targetSchemaPath, "*.sql", SearchOption.AllDirectories);
            Console.WriteLine($"  ✓ Found {sqlFiles.Length} SQL files after extraction");
            
            // Normalize all SQL files to remove BOM and fix line endings
            // This prevents false positive sp_updateextendedproperty differences
            Console.WriteLine("  Normalizing SQL files (removing BOM, converting to LF line endings)...");
            
            // Use parallel processing for better performance with large numbers of files
            var normalizedCount = 0;
            var parallelOptions = new ParallelOptions 
            { 
                MaxDegreeOfParallelism = Environment.ProcessorCount 
            };
            
            Parallel.ForEach(sqlFiles, parallelOptions, sqlFile =>
            {
                _fileSystemManager.NormalizeFile(sqlFile);
                var count = System.Threading.Interlocked.Increment(ref normalizedCount);
                if (count % 100 == 0)
                {
                    Console.WriteLine($"    Normalized {count}/{sqlFiles.Length} files...");
                }
            });
            
            Console.WriteLine($"  ✓ Normalized all {normalizedCount} SQL files");
            
            // Stage the extracted files to normalize line endings through Git
            if (_gitAnalyzer.IsGitRepository(context.OutputPath) && sqlFiles.Length > 0)
            {
                Console.WriteLine("  Staging extracted source files for Git...");
                var relativePath = Path.GetRelativePath(context.OutputPath, targetSchemaPath);
                Console.WriteLine($"    Relative path for staging: {relativePath}");
                
                var stageResult = await _gitManager.StageFilesForLineEndingNormalization(context.OutputPath, relativePath);
                if (stageResult.IsFailure)
                {
                    Console.WriteLine($"  ⚠ Warning: Could not stage files: {stageResult.Error}");
                    // Continue anyway - staging is optional
                }
                else
                {
                    Console.WriteLine("  ✓ Files staged for Git");
                }
            }
        }
        else
        {
            Console.WriteLine($"  ⚠ Directory does not exist: {targetSchemaPath}");
        }

        return Result.Success();
    }

    /// <summary>
    /// Cleans the target directory before extraction
    /// </summary>
    void CleanTargetDirectory(string targetPath)
    {
        Console.WriteLine("Cleaning target directory before extraction...");

        if (!Directory.Exists(targetPath))
            return;

        // Get all subdirectories except migrations and special directories
        var subdirs = Directory.GetDirectories(targetPath)
            .Where(d =>
            {
                var dirName = Path.GetFileName(d);
                return !dirName.Equals(DacpacConstants.Directories.Migrations, StringComparison.OrdinalIgnoreCase) &&
                       !dirName.Equals(DacpacConstants.Directories.ReverseMigrations,
                           StringComparison.OrdinalIgnoreCase) &&
                       !dirName.Equals(DacpacConstants.Directories.ChangeManifests, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        // Delete each subdirectory
        foreach (var dir in subdirs)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                Console.WriteLine($"  Cleaned: {Path.GetFileName(dir)}/");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ Could not clean {Path.GetFileName(dir)}: {ex.Message}");
            }
        }

        // Delete all files in the root except .dacpac-exclusions.json and DACPAC files
        foreach (var file in Directory.GetFiles(targetPath))
        {
            var fileName = Path.GetFileName(file);
            if (!fileName.Equals(DacpacConstants.Files.ExclusionsFile, StringComparison.OrdinalIgnoreCase) &&
                !fileName.EndsWith(DacpacConstants.Files.DacpacExtension, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠ Could not delete {fileName}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Removes AUTHORIZATION clauses from CREATE SCHEMA statements
    /// </summary>
    string RemoveAuthorizationClauses(string script)
    {
        if (!script.Contains("AUTHORIZATION"))
            return script;

        var schemaAuthPattern = @"(CREATE\s+SCHEMA\s+\[[^\]]+\])\s+AUTHORIZATION\s+\[[^\]]+\]";
        return Regex.Replace(
            script,
            schemaAuthPattern,
            "$1",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
    }
}