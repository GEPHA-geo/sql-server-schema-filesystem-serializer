using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace SqlServer.Schema.Migration.Runner.Core;

public class MigrationExecutor(string connectionString)
{
    readonly string _connectionString = connectionString;
    readonly DatabaseConnection _db = new(connectionString);

    public async Task ExecuteMigrations(string migrationsPath, bool dryRun)
    {
        if (!Directory.Exists(migrationsPath))
        {
            throw new DirectoryNotFoundException($"Migrations directory not found: {migrationsPath}");
        }

        // Ensure migration history table exists
        await EnsureMigrationHistoryTable();

        // Get all migration files
        var migrationFiles = Directory.GetFiles(migrationsPath, "*.sql")
            .OrderBy(f => f)
            .Select(f => new MigrationFile(f))
            .ToList();

        if (!migrationFiles.Any())
        {
            Console.WriteLine("No migration files found.");
            return;
        }

        // Get applied migrations
        var appliedMigrations = await GetAppliedMigrations();

        // Find pending migrations
        var pendingMigrations = migrationFiles
            .Where(m => !appliedMigrations.Contains(m.MigrationId))
            .ToList();

        if (!pendingMigrations.Any())
        {
            Console.WriteLine("All migrations are up to date.");
            return;
        }

        Console.WriteLine($"Found {pendingMigrations.Count} pending migration(s):");
        foreach (var migration in pendingMigrations)
        {
            Console.WriteLine($"  - {migration.FileName}");
        }

        if (dryRun)
        {
            Console.WriteLine("\nDry run mode - no changes will be made.");
            return;
        }

        Console.WriteLine("\nApplying migrations...");

        foreach (var migration in pendingMigrations)
        {
            await ExecuteMigration(migration);
        }

        Console.WriteLine("\nAll migrations completed successfully.");
    }

    public async Task ShowStatus(string migrationsPath)
    {
        if (!Directory.Exists(migrationsPath))
        {
            throw new DirectoryNotFoundException($"Migrations directory not found: {migrationsPath}");
        }

        // Ensure migration history table exists
        await EnsureMigrationHistoryTable();

        // Get all migration files
        var migrationFiles = Directory.GetFiles(migrationsPath, "*.sql")
            .OrderBy(f => f)
            .Select(f => new MigrationFile(f))
            .ToList();

        // Get applied migrations with details
        var appliedMigrations = await GetAppliedMigrationDetails();

        Console.WriteLine("Migration Status:");
        Console.WriteLine("=================");

        foreach (var file in migrationFiles)
        {
            var applied = appliedMigrations.FirstOrDefault(m => m.MigrationId == file.MigrationId);
            if (applied != null)
            {
                Console.WriteLine($"[✓] {file.FileName} - Applied on {applied.AppliedDate:yyyy-MM-dd HH:mm:ss}");
                if (applied.Status != "Success")
                {
                    Console.WriteLine($"    Status: {applied.Status}");
                    if (!string.IsNullOrWhiteSpace(applied.ErrorMessage))
                    {
                        Console.WriteLine($"    Error: {applied.ErrorMessage}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"[ ] {file.FileName} - Pending");
            }
        }
    }

    async Task EnsureMigrationHistoryTable()
    {
        var checkTableSql = @"
            SELECT COUNT(*) 
            FROM sys.tables 
            WHERE name = 'DatabaseMigrationHistory' AND schema_id = SCHEMA_ID('dbo')";

        var exists = await _db.ExecuteScalarAsync<int>(checkTableSql) > 0;

        if (!exists)
        {
            Console.WriteLine("Creating migration history table...");
            var createTableSql = await GetBootstrapScript();
            await _db.ExecuteNonQueryAsync(createTableSql);
        }
    }

    static async Task<string> GetBootstrapScript()
    {
        // Try to find bootstrap script in Scripts directory
        var scriptsPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "00000000_000000_create_migration_history_table.sql");
        if (File.Exists(scriptsPath))
        {
            return await File.ReadAllTextAsync(scriptsPath);
        }

        // Fallback to embedded script
        return @"
CREATE TABLE [dbo].[DatabaseMigrationHistory] (
    [Id] INT IDENTITY(1,1) PRIMARY KEY,
    [MigrationId] NVARCHAR(100) NOT NULL UNIQUE,
    [Filename] NVARCHAR(500) NOT NULL,
    [AppliedDate] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [Checksum] NVARCHAR(64) NOT NULL,
    [Status] NVARCHAR(50) NOT NULL,
    [ExecutionTime] INT NULL,
    [ErrorMessage] NVARCHAR(MAX) NULL
);";
    }

    async Task<HashSet<string>> GetAppliedMigrations()
    {
        var sql = "SELECT MigrationId FROM [dbo].[DatabaseMigrationHistory] WHERE Status = 'Success'";
        var result = await _db.QueryAsync<MigrationHistory>(sql);
        return new HashSet<string>(result.Select(m => m.MigrationId));
    }

    async Task<List<MigrationHistory>> GetAppliedMigrationDetails()
    {
        var sql = "SELECT * FROM [dbo].[DatabaseMigrationHistory] ORDER BY AppliedDate";
        return await _db.QueryAsync<MigrationHistory>(sql);
    }

    async Task ExecuteMigration(MigrationFile migration)
    {
        Console.Write($"Applying {migration.FileName}... ");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _db.ExecuteMigrationAsync(migration.Content);
            stopwatch.Stop();

            // Record success
            await RecordMigration(migration, "Success", (int)stopwatch.ElapsedMilliseconds, null);

            Console.WriteLine($"Done ({stopwatch.ElapsedMilliseconds}ms)");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Console.WriteLine("Failed!");
            Console.WriteLine($"  Error: {ex.Message}");

            // Record failure
            await RecordMigration(migration, "Failed", (int)stopwatch.ElapsedMilliseconds, ex.Message);

            throw;
        }
    }

    async Task RecordMigration(MigrationFile migration, string status, int executionTime, string? errorMessage)
    {
        var sql = @"
            INSERT INTO [dbo].[DatabaseMigrationHistory] 
                ([MigrationId], [Filename], [Checksum], [Status], [ExecutionTime], [ErrorMessage])
            VALUES 
                (@MigrationId, @Filename, @Checksum, @Status, @ExecutionTime, @ErrorMessage)";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            migration.MigrationId,
            migration.FileName,
            migration.Checksum,
            Status = status,
            ExecutionTime = executionTime,
            ErrorMessage = errorMessage
        });
    }
}