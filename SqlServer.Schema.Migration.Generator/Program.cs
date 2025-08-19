using System.CommandLine;

namespace SqlServer.Schema.Migration.Generator;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("SQL Server Schema Migration Generator");

        var outputPathOption = new Option<string>(
            "--output-path",
            "The path where database schema was serialized"
        )
        { IsRequired = true };

        var targetServerOption = new Option<string>(
            "--target-server",
            "The target server name or IP address"
        )
        { IsRequired = true };

        var targetDatabaseOption = new Option<string>(
            "--target-database",
            "The target database name"
        )
        { IsRequired = true };

        var actorOption = new Option<string?>(
            "--actor",
            "The actor/user creating the migration (defaults to GITHUB_ACTOR env var or current user)"
        )
        { IsRequired = false };

        var referenceDacpacOption = new Option<string?>(
            "--reference-dacpac",
            "Path to a reference DACPAC file to resolve external references (optional)"
        )
        { IsRequired = false };

        rootCommand.AddOption(outputPathOption);
        rootCommand.AddOption(targetServerOption);
        rootCommand.AddOption(targetDatabaseOption);
        rootCommand.AddOption(actorOption);
        rootCommand.AddOption(referenceDacpacOption);

        rootCommand.SetHandler((string outputPath, string targetServer, string targetDatabase, string? actor, string? referenceDacpac) =>
        {
            try
            {
                // Determine actor name
                if (string.IsNullOrEmpty(actor))
                {
                    // Try GitHub Actions environment variable
                    actor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");

                    // Fall back to current user
                    if (string.IsNullOrEmpty(actor))
                    {
                        actor = Environment.UserName;
                    }
                }

                // If no reference DACPAC specified, look for it in conventional locations
                if (string.IsNullOrEmpty(referenceDacpac))
                {
                    // Convention 1: Look for reference DACPAC in the database directory
                    var dbReferencePath = Path.Combine(outputPath, "servers", targetServer, targetDatabase, "reference.dacpac");
                    if (File.Exists(dbReferencePath))
                    {
                        referenceDacpac = dbReferencePath;
                        Console.WriteLine($"Found reference DACPAC at: {dbReferencePath}");
                    }
                    else
                    {
                        // Convention 2: Look for server-wide reference DACPAC
                        var serverReferencePath = Path.Combine(outputPath, "servers", targetServer, "reference.dacpac");
                        if (File.Exists(serverReferencePath))
                        {
                            referenceDacpac = serverReferencePath;
                            Console.WriteLine($"Found server-wide reference DACPAC at: {serverReferencePath}");
                        }
                        else
                        {
                            // Convention 3: Look in references folder
                            var referencesPath = Path.Combine(outputPath, "references", $"{targetDatabase}.dacpac");
                            if (File.Exists(referencesPath))
                            {
                                referenceDacpac = referencesPath;
                                Console.WriteLine($"Found reference DACPAC in references folder: {referencesPath}");
                            }
                        }
                    }
                }
                else if (File.Exists(referenceDacpac))
                {
                    Console.WriteLine($"Using specified reference DACPAC: {referenceDacpac}");
                }
                else
                {
                    Console.WriteLine($"Warning: Specified reference DACPAC not found: {referenceDacpac}");
                    referenceDacpac = null;
                }

                var generator = new DacpacMigrationGenerator();
                var migrationsPath = Path.Combine(outputPath, "servers", targetServer, targetDatabase, "z_migrations");

                var result = generator.GenerateMigrationAsync(outputPath, targetServer, targetDatabase, migrationsPath, null, actor, referenceDacpac).Result;
                var changesDetected = result.Success && result.HasChanges;

                if (changesDetected)
                {
                    Console.WriteLine($"Migration files generated in: {migrationsPath}");
                }
                else
                {
                    Console.WriteLine("No schema changes detected.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, outputPathOption, targetServerOption, targetDatabaseOption, actorOption, referenceDacpacOption);

        return await rootCommand.InvokeAsync(args);
    }
}