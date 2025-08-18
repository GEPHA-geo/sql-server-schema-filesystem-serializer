using System.CommandLine;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Compare;
using Microsoft.SqlServer.Dac.Model;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Core;
using SqlServer.Schema.Migration.Generator.GitIntegration;
using SqlServer.Schema.Exclusion.Manager.Core.Services;
using SqlServer.Schema.Exclusion.Manager.Core.Models;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner;

internal record MigrationValidationResult(bool IsValid, string? ErrorMessage = null);

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("SQL Server SCMP-based DACPAC Tool - Generates database comparisons and migrations using SCMP files");
        
        // SCMP file is now REQUIRED
        var scmpFileOption = new Option<string>(
            aliases: new[] { "--scmp" },
            description: "Path to SCMP file containing comparison settings and exclusions (REQUIRED)"
        ) { IsRequired = true };
        
        // Required passwords for source and target databases
        var sourcePasswordOption = new Option<string>(
            aliases: new[] { "--source-password" },
            description: "Password for source database connection (REQUIRED)"
        ) { IsRequired = true };
        
        var targetPasswordOption = new Option<string>(
            aliases: new[] { "--target-password" },
            description: "Password for target database connection (REQUIRED)"
        ) { IsRequired = true };
        
        // Output path is required
        var outputPathOption = new Option<string>(
            aliases: new[] { "--output-path" },
            description: "Output directory path for generated files (REQUIRED)"
        ) { IsRequired = true };
        
        // Optional parameters
        var commitMessageOption = new Option<string?>(
            aliases: new[] { "--commit-message" },
            description: "Custom commit message (optional)"
        ) { IsRequired = false };
        
        var skipExclusionManagerOption = new Option<bool>(
            aliases: new[] { "--skip-exclusion-manager" },
            description: "Skip exclusion manager step (optional)"
        ) { IsRequired = false };
        
        // Add options to root command
        rootCommand.AddOption(scmpFileOption);
        rootCommand.AddOption(sourcePasswordOption);
        rootCommand.AddOption(targetPasswordOption);
        rootCommand.AddOption(outputPathOption);
        rootCommand.AddOption(commitMessageOption);
        rootCommand.AddOption(skipExclusionManagerOption);
        
        // Set handler with all required parameters
        rootCommand.SetHandler(async (string scmpFile, string sourcePassword, string targetPassword, string outputPath, string? commitMessage, bool skipExclusionManager) =>
        {
            await RunDacpacExtraction(scmpFile, sourcePassword, targetPassword, outputPath, commitMessage);
        }, scmpFileOption, sourcePasswordOption, targetPasswordOption, outputPathOption, commitMessageOption, skipExclusionManagerOption);
        
        return await rootCommand.InvokeAsync(args);
    }
    
    static async Task RunDacpacExtraction(string scmpFilePath, string sourcePassword, string targetPassword, string outputPath, string? commitMessage = null)
    {
        try
        {
            Console.WriteLine("=== SCMP-Based DACPAC Tool ===");
            Console.WriteLine($"Loading SCMP file: {scmpFilePath}");
            
            // Configure Git safe directory for Docker environments
            ConfigureGitSafeDirectory(outputPath);
            
            // Load SCMP file using our custom handler to extract connection info
            var scmpHandler = new ScmpManifestHandler();
            var scmpModel = await scmpHandler.LoadManifestAsync(scmpFilePath);
            
            if (scmpModel == null)
            {
                Console.WriteLine("❌ Error: Failed to load SCMP file");
                Environment.Exit(1);
            }
            
            // Extract server and database information from SCMP
            var (sourceServer, targetServer) = scmpHandler.GetServerInfo(scmpModel);
            var (sourceDb, targetDb) = scmpHandler.GetDatabaseInfo(scmpModel);
            
            Console.WriteLine($"Source: {sourceServer}/{sourceDb}");
            Console.WriteLine($"Target: {targetServer}/{targetDb}");
            
            // Get connection strings from SCMP and update with provided passwords
            var sourceConnectionString = scmpModel.SourceModelProvider?.ConnectionBasedModelProvider?.ConnectionString;
            var targetConnectionString = scmpModel.TargetModelProvider?.ConnectionBasedModelProvider?.ConnectionString;
            
            if (string.IsNullOrEmpty(sourceConnectionString))
            {
                Console.WriteLine("❌ Error: Source connection string not found in SCMP file");
                Environment.Exit(1);
            }
            
            if (string.IsNullOrEmpty(targetConnectionString))
            {
                Console.WriteLine("❌ Error: Target connection string not found in SCMP file");
                Environment.Exit(1);
            }
            
            // Update connection strings with provided passwords
            var sourceBuilder = new SqlConnectionStringBuilder(sourceConnectionString);
            if (!sourceBuilder.IntegratedSecurity)
            {
                sourceBuilder.Password = sourcePassword;
            }
            sourceConnectionString = sourceBuilder.ConnectionString;
            
            var targetBuilder = new SqlConnectionStringBuilder(targetConnectionString);
            if (!targetBuilder.IntegratedSecurity)
            {
                targetBuilder.Password = targetPassword;
            }
            targetConnectionString = targetBuilder.ConnectionString;
            
            // Sanitize server names for folder paths
            var sanitizedTargetServer = targetServer?.Replace('\\', '-').Replace(':', '-') ?? "unknown-server";
            
            // Set up output paths
            var targetOutputPath = Path.Combine(outputPath, "servers", sanitizedTargetServer, targetDb ?? "unknown-db");
            Directory.CreateDirectory(targetOutputPath);
            
            Console.WriteLine($"Output path: {targetOutputPath}");
            
            // Initialize Git repository state
            var gitAnalyzer = new GitDiffAnalyzer();
            if (gitAnalyzer.IsGitRepository(outputPath))
            {
                Console.WriteLine("=== Preparing Git repository ===");
                var checkoutResult = gitAnalyzer.CheckoutBranch(outputPath, "origin/main");
                if (!checkoutResult.success)
                {
                    Console.WriteLine($"❌ Git operation failed: {checkoutResult.message}");
                    throw new InvalidOperationException($"Git branch setup failed: {checkoutResult.message}");
                }
                Console.WriteLine(checkoutResult.message);
            }
            
            // === Phase 1: Build Target Filesystem DACPAC ===
            Console.WriteLine("\n=== Phase 1: Building Target Filesystem DACPAC ===");
            // Create descriptive DACPAC filename with server and database names
            var targetServerClean = targetServer?.Replace("\\", "_").Replace(".", "_").Replace(":", "_") ?? "unknown";
            var targetDbClean = targetDb?.Replace(" ", "_") ?? "unknown";
            var targetFilesystemDacpac = Path.Combine(targetOutputPath, $"{targetServerClean}_{targetDbClean}_filesystem.dacpac");
            
            // Create git worktree of committed state
            var worktreePath = Path.Combine(Path.GetTempPath(), $"worktree_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
            try
            {
                // Create worktree from origin/main
                var worktreeProcess = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = $"worktree add \"{worktreePath}\" origin/main",
                        WorkingDirectory = outputPath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                worktreeProcess.Start();
                var worktreeOutput = await worktreeProcess.StandardOutput.ReadToEndAsync();
                var worktreeError = await worktreeProcess.StandardError.ReadToEndAsync();
                worktreeProcess.WaitForExit();
                
                if (worktreeProcess.ExitCode != 0)
                {
                    Console.WriteLine($"⚠ Could not create worktree: {worktreeError}");
                    Console.WriteLine("Using current filesystem state instead");
                    worktreePath = outputPath;
                }
                else
                {
                    Console.WriteLine("Created git worktree for committed state");
                }
                
                // Build DACPAC from worktree's target directory
                var worktreeTargetPath = Path.Combine(worktreePath, "servers", sanitizedTargetServer, targetDb ?? "unknown-db");
                if (Directory.Exists(worktreeTargetPath) && Directory.GetFiles(worktreeTargetPath, "*.sql", SearchOption.AllDirectories).Any())
                {
                    // Normalize line endings before building
                    var fsManager = new FileSystemManager();
                    fsManager.NormalizeDirectoryLineEndings(worktreeTargetPath);
                    
                    Console.WriteLine("Building DACPAC from committed filesystem state...");
                    var migrationGen = new Migration.Generator.DacpacMigrationGenerator();
                    var tempBuildPath = Path.Combine(Path.GetTempPath(), $"target-filesystem-build_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
                    
                    var builtDacpac = await migrationGen.BuildDacpacFromFileSystem(
                        worktreeTargetPath,
                        tempBuildPath,
                        "TargetFilesystem",
                        null
                    );
                    
                    if (!string.IsNullOrEmpty(builtDacpac) && File.Exists(builtDacpac))
                    {
                        File.Copy(builtDacpac, targetFilesystemDacpac, overwrite: true);
                        Console.WriteLine($"✓ Target filesystem DACPAC created: {Path.GetFileName(targetFilesystemDacpac)}");
                    }
                    else
                    {
                        CreateEmptyDacpacFile(targetFilesystemDacpac);
                        Console.WriteLine("✓ Created empty target filesystem DACPAC (no existing schema)");
                    }
                    
                    // Clean up temp directory unless debugger is attached
                    if (Directory.Exists(tempBuildPath) && !Debugger.IsAttached)
                    {
                        try
                        {
                            Directory.Delete(tempBuildPath, recursive: true);
                            Console.WriteLine($"  Cleaned up temp build directory");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  ⚠ Could not clean up temp directory: {ex.Message}");
                        }
                    }
                    else if (Debugger.IsAttached && Directory.Exists(tempBuildPath))
                    {
                        Console.WriteLine($"  Debug mode: Temp directory preserved at {tempBuildPath}");
                    }
                }
                else
                {
                    CreateEmptyDacpacFile(targetFilesystemDacpac);
                    Console.WriteLine("✓ Created empty target filesystem DACPAC (no existing schema)");
                }
            }
            finally
            {
                // Clean up worktree if it was created (unless debugger is attached)
                if (worktreePath != outputPath && Directory.Exists(worktreePath))
                {
                    if (!Debugger.IsAttached)
                    {
                        try
                        {
                            var removeProcess = new System.Diagnostics.Process
                            {
                                StartInfo = new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "git",
                                    Arguments = $"worktree remove \"{worktreePath}\" --force",
                                    WorkingDirectory = outputPath,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                }
                            };
                            removeProcess.Start();
                            removeProcess.WaitForExit();
                            
                            // Also try to delete the directory if git worktree remove failed
                            if (Directory.Exists(worktreePath))
                            {
                                Directory.Delete(worktreePath, recursive: true);
                            }
                            Console.WriteLine("  Cleaned up git worktree");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  ⚠ Could not clean up worktree: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  Debug mode: Worktree preserved at {worktreePath}");
                    }
                }
            }
            
            // === Phase 2: Extract Target Original DACPAC ===
            Console.WriteLine("\n=== Phase 2: Extracting Target Original DACPAC ===");
            var targetOriginalDacpac = Path.Combine(targetOutputPath, $"{targetServerClean}_{targetDbClean}_original.dacpac");
            
            var targetDacServices = new DacServices(targetConnectionString);
            var extractOptions = new DacExtractOptions
            {
                ExtractAllTableData = false,
                IgnoreExtendedProperties = false,
                IgnorePermissions = false,
                IgnoreUserLoginMappings = true
            };
            
            targetDacServices.Extract(targetOriginalDacpac, targetDb, "TargetOriginal", new Version(1, 0), null, null, extractOptions);
            Console.WriteLine($"✓ Target original DACPAC extracted: {Path.GetFileName(targetOriginalDacpac)}");
            
            // === Phase 3: Extract Source Original DACPAC ===
            Console.WriteLine("\n=== Phase 3: Extracting Source Original DACPAC ===");
            // Create descriptive source DACPAC filename
            var sourceServerClean = sourceServer?.Replace("\\", "_").Replace(".", "_").Replace(":", "_") ?? "unknown";
            var sourceDbClean = sourceDb?.Replace(" ", "_") ?? "unknown";
            var sourceOriginalDacpac = Path.Combine(targetOutputPath, $"{sourceServerClean}_{sourceDbClean}_original.dacpac");
            
            var sourceDacServices = new DacServices(sourceConnectionString);
            sourceDacServices.Extract(sourceOriginalDacpac, sourceDb, "SourceOriginal", new Version(1, 0), null, null, extractOptions);
            Console.WriteLine($"✓ Source original DACPAC extracted: {Path.GetFileName(sourceOriginalDacpac)}");
            
            // === Phase 4: Extract Source to Filesystem and Build Filesystem DACPAC ===
            Console.WriteLine("\n=== Phase 4: Extracting Source Schema and Building Filesystem DACPAC ===");
            
            // Generate deployment script from source DACPAC
            var sourceDacpac = DacPackage.Load(sourceOriginalDacpac);
            var deployOptions = new DacDeployOptions
            {
                CreateNewDatabase = true,
                IgnoreAuthorizer = true,
                IgnoreExtendedProperties = false,
                IgnorePermissions = false
            };
            
            var script = sourceDacServices.GenerateDeployScript(sourceDacpac, sourceDb, deployOptions, null);
            
            // Remove AUTHORIZATION clauses from CREATE SCHEMA statements
            if (script.Contains("AUTHORIZATION"))
            {
                var schemaAuthPattern = @"(CREATE\s+SCHEMA\s+\[[^\]]+\])\s+AUTHORIZATION\s+\[[^\]]+\]";
                script = System.Text.RegularExpressions.Regex.Replace(
                    script, 
                    schemaAuthPattern, 
                    "$1",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline
                );
            }
            
            // Clean the target directory before extraction (preserve migrations and special files)
            Console.WriteLine("Cleaning target directory before extraction...");
            if (Directory.Exists(targetOutputPath))
            {
                // Get all subdirectories except migrations, change manifests, and other special directories
                var subdirs = Directory.GetDirectories(targetOutputPath)
                    .Where(d => !Path.GetFileName(d).Equals("z_migrations", StringComparison.OrdinalIgnoreCase) &&
                               !Path.GetFileName(d).Equals("z_migrations_reverse", StringComparison.OrdinalIgnoreCase) &&
                               !Path.GetFileName(d).Equals("_change-manifests", StringComparison.OrdinalIgnoreCase))
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
                foreach (var file in Directory.GetFiles(targetOutputPath))
                {
                    var fileName = Path.GetFileName(file);
                    if (!fileName.Equals(".dacpac-exclusions.json", StringComparison.OrdinalIgnoreCase) &&
                        !fileName.EndsWith(".dacpac", StringComparison.OrdinalIgnoreCase))
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
            
            // Parse and extract to filesystem (overwrites current schemas/ folder)
            Console.WriteLine("Extracting source schema to filesystem...");
            var parser = new DacpacScriptParser();
            parser.ParseAndOrganizeScripts(script, outputPath, sanitizedTargetServer, targetDb ?? "unknown-db", 
                sourceServerClean, sourceDbClean);
            
            // Apply line ending normalization
            var targetSchemaPath = Path.Combine(outputPath, "servers", sanitizedTargetServer, targetDb ?? "unknown-db");
            var fileSystemManager = new FileSystemManager();
            fileSystemManager.NormalizeDirectoryLineEndings(targetSchemaPath);
            Console.WriteLine("✓ Line endings normalized");
            
            // Build source filesystem DACPAC
            var sourceFilesystemDacpac = Path.Combine(targetOutputPath, $"{sourceServerClean}_{sourceDbClean}_filesystem.dacpac");
            var migrationGenerator = new Migration.Generator.DacpacMigrationGenerator();
            var tempSourceBuildPath = Path.Combine(Path.GetTempPath(), $"source-filesystem-build_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
            
            var sourceBuiltDacpac = await migrationGenerator.BuildDacpacFromFileSystem(
                targetSchemaPath,
                tempSourceBuildPath,
                "SourceFilesystem",
                null
            );
            
            if (!string.IsNullOrEmpty(sourceBuiltDacpac) && File.Exists(sourceBuiltDacpac))
            {
                File.Copy(sourceBuiltDacpac, sourceFilesystemDacpac, overwrite: true);
                Console.WriteLine($"✓ Source filesystem DACPAC created: {Path.GetFileName(sourceFilesystemDacpac)}");
            }
            
            // Clean up temp directory unless debugger is attached
            if (Directory.Exists(tempSourceBuildPath) && !Debugger.IsAttached)
            {
                try
                {
                    Directory.Delete(tempSourceBuildPath, recursive: true);
                    Console.WriteLine($"  Cleaned up temp build directory");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠ Could not clean up temp directory: {ex.Message}");
                }
            }
            else if (Debugger.IsAttached && Directory.Exists(tempSourceBuildPath))
            {
                Console.WriteLine($"  Debug mode: Temp directory preserved at {tempSourceBuildPath}");
            }
            
            // === Phase 5: Schema Comparison using Official API ===
            Console.WriteLine("\n=== Phase 5: Running Schema Comparison ===");
            
            // Create comparison using Microsoft's official API
            var sourceEndpoint = new SchemaCompareDacpacEndpoint(sourceFilesystemDacpac);
            var targetEndpoint = new SchemaCompareDacpacEndpoint(targetFilesystemDacpac);
            
            // Create new comparison with DACPAC endpoints instead of loading SCMP
            // We'll use the SCMP settings from scmpModel for configuration
            var comparison = new Microsoft.SqlServer.Dac.Compare.SchemaComparison(sourceEndpoint, targetEndpoint);
            
            Console.WriteLine("Comparing schemas...");
            var comparisonResult = comparison.Compare();
            
            Console.WriteLine($"Found {comparisonResult.Differences.Count()} total differences");
            Console.WriteLine($"Included differences: {comparisonResult.Differences.Count(d => d.Included)}");
            Console.WriteLine($"Excluded differences: {comparisonResult.Differences.Count(d => !d.Included)}");
            
            // List excluded objects
            var excludedDifferences = comparisonResult.Differences.Where(d => !d.Included).ToList();
            if (excludedDifferences.Any())
            {
                Console.WriteLine("\nExcluded objects:");
                foreach (var diff in excludedDifferences)
                {
                    Console.WriteLine($"  - {diff.Name} ({diff.UpdateAction})");
                }
            }
            
            // Generate migration script
            var publishResult = comparisonResult.GenerateScript(targetDb);
            var migrationScript = publishResult.Script;
            
            if (!string.IsNullOrEmpty(publishResult.MasterScript))
            {
                migrationScript = publishResult.MasterScript + "\n" + migrationScript;
            }
            
            // Save migration to z_migrations directory
            var migrationsPath = Path.Combine(targetOutputPath, "z_migrations");
            Directory.CreateDirectory(migrationsPath);
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var actor = Environment.GetEnvironmentVariable("GITHUB_ACTOR") ?? Environment.UserName;
            var migrationFileName = $"_{timestamp}_{actor}_migration.sql";
            var migrationFilePath = Path.Combine(migrationsPath, migrationFileName);
            
            await File.WriteAllTextAsync(migrationFilePath, migrationScript);
            Console.WriteLine($"✓ Migration saved to: {migrationFileName}");
            
            // // Create reverse migration placeholder
            // var reverseMigrationsPath = Path.Combine(targetOutputPath, "z_migrations_reverse");
            // Directory.CreateDirectory(reverseMigrationsPath);
            // var reverseMigrationPath = Path.Combine(reverseMigrationsPath, migrationFileName);
            // await File.WriteAllTextAsync(reverseMigrationPath, "-- Reverse migration placeholder\n-- Manual review required for rollback script");
            //
            // Commit changes if any
            if (gitAnalyzer.GetUncommittedChanges(outputPath, "").Count != 0)
            {
                var commitMsg = !string.IsNullOrWhiteSpace(commitMessage) 
                    ? commitMessage 
                    : "Schema update with migrations";
                    
                Console.WriteLine($"\n📝 Committing changes: {commitMsg}");
                gitAnalyzer.CommitChanges(outputPath, commitMsg);
            }
            
            Console.WriteLine("\n✓ SCMP-based extraction completed successfully");
            Console.WriteLine($"  - 4 DACPACs generated in: {targetOutputPath}");
            Console.WriteLine($"  - Migration script generated in: z_migrations/");
            Console.WriteLine($"  - Source schema extracted to: schemas/");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }
    
    static void CreateEmptyDacpacFile(string path)
    {
        // Create a minimal empty DACPAC
        var metadata = new PackageMetadata 
        { 
            Name = "Empty", 
            Version = "1.0.0.0" 
        };
        
        var model = new TSqlModel(SqlServerVersion.Sql150, new TSqlModelOptions());
        DacPackageExtensions.BuildPackage(path, model, metadata);
    }
    
    static void ConfigureGitSafeDirectory(string path)
    {
        try
        {
            // Configure Git to trust the output directory and common Docker mount points
            var directories = new[] { path, "/workspace", "/output", "." };
            
            foreach (var dir in directories)
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = $"config --global --add safe.directory {dir}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                process.WaitForExit();
            }
        }
        catch
        {
            // Ignore Git configuration errors - it's not critical for DACPAC extraction
            // This is just to help with migration generation later
        }
    }
    
    static string? FindSqlFileForObject(string basePath, string schema, string objectName)
    {
        // Common object type mappings
        var possiblePaths = new List<string>
        {
            Path.Combine(basePath, "tables", schema, $"{objectName}.sql"),
            Path.Combine(basePath, "views", schema, $"{objectName}.sql"),
            Path.Combine(basePath, "stored-procedures", schema, $"{objectName}.sql"),
            Path.Combine(basePath, "functions", schema, $"{objectName}.sql"),
            Path.Combine(basePath, "triggers", schema, $"{objectName}.sql"),
            Path.Combine(basePath, "indexes", schema, $"{objectName}.sql"),
            Path.Combine(basePath, "synonyms", schema, $"{objectName}.sql"),
            Path.Combine(basePath, "sequences", schema, $"{objectName}.sql"),
            Path.Combine(basePath, "types", schema, $"{objectName}.sql"),
            Path.Combine(basePath, "schemas", $"{schema}.sql")
        };
        
        // Find the first existing file
        return possiblePaths.FirstOrDefault(File.Exists);
    }
    
    static async Task ApplyExclusionToFile(string filePath, string objectName)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            
            // Check if already excluded
            if (content.StartsWith("-- EXCLUDED:"))
            {
                Console.WriteLine($"  ✓ {objectName} already marked as excluded");
                return;
            }
            
            // Add exclusion comment at the beginning
            var excludedContent = $"-- EXCLUDED: {objectName}\n-- This object is excluded from deployment based on SCMP configuration\n-- Remove this comment to include the object in deployments\n\n{content}";
            
            await File.WriteAllTextAsync(filePath, excludedContent);
            Console.WriteLine($"  ✓ Marked {objectName} as excluded in {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠ Could not apply exclusion to {objectName}: {ex.Message}");
        }
    }
    
    static MigrationValidationResult ValidateMigrationGeneration(
        string outputPath, 
        string sanitizedTargetServer, 
        string targetDatabase,
        string migrationsPath,
        bool migrationExpected)
    {
        try
        {
            var gitAnalyzer = new GitDiffAnalyzer();
            var dbPath = Path.Combine("servers", sanitizedTargetServer, targetDatabase);
            
            // Get list of migration files created in this run
            var migrationFiles = Directory.GetFiles(migrationsPath, "*.sql")
                .Where(f => !Path.GetFileName(f).StartsWith("_00000000_000000_")) // Exclude bootstrap migration
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ToList();
                
            var reverseMigrationsPath = Path.Combine(Path.GetDirectoryName(migrationsPath)!, "z_migrations_reverse");
            var reverseMigrationFiles = Directory.Exists(reverseMigrationsPath) 
                ? Directory.GetFiles(reverseMigrationsPath, "*.sql")
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .ToList()
                : new List<string>();
            
            // Check most recent migration (if any)
            var recentMigration = migrationFiles.FirstOrDefault();
            var recentReverseMigration = reverseMigrationFiles.FirstOrDefault();
            
            // Check if a fresh migration was created (within last 30 seconds to account for processing time)
            var freshMigrationCreated = recentMigration != null && 
                (DateTime.UtcNow - File.GetLastWriteTimeUtc(recentMigration)).TotalSeconds <= 30;
            
            // Validation rules:
            // 1. If migration was expected but no fresh migration was created
            if (migrationExpected && !freshMigrationCreated)
            {
                return new MigrationValidationResult(false, 
                    "Migration generation was reported as successful but no fresh migration file was found.");
            }
            
            // 2. If no migration was expected but a fresh migration exists
            if (!migrationExpected && freshMigrationCreated)
            {
                return new MigrationValidationResult(false, 
                    $"No migration was expected but a fresh migration file was created: {Path.GetFileName(recentMigration)}");
            }
            
            // 3. If migration was generated, perform additional checks
            if (migrationExpected && freshMigrationCreated && recentMigration != null)
            {
                // Check that reverse migration exists with same filename
                var migrationFileName = Path.GetFileName(recentMigration);
                var expectedReversePath = Path.Combine(reverseMigrationsPath, migrationFileName);
                if (!File.Exists(expectedReversePath))
                {
                    return new MigrationValidationResult(false, 
                        $"Reverse migration file not found. Expected: {expectedReversePath}");
                }
                
                // Check reverse migration was also created recently
                var reverseAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(expectedReversePath);
                if (reverseAge.TotalSeconds > 30)
                {
                    return new MigrationValidationResult(false, 
                        $"Reverse migration file exists but appears to be old (created {reverseAge.TotalSeconds:F1} seconds ago).");
                }
            }
            
            return new MigrationValidationResult(true);
        }
        catch (Exception ex)
        {
            return new MigrationValidationResult(false, $"Validation error: {ex.Message}");
        }
    }
}