using System.CommandLine;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
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
        var rootCommand = new RootCommand("SQL Server DACPAC Structure Generator - Extracts database schema and generates file-based representation");
        
        // Define options - now optional when SCMP is provided
        var sourceConnectionOption = new Option<string?>(
            aliases: new[] { "--source-connection" },
            description: "Source database connection string (optional if SCMP file is provided)"
        ) { IsRequired = false };
        
        var targetServerOption = new Option<string?>(
            aliases: new[] { "--target-server" },
            description: "Target server name for output directory (optional if SCMP file is provided)"
        ) { IsRequired = false };
        
        var targetDatabaseOption = new Option<string?>(
            aliases: new[] { "--target-database" },
            description: "Target database name for output directory (optional if SCMP file is provided)"
        ) { IsRequired = false };
        
        var outputPathOption = new Option<string>(
            aliases: new[] { "--output-path" },
            description: "Output directory path for generated schema files"
        ) { IsRequired = true };
        
        var commitMessageOption = new Option<string?>(
            aliases: new[] { "--commit-message" },
            description: "Custom commit message for migration generation (optional)"
        ) { IsRequired = false };
        
        // Define skip exclusion manager option
        var skipExclusionManagerOption = new Option<bool>(
            aliases: new[] { "--skip-exclusion-manager" },
            description: "Skip running the exclusion manager after schema extraction"
        ) { IsRequired = false };
        
        // Define SCMP file option for exclusion management
        var scmpFileOption = new Option<string?>(
            aliases: new[] { "--scmp" },
            description: "Path to SCMP file containing comparison settings and exclusions (optional)"
        ) { IsRequired = false };
        
        // Define source password option for SCMP scenarios
        var sourcePasswordOption = new Option<string?>(
            aliases: new[] { "--source-password" },
            description: "Password for source database connection (required when using SCMP file with SQL authentication)"
        ) { IsRequired = false };
        
        // Add options to root command
        rootCommand.AddOption(sourceConnectionOption);
        rootCommand.AddOption(targetServerOption);
        rootCommand.AddOption(targetDatabaseOption);
        rootCommand.AddOption(outputPathOption);
        rootCommand.AddOption(commitMessageOption);
        rootCommand.AddOption(skipExclusionManagerOption);
        rootCommand.AddOption(scmpFileOption);
        rootCommand.AddOption(sourcePasswordOption);
        
        // Set handler - now with SCMP support and optional source/target parameters
        rootCommand.SetHandler(async (string? sourceConnection, string? targetServer, string? targetDatabase, string outputPath, string? commitMessage, bool skipExclusionManager, string? scmpFile, string? sourcePassword) =>
        {
            await RunDacpacExtraction(sourceConnection, targetServer, targetDatabase, outputPath, commitMessage, skipExclusionManager, scmpFile, sourcePassword);
        }, sourceConnectionOption, targetServerOption, targetDatabaseOption, outputPathOption, commitMessageOption, skipExclusionManagerOption, scmpFileOption, sourcePasswordOption);
        
        // Named parameters are now required - no positional argument support
        return await rootCommand.InvokeAsync(args);
    }
    
    static async Task RunDacpacExtraction(string? sourceConnectionString, string? targetServer, string? targetDatabase, string outputPath, string? commitMessage = null, bool skipExclusionManager = false, string? scmpFilePath = null, string? sourcePassword = null)
    {
        // Store the loaded SCMP comparison for later use
        SchemaComparison? scmpComparison = null;
        
        // If SCMP file is provided, extract connection information from it
        if (!string.IsNullOrEmpty(scmpFilePath))
        {
            Console.WriteLine($"Loading SCMP file to extract connection information: {scmpFilePath}");
            
            try
            {
                var scmpHandler = new ScmpManifestHandler();
                scmpComparison = await scmpHandler.LoadManifestAsync(scmpFilePath);
                
                if (scmpComparison != null)
                {
                    // Extract server and database information from SCMP
                    var (scmpSourceServer, scmpTargetServer) = scmpHandler.GetServerInfo(scmpComparison);
                    var (scmpSourceDb, scmpTargetDb) = scmpHandler.GetDatabaseInfo(scmpComparison);
                    
                    // If parameters weren't provided explicitly, use values from SCMP
                    if (string.IsNullOrEmpty(sourceConnectionString))
                    {
                        // Get connection string from SCMP source
                        sourceConnectionString = scmpComparison.SourceModelProvider?.ConnectionBasedModelProvider?.ConnectionString;
                        
                        // If a password was provided separately, update the connection string
                        if (!string.IsNullOrEmpty(sourceConnectionString) && !string.IsNullOrEmpty(sourcePassword))
                        {
                            var builder = new SqlConnectionStringBuilder(sourceConnectionString);
                            
                            // Only update password if the connection string uses SQL authentication
                            if (!builder.IntegratedSecurity)
                            {
                                builder.Password = sourcePassword;
                                sourceConnectionString = builder.ConnectionString;
                                Console.WriteLine($"Using source connection from SCMP with provided password: {scmpSourceServer}/{scmpSourceDb}");
                            }
                            else
                            {
                                Console.WriteLine($"Using source connection from SCMP (Windows auth): {scmpSourceServer}/{scmpSourceDb}");
                            }
                        }
                        else if (!string.IsNullOrEmpty(sourceConnectionString))
                        {
                            Console.WriteLine($"Using source connection from SCMP: {scmpSourceServer}/{scmpSourceDb}");
                        }
                    }
                    
                    if (string.IsNullOrEmpty(targetServer))
                    {
                        targetServer = scmpTargetServer;
                        if (!string.IsNullOrEmpty(targetServer))
                        {
                            Console.WriteLine($"Using target server from SCMP: {targetServer}");
                        }
                    }
                    
                    if (string.IsNullOrEmpty(targetDatabase))
                    {
                        targetDatabase = scmpTargetDb;
                        if (!string.IsNullOrEmpty(targetDatabase))
                        {
                            Console.WriteLine($"Using target database from SCMP: {targetDatabase}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"⚠ Warning: Could not load SCMP file: {scmpFilePath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Warning: Error loading SCMP file: {ex.Message}");
                // Continue with explicitly provided parameters if SCMP load fails
            }
        }
        
        // Validate that we have all required parameters (either from CLI or SCMP)
        if (string.IsNullOrEmpty(sourceConnectionString))
        {
            Console.WriteLine("❌ Error: Source connection string is required. Provide it via --source-connection or through an SCMP file.");
            Environment.Exit(1);
        }
        
        if (string.IsNullOrEmpty(targetServer))
        {
            Console.WriteLine("❌ Error: Target server is required. Provide it via --target-server or through an SCMP file.");
            Environment.Exit(1);
        }
        
        if (string.IsNullOrEmpty(targetDatabase))
        {
            Console.WriteLine("❌ Error: Target database is required. Provide it via --target-database or through an SCMP file.");
            Environment.Exit(1);
        }
        
        // Sanitize target server name for use in folder names
        var sanitizedTargetServer = targetServer.Replace('\\', '-').Replace(':', '-');
        
        // Log the resolved server name and target path
        Console.WriteLine($"Target Server: {targetServer}");
        Console.WriteLine($"Target Server (sanitized): {sanitizedTargetServer}");
        Console.WriteLine($"Target Database: {targetDatabase}");
        Console.WriteLine($"Target Path: servers/{sanitizedTargetServer}/{targetDatabase}/");

        // Configure Git safe directory for Docker environments
        ConfigureGitSafeDirectory(outputPath);

        try
        {
            // Log environment information for debugging
            Console.WriteLine("=== Environment Information ===");
            Console.WriteLine($"Current Directory: {Environment.CurrentDirectory}");
            Console.WriteLine($"Output Path: {outputPath}");
            Console.WriteLine($"GitHub Workspace: {Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? "Not set"}");
            Console.WriteLine($"GitHub Repository: {Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ?? "Not set"}");
            Console.WriteLine($"Running in GitHub Actions: {!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"))}");
            
            // Ensure we're on origin/main with clean state before generating files
            var gitAnalyzer = new GitDiffAnalyzer();
            if (gitAnalyzer.IsGitRepository(outputPath))
            {
                try
                {
                    Console.WriteLine("=== Preparing Git repository for schema extraction ===");
                    
                    // Use the enhanced CheckoutBranch method which includes hard reset
                    var checkoutResult = gitAnalyzer.CheckoutBranch(outputPath, "origin/main");
                    if (!checkoutResult.success)
                    {
                        Console.WriteLine($"❌ CRITICAL: Git operation failed: {checkoutResult.message}");
                        Console.WriteLine("Cannot continue without proper git configuration.");
                        throw new InvalidOperationException($"Git branch setup failed: {checkoutResult.message}");
                    }
                    else
                    {
                        Console.WriteLine(checkoutResult.message);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ CRITICAL: Could not prepare Git repository: {ex.Message}");
                    // Re-throw to stop execution
                    throw;
                }
            }
            
            // Extract database to DACPAC
            Console.WriteLine("Extracting database to DACPAC...");
            var dacpacPath = Path.Combine(Path.GetTempPath(), "temp_database.dacpac");
            var dacServices = new DacServices(sourceConnectionString);
            
            // Extract source database name from source connection string
            var sourceBuilder = new SqlConnectionStringBuilder(sourceConnectionString);
            var sourceDatabaseName = sourceBuilder.InitialCatalog;

            try
            {
                // Configure extract options to include extended properties
                var extractOptions = new DacExtractOptions
                {
                    ExtractAllTableData = false,
                    IgnoreExtendedProperties = false,  // Include extended properties (column descriptions)
                    IgnorePermissions = true,
                    IgnoreUserLoginMappings = true
                };
                
                dacServices.Extract(dacpacPath, sourceDatabaseName, "DacpacStructureGenerator", new Version(1, 0), null, null, extractOptions);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                throw;
            }
            
            Console.WriteLine($"DACPAC extracted successfully to: {dacpacPath}");

            // Load the DACPAC
            var dacpac = DacPackage.Load(dacpacPath);
            
            // Configure deployment options
            DacDeployOptions deployOptions;
            
            // If SCMP was loaded, use its configuration options
            if (scmpComparison != null)
            {
                Console.WriteLine("Using deployment options from SCMP file...");
                var mapper = new ScmpToDeployOptions();
                deployOptions = mapper.MapOptions(scmpComparison);
                
                // Ensure we're generating full schema scripts
                deployOptions.CreateNewDatabase = true;
                
                // Log key settings
                Console.WriteLine($"  Block on data loss: {deployOptions.BlockOnPossibleDataLoss}");
                Console.WriteLine($"  Drop objects not in source: {deployOptions.DropObjectsNotInSource}");
                Console.WriteLine($"  Ignore permissions: {deployOptions.IgnorePermissions}");
            }
            else
            {
                // Use default conservative options
                deployOptions = new DacDeployOptions
                {
                    CreateNewDatabase = true,
                    IgnorePermissions = true,
                    IgnoreUserSettingsObjects = true,
                    IgnoreLoginSids = true,
                    IgnoreRoleMembership = true,
                    IgnoreExtendedProperties = false,  // Include column descriptions and other extended properties
                    ExcludeObjectTypes =
                    [
                        Microsoft.SqlServer.Dac.ObjectType.Users,
                        Microsoft.SqlServer.Dac.ObjectType.Logins,
                        Microsoft.SqlServer.Dac.ObjectType.RoleMembership,
                        Microsoft.SqlServer.Dac.ObjectType.Permissions
                    ]
                };
            }
            
            // Generate deployment script
            Console.WriteLine("Generating deployment script...");
            var script = dacServices.GenerateDeployScript(
                dacpac,
                sourceDatabaseName,
                deployOptions
            );
            
            // Save script for debugging
            await File.WriteAllTextAsync("generated_script.sql", script);
            Console.WriteLine($"Script saved to generated_script.sql ({script.Length} characters)");
            
            // Clean only the database-specific directory (preserving migrations and change manifests)
            var targetOutputPath = Path.Combine(outputPath, "servers", sanitizedTargetServer, targetDatabase);
            Console.WriteLine($"Full target output path: {targetOutputPath}");
            
            if (Directory.Exists(targetOutputPath))
            {
                Console.WriteLine($"Cleaning database directory: {targetOutputPath} (preserving z_migrations, z_migrations_reverse, and _change-manifests)");
                
                // Get all subdirectories except migrations and change manifests
                var subdirs = Directory.GetDirectories(targetOutputPath)
                    .Where(d => !Path.GetFileName(d).Equals("z_migrations", StringComparison.OrdinalIgnoreCase) &&
                               !Path.GetFileName(d).Equals("z_migrations_reverse", StringComparison.OrdinalIgnoreCase) &&
                               !Path.GetFileName(d).Equals("_change-manifests", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                // Delete each subdirectory
                foreach (var dir in subdirs)
                {
                    Directory.Delete(dir, recursive: true);
                }
                
                // Delete all files in the root (if any)
                foreach (var file in Directory.GetFiles(targetOutputPath))
                {
                    File.Delete(file);
                }
            }
            
            // Parse and organize the script into separate files
            Console.WriteLine("Parsing and organizing scripts...");
            var parser = new DacpacScriptParser();
            parser.ParseAndOrganizeScripts(script, outputPath, sanitizedTargetServer, targetDatabase);
            
            // Clean up temporary script file
            if (File.Exists("generated_script.sql"))
            {
                File.Delete("generated_script.sql");
            }
            
            // Clean up temp DACPAC file
            if (File.Exists(dacpacPath))
            {
                File.Delete(dacpacPath);
            }
            
            Console.WriteLine($"Database structure generated successfully at: {targetOutputPath}");
            
            // Generate migrations using DACPAC comparison
            Console.WriteLine("\nGenerating DACPAC-based migrations...");
            var migrationGenerator = new Migration.Generator.DacpacMigrationGenerator();
            var migrationsPath = Path.Combine(targetOutputPath, "z_migrations");
            
            // Ensure migrations directory exists
            Directory.CreateDirectory(migrationsPath);
            
            // Get actor from environment variable (GitHub Actions provides GITHUB_ACTOR)
            var actor = Environment.GetEnvironmentVariable("GITHUB_ACTOR") ?? Environment.UserName;
            
            // Generate migration using DACPAC comparison (committed vs uncommitted)
            var migrationResult = await migrationGenerator.GenerateMigrationAsync(
                outputPath,
                sanitizedTargetServer,
                targetDatabase,
                migrationsPath,
                scmpComparison,  // Pass SCMP comparison for settings
                actor,
                validateMigration: true,
                connectionString: sourceConnectionString);
            
            var changesDetected = migrationResult.Success && migrationResult.HasChanges;
            Console.WriteLine(changesDetected ? $"✓ Migration files generated in: {migrationsPath}" : "No schema changes detected.");
            
            // Run Exclusion Manager to create/update manifest and apply exclusions (unless skipped)
            if (!skipExclusionManager && scmpComparison != null)
            {
                // Source server and database are already extracted above (sourceBuilder, sourceDatabaseName)
                var sourceServer = sourceBuilder.DataSource.Replace('\\', '-').Replace(':', '-'); // Sanitize for folder names
                var sourceDatabase = sourceDatabaseName;
                
                Console.WriteLine("\n=== Managing Exclusions from SCMP ===");
                Console.WriteLine($"Source: {sourceServer}/{sourceDatabase}");
                Console.WriteLine($"Target: {targetServer}/{targetDatabase}");
                
                try
                {
                    var scmpHandler = new ScmpManifestHandler();
                    
                    // Extract exclusions from the already-loaded SCMP comparison
                    var excludedObjects = scmpHandler.GetExcludedObjects(scmpComparison);
                    
                    if (excludedObjects.Any())
                    {
                        Console.WriteLine($"Found {excludedObjects.Count} excluded objects in SCMP file:");
                        foreach (var obj in excludedObjects.Take(10))
                        {
                            Console.WriteLine($"  - {obj}");
                        }
                        if (excludedObjects.Count > 10)
                        {
                            Console.WriteLine($"  ... and {excludedObjects.Count - 10} more");
                        }
                        
                        // Apply exclusions to generated SQL files
                        Console.WriteLine("\nApplying exclusions to generated SQL files...");
                        
                        // Parse excluded objects and apply exclusion comments
                        foreach (var excludedObj in excludedObjects)
                        {
                            // Remove the (Source) or (Target) suffix to get the actual object name
                            var objectName = excludedObj.Replace(" (Source)", "").Replace(" (Target)", "").Trim();
                            
                            // Split the object name (e.g., "dbo.TableName" or "dbo.sp_ProcedureName")
                            var parts = objectName.Split('.');
                            if (parts.Length >= 2)
                            {
                                var schema = parts[0];
                                var objName = string.Join(".", parts.Skip(1));
                                
                                // Try to find and mark the corresponding file
                                var sqlFilePath = FindSqlFileForObject(targetOutputPath, schema, objName);
                                if (!string.IsNullOrEmpty(sqlFilePath) && File.Exists(sqlFilePath))
                                {
                                    await ApplyExclusionToFile(sqlFilePath, objectName);
                                }
                            }
                        }
                        
                        Console.WriteLine("✓ Exclusions applied to SQL files");
                    }
                    else
                    {
                        Console.WriteLine("No exclusions found in SCMP file");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠ Error applying SCMP exclusions: {ex.Message}");
                    Console.WriteLine("Continuing without exclusion management...");
                }
            }
            else if (!skipExclusionManager)
            {
                Console.WriteLine("\n=== Exclusion Manager Skipped ===");
                Console.WriteLine("No SCMP file provided - exclusion management skipped");
                Console.WriteLine("Use --scmp parameter to provide an SCMP file with exclusions");
            }
            else
            {
                Console.WriteLine("\n=== Exclusion Manager Skipped ===");
                Console.WriteLine("Exclusion manager was skipped as requested via --skip-exclusion-manager flag");
            }
            
            // Now commit all changes together (schema files, migrations, and manifest)
            if (changesDetected || gitAnalyzer.GetUncommittedChanges(outputPath, "").Any())
            {
                var commitMsg = !string.IsNullOrWhiteSpace(commitMessage) 
                    ? commitMessage 
                    : "Schema update with migrations and exclusions";
                    
                Console.WriteLine($"\n📝 Committing all changes: {commitMsg}");
                gitAnalyzer.CommitChanges(outputPath, commitMsg);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }
    
    static void ConfigureGitSafeDirectory(string path)
    {
        try
        {
            // Configure Git to trust the output directory and common Docker mount points
            var directories = new[] { path, "/workspace", "/output", "." };
            
            foreach (var dir in directories)
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
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