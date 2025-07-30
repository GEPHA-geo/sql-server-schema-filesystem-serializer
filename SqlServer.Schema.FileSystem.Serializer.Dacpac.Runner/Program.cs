using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Core;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner;

internal static class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.WriteLine("Usage: DacpacStructureGenerator <sourceConnectionString> <targetConnectionString> <outputPath>");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine(@"  DacpacStructureGenerator ""Server=dev;Database=DevDB;..."" ""Server=prod;Database=ProdDB;..."" ""/output""");
            return;
        }

        var sourceConnectionString = args[0];
        var targetConnectionString = args[1];
        var outputPath = args[2];
        
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
            var gitAnalyzer = new Migration.Generator.GitIntegration.GitDiffAnalyzer();
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
                dacServices.Extract(dacpacPath, sourceDatabaseName, "DacpacStructureGenerator", new Version(1, 0));
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
                Console.WriteLine($"Cleaning database directory: {targetOutputPath} (preserving _migrations)");
                
                // Get all subdirectories except migrations
                var subdirs = Directory.GetDirectories(targetOutputPath)
                    .Where(d => !Path.GetFileName(d).Equals("_migrations", StringComparison.OrdinalIgnoreCase))
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
            var migrationsPath = Path.Combine(targetOutputPath, "_migrations");
            
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