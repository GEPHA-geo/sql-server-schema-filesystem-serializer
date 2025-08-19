using System.CommandLine;
using Microsoft.Data.SqlClient;

namespace SqlServer.Schema.Migration.Runner;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("SQL Server Schema Migration Runner");

        // Migrate command
        var migrateCommand = new Command("migrate", "Run pending migrations");

        var connectionOption = new Option<string>(
            "--connection",
            "SQL Server connection string"
        )
        { IsRequired = true };

        var migrationsPathOption = new Option<string>(
            "--migrations",
            "Path to migrations directory"
        )
        { IsRequired = true };

        var dryRunOption = new Option<bool>(
            "--dry-run",
            getDefaultValue: () => false,
            "Show what would be executed without making changes"
        );

        migrateCommand.AddOption(connectionOption);
        migrateCommand.AddOption(migrationsPathOption);
        migrateCommand.AddOption(dryRunOption);

        migrateCommand.SetHandler(async (string connectionString, string migrationsPath, bool dryRun) =>
        {
            try
            {
                var executor = new Core.MigrationExecutor(connectionString);
                await executor.ExecuteMigrations(migrationsPath, dryRun);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, connectionOption, migrationsPathOption, dryRunOption);

        // Status command
        var statusCommand = new Command("status", "Check migration status");

        statusCommand.AddOption(connectionOption);
        statusCommand.AddOption(migrationsPathOption);

        statusCommand.SetHandler(async (string connectionString, string migrationsPath) =>
        {
            try
            {
                var executor = new Core.MigrationExecutor(connectionString);
                await executor.ShowStatus(migrationsPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, connectionOption, migrationsPathOption);

        rootCommand.AddCommand(migrateCommand);
        rootCommand.AddCommand(statusCommand);

        return await rootCommand.InvokeAsync(args);
    }
}