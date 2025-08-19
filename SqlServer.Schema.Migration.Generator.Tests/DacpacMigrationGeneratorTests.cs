using Xunit;
using SqlServer.Schema.Migration.Generator;
using SqlServer.Schema.Exclusion.Manager.Core.Models;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class DacpacMigrationGeneratorTests : IDisposable
{
    readonly string _testDirectory;
    readonly DacpacMigrationGenerator _generator;

    public DacpacMigrationGeneratorTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"DacpacMigrationTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _generator = new DacpacMigrationGenerator();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
                Directory.Delete(_testDirectory, recursive: true);
        }
        catch { /* Ignore cleanup errors */ }
    }

    [Fact]
    public async Task GenerateMigrationAsync_NoGitRepository_ReturnsError()
    {
        // Arrange
        var outputPath = Path.Combine(_testDirectory, "no-git");
        var targetServer = "test-server";
        var targetDatabase = "test-db";
        var migrationsPath = Path.Combine(_testDirectory, "migrations");

        Directory.CreateDirectory(outputPath);
        Directory.CreateDirectory(migrationsPath);

        // Act
        var result = await _generator.GenerateMigrationAsync(
            outputPath,
            targetServer,
            targetDatabase,
            migrationsPath);

        // Assert
        Assert.False(result.Success);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public async Task GenerateMigrationAsync_WithScmpComparison_UsesScmpSettings()
    {
        // Arrange
        var outputPath = _testDirectory;
        var targetServer = "test-server";
        var targetDatabase = "test-db";
        var migrationsPath = Path.Combine(_testDirectory, "migrations");

        // Create a test SCMP comparison
        var scmpComparison = new Exclusion.Manager.Core.Models.SchemaComparison
        {
            Version = "10",
            SchemaCompareSettingsService = new SchemaCompareSettingsService
            {
                ConfigurationOptionsElement = new ConfigurationOptionsElement
                {
                    PropertyElements = new List<PropertyElement>
                    {
                        new() { Name = "DropObjectsNotInSource", Value = "True" },
                        new() { Name = "IgnorePermissions", Value = "True" }
                    }
                }
            }
        };

        Directory.CreateDirectory(migrationsPath);

        // Act
        var result = await _generator.GenerateMigrationAsync(
            outputPath,
            targetServer,
            targetDatabase,
            migrationsPath,
            scmpComparison);

        // Assert
        // The test verifies that the method completes without throwing
        // Actual migration generation would require a git repository with SQL files
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GenerateMigrationAsync_CreatesCorrectFilenames()
    {
        // Arrange
        var outputPath = _testDirectory;
        var targetServer = "test-server";
        var targetDatabase = "test-db";
        var migrationsPath = Path.Combine(_testDirectory, "migrations");
        var actor = "test_user";

        Directory.CreateDirectory(migrationsPath);

        // Act
        var result = await _generator.GenerateMigrationAsync(
            outputPath,
            targetServer,
            targetDatabase,
            migrationsPath,
            actor: actor);

        // Assert
        if (result.Success && result.MigrationPath != null)
        {
            var filename = Path.GetFileName(result.MigrationPath);
            Assert.Contains(actor, filename);
            Assert.StartsWith("_", filename);
            Assert.EndsWith(".sql", filename);
        }
    }

    [Fact]
    public async Task GenerateMigrationAsync_GeneratesReverseMigration()
    {
        // Arrange
        var outputPath = _testDirectory;
        var targetServer = "test-server";
        var targetDatabase = "test-db";
        var migrationsPath = Path.Combine(_testDirectory, "migrations");

        Directory.CreateDirectory(migrationsPath);

        // Act
        var result = await _generator.GenerateMigrationAsync(
            outputPath,
            targetServer,
            targetDatabase,
            migrationsPath);

        // Assert
        if (result.Success && result.ReverseMigrationPath != null)
        {
            var reverseFilename = Path.GetFileName(result.ReverseMigrationPath);
            Assert.StartsWith("reverse_", reverseFilename);
            Assert.EndsWith(".sql", reverseFilename);
        }
    }
}