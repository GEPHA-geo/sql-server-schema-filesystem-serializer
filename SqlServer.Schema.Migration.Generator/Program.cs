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
        
        var databaseNameOption = new Option<string>(
            "--database",
            "The database name to process"
        ) { IsRequired = true };
        
        var actorOption = new Option<string?>(
            "--actor",
            "The actor/user creating the migration (defaults to GITHUB_ACTOR env var or current user)"
        ) { IsRequired = false };
        
        rootCommand.AddOption(outputPathOption);
        rootCommand.AddOption(databaseNameOption);
        rootCommand.AddOption(actorOption);
        
        rootCommand.SetHandler((string outputPath, string databaseName, string? actor) =>
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
                var migrationsPath = Path.Combine(outputPath, "migrations");
                
                var changesDetected = generator.GenerateMigrations(outputPath, databaseName, migrationsPath, actor);
                
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
        }, outputPathOption, databaseNameOption, actorOption);
        
        return await rootCommand.InvokeAsync(args);
    }
}