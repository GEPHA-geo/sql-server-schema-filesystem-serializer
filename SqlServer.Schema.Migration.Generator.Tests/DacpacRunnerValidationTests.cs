using Xunit;
using System.IO;
using System;
using System.Linq;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class DacpacRunnerValidationTests : IDisposable
{
    readonly string _testDirectory;
    readonly string _outputPath;
    readonly string _targetServer = "test-server";
    readonly string _targetDatabase = "test-db";
    readonly string _dbPath;
    readonly string _migrationsPath;
    readonly string _reverseMigrationsPath;
    readonly GitDiffAnalyzer _gitAnalyzer;

    public DacpacRunnerValidationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"DacpacValidationTest_{Guid.NewGuid()}");
        _outputPath = _testDirectory;
        _dbPath = Path.Combine("servers", _targetServer, _targetDatabase);
        _migrationsPath = Path.Combine(_testDirectory, _dbPath, "z_migrations");
        _reverseMigrationsPath = Path.Combine(_testDirectory, _dbPath, "z_migrations_reverse");
        _gitAnalyzer = new GitDiffAnalyzer();
        
        // Create test directory structure
        Directory.CreateDirectory(_migrationsPath);
        Directory.CreateDirectory(_reverseMigrationsPath);
        
        // Initialize git repo
        _gitAnalyzer.InitializeRepository(_testDirectory);
        
        // Configure git for test - must be done after init
        ConfigureGitForTest();
        
        // Create an initial file to have something to commit
        File.WriteAllText(Path.Combine(_testDirectory, ".gitkeep"), "");
        
        // Stage and commit the initial file
        ExecuteGitCommand("add .");
        ExecuteGitCommand("commit -m \"Initial commit\"");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                // Force cleanup by setting all files as non-readonly
                SetDirectoryWritable(_testDirectory);
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    static void SetDirectoryWritable(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }
    
    void ExecuteGitCommand(string arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = _testDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = System.Diagnostics.Process.Start(startInfo);
        process?.WaitForExit();
        
        if (process?.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"Git command failed: {arguments}. Error: {error}");
        }
    }

    void ConfigureGitForTest()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = _testDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // Set user email locally for the test repository
                Arguments = "config --local user.email \"test@example.com\""
            };

            using (var process = System.Diagnostics.Process.Start(startInfo))
            {
                process?.WaitForExit();
                if (process?.ExitCode != 0)
                {
                    throw new Exception("Failed to set git user.email");
                }
            }
            
            // Set user name locally for the test repository
            startInfo.Arguments = "config --local user.name \"Test User\"";
            using (var process = System.Diagnostics.Process.Start(startInfo))
            {
                process?.WaitForExit();
                if (process?.ExitCode != 0)
                {
                    throw new Exception("Failed to set git user.name");
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to configure git for test: {ex.Message}", ex);
        }
    }

    [Fact]
    public void ValidateMigrationGeneration_WithSchemaChangesAndMigration_ShouldPass()
    {
        // Arrange
        CreateSchemaFile("test_table.sql", "CREATE TABLE TestTable (Id INT)");
        CreateMigrationFiles("_20250131_120000_test_add_table.sql", "CREATE TABLE TestTable (Id INT)");
        
        // Act
        var result = ValidateMigrationGenerationLogic(
            _outputPath, _targetServer, _targetDatabase, _migrationsPath, migrationExpected: true);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateMigrationGeneration_WithExpectedMigrationButNoneCreated_ShouldFail()
    {
        // Arrange
        CreateSchemaFile("test_table.sql", "CREATE TABLE TestTable (Id INT)");
        // No migration files created, but migration was expected
        
        // Act
        var result = ValidateMigrationGenerationLogic(
            _outputPath, _targetServer, _targetDatabase, _migrationsPath, migrationExpected: true);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Migration generation was reported as successful but no fresh migration file was found", result.ErrorMessage);
    }

    [Fact]
    public void ValidateMigrationGeneration_WithNoSchemaChangesAndNoMigration_ShouldPass()
    {
        // Arrange
        // No changes, no migrations
        
        // Act
        var result = ValidateMigrationGenerationLogic(
            _outputPath, _targetServer, _targetDatabase, _migrationsPath, migrationExpected: false);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateMigrationGeneration_WithUnexpectedMigration_ShouldFail()
    {
        // Arrange
        // Create a fresh migration file but report no migration expected
        CreateMigrationFiles("_20250131_120000_test_add_table.sql", "CREATE TABLE TestTable (Id INT)");
        
        // Act
        var result = ValidateMigrationGenerationLogic(
            _outputPath, _targetServer, _targetDatabase, _migrationsPath, migrationExpected: false);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("No migration was expected but a fresh migration file was created", result.ErrorMessage);
    }

    [Fact]
    public void ValidateMigrationGeneration_WithMigrationButNoReverseMigration_ShouldFail()
    {
        // Arrange
        CreateSchemaFile("test_table.sql", "CREATE TABLE TestTable (Id INT)");
        // Only create forward migration, no reverse
        var migrationPath = Path.Combine(_migrationsPath, "_20250131_120000_test_add_table.sql");
        File.WriteAllText(migrationPath, "CREATE TABLE TestTable (Id INT)");
        
        // Act
        var result = ValidateMigrationGenerationLogic(
            _outputPath, _targetServer, _targetDatabase, _migrationsPath, migrationExpected: true);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Reverse migration file not found", result.ErrorMessage);
    }

    [Fact]
    public void ValidateMigrationGeneration_WithOldMigrationFiles_ShouldFail()
    {
        // Arrange
        CreateSchemaFile("test_table.sql", "CREATE TABLE TestTable (Id INT)");
        
        // Create migration files with old timestamp
        var migrationPath = Path.Combine(_migrationsPath, "_20250131_120000_test_add_table.sql");
        var reversePath = Path.Combine(_reverseMigrationsPath, "_20250131_120000_test_add_table.sql");
        File.WriteAllText(migrationPath, "CREATE TABLE TestTable (Id INT)");
        File.WriteAllText(reversePath, "DROP TABLE TestTable");
        
        // Make files appear old
        var oldTime = DateTime.UtcNow.AddMinutes(-1);
        File.SetLastWriteTimeUtc(migrationPath, oldTime);
        File.SetLastWriteTimeUtc(reversePath, oldTime);
        
        // Act
        var result = ValidateMigrationGenerationLogic(
            _outputPath, _targetServer, _targetDatabase, _migrationsPath, migrationExpected: true);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Migration generation was reported as successful but no fresh migration file was found", result.ErrorMessage);
    }

    void CreateSchemaFile(string filename, string content)
    {
        var schemaPath = Path.Combine(_testDirectory, _dbPath, "schemas", "dbo", "Tables");
        Directory.CreateDirectory(schemaPath);
        File.WriteAllText(Path.Combine(schemaPath, filename), content);
    }

    void CreateMigrationFiles(string filename, string content)
    {
        var migrationPath = Path.Combine(_migrationsPath, filename);
        var reversePath = Path.Combine(_reverseMigrationsPath, filename);
        
        File.WriteAllText(migrationPath, content);
        File.WriteAllText(reversePath, $"-- Reverse of {content}");
    }

    // This is a copy of the validation logic from Program.cs
    // In a real scenario, this would be refactored to a shared location
    record MigrationValidationResult(bool IsValid, string? ErrorMessage = null);
    
    MigrationValidationResult ValidateMigrationGenerationLogic(
        string outputPath, 
        string targetServer, 
        string targetDatabase,
        string migrationsPath,
        bool migrationExpected)
    {
        try
        {
            var gitAnalyzer = new GitDiffAnalyzer();
            var dbPath = Path.Combine("servers", targetServer, targetDatabase);
            
            // Get list of migration files created in this run
            var migrationFiles = Directory.GetFiles(migrationsPath, "*.sql")
                .Where(f => !Path.GetFileName(f).StartsWith("_00000000_000000_")) // Exclude bootstrap migration
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ToList();
                
            var reverseMigrationsPath = Path.Combine(Path.GetDirectoryName(migrationsPath)!, "z_migrations_reverse");
            var reverseMigrationFiles = Directory.Exists(reverseMigrationsPath) 
                ? Directory.GetFiles(reverseMigrationsPath, "*.sql")
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .ToList()
                : new List<string>();
            
            // Check most recent migration (if any)
            var recentMigration = migrationFiles.FirstOrDefault();
            var recentReverseMigration = reverseMigrationFiles.FirstOrDefault();
            
            // Check if a fresh migration was created (within last 5 seconds for tests)
            var freshMigrationCreated = recentMigration != null && 
                (DateTime.UtcNow - File.GetLastWriteTimeUtc(recentMigration)).TotalSeconds <= 5;
            
            // Validation rules:
            // 1. If migration was expected but no fresh migration was created
            if (migrationExpected && !freshMigrationCreated)
            {
                return new MigrationValidationResult(false, 
                    "Migration generation was reported as successful but no fresh migration file was found.");
            }
            
            // 2. If no migration was expected but a fresh migration exists
            if (!migrationExpected && freshMigrationCreated)
            {
                return new MigrationValidationResult(false, 
                    $"No migration was expected but a fresh migration file was created: {Path.GetFileName(recentMigration)}");
            }
            
            // 3. If migration was generated, perform additional checks
            if (migrationExpected && freshMigrationCreated && recentMigration != null)
            {
                // Check that reverse migration exists with same filename
                var migrationFileName = Path.GetFileName(recentMigration);
                var expectedReversePath = Path.Combine(reverseMigrationsPath, migrationFileName);
                if (!File.Exists(expectedReversePath))
                {
                    return new MigrationValidationResult(false, 
                        $"Reverse migration file not found. Expected: {expectedReversePath}");
                }
                
                // Check reverse migration was also created recently
                var reverseAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(expectedReversePath);
                if (reverseAge.TotalSeconds > 5)
                {
                    return new MigrationValidationResult(false, 
                        $"Reverse migration file exists but appears to be old (created {reverseAge.TotalSeconds:F1} seconds ago).");
                }
            }
            
            return new MigrationValidationResult(true);
        }
        catch (Exception ex)
        {
            return new MigrationValidationResult(false, $"Validation error: {ex.Message}");
        }
    }
}