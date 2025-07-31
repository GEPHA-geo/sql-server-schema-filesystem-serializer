using System.CommandLine;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Core;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner;

internal record MigrationValidationResult(bool IsValid, string? ErrorMessage = null);

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("SQL Server DACPAC Structure Generator - Extracts database schema and generates file-based representation");
        
        // Define required options
        var sourceConnectionOption = new Option<string>(
            aliases: new[] { "--source-connection", "-s" },
            description: "Source database connection string"
        ) { IsRequired = true };
        
        var targetConnectionOption = new Option<string>(
            aliases: new[] { "--target-connection", "-t" },
            description: "Target database connection string (used for organizing output)"
        ) { IsRequired = true };
        
        var outputPathOption = new Option<string>(
            aliases: new[] { "--output-path", "-o" },
            description: "Output directory path for generated schema files"
        ) { IsRequired = true };
        
        var commitMessageOption = new Option<string?>(
            aliases: new[] { "--commit-message", "-m" },
            description: "Custom commit message for migration generation (optional)"
        ) { IsRequired = false };
        
        // Add options to root command
        rootCommand.AddOption(sourceConnectionOption);
        rootCommand.AddOption(targetConnectionOption);
        rootCommand.AddOption(outputPathOption);
        rootCommand.AddOption(commitMessageOption);
        
        // Set handler
        rootCommand.SetHandler(async (string sourceConnection, string targetConnection, string outputPath, string? commitMessage) =>
        {
            await RunDacpacExtraction(sourceConnection, targetConnection, outputPath, commitMessage);
        }, sourceConnectionOption, targetConnectionOption, outputPathOption, commitMessageOption);
        
        // Support legacy positional arguments for backward compatibility
        if (args.Length >= 3 && !args.Any(arg => arg.StartsWith("-")))
        {
            // Convert positional arguments to named options
            var newArgs = new List<string>
            {
                "--source-connection", args[0],
                "--target-connection", args[1],
                "--output-path", args[2]
            };
            
            // Add optional commit message if provided as 4th parameter
            if (args.Length >= 4)
            {
                newArgs.Add("--commit-message");
                newArgs.Add(args[3]);
            }
            
            args = newArgs.ToArray();
        }
        
        return await rootCommand.InvokeAsync(args);
    }
    
    static async Task RunDacpacExtraction(string sourceConnectionString, string targetConnectionString, string outputPath, string? commitMessage = null)
    {
        // Extract target server and database from target connection string
        var targetBuilder = new SqlConnectionStringBuilder(targetConnectionString);
        var targetServer = targetBuilder.DataSource.Replace('\\', '-').Replace(':', '-'); // Sanitize for folder names
        var targetDatabase = targetBuilder.InitialCatalog;
        
        // Log the resolved server name and target path
        Console.WriteLine($"Target Server (from connection): {targetBuilder.DataSource}");
        Console.WriteLine($"Target Server (sanitized): {targetServer}");
        Console.WriteLine($"Target Database: {targetDatabase}");
        Console.WriteLine($"Target Path: servers/{targetServer}/{targetDatabase}/");

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
            
            // Configure deployment options to generate full schema scripts
            var deployOptions = new DacDeployOptions
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
            
            // Clean only the database-specific directory (preserving migrations)
            var targetOutputPath = Path.Combine(outputPath, "servers", targetServer, targetDatabase);
            Console.WriteLine($"Full target output path: {targetOutputPath}");
            
            if (Directory.Exists(targetOutputPath))
            {
                Console.WriteLine($"Cleaning database directory: {targetOutputPath} (preserving z_migrations and z_migrations_reverse)");
                
                // Get all subdirectories except migrations
                var subdirs = Directory.GetDirectories(targetOutputPath)
                    .Where(d => !Path.GetFileName(d).Equals("z_migrations", StringComparison.OrdinalIgnoreCase) &&
                               !Path.GetFileName(d).Equals("z_migrations_reverse", StringComparison.OrdinalIgnoreCase))
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
            parser.ParseAndOrganizeScripts(script, outputPath, targetServer, targetDatabase);
            
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
            
            // Generate migrations
            Console.WriteLine("\nChecking for schema changes...");
            var migrationGenerator = new Migration.Generator.MigrationGenerator();
            var migrationsPath = Path.Combine(targetOutputPath, "z_migrations");
            
            // Get actor from environment variable (GitHub Actions provides GITHUB_ACTOR)
            var actor = Environment.GetEnvironmentVariable("GITHUB_ACTOR") ?? Environment.UserName;
            
            // Pass connection string for validation (use source for validation)
            var changesDetected = await migrationGenerator.GenerateMigrationsAsync(
                outputPath, 
                targetServer,
                targetDatabase, 
                migrationsPath,
                actor,
                sourceConnectionString,  // Use source connection for validation
                validateMigration: true,
                customCommitMessage: commitMessage);

            Console.WriteLine(changesDetected ? $"Migration files generated in: {migrationsPath}" : "No schema changes detected.");
            
            // Validate migration generation consistency
            var validationResult = ValidateMigrationGeneration(outputPath, targetServer, targetDatabase, migrationsPath, changesDetected);
            if (!validationResult.IsValid)
            {
                Console.WriteLine($"❌ Migration validation failed: {validationResult.ErrorMessage}");
                Environment.Exit(1);
            }
            else
            {
                Console.WriteLine("✓ Migration validation passed");
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
    
    static MigrationValidationResult ValidateMigrationGeneration(
        string outputPath, 
        string targetServer, 
        string targetDatabase,
        string migrationsPath,
        bool migrationExpected)
    {
        try
        {
            var gitAnalyzer = new GitDiffAnalyzer();
            var dbPath = Path.Combine("servers", targetServer, targetDatabase);
            
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