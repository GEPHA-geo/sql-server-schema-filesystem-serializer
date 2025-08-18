using CSharpFunctionalExtensions;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Constants;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Models;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Services;

/// <summary>
/// Handles migration script generation and saving
/// </summary>
public class MigrationGenerator
{
    /// <summary>
    /// Generates and saves migration script from comparison result
    /// </summary>
    public async Task<Result<MigrationResult>> GenerateAndSaveMigration(
        DacpacExtractionContext context,
        SchemaComparisonResult comparisonResult)
    {
        if (comparisonResult.ComparisonResult == null)
        {
            return Result.Failure<MigrationResult>("No comparison result available");
        }
        
        // Generate migration script
        Console.WriteLine("Generating migration script...");
        var publishResult = comparisonResult.ComparisonResult.GenerateScript(
            context.TargetConnection.Database);
        
        var migrationScript = publishResult.Script;
        
        // Include master script if available
        if (!string.IsNullOrEmpty(publishResult.MasterScript))
        {
            migrationScript = publishResult.MasterScript + "\n" + migrationScript;
        }
        
        if (string.IsNullOrEmpty(migrationScript))
        {
            Console.WriteLine("No changes detected - no migration needed");
            return Result.Success(new MigrationResult { HasChanges = false });
        }
        
        // Save migration to z_migrations directory
        var migrationPath = await SaveMigrationScript(context, migrationScript);
        
        return Result.Success(new MigrationResult 
        {
            HasChanges = true,
            MigrationPath = migrationPath
        });
    }
    
    /// <summary>
    /// Saves the migration script to the appropriate directory
    /// </summary>
    async Task<string> SaveMigrationScript(
        DacpacExtractionContext context,
        string migrationScript)
    {
        var migrationsPath = Path.Combine(
            context.TargetOutputPath,
            DacpacConstants.Directories.Migrations);
        
        Directory.CreateDirectory(migrationsPath);
        
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var actor = Environment.GetEnvironmentVariable("GITHUB_ACTOR") ?? Environment.UserName;
        var migrationFileName = $"_{timestamp}_{actor}_migration.sql";
        var migrationFilePath = Path.Combine(migrationsPath, migrationFileName);
        
        await File.WriteAllTextAsync(migrationFilePath, migrationScript);
        Console.WriteLine($"✓ Migration saved to: {migrationFileName}");
        
        return migrationFilePath;
    }
}

/// <summary>
/// Result of migration generation
/// </summary>
public class MigrationResult
{
    /// <summary>
    /// Whether there are any changes in the migration
    /// </summary>
    public bool HasChanges { get; set; }
    
    /// <summary>
    /// Path to the generated migration file
    /// </summary>
    public string? MigrationPath { get; set; }
}