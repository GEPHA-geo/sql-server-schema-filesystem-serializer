using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Microsoft.SqlServer.Dac.Compare;
using SqlServer.Schema.Exclusion.Manager.Core.Models;
using SqlServer.Schema.Exclusion.Manager.Core.Services;

namespace SqlServer.Schema.Migration.Generator;

/// <summary>
/// Generates migrations by comparing DACPACs built from file system at different points
/// </summary>
public class DacpacMigrationGenerator
{
    const string SQL_UNRESOLVED_REFERENCE_ERROR = "SQL71561";
    
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
        Exclusion.Manager.Core.Models.SchemaComparison? scmpComparison = null,
        string? actor = null,
        string? referenceDacpacPath = null,
        bool validateMigration = true,
        string? connectionString = null)
    {
        var result = new MigrationGenerationResult();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        // Create build directory in temp folder to avoid polluting the repository
        var buildDir = Path.Combine(Path.GetTempPath(), "DacpacMigrations", $"build_{timestamp}");
        
        // Clean up old temp directories if they exist
        var tempRoot = Path.Combine(Path.GetTempPath(), "DacpacMigrations");
        if (Directory.Exists(tempRoot))
        {
            try
            {
                // Delete old build directories (older than 1 hour)
                var oldDirs = Directory.GetDirectories(tempRoot, "build_*")
                    .Where(d => Directory.GetCreationTimeUtc(d) < DateTime.UtcNow.AddHours(-1));
                foreach (var oldDir in oldDirs)
                {
                    try { Directory.Delete(oldDir, recursive: true); } catch { /* Ignore */ }
                }
            }
            catch { /* Ignore cleanup errors */ }
        }
        
        Directory.CreateDirectory(buildDir);
        var tempDir = buildDir;
        
        // Worktree path for committed state
        string? committedWorktreePath = null;
        
        try
        {
            Directory.CreateDirectory(tempDir);
            
            // Path to the database schema files (relative to repo root)
            var schemaRelativePath = Path.Combine("servers", targetServer, targetDatabase);
            
            Console.WriteLine("=== Generating DACPAC-based Migration ===");
            
            // If connection string is provided, compare database with file system
            if (!string.IsNullOrEmpty(connectionString))
            {
                Console.WriteLine("Comparing database schema with file system...");
                
                // Extract DACPAC from database as source
                var sourceDacpacPath = await ExtractDacpacFromDatabase(
                    connectionString,
                    Path.Combine(tempDir, "database-extract"),
                    "DatabaseSchema");
                
                if (string.IsNullOrEmpty(sourceDacpacPath))
                {
                    result.Success = false;
                    result.Error = "Failed to extract schema from database";
                    return result;
                }
                
                // Build DACPAC from file system as target
                var currentSchemaPath = Path.Combine(outputPath, schemaRelativePath);
                Console.WriteLine("Building target DACPAC from file system...");
                // Look for a reference DACPAC in the schema directory
                string? referenceDacpac = null;
                var dacpacFiles = Directory.GetFiles(currentSchemaPath, "*.dacpac", SearchOption.TopDirectoryOnly);
                if (dacpacFiles.Any())
                {
                    referenceDacpac = dacpacFiles.First();
                    Console.WriteLine($"  Found reference DACPAC: {Path.GetFileName(referenceDacpac)}");
                }
                
                var targetDacpacPath = await BuildDacpacFromFileSystem(
                    currentSchemaPath,
                    Path.Combine(tempDir, "filesystem-build"),
                    "FileSystemSchema",
                    referenceDacpac);
                
                if (string.IsNullOrEmpty(targetDacpacPath))
                {
                    // Build failed - this should not happen as we now build with errors
                    result.Success = false;
                    result.Error = "Failed to build DACPAC from file system";
                    return result;
                }
                
                // Generate migration from database to file system
                result = await GenerateMigrationFromDacpacs(
                    sourceDacpacPath,
                    targetDacpacPath,
                    tempDir,
                    migrationsPath,
                    scmpComparison,
                    actor,
                    timestamp);
                
                return result;
            }
            
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
                    // Look for a reference DACPAC in the committed schema directory
                    string? committedReferenceDacpac = null;
                    if (Directory.Exists(committedSchemaPath))
                    {
                        var committedDacpacFiles = Directory.GetFiles(committedSchemaPath, "*.dacpac", SearchOption.TopDirectoryOnly);
                        if (committedDacpacFiles.Any())
                        {
                            committedReferenceDacpac = committedDacpacFiles.First();
                            Console.WriteLine($"  Found reference DACPAC in committed state: {Path.GetFileName(committedReferenceDacpac)}");
                        }
                    }
                    sourceDacpacPath = await BuildDacpacFromFileSystem(
                        committedSchemaPath,
                        Path.Combine(tempDir, "source-build"),
                        "SourceDatabase",
                        committedReferenceDacpac);
                    
                    if (string.IsNullOrEmpty(sourceDacpacPath))
                    {
                        // Build failed - this should not happen as we now build with errors
                        result.Success = false;
                        result.Error = "Failed to build DACPAC from committed state";
                        return result;
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
                // Look for a reference DACPAC in the current schema directory
                string? currentReferenceDacpac = null;
                var currentDacpacFiles = Directory.GetFiles(currentSchemaPath, "*.dacpac", SearchOption.TopDirectoryOnly);
                if (currentDacpacFiles.Any())
                {
                    currentReferenceDacpac = currentDacpacFiles.First();
                    Console.WriteLine($"  Found reference DACPAC: {Path.GetFileName(currentReferenceDacpac)}");
                }
                var targetDacpacPath = await BuildDacpacFromFileSystem(
                    currentSchemaPath,
                    Path.Combine(tempDir, "target-build"),
                    "TargetDatabase",
                    currentReferenceDacpac);
                
                if (string.IsNullOrEmpty(targetDacpacPath))
                {
                    // Build failed - this should not happen as we now build with errors
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
                    // Build failed - this should not happen as we now build with errors
                    result.Success = false;
                    result.Error = "Failed to build target DACPAC from current state";
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
            
            // Clean up temp build directory
            try
            {
                if (Directory.Exists(tempDir))
                    // Temporarily disabled for debugging
                    Console.WriteLine($"Temp files preserved at: {tempDir}");
                    // Directory.Delete(tempDir, recursive: true);
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
        Exclusion.Manager.Core.Models.SchemaComparison? scmpComparison,
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
            
            // Save migration files
            var description = ExtractDescription(migrationScript);
            var sanitizedActor = SanitizeForFilename(actor ?? "system");
            var filename = $"_{timestamp}_{sanitizedActor}_{description}.sql";
            
            // Create migration directory structure with the new folder-based approach
            var migrationDirName = Path.GetFileNameWithoutExtension(filename);
            var migrationDir = Path.Combine(migrationsPath, migrationDirName);
            Directory.CreateDirectory(migrationDir);
            
            // Create a temporary file for the splitter to process
            var tempMigrationPath = Path.Combine(tempDir, "temp_migration.sql");
            await File.WriteAllTextAsync(tempMigrationPath, migrationScript);
            Console.WriteLine($"✓ Generated migration: {migrationDirName}");
            
            // Split the migration script into organized segments
            Console.WriteLine("Splitting migration into organized segments...");
            var splitter = new MigrationScriptSplitter();
            await splitter.SplitMigrationScript(tempMigrationPath, migrationDir);
            
            // Count the number of segments created (now in the main directory)
            var segmentFiles = Directory.GetFiles(migrationDir, "*.sql")
                .Where(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"^\d{3}_"))
                .ToArray();
            var segmentCount = segmentFiles.Length;
            if (segmentCount > 0)
            {
                Console.WriteLine($"✓ Split migration into {segmentCount} segments");
            }
            
            result.Success = true;
            result.MigrationPath = migrationDir;  // Return directory path instead of file path
            result.ReverseMigrationPath = null;  // No longer generating reverse migrations
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
    public async Task<string> BuildDacpacFromFileSystem(string schemaPath, string outputDir, string projectName, string? referenceDacpacPath = null)
    {
        try
        {
            if (!Directory.Exists(schemaPath))
            {
                Console.WriteLine($"  Schema path does not exist: {schemaPath}");
                return string.Empty;
            }
            
            Directory.CreateDirectory(outputDir);
            
            // Copy SQL files to temp location (excluding migrations and other non-schema files)
            Console.WriteLine($"  Copying SQL files from {schemaPath} to build directory...");
            var projectDir = Path.Combine(outputDir, projectName);
            Directory.CreateDirectory(projectDir);
            
            // Copy only schema files, excluding migrations and change manifests
            CopySchemaFiles(schemaPath, projectDir);
            
            // Copy reference DACPAC to build directory if provided
            string? localReferenceDacpacPath = null;
            if (!string.IsNullOrEmpty(referenceDacpacPath) && File.Exists(referenceDacpacPath))
            {
                var referenceDacpacFileName = Path.GetFileName(referenceDacpacPath);
                localReferenceDacpacPath = Path.Combine(projectDir, referenceDacpacFileName);
                File.Copy(referenceDacpacPath, localReferenceDacpacPath, overwrite: true);
                Console.WriteLine($"  Copied reference DACPAC to build directory: {referenceDacpacFileName}");
            }
            
            // Create a simple SDK-style .sqlproj file with optional reference DACPAC
            var projectPath = Path.Combine(projectDir, $"{projectName}.sqlproj");
            Console.WriteLine($"  Creating SQL project: {projectPath}");
            if (!string.IsNullOrEmpty(localReferenceDacpacPath))
            {
                Console.WriteLine($"  Adding reference DACPAC: {Path.GetFileName(localReferenceDacpacPath)}");
            }
            CreateSdkStyleSqlProject(projectPath, projectName, localReferenceDacpacPath);
            
            // Build the project to generate DACPAC - pass the original schema path for exclusion file
            var dacpacPath = await BuildSqlProject(projectPath, schemaPath);
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
    /// Extracts a DACPAC from a live database
    /// </summary>
    async Task<string> ExtractDacpacFromDatabase(string connectionString, string outputDir, string dacpacName)
    {
        return await Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(outputDir);
                var dacpacPath = Path.Combine(outputDir, $"{dacpacName}.dacpac");
                
                Console.WriteLine($"  Extracting schema from database...");
                
                // Use DacServices to extract DACPAC from database
                var dacServices = new DacServices(connectionString);
                
                // Subscribe to messages for progress
                dacServices.Message += (sender, e) =>
                {
                    if (e.Message.MessageType == DacMessageType.Error)
                        Console.WriteLine($"    Error: {e.Message.Message}");
                };
                
                // Extract the DACPAC from the database
                dacServices.Extract(
                    dacpacPath,
                    "DatabaseSchema",  // database name in DACPAC
                    "Database Schema",  // application name
                    new Version(1, 0, 0, 0));
                
                Console.WriteLine($"  ✓ Extracted DACPAC from database: {Path.GetFileName(dacpacPath)}");
                return dacpacPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error extracting DACPAC from database: {ex.Message}");
                return string.Empty;
            }
        });
    }
    
    /// <summary>
    /// Compares two DACPACs using SqlPackage and returns the migration script
    /// </summary>
    async Task<string> CompareDacpacs(
        string sourceDacpac,
        string targetDacpac,
        string outputDir,
        Exclusion.Manager.Core.Models.SchemaComparison? scmpComparison)
    {
        var scriptPath = Path.Combine(outputDir, "migration.sql");
        
        try
        {
            Console.WriteLine($"Loading source DACPAC: {Path.GetFileName(sourceDacpac)}");
            using var sourcePac = DacPackage.Load(sourceDacpac);
            
            Console.WriteLine($"Loading target DACPAC: {Path.GetFileName(targetDacpac)}");
            using var targetPac = DacPackage.Load(targetDacpac);
            
            // Create deployment options
            var deployOptions = new DacDeployOptions
            {
                IgnoreAuthorizer = true
            };

            // Apply SCMP options if provided
            if (scmpComparison != null)
            {
                var mappedOptions = _optionsMapper.MapOptions(scmpComparison);
                
                // Map options from SCMP to DacDeployOptions
                deployOptions.DropObjectsNotInSource = mappedOptions.DropObjectsNotInSource;
                deployOptions.BlockOnPossibleDataLoss = mappedOptions.BlockOnPossibleDataLoss;
                deployOptions.IgnorePermissions = mappedOptions.IgnorePermissions;
                deployOptions.IgnoreRoleMembership = mappedOptions.IgnoreRoleMembership;
                deployOptions.IgnoreUserSettingsObjects = mappedOptions.IgnoreUserSettingsObjects;
                deployOptions.IgnoreLoginSids = mappedOptions.IgnoreLoginSids;
                deployOptions.IgnoreExtendedProperties = mappedOptions.IgnoreExtendedProperties;
                deployOptions.IgnoreWhitespace = mappedOptions.IgnoreWhitespace;
                deployOptions.IgnoreKeywordCasing = mappedOptions.IgnoreKeywordCasing;
                deployOptions.IgnoreSemicolonBetweenStatements = mappedOptions.IgnoreSemicolonBetweenStatements;
                // Always ignore comments to reduce false positives, regardless of SCMP setting
                deployOptions.IgnoreComments = true; // mappedOptions.IgnoreComments;
                deployOptions.GenerateSmartDefaults = mappedOptions.GenerateSmartDefaults;
                deployOptions.IncludeCompositeObjects = mappedOptions.IncludeCompositeObjects;
                deployOptions.IncludeTransactionalScripts = mappedOptions.IncludeTransactionalScripts;
                

            }
            else
            {
                // Default conservative options
                deployOptions.DropObjectsNotInSource = false;
                deployOptions.BlockOnPossibleDataLoss = true;
                deployOptions.IgnorePermissions = true;
                deployOptions.IgnoreRoleMembership = true;
                deployOptions.IgnoreUserSettingsObjects = true;
                deployOptions.IgnoreLoginSids = true;
                deployOptions.IgnoreExtendedProperties = true;
                deployOptions.IgnoreWhitespace = true;
                deployOptions.IgnoreComments = true;
            }
            
            // Additional options to handle errors and reduce false positives
            deployOptions.AllowIncompatiblePlatform = true;
            deployOptions.IgnoreFileAndLogFilePath = true;
            deployOptions.IgnoreFilegroupPlacement = true;
            deployOptions.IgnoreFileSize = true;
            deployOptions.IgnoreFullTextCatalogFilePath = true;
            
            // Additional options to reduce false positives
            deployOptions.IgnoreColumnOrder = true;
            deployOptions.IgnoreTableOptions = true;
            deployOptions.IgnoreIndexOptions = true;
            deployOptions.IgnoreIndexPadding = true;
            deployOptions.IgnoreFillFactor = true;
            deployOptions.IgnoreIdentitySeed = true;
            deployOptions.IgnoreIncrement = true;
            deployOptions.IgnoreQuotedIdentifiers = true;
            deployOptions.IgnoreAnsiNulls = true;
            deployOptions.IgnoreColumnCollation = true;
            deployOptions.IgnoreLockHintsOnIndexes = true;
            deployOptions.IgnoreWithNocheckOnCheckConstraints = true;
            deployOptions.IgnoreWithNocheckOnForeignKeys = true;
            deployOptions.IgnoreDmlTriggerOrder = true;
            deployOptions.IgnoreDmlTriggerState = true;
            deployOptions.IgnoreDdlTriggerOrder = true;
            deployOptions.IgnoreDdlTriggerState = true;
            deployOptions.IgnoreDefaultSchema = true;
            deployOptions.IgnorePartitionSchemes = true;
            deployOptions.IgnoreAuthorizer = true;
            deployOptions.IgnoreCryptographicProviderFilePath = true;
            deployOptions.IgnoreRouteLifetime = true;
            deployOptions.IgnoreNotForReplication = true;
            
            Console.WriteLine("Generating deployment script...");
            
            // Use DacServices to generate the script
            // We need a target database name for the script generation
            var targetDatabaseName = "TargetDatabase";
            
            // Generate the deployment script (static method)
            var deployScript = DacServices.GenerateDeployScript(targetPac, sourcePac, targetDatabaseName, deployOptions);
            
            if (!string.IsNullOrEmpty(deployScript))
            {
                Console.WriteLine($"Writing migration script to: {scriptPath}");
                await File.WriteAllTextAsync(scriptPath, deployScript);
                
                // Check if script has actual changes
                if (string.IsNullOrWhiteSpace(deployScript) || 
                    deployScript.Contains("No schema differences detected") ||
                    !ContainsSchemaChanges(deployScript))
                {
                    Console.WriteLine("No meaningful schema changes detected in script");
                    return string.Empty;
                }
                
                return deployScript;
            }
            else
            {
                Console.WriteLine("No differences found between DACPACs");
                return string.Empty;
            }
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
    async Task<string?> BuildSqlProject(string projectPath, string schemaPath)
    {
        var projectDir = Path.GetDirectoryName(projectPath)!;
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        
        try
        {
            Console.WriteLine($"  Building SQL project with dotnet build: {projectPath}");
            
            // Try to build using dotnet build - this is the only supported method now
            var dotnetResult = await BuildUsingDotnet(projectPath, schemaPath);
            if (!string.IsNullOrEmpty(dotnetResult))
            {
                return dotnetResult;
            }
            
            // Dotnet build is required - no fallback
            Console.WriteLine("  ERROR: dotnet build failed. Please ensure:");
            Console.WriteLine("    1. .NET SDK is installed");
            Console.WriteLine("    2. SQL Database Projects extension (Microsoft.Build.Sql) is installed");
            Console.WriteLine("    3. The .sqlproj file is properly configured");
            Console.WriteLine("  To install SQL project support: dotnet add package Microsoft.Build.Sql");
            return string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error building SQL project: {ex.Message}");
            return string.Empty;
        }
    }
    
    /// <summary>
    /// Attempts to build SQL project using dotnet build
    /// </summary>
    async Task<string?> BuildUsingDotnet(string projectPath, string schemaPath)
    {
        try
        {
            var projectDir = Path.GetDirectoryName(projectPath)!;
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            
            // Exclusion file path in the original schema directory
            var exclusionFilePath = Path.Combine(schemaPath, ".dacpac-exclusions.json");
            
            // Create a temporary directory for excluded files
            var tempExcludeDir = Path.Combine(Path.GetTempPath(), $"DacpacExcluded_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempExcludeDir);
            
            try
            {
                ExclusionFile? existingExclusions = null;
                var appliedExclusions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                
                // Step 1: Load and apply existing exclusions if they exist
                if (File.Exists(exclusionFilePath))
                {
                    existingExclusions = LoadExclusionFile(exclusionFilePath);
                    if (existingExclusions != null && existingExclusions.Exclusions.Any())
                    {
                        Console.WriteLine($"  Applying {existingExclusions.Exclusions.Count} exclusions from .dacpac-exclusions.json");
                        
                        // Apply existing exclusions
                        var filesFound = 0;
                        var filesNotFound = 0;
                        var filesMoved = 0;
                        
                        foreach (var exclusion in existingExclusions.Exclusions)
                        {
                            var fullPath = Path.Combine(projectDir, exclusion.File);
                            Console.WriteLine($"    Checking exclusion: {exclusion.File}");
                            Console.WriteLine($"      Full path: {fullPath}");
                            
                            if (File.Exists(fullPath))
                            {
                                filesFound++;
                                Console.WriteLine($"      ✓ File exists");
                                
                                var tempPath = Path.Combine(tempExcludeDir, exclusion.File);
                                Console.WriteLine($"      Temp path: {tempPath}");
                                
                                var tempFileDir = Path.GetDirectoryName(tempPath);
                                if (!string.IsNullOrEmpty(tempFileDir))
                                {
                                    Console.WriteLine($"      Creating temp directory: {tempFileDir}");
                                    Directory.CreateDirectory(tempFileDir);
                                }
                                
                                try
                                {
                                    Console.WriteLine($"      Moving file from {fullPath} to {tempPath}");
                                    File.Move(fullPath, tempPath);
                                    appliedExclusions[exclusion.File] = tempPath;
                                    filesMoved++;
                                    Console.WriteLine($"      ✓ File moved successfully");
                                }
                                catch (Exception moveEx)
                                {
                                    Console.WriteLine($"      ✗ Failed to move file: {moveEx.Message}");
                                }
                            }
                            else
                            {
                                filesNotFound++;
                                Console.WriteLine($"      ✗ File does not exist");
                            }
                        }
                        
                        Console.WriteLine($"    Exclusion summary:");
                        Console.WriteLine($"      Total exclusions: {existingExclusions.Exclusions.Count}");
                        Console.WriteLine($"      Files found: {filesFound}");
                        Console.WriteLine($"      Files not found: {filesNotFound}");
                        Console.WriteLine($"      Files moved: {filesMoved}");
                        
                        if (appliedExclusions.Count > 0)
                        {
                            Console.WriteLine($"    Successfully excluded {appliedExclusions.Count} files");
                        }
                    }
                }
                
                // Step 2: Attempt build
                Console.WriteLine("  Attempting build...");
                var processInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build \"{projectPath}\" --configuration Release /p:TreatTSqlWarningsAsErrors=false /p:SuppressTSqlWarnings=71561",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = projectDir
                };
                
                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    Console.WriteLine("  ERROR: Failed to start dotnet build process");
                    return null;
                }
                
                var output = await process.StandardOutput.ReadToEndAsync();
                var errors = await process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0)
                {
                    // Build succeeded with existing exclusions (or no exclusions)
                    var dacpacPath = FindGeneratedDacpac(projectDir, projectName);
                    if (!string.IsNullOrEmpty(dacpacPath))
                    {
                        Console.WriteLine($"  ✓ Build succeeded");
                        
                        // Update last successful build time if we used exclusions
                        if (existingExclusions != null)
                        {
                            existingExclusions.LastSuccessfulBuild = DateTime.UtcNow;
                            SaveExclusionFile(exclusionFilePath, existingExclusions);
                        }
                        
                        return dacpacPath;
                    }
                    
                    Console.WriteLine("  ERROR: Build succeeded but DACPAC file was not found");
                    return null;
                }
                
                // Step 3: Build failed - regenerate exclusions
                Console.WriteLine("  Build failed. Regenerating exclusion file...");
                
                // First restore all previously excluded files
                foreach (var kvp in appliedExclusions)
                {
                    var originalPath = Path.Combine(projectDir, kvp.Key);
                    File.Move(kvp.Value, originalPath);
                }
                appliedExclusions.Clear();
                
                // Step 4: Run iterative exclusion process
                var newExclusionFile = new ExclusionFile
                {
                    Version = "1.0",
                    Generated = DateTime.UtcNow,
                    LastSuccessfulBuild = DateTime.UtcNow
                };
                
                var iteration = 0;
                var maxIterations = 10;
                var allExcludedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                
                while (iteration < maxIterations)
                {
                    iteration++;
                    Dictionary<string, string> filesToExclude;
                    
                    if (iteration == 1)
                    {
                        // First iteration: ONLY SQL71561 errors
                        Console.WriteLine($"  Iteration 1: Analyzing SQL71561 errors...");
                        filesToExclude = ExtractSQL71561FilesWithReasons(output, errors);
                        
                        if (filesToExclude.Any())
                        {
                            Console.WriteLine($"    Found {filesToExclude.Count} files with SQL71561 errors");
                            
                            foreach (var kvp in filesToExclude)
                            {
                                newExclusionFile.Exclusions.Add(new ExclusionEntry
                                {
                                    File = kvp.Key,
                                    Reason = kvp.Value,
                                    ExcludedOn = DateTime.UtcNow,
                                    Iteration = iteration,
                                    ErrorCode = SQL_UNRESOLVED_REFERENCE_ERROR
                                });
                            }
                        }
                    }
                    else
                    {
                        // Subsequent iterations: ONLY cascading dependencies (NOT new SQL71561 errors)
                        Console.WriteLine($"  Iteration {iteration}: Analyzing cascading dependencies...");
                        filesToExclude = FindCascadingDependenciesOnly(output, errors, allExcludedFiles.Keys.ToHashSet());
                        
                        if (!filesToExclude.Any())
                        {
                            Console.WriteLine($"    No cascading dependencies found");
                            break;
                        }
                        
                        Console.WriteLine($"    Found {filesToExclude.Count} cascading dependencies");
                        
                        foreach (var kvp in filesToExclude)
                        {
                            newExclusionFile.Exclusions.Add(new ExclusionEntry
                            {
                                File = kvp.Key,
                                Reason = kvp.Value,
                                ExcludedOn = DateTime.UtcNow,
                                Iteration = iteration,
                                ErrorCode = "CASCADING"
                            });
                        }
                    }
                    
                    // If no files to exclude, we can't proceed
                    if (!filesToExclude.Any() && iteration == 1)
                    {
                        Console.WriteLine("  No files identified for exclusion. Build cannot proceed.");
                        break;
                    }
                    
                    // Move files to temp and track
                    foreach (var kvp in filesToExclude)
                    {
                        var fullPath = Path.Combine(projectDir, kvp.Key);
                        if (File.Exists(fullPath))
                        {
                            var tempPath = Path.Combine(tempExcludeDir, kvp.Key);
                            var tempFileDir = Path.GetDirectoryName(tempPath);
                            if (!string.IsNullOrEmpty(tempFileDir))
                                Directory.CreateDirectory(tempFileDir);
                                
                            File.Move(fullPath, tempPath);
                            allExcludedFiles[kvp.Key] = tempPath;
                        }
                    }
                    
                    // Retry build
                    Console.WriteLine($"  Retrying build (iteration {iteration})...");
                    
                    using var retryProcess = Process.Start(processInfo);
                    if (retryProcess == null)
                    {
                        Console.WriteLine("  ERROR: Failed to start retry build process");
                        break;
                    }
                    
                    output = await retryProcess.StandardOutput.ReadToEndAsync();
                    errors = await retryProcess.StandardError.ReadToEndAsync();
                    
                    await retryProcess.WaitForExitAsync();
                    
                    if (retryProcess.ExitCode == 0)
                    {
                        // Build succeeded!
                        var dacpacPath = FindGeneratedDacpac(projectDir, projectName);
                        if (!string.IsNullOrEmpty(dacpacPath))
                        {
                            Console.WriteLine($"  ✓ Build succeeded after {iteration} iterations");
                            Console.WriteLine($"  ✓ Total files excluded: {newExclusionFile.Exclusions.Count}");
                            
                            // Save the new exclusion file
                            SaveExclusionFile(exclusionFilePath, newExclusionFile);
                            
                            return dacpacPath;
                        }
                        
                        Console.WriteLine("  ERROR: Build succeeded but DACPAC file was not found");
                        break;
                    }
                    
                    // Continue to next iteration if build still fails
                }
                
                if (iteration >= maxIterations)
                {
                    Console.WriteLine($"  ERROR: Reached maximum iterations ({maxIterations}), giving up");
                }
                else
                {
                    Console.WriteLine($"  ERROR: Build still failing after {iteration} iterations");
                }
                
                // Display limited error output from last attempt
                if (!string.IsNullOrWhiteSpace(errors))
                {
                    var errorLines = errors.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var sqlErrors = errorLines.Where(l => l.Contains("SQL", StringComparison.OrdinalIgnoreCase)).Take(5);
                    if (sqlErrors.Any())
                    {
                        Console.WriteLine("  Last build errors:");
                        foreach (var line in sqlErrors)
                        {
                            // Keep file path and error on same line, remove extra whitespace
                            var trimmedLine = System.Text.RegularExpressions.Regex.Replace(line.Trim(), @"\s+", " ");
                            Console.WriteLine($"    {trimmedLine}");
                        }
                    }
                }
                
                return null;
            }
            finally
            {
                // Always restore files from temporary location
                if (Directory.Exists(tempExcludeDir))
                {
                    RestoreFilesFromTemp(projectDir, tempExcludeDir);
                    
                    // Clean up temp directory
                    try
                    {
                        Directory.Delete(tempExcludeDir, recursive: true);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ERROR: Exception during dotnet build: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Finds the generated DACPAC file in the build output
    /// </summary>
    string? FindGeneratedDacpac(string projectDir, string projectName)
    {
        // Check Release build output first
        var releaseDacpac = Path.Combine(projectDir, "bin", "Release", $"{projectName}.dacpac");
        if (File.Exists(releaseDacpac))
        {
            Console.WriteLine($"  ✓ Found DACPAC: {Path.GetFileName(releaseDacpac)}");
            return releaseDacpac;
        }
        
        // Check Debug build output
        var debugDacpac = Path.Combine(projectDir, "bin", "Debug", $"{projectName}.dacpac");
        if (File.Exists(debugDacpac))
        {
            Console.WriteLine($"  ✓ Found DACPAC: {Path.GetFileName(debugDacpac)}");
            return debugDacpac;
        }
        
        return null;
    }
    
    /// <summary>
    /// Restores excluded files from their backup locations
    /// </summary>
    void RestoreExcludedFiles(string projectDir, HashSet<string> excludedFiles)
    {
        foreach (var file in excludedFiles)
        {
            var fullPath = Path.Combine(projectDir, file);
            var backupPath = fullPath + ".excluded";
            if (File.Exists(backupPath))
            {
                File.Move(backupPath, fullPath, overwrite: true);
            }
        }
    }

    /// <summary>
    /// Restores files from temporary location back to project directory
    /// </summary>
    void RestoreFilesFromTemp(string projectDir, string tempExcludeDir)
    {
        if (!Directory.Exists(tempExcludeDir))
            return;
            
        try
        {
            // Enumerate all files in the temp directory and restore them
            foreach (var tempFile in Directory.GetFiles(tempExcludeDir, "*.sql", SearchOption.AllDirectories))
            {
                // Calculate relative path from temp directory
                var relativePath = Path.GetRelativePath(tempExcludeDir, tempFile);
                var originalPath = Path.Combine(projectDir, relativePath);
                
                // Ensure directory exists
                var originalDir = Path.GetDirectoryName(originalPath);
                if (!string.IsNullOrEmpty(originalDir))
                {
                    Directory.CreateDirectory(originalDir);
                }
                
                // Move file back
                if (File.Exists(tempFile))
                {
                    File.Move(tempFile, originalPath, overwrite: true);
                }
            }
            
            Console.WriteLine($"  Restored all excluded files from temporary location");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WARNING: Error restoring files from temp: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Finds files that reference already excluded objects (cascading dependencies)
    /// </summary>
    HashSet<string> FindCascadingDependencies(string output, string errors, HashSet<string> excludedFiles)
    {
        var cascadingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        if (!excludedFiles.Any())
            return cascadingFiles;
        
        // Extract object names from excluded files
        var excludedObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in excludedFiles)
        {
            // Extract object name from file path (e.g., "schemas/dbo/Views/vw_Something.sql" -> "vw_Something")
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!string.IsNullOrEmpty(fileName))
            {
                excludedObjects.Add(fileName);
                
                // Also add with schema prefix if available
                var parts = file.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    // Try to find schema name (usually after "schemas" folder)
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        if (parts[i].Equals("schemas", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
                        {
                            var schema = parts[i + 1];
                            excludedObjects.Add($"[{schema}].[{fileName}]");
                            excludedObjects.Add($"{schema}.{fileName}");
                            break;
                        }
                    }
                }
            }
        }
        
        var combinedOutput = output + "\n" + errors;
        
        // Look for errors mentioning the excluded objects
        foreach (var excludedObject in excludedObjects)
        {
            // Pattern to find references to excluded objects in error messages
            var patterns = new[]
            {
                // SQL71561 errors referencing the excluded object
                $@"SQL71561:.*?has an unresolved reference to object.*?{Regex.Escape(excludedObject)}",
                $@"SQL71561:.*?contains an unresolved reference to an object.*?{Regex.Escape(excludedObject)}",
                // Object reference patterns
                $@"([^:\r\n]+\.sql).*?{Regex.Escape(excludedObject)}",
                // Files containing references to excluded objects
                $@"Error validating element.*?\[([^\]]+)\]\.\[([^\]]+)\].*?{Regex.Escape(excludedObject)}"
            };
            
            foreach (var pattern in patterns)
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                
                foreach (Match match in regex.Matches(combinedOutput))
                {
                    // Try to extract file path from the error
                    string? filePath = null;
                    
                    // Check if first group contains a file path
                    if (match.Groups.Count > 1 && match.Groups[1].Value.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                    {
                        filePath = ProcessFilePath(match.Groups[1].Value);
                    }
                    else
                    {
                        // Try to extract from the full line containing this match
                        var lineStart = combinedOutput.LastIndexOf('\n', match.Index) + 1;
                        var lineEnd = combinedOutput.IndexOf('\n', match.Index);
                        if (lineEnd == -1) lineEnd = combinedOutput.Length;
                        
                        var line = combinedOutput.Substring(lineStart, lineEnd - lineStart);
                        
                        // Look for file path in the line
                        var fileMatch = Regex.Match(line, @"([^:\s]+\.sql)", RegexOptions.IgnoreCase);
                        if (fileMatch.Success)
                        {
                            filePath = ProcessFilePath(fileMatch.Groups[1].Value);
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(filePath) && !excludedFiles.Contains(filePath))
                    {
                        cascadingFiles.Add(filePath);
                    }
                }
            }
        }
        
        if (cascadingFiles.Any())
        {
            Console.WriteLine($"    Found {cascadingFiles.Count} files with cascading dependencies");
            // Detailed output removed - reasons are shown elsewhere
        }
        
        return cascadingFiles;
    }
    
    /// <summary>
    /// Extracts file paths from SQL71561 error messages
    /// </summary>
    /// <summary>
    /// Extracts file paths from SQL71561 error messages
    /// </summary>
    /// <summary>
    /// Extracts file paths from SQL71561 error messages
    /// </summary>
    /// <summary>
    /// Extracts file paths from unresolved external reference error messages
    /// </summary>
    HashSet<string> ExtractProblematicFiles(string output, string errors)
    {
        var problematicFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var combinedOutput = output + "\n" + errors;
        
        // Multiple patterns to match different error formats for unresolved references
        var patterns = new[]
        {
            // Pattern 1: Standard MSBuild error format (works on both Windows and Linux)
            // Example: "C:\path\to\file.sql(10,5): Error SQL71561: ..."
            // Example: "/path/to/file.sql(10,5): Error SQL71561: ..."
            $@"([^:\r\n]+\.sql)\(\d+,\d+\):\s*Error\s+{SQL_UNRESOLVED_REFERENCE_ERROR}",
            
            // Pattern 2: Build output format with file path
            // Example: "schemas/dbo/views/vw_Something.sql : error SQL71561"
            $@"([^\s:]+\.sql)\s*:\s*error\s+{SQL_UNRESOLVED_REFERENCE_ERROR}",
            
            // Pattern 3: Alternative format with full path in error
            // Example: "Error SQL71561: File /tmp/build/schemas/dbo/tables/Table1.sql"
            $@"Error\s+{SQL_UNRESOLVED_REFERENCE_ERROR}:.*?(?:File\s+)?([\/\\][^\s]+\.sql)",
            
            // Pattern 4: Simple relative path before SQL71561
            $@"([^\s\(\)]+\.sql).*?{SQL_UNRESOLVED_REFERENCE_ERROR}"
        };
        
        // First, try to extract file paths directly
        foreach (var pattern in patterns)
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            
            foreach (Match match in regex.Matches(combinedOutput))
            {
                if (match.Groups.Count > 1)
                {
                    var filePath = match.Groups[1].Value.Trim();
                    
                    // Skip if this looks like a command line argument
                    if (filePath.StartsWith("-") || filePath.StartsWith("/p:"))
                        continue;
                    
                    // Process the file path to make it relative and normalized
                    filePath = ProcessFilePath(filePath);
                    
                    if (!string.IsNullOrEmpty(filePath))
                        problematicFiles.Add(filePath);
                }
            }
        }
        
        // If no files found yet, try to extract object names and map them to files
        if (!problematicFiles.Any())
        {
            // Match error patterns with object names for unresolved references
            var objectPattern = $@"{SQL_UNRESOLVED_REFERENCE_ERROR}:\s*(?:Error validating element\s*)?(?:View|Table|Procedure|Function|Synonym|Trigger|Schema|SqlFile):\s*\[([^\]]+)\]\.\[([^\]]+)\]";
            var objectRegex = new Regex(objectPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            
            foreach (Match match in objectRegex.Matches(combinedOutput))
            {
                if (match.Groups.Count > 2)
                {
                    var schema = match.Groups[1].Value;
                    var objectName = match.Groups[2].Value;
                    
                    // Try to find the file based on schema and object name
                    // Common patterns for SQL file organization
                    var possiblePaths = new[]
                    {
                        Path.Combine("schemas", schema, "Tables", $"{objectName}.sql"),
                        Path.Combine("schemas", schema, "Views", $"{objectName}.sql"),
                        Path.Combine("schemas", schema, "Procedures", $"{objectName}.sql"),
                        Path.Combine("schemas", schema, "Functions", $"{objectName}.sql"),
                        Path.Combine("schemas", schema, $"{objectName}.sql"),
                        Path.Combine("Tables", $"{objectName}.sql"),
                        Path.Combine("Views", $"{objectName}.sql"),
                        Path.Combine("Procedures", $"{objectName}.sql"),
                        Path.Combine("Functions", $"{objectName}.sql"),
                        Path.Combine(schema, $"{objectName}.sql"),
                        $"{objectName}.sql"
                    };
                    
                    // Add all possible paths
                    foreach (var path in possiblePaths)
                    {
                        problematicFiles.Add(path);
                    }
                }
            }
        }
        
        // Log what we found for debugging
        Console.WriteLine($"    Extracted {problematicFiles.Count} potential problematic files from {SQL_UNRESOLVED_REFERENCE_ERROR} errors");
        if (problematicFiles.Count > 0 && problematicFiles.Count <= 10)
        {
            foreach (var file in problematicFiles.Take(10))
            {
                Console.WriteLine($"      - {file}");
            }
        }
        
        return problematicFiles;
    }
    
    /// <summary>
    /// Processes a file path to make it relative and normalized for the current OS
    /// </summary>
    string ProcessFilePath(string filePath)
    {
        // Remove any quotes
        filePath = filePath.Trim('"', '\'');
        
        // If it's an absolute path, try to make it relative
        if (Path.IsPathRooted(filePath))
        {
            // Look for common SQL project directory patterns
            var patterns = new[] { "schemas", "Tables", "Views", "Procedures", "Functions", "Synonyms", "Triggers" };
            
            // Split by both Windows and Unix separators
            var parts = filePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Find where the SQL structure starts
            int startIndex = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (patterns.Any(p => string.Equals(parts[i], p, StringComparison.OrdinalIgnoreCase)))
                {
                    startIndex = i;
                    break;
                }
            }
            
            if (startIndex >= 0)
            {
                // Reconstruct the relative path from the SQL structure point
                var relativeParts = parts.Skip(startIndex).ToArray();
                filePath = Path.Combine(relativeParts);
            }
            else
            {
                // If we can't find a pattern, look for the .sql file and work backwards
                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    if (parts[i].EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                    {
                        // Take at most 3 levels up from the SQL file
                        var takeCount = Math.Min(3, i + 1);
                        var relevantParts = parts.Skip(Math.Max(0, i - takeCount + 1)).Take(takeCount).ToArray();
                        filePath = Path.Combine(relevantParts);
                        break;
                    }
                }
                
                // Last resort: just use the filename
                if (Path.IsPathRooted(filePath))
                {
                    filePath = Path.GetFileName(filePath);
                }
            }
        }
        
        // Always normalize the separators to the current OS
        // This ensures the exclusion file paths will match when read back
        filePath = filePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        
        return filePath;
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
    /// <summary>
    /// Copies schema files from source to destination, excluding migrations and change manifests
    /// </summary>
    void CopySchemaFiles(string sourceDir, string destDir)
    {
        var filesToCopy = 0;
        var filesToSkip = 0;
        
        // Get all SQL files and pre-filter them to avoid unnecessary processing
        var sqlFiles = Directory.EnumerateFiles(sourceDir, "*.sql", SearchOption.AllDirectories)
            .Where(file => {
                var relativePath = Path.GetRelativePath(sourceDir, file);
                // Skip migrations, change manifests, *_extra.sql, and other non-schema files
                return !relativePath.Contains("z_migrations") && 
                       !relativePath.Contains("z_migrations_reverse") &&
                       !relativePath.Contains("_change-manifests") &&
                       !relativePath.EndsWith("_extra.sql", StringComparison.OrdinalIgnoreCase);
            }).ToList();
        
        // Track total files for skip counting
        var totalFiles = Directory.GetFiles(sourceDir, "*.sql", SearchOption.AllDirectories).Length;
        
        // Process files in parallel for better performance
        Parallel.ForEach(sqlFiles, file =>
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var destPath = Path.Combine(destDir, relativePath);
            var destFileDir = Path.GetDirectoryName(destPath);
            
            if (!string.IsNullOrEmpty(destFileDir))
                Directory.CreateDirectory(destFileDir);
            
            File.Copy(file, destPath, overwrite: true);
            Interlocked.Increment(ref filesToCopy);
        });
        
        filesToSkip = totalFiles - filesToCopy;
        Console.WriteLine($"    Copied {filesToCopy} SQL files total (skipped {filesToSkip} non-schema files)");
    }
    
    /// <summary>
    /// Creates a simple SDK-style SQL project file
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
  </ItemGroup>";

        // Add reference DACPAC if provided
        if (!string.IsNullOrEmpty(referenceDacpacPath) && File.Exists(referenceDacpacPath))
        {
            var dacpacFileName = Path.GetFileName(referenceDacpacPath);
            var dacpacName = Path.GetFileNameWithoutExtension(referenceDacpacPath);
            
            // Use just the filename since the DACPAC is copied to the same directory as the project
            projectContent += $@"
  
  <!-- Reference to original DACPAC to resolve external references -->
  <ItemGroup>
    <ArtifactReference Include=""{dacpacFileName}"">
      <HintPath>{dacpacFileName}</HintPath>
      <SuppressMissingDependenciesErrors>true</SuppressMissingDependenciesErrors>
      <DatabaseVariableLiteralValue>{dacpacName}</DatabaseVariableLiteralValue>
    </ArtifactReference>
  </ItemGroup>";
        }

        projectContent += @"
</Project>";
        
        File.WriteAllText(projectPath, projectContent);
    }

    /// <summary>
    /// Loads exclusion file from the schema directory
    /// </summary>
    /// <summary>
    /// Extracts files with SQL71561 errors along with the specific error reasons
    /// </summary>
    /// <summary>
    /// Finds ONLY files that have cascading dependencies on already excluded objects
    /// Does NOT look for new SQL71561 errors
    /// </summary>
    Dictionary<string, string> FindCascadingDependenciesOnly(string output, string errors, HashSet<string> excludedFiles)
    {
        var cascadingFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        if (!excludedFiles.Any())
        {
            Console.WriteLine("      No excluded files to check for cascading dependencies");
            return cascadingFiles;
        }
        
        // Extract object names from excluded files
        var excludedObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in excludedFiles)
        {
            // Extract object name from file path (e.g., "schemas/dbo/Views/vw_Something.sql" -> "vw_Something")
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!string.IsNullOrEmpty(fileName))
            {
                excludedObjects.Add(fileName);
                
                // Also add with schema prefix if available
                var parts = file.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    // Try to find schema name (usually after "schemas" folder)
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        if (parts[i].Equals("schemas", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
                        {
                            var schema = parts[i + 1];
                            excludedObjects.Add($"[{schema}].[{fileName}]");
                            excludedObjects.Add($"{schema}.{fileName}");
                            break;
                        }
                    }
                }
            }
        }
        
        Console.WriteLine($"      Looking for cascading dependencies from {excludedFiles.Count} excluded files...");
        
        var combinedOutput = output + "\n" + errors;
        var lines = combinedOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        // Also look for any SQL71561 errors that weren't caught in first iteration
        var newSQL71561Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Process each line looking for references to excluded objects
        foreach (var line in lines)
        {
            // Check if this is a NEW SQL71561 error (not caught in iteration 1)
            if (line.Contains(SQL_UNRESOLVED_REFERENCE_ERROR, StringComparison.OrdinalIgnoreCase))
            {
                // Extract file path from this error
                var fileMatch = Regex.Match(line, @"([^:\s]+\.sql)(?:\(\d+,\d+\))?", RegexOptions.IgnoreCase);
                if (fileMatch.Success)
                {
                    var filePath = ProcessFilePath(fileMatch.Groups[1].Value.Trim());
                    if (!excludedFiles.Contains(filePath))
                    {
                        newSQL71561Files.Add(filePath);
                    }
                }
            }
            
            // Check if line mentions an excluded object
            bool mentionsExcludedObject = false;
            string? referencedExcludedObject = null;
            
            foreach (var excludedObject in excludedObjects)
            {
                if (line.Contains(excludedObject, StringComparison.OrdinalIgnoreCase))
                {
                    mentionsExcludedObject = true;
                    referencedExcludedObject = excludedObject;
                    break;
                }
            }
            
            if (!mentionsExcludedObject)
                continue;
            
            // Now extract the file that has the dependency
            string? filePath2 = null;
            string reason = "";
            
            // Look for file paths in the error line
            var fileMatch2 = Regex.Match(line, @"([^:\s]+\.sql)(?:\(\d+,\d+\))?", RegexOptions.IgnoreCase);
            if (fileMatch2.Success)
            {
                filePath2 = ProcessFilePath(fileMatch2.Groups[1].Value.Trim());
                
                // Don't include files that are already excluded
                if (excludedFiles.Contains(filePath2))
                {
                    filePath2 = null;
                }
                else
                {
                    // Build a reason based on the error
                    if (line.Contains(SQL_UNRESOLVED_REFERENCE_ERROR, StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract the specific error message
                        var errorMatch = Regex.Match(line, @"SQL71561:\s*(.+)$", RegexOptions.IgnoreCase);
                        if (errorMatch.Success)
                        {
                            reason = $"Cascading dependency: {errorMatch.Groups[1].Value.Trim()}";
                        }
                        else
                        {
                            // Include the file name in the reason for clarity
                        var excludedFileName = excludedFiles.FirstOrDefault(f => f.Contains(referencedExcludedObject, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(excludedFileName))
                        {
                            reason = $"Cascading dependency: References excluded {referencedExcludedObject} from {Path.GetFileName(excludedFileName)}";
                        }
                        else
                        {
                            reason = $"Cascading dependency: References excluded object {referencedExcludedObject}";
                        }
                        }
                    }
                    else
                    {
                        // Include the file name in the reason for clarity
                        var excludedFileName = excludedFiles.FirstOrDefault(f => f.Contains(referencedExcludedObject, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(excludedFileName))
                        {
                            reason = $"Cascading dependency: References excluded {referencedExcludedObject} from {Path.GetFileName(excludedFileName)}";
                        }
                        else
                        {
                            reason = $"Cascading dependency: References excluded object {referencedExcludedObject}";
                        }
                    }
                    
                    // Debug logging removed - too verbose
                }
            }
            
            // Add the file if we found one
            if (!string.IsNullOrEmpty(filePath2) && !cascadingFiles.ContainsKey(filePath2))
            {
                cascadingFiles[filePath2] = reason;
            }
        }
        
        // Also check for binding errors that might not have SQL71561
        foreach (var excludedObject in excludedObjects)
        {
            var bindingPattern = $@"([^:\s]+\.sql).*?could not be resolved.*?{Regex.Escape(excludedObject)}";
            var bindingRegex = new Regex(bindingPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            
            foreach (Match match in bindingRegex.Matches(combinedOutput))
            {
                if (match.Groups.Count > 0)
                {
                    var filePath = ProcessFilePath(match.Groups[1].Value.Trim());
                    
                    if (!excludedFiles.Contains(filePath) && !cascadingFiles.ContainsKey(filePath))
                    {
                        cascadingFiles[filePath] = $"Cascading dependency: Binding error referencing excluded object {excludedObject}";
                        Console.WriteLine($"        Found binding error for {filePath} referencing {excludedObject}");
                    }
                }
            }
        }
        
        // Log summary of what we found
        if (newSQL71561Files.Any())
        {
            Console.WriteLine($"      WARNING: Found {newSQL71561Files.Count} NEW SQL71561 errors not caught in iteration 1:");
            foreach (var file in newSQL71561Files.Take(5))
            {
                Console.WriteLine($"        - {file}");
            }
            Console.WriteLine("      These should have been caught in iteration 1 - checking why they were missed...");
        }
        
        if (cascadingFiles.Any())
        {
            Console.WriteLine($"    Found {cascadingFiles.Count} files with cascading dependencies");
            foreach (var kvp in cascadingFiles)
            {
                // Just print the reason which contains all necessary information
                Console.WriteLine($"      {kvp.Value}");
            }
        }
        else
        {
            Console.WriteLine("      No cascading dependencies found");
        }
        
        return cascadingFiles;
    }

    Dictionary<string, string> ExtractSQL71561FilesWithReasons(string output, string errors)
    {
        var filesWithReasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var combinedOutput = output + "\n" + errors;
        
        // Split by lines to process each error individually
        var lines = combinedOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        Console.WriteLine($"      Scanning {lines.Length} lines for SQL71561 errors...");
        var sql71561Count = 0;
        
        foreach (var line in lines)
        {
            // Only process lines that contain SQL71561
            if (!line.Contains(SQL_UNRESOLVED_REFERENCE_ERROR, StringComparison.OrdinalIgnoreCase))
                continue;
            
            sql71561Count++;
            
            string? filePath = null;
            string reason = line.Trim();
            
            // Debug logging removed
            
            // Pattern 1: Standard MSBuild error format
            // Example: "C:\path\to\file.sql(10,5): Error SQL71561: View: [dbo].[vw_Something] has an unresolved reference to object [OtherDB].[dbo].[Table]"
            var match = Regex.Match(line, @"([^:\r\n]+\.sql)\(\d+,\d+\):\s*(?:Build\s+)?(?:error|Error)\s+" + SQL_UNRESOLVED_REFERENCE_ERROR + @":\s*(.+)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                filePath = ProcessFilePath(match.Groups[1].Value.Trim());
                // Remove the .sqlproj path from the error message
                var errorText = match.Groups[2].Value.Trim();
                // Only remove the trailing .sqlproj path, not the error content
                errorText = System.Text.RegularExpressions.Regex.Replace(errorText, @"\s*\[[^\[\]]*\.sqlproj\]\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                reason = $"SQL71561: {errorText}";
                // Debug output removed
            }
            else
            {
                // Pattern 2: Alternative format
                // Example: "schemas/dbo/views/vw_Something.sql : error SQL71561: View has unresolved reference"
                match = Regex.Match(line, @"([^\s:]+\.sql)\s*:\s*error\s+" + SQL_UNRESOLVED_REFERENCE_ERROR + @":\s*(.+)$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    filePath = ProcessFilePath(match.Groups[1].Value.Trim());
                    // Remove the .sqlproj path from the error message
                var errorText = match.Groups[2].Value.Trim();
                // Only remove the trailing .sqlproj path, not the error content
                errorText = System.Text.RegularExpressions.Regex.Replace(errorText, @"\s*\[[^\[\]]*\.sqlproj\]\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                reason = $"SQL71561: {errorText}";
                    // Debug output removed
                }
                else
                {
                    // Pattern 3: Try to extract any file path from the line
                    match = Regex.Match(line, @"([^\s\(\)]+\.sql)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        filePath = ProcessFilePath(match.Groups[1].Value.Trim());
                        
                        // Extract the error message after SQL71561
                        var errorMatch = Regex.Match(line, SQL_UNRESOLVED_REFERENCE_ERROR + @":\s*(.+)$", RegexOptions.IgnoreCase);
                        if (errorMatch.Success)
                        {
                            var errorText = errorMatch.Groups[1].Value.Trim();
                            // Only remove the trailing .sqlproj path, not the error content
                errorText = System.Text.RegularExpressions.Regex.Replace(errorText, @"\s*\[[^\[\]]*\.sqlproj\]\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            reason = $"SQL71561: {errorText}";
                        }
                        else
                        {
                            reason = $"SQL71561: Unresolved reference error";
                        }
                        // Debug output removed
                    }
                    // No file path found - skip silently
                }
            }
            
            // Add the file and reason if we found a valid file path
            if (!string.IsNullOrEmpty(filePath) && !filesWithReasons.ContainsKey(filePath))
            {
                // Clean up the reason - remove excessive whitespace and truncate if too long
                reason = Regex.Replace(reason, @"\s+", " ").Trim();
                if (reason.Length > 500)
                {
                    reason = reason.Substring(0, 497) + "...";
                }
                
                filesWithReasons[filePath] = reason;
                // Debug output removed
            }
            // Duplicate handling - no output needed
        }
        
        Console.WriteLine($"      Found {sql71561Count} SQL71561 errors");
        Console.WriteLine($"    Extracted {filesWithReasons.Count} unique files with SQL71561 errors");
        
        // If no files found with detailed reasons but we have SQL71561 errors, fall back to object-based extraction
        if (!filesWithReasons.Any() && sql71561Count > 0)
        {
            Console.WriteLine("      No files extracted from error lines, trying object-based extraction...");
            
            // Match error patterns with object names for unresolved references
            var objectPattern = SQL_UNRESOLVED_REFERENCE_ERROR + @":\s*(?:Error validating element\s*)?(?:View|Table|Procedure|Function|Synonym|Trigger|Schema|SqlFile):\s*\[([^\]]+)\]\.?\[([^\]]+)\].*?(?:has an unresolved reference to (?:object\s*)?(.+?)(?:\.|$))?";
            var objectRegex = new Regex(objectPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            
            foreach (Match match in objectRegex.Matches(combinedOutput))
            {
                if (match.Groups.Count > 2)
                {
                    var schema = match.Groups[1].Value;
                    var objectName = match.Groups[2].Value;
                    var referencedObject = match.Groups.Count > 3 ? match.Groups[3].Value.Trim() : "unknown object";
                    
                    Console.WriteLine($"        Found object reference: [{schema}].[{objectName}] -> {referencedObject}");
                    
                    // Try to find the file based on schema and object name
                    var possiblePaths = new[]
                    {
                        Path.Combine("schemas", schema, "Tables", $"{objectName}.sql"),
                        Path.Combine("schemas", schema, "Views", $"{objectName}.sql"),
                        Path.Combine("schemas", schema, "Procedures", $"{objectName}.sql"),
                        Path.Combine("schemas", schema, "Functions", $"{objectName}.sql"),
                        Path.Combine("schemas", schema, $"{objectName}.sql")
                    };
                    
                    var reason = $"SQL71561: [{schema}].[{objectName}] has unresolved reference to {referencedObject}";
                    
                    foreach (var path in possiblePaths)
                    {
                        if (!filesWithReasons.ContainsKey(path))
                        {
                            filesWithReasons[path] = reason;
                            Console.WriteLine($"          Guessed file path: {path}");
                            break; // Only add once
                        }
                    }
                }
            }
        }
        
        if (filesWithReasons.Count > 0)
        {
            // Show all errors, not truncated
            foreach (var kvp in filesWithReasons)
            {
                // Just print the error reason which already contains the file path
                Console.WriteLine($"      {kvp.Value}");
            }
        }
        
        return filesWithReasons;
    }

    ExclusionFile? LoadExclusionFile(string exclusionFilePath)
    {
        try
        {
            if (!File.Exists(exclusionFilePath))
                return null;
                
            var json = File.ReadAllText(exclusionFilePath);
            var exclusionFile = System.Text.Json.JsonSerializer.Deserialize<ExclusionFile>(json);
            
            if (exclusionFile != null)
            {
                Console.WriteLine($"  Loaded exclusion file with {exclusionFile.Exclusions.Count} exclusions");
                Console.WriteLine($"  Last successful build: {exclusionFile.LastSuccessfulBuild:yyyy-MM-dd HH:mm:ss}");
                
                // Normalize the file paths to the current OS format
                // This handles exclusion files created on Windows being used on Linux and vice versa
                foreach (var exclusion in exclusionFile.Exclusions)
                {
                    // Replace both Windows and Unix path separators with the current OS separator
                    exclusion.File = exclusion.File.Replace('\\', Path.DirectorySeparatorChar)
                                                   .Replace('/', Path.DirectorySeparatorChar);
                }
            }
            
            return exclusionFile;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WARNING: Failed to load exclusion file: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Saves exclusion file to the schema directory
    /// </summary>
    void SaveExclusionFile(string exclusionFilePath, ExclusionFile exclusionFile)
    {
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(exclusionFile, options);
            File.WriteAllText(exclusionFilePath, json);
            
            Console.WriteLine($"  ✓ Saved exclusion file: {Path.GetFileName(exclusionFilePath)}");
            Console.WriteLine($"    Total exclusions: {exclusionFile.Exclusions.Count}");
            
            // Group by error code for summary
            var byErrorCode = exclusionFile.Exclusions.GroupBy(e => e.ErrorCode);
            foreach (var group in byErrorCode)
            {
                Console.WriteLine($"    - {group.Key}: {group.Count()} files");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WARNING: Failed to save exclusion file: {ex.Message}");
        }
    }

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

/// <summary>
/// Represents a single excluded file entry in the DACPAC build exclusion file
/// </summary>
public class ExclusionEntry
{
    public string File { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime ExcludedOn { get; set; }
    public int Iteration { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
}

/// <summary>
/// Represents the DACPAC build exclusion file structure
/// </summary>
public class ExclusionFile
{
    public string Version { get; set; } = "1.0";
    public DateTime Generated { get; set; }
    public DateTime LastSuccessfulBuild { get; set; }
    public List<ExclusionEntry> Exclusions { get; set; } = new();
}
