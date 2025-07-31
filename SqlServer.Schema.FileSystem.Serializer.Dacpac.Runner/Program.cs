using System.CommandLine;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Core;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner;

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
        
        // Define optional commit message option
        var commitMessageOption = new Option<string?>(
            aliases: new[] { "--commit-message", "-m" },
            description: "Custom git commit message for database structure changes"
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
            
            if (args.Length >= 4)
            {
                newArgs.Add("--commit-message");
                newArgs.Add(args[3]);
            }
            
            args = newArgs.ToArray();
        }
        
        return await rootCommand.InvokeAsync(args);
    }
    
    static async Task RunDacpacExtraction(string sourceConnectionString, string targetConnectionString, string outputPath, string? commitMessage)
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
        if (!string.IsNullOrWhiteSpace(commitMessage))
        {
            Console.WriteLine($"Commit Message: {commitMessage}");
        }

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
            
            // Commit the database structure changes if there are any
            if (gitAnalyzer.IsGitRepository(outputPath))
            {
                try
                {
                    // Check for uncommitted changes in the database directory
                    var dbPath = Path.Combine("servers", targetServer, targetDatabase);
                    var changes = gitAnalyzer.GetUncommittedChanges(outputPath, dbPath);
                    
                    if (changes.Any())
                    {
                        Console.WriteLine($"\nDetected {changes.Count} changed files in database structure");
                        
                        // Determine commit message
                        var actualCommitMessage = string.IsNullOrWhiteSpace(commitMessage) 
                            ? $"Update database structure for {targetServer}/{targetDatabase}" 
                            : commitMessage;
                        
                        Console.WriteLine($"Committing changes with message: {actualCommitMessage}");
                        
                        // Commit only the database structure changes (not migrations)
                        gitAnalyzer.CommitSpecificFiles(outputPath, dbPath, actualCommitMessage);
                        
                        Console.WriteLine("✓ Database structure changes committed successfully");
                    }
                    else
                    {
                        Console.WriteLine("\nNo database structure changes to commit");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not commit database structure changes: {ex.Message}");
                    // Don't fail the entire process if commit fails
                }
            }
            
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
                validateMigration: true);

            Console.WriteLine(changesDetected ? $"Migration files generated in: {migrationsPath}" : "No schema changes detected.");
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
}