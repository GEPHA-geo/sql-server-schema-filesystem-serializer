namespace SqlServer.Schema.Migration.Generator;

public class MigrationGenerator
{
    readonly GitIntegration.GitDiffAnalyzer _gitAnalyzer = new();
    readonly Parsing.SqlFileChangeDetector _changeDetector = new();
    readonly Generation.MigrationScriptBuilder _scriptBuilder = new();

    public bool GenerateMigrations(string outputPath, string databaseName, string migrationsPath)
    {
        try
        {
            // Ensure migrations directory exists
            Directory.CreateDirectory(migrationsPath);
            
            // Initialize Git repository if not exists
            if (!_gitAnalyzer.IsGitRepository(outputPath))
            {
                Console.WriteLine("Initializing Git repository for change tracking...");
                _gitAnalyzer.InitializeRepository(outputPath);
                
                // Create bootstrap migration
                CreateBootstrapMigration(migrationsPath);
                
                // Initial commit
                _gitAnalyzer.CommitChanges(outputPath, $"Initial schema snapshot for {databaseName}");
                return false; // No changes on first run
            }
            
            // Check for uncommitted changes
            var changes = _gitAnalyzer.GetUncommittedChanges(outputPath, databaseName);
            if (!changes.Any())
            {
                return false;
            }
            
            Console.WriteLine($"Detected {changes.Count} changed files");
            
            // Analyze changes and generate migration
            var schemaChanges = _changeDetector.AnalyzeChanges(outputPath, changes);
            if (!schemaChanges.Any())
            {
                Console.WriteLine("No schema changes requiring migration");
                return false;
            }
            
            // Generate migration script
            var migrationScript = _scriptBuilder.BuildMigration(schemaChanges, databaseName);
            
            // Save migration file
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var description = GenerateDescription(schemaChanges);
            var filename = $"{timestamp}_{description}.sql";
            var migrationPath = Path.Combine(migrationsPath, filename);
            
            File.WriteAllText(migrationPath, migrationScript);
            Console.WriteLine($"Generated migration: {filename}");
            
            // Commit changes
            _gitAnalyzer.CommitChanges(outputPath, $"Schema update: {description}");
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating migrations: {ex.Message}");
            return false;
        }
    }

    void CreateBootstrapMigration(string migrationsPath)
    {
        var bootstrapScript = @"-- Migration: 00000000_000000_create_migration_history_table.sql
-- MigrationId: 00000000_000000_create_migration_history_table
-- Description: Bootstrap migration - creates the migration tracking table
-- Author: System
-- Date: System Bootstrap

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DatabaseMigrationHistory' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[DatabaseMigrationHistory] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [MigrationId] NVARCHAR(100) NOT NULL UNIQUE,
        [Filename] NVARCHAR(500) NOT NULL,
        [AppliedDate] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [Checksum] NVARCHAR(64) NOT NULL,
        [Status] NVARCHAR(50) NOT NULL,
        [ExecutionTime] INT NULL,
        [ErrorMessage] NVARCHAR(MAX) NULL
    );
    
    -- Self-register this bootstrap migration
    INSERT INTO [dbo].[DatabaseMigrationHistory] 
        ([MigrationId], [Filename], [Checksum], [Status], [ExecutionTime])
    VALUES 
        ('00000000_000000_create_migration_history_table', 
         '00000000_000000_create_migration_history_table.sql',
         'BOOTSTRAP', 
         'Success', 
         0);
END
GO";
        
        var bootstrapPath = Path.Combine(migrationsPath, "00000000_000000_create_migration_history_table.sql");
        if (!File.Exists(bootstrapPath))
        {
            File.WriteAllText(bootstrapPath, bootstrapScript);
            Console.WriteLine("Created bootstrap migration");
        }
    }

    string GenerateDescription(List<Parsing.SchemaChange> changes)
    {
        // Generate a concise description based on changes
        var summary = new List<string>();
        
        var tableChanges = changes.Where(c => c.ObjectType == "Table").ToList();
        var indexChanges = changes.Where(c => c.ObjectType == "Index").ToList();
        var otherChanges = changes.Where(c => c.ObjectType != "Table" && c.ObjectType != "Index").ToList();
        
        if (tableChanges.Any())
            summary.Add($"{tableChanges.Count}_tables");
        if (indexChanges.Any())
            summary.Add($"{indexChanges.Count}_indexes");
        if (otherChanges.Any())
            summary.Add($"{otherChanges.Count}_other");
            
        return string.Join("_", summary).Replace(" ", "_").ToLower();
    }
}