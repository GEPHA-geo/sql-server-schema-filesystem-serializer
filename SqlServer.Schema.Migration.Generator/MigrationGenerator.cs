namespace SqlServer.Schema.Migration.Generator;

public class MigrationGenerator
{
    readonly GitIntegration.GitDiffAnalyzer _gitAnalyzer = new();
    readonly Parsing.SqlFileChangeDetector _changeDetector = new();
    readonly Generation.MigrationScriptBuilder _scriptBuilder = new();
    readonly Generation.ReverseMigrationBuilder _reverseScriptBuilder = new();

    public bool GenerateMigrations(string outputPath, string targetServer, string targetDatabase, string migrationsPath, string? actor = null)
    {
        return GenerateMigrationsAsync(outputPath, targetServer, targetDatabase, migrationsPath, actor, null, true).GetAwaiter().GetResult();
    }

    public async Task<bool> GenerateMigrationsAsync(
        string outputPath, 
        string targetServer,
        string targetDatabase, 
        string migrationsPath,
        string? actor = null,
        string? connectionString = null,
        bool validateMigration = true,
        string? customCommitMessage = null)
    {
        try
        {
            // Ensure migrations directory exists
            Directory.CreateDirectory(migrationsPath);
            
            // Create reverse migrations directory
            var reverseMigrationsPath = Path.Combine(Path.GetDirectoryName(migrationsPath)!, "z_migrations_reverse");
            Directory.CreateDirectory(reverseMigrationsPath);
            
            // Check if migrations directory is empty (first run for this server/database)
            var existingMigrations = Directory.GetFiles(migrationsPath, "*.sql");
            if (!existingMigrations.Any())
            {
                Console.WriteLine($"First run for {targetServer}/{targetDatabase} - creating bootstrap migration...");
                CreateBootstrapMigration(migrationsPath);
            }
            
            // Initialize Git repository if not exists at root level
            if (!_gitAnalyzer.IsGitRepository(outputPath))
            {
                Console.WriteLine("Initializing Git repository for change tracking...");
                _gitAnalyzer.InitializeRepository(outputPath);
                
                // Initial commit
                _gitAnalyzer.CommitChanges(outputPath, $"Initial schema snapshot for {targetServer}/{targetDatabase}");
                return false; // No changes on first run
            }
            
            // Check for uncommitted changes
            var changes = _gitAnalyzer.GetUncommittedChanges(outputPath, Path.Combine("servers", targetServer, targetDatabase));
            if (!changes.Any())
            {
                Console.WriteLine("No uncommitted changes detected by git");
                return false;
            }
            
            Console.WriteLine($"Detected {changes.Count} changed files");
            
            // Analyze changes and generate migration
            var schemaChanges = _changeDetector.AnalyzeChanges(outputPath, changes);
            if (!schemaChanges.Any())
            {
                Console.WriteLine("No schema changes requiring migration");
                Console.WriteLine($"[DEBUG] Files checked: {changes.Count}, but none had schema changes");
                return false;
            }
            
            // Generate migration script
            var migrationScript = _scriptBuilder.BuildMigration(schemaChanges, targetDatabase, actor);
            
            // Generate reverse migration script
            var reverseMigrationScript = _reverseScriptBuilder.BuildReverseMigration(schemaChanges, targetDatabase, actor);
            
            // Save migration file
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var description = GenerateDescription(schemaChanges);
            
            // Sanitize actor name for filename (remove special characters)
            var sanitizedActor = string.IsNullOrEmpty(actor) ? "unknown" : SanitizeForFilename(actor);
            
            var filename = $"_{timestamp}_{sanitizedActor}_{description}.sql";
            var migrationPath = Path.Combine(migrationsPath, filename);
            var reverseMigrationPath = Path.Combine(reverseMigrationsPath, filename);
            
            // Validate migration if requested and connection string provided
            // TODO: Enable validation after resolving Git export issues in Windows/WSL environment
            /*
            if (validateMigration && !string.IsNullOrEmpty(connectionString))
            {
                Console.WriteLine("\nValidating migration before saving...");
                
                var validator = new Validation.MigrationValidator(connectionString);
                var validationResult = await validator.ValidateMigrationAsync(
                    migrationScript,
                    outputPath,
                    targetDatabase);
                    
                if (!validationResult.Success)
                {
                    Console.WriteLine($"Migration validation failed: {validationResult.Error}");
                    Console.WriteLine("Migration file was not saved due to validation failure.");
                    return false;
                }
                
                Console.WriteLine("Migration validated successfully!");
            }
            */
            
            await File.WriteAllTextAsync(migrationPath, migrationScript);
            Console.WriteLine($"Generated migration: {filename}");
            
            await File.WriteAllTextAsync(reverseMigrationPath, reverseMigrationScript);
            Console.WriteLine($"Generated reverse migration: z_migrations_reverse/{filename}");
            
            // Validate before committing - check that migration files exist in git's uncommitted changes
            var allUncommittedChanges = _gitAnalyzer.GetUncommittedChanges(outputPath, "");
            var migrationFileCreated = allUncommittedChanges.Any(c => c.Path.Contains($"/z_migrations/{filename}"));
            var reverseMigrationFileCreated = allUncommittedChanges.Any(c => c.Path.Contains($"/z_migrations_reverse/{filename}"));
            
            if (!migrationFileCreated || !reverseMigrationFileCreated)
            {
                Console.WriteLine($"❌ Validation failed: Migration files were not detected by git");
                Console.WriteLine($"Migration file detected: {migrationFileCreated}");
                Console.WriteLine($"Reverse migration file detected: {reverseMigrationFileCreated}");
                return false;
            }
            
            // Commit changes
            var commitMessage = !string.IsNullOrWhiteSpace(customCommitMessage) 
                ? customCommitMessage 
                : $"Schema update: {description}";
            _gitAnalyzer.CommitChanges(outputPath, commitMessage);
            
            // Return true since we successfully created migration files
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating migrations: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    void CreateBootstrapMigration(string migrationsPath)
    {
        var bootstrapScript = @"-- Migration: _00000000_000000_system_create_migration_history_table.sql
-- MigrationId: 00000000_000000_create_migration_history_table
-- Actor: system
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
         '_00000000_000000_system_create_migration_history_table.sql',
         'BOOTSTRAP', 
         'Success', 
         0);
END
GO";
        
        var bootstrapPath = Path.Combine(migrationsPath, "_00000000_000000_system_create_migration_history_table.sql");
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
    
    string SanitizeForFilename(string input)
    {
        // Replace invalid filename characters with underscores
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = input;
        
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }
        
        // Also replace common problematic characters
        sanitized = sanitized
            .Replace(' ', '_')
            .Replace('.', '_')
            .Replace('@', '_')
            .Replace('-', '_');
            
        // Remove consecutive underscores
        while (sanitized.Contains("__"))
        {
            sanitized = sanitized.Replace("__", "_");
        }
        
        // Trim underscores from start and end
        sanitized = sanitized.Trim('_');
        
        // Ensure it's not empty
        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = "unknown";
        }
        
        // Limit length to avoid excessively long filenames
        if (sanitized.Length > 50)
        {
            sanitized = sanitized.Substring(0, 50);
        }
        
        return sanitized.ToLower();
    }
}