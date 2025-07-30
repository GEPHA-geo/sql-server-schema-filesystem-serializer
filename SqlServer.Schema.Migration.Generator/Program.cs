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
        ) { IsRequired = true };
        
        var targetServerOption = new Option<string>(
            "--target-server",
            "The target server name or IP address"
        ) { IsRequired = true };
        
        var targetDatabaseOption = new Option<string>(
            "--target-database",
            "The target database name"
        ) { IsRequired = true };
        
        var actorOption = new Option<string?>(
            "--actor",
            "The actor/user creating the migration (defaults to GITHUB_ACTOR env var or current user)"
        ) { IsRequired = false };
        
        rootCommand.AddOption(outputPathOption);
        rootCommand.AddOption(targetServerOption);
        rootCommand.AddOption(targetDatabaseOption);
        rootCommand.AddOption(actorOption);
        
        rootCommand.SetHandler((string outputPath, string targetServer, string targetDatabase, string? actor) =>
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
                
                var generator = new MigrationGenerator();
                var migrationsPath = Path.Combine(outputPath, "servers", targetServer, targetDatabase, "z_migrations");
                
                var changesDetected = generator.GenerateMigrations(outputPath, targetServer, targetDatabase, migrationsPath, actor);
                
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
        }, outputPathOption, targetServerOption, targetDatabaseOption, actorOption);
        
        return await rootCommand.InvokeAsync(args);
    }
}