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
        
        rootCommand.AddOption(outputPathOption);
        rootCommand.AddOption(databaseNameOption);
        
        rootCommand.SetHandler((string outputPath, string databaseName) =>
        {
            try
            {
                var generator = new MigrationGenerator();
                var migrationsPath = Path.Combine(outputPath, "migrations");
                
                var changesDetected = generator.GenerateMigrations(outputPath, databaseName, migrationsPath);
                
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
        }, outputPathOption, databaseNameOption);
        
        return await rootCommand.InvokeAsync(args);
    }
}