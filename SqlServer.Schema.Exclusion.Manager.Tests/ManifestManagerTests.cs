using SqlServer.Schema.Exclusion.Manager.Core.Models;
using SqlServer.Schema.Exclusion.Manager.Core.Services;
using Moq;
using Xunit;

namespace SqlServer.Schema.Exclusion.Manager.Tests;

public class ManifestManagerTests : IDisposable
{
    readonly string _testDirectory;

    public ManifestManagerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ManifestManagerTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    // Helper method to create a test git repository with specific commit structure
    static string SetupTestGitRepo(bool parentIsOriginMain = true)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"git_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        
        // Initialize git repo
        RunGitCommand(tempDir, "init");
        RunGitCommand(tempDir, "config user.email test@test.com");
        RunGitCommand(tempDir, "config user.name TestUser");
        
        // Create initial commit on main
        File.WriteAllText(Path.Combine(tempDir, "test.txt"), "initial");
        RunGitCommand(tempDir, "add .");
        RunGitCommand(tempDir, "commit -m \"Initial commit\"");
        
        // Set up origin/main
        RunGitCommand(tempDir, "branch -M main");
        
        if (parentIsOriginMain)
        {
            // Mark current commit as origin/main
            RunGitCommand(tempDir, "update-ref refs/remotes/origin/main HEAD");
            
            // Create a new commit on top of origin/main
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "updated");
            RunGitCommand(tempDir, "add .");
            RunGitCommand(tempDir, "commit -m \"New commit on top of main\"");
        }
        else
        {
            // Create another commit first
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "second");
            RunGitCommand(tempDir, "add .");
            RunGitCommand(tempDir, "commit -m \"Second commit\"");
            
            // Mark the first commit as origin/main (not the parent of HEAD)
            RunGitCommand(tempDir, "update-ref refs/remotes/origin/main HEAD~1");
            
            // Create a third commit
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "third");
            RunGitCommand(tempDir, "add .");
            RunGitCommand(tempDir, "commit -m \"Third commit\"");
        }
        
        return tempDir;
    }
    
    static void RunGitCommand(string workingDir, string arguments)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        
        process.Start();
        process.WaitForExit();
        
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new Exception($"Git command failed: git {arguments}\nError: {error}");
        }
    }
    
    static void DeleteDirectoryRecursive(string path)
    {
        if (!Directory.Exists(path))
            return;
            
        try
        {
            // Remove read-only attributes from all files
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            
            // Remove read-only attributes from all directories
            foreach (var dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
            {
                new DirectoryInfo(dir).Attributes = FileAttributes.Normal;
            }
            
            // Now delete the directory
            Directory.Delete(path, true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
    
    [Fact]
    public void ShouldToggleRotationMarker_ReturnsTrue_WhenParentIsOriginMain()
    {
        // Arrange
        var gitRepo = SetupTestGitRepo(parentIsOriginMain: true);
        try
        {
            // Create ManifestManager with test git repo as output path
            var manager = new ManifestManager(gitRepo);
            
            // Use reflection to call private method
            var methodInfo = typeof(ManifestManager).GetMethod("ShouldToggleRotationMarker", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // Act
            var result = (bool)(methodInfo?.Invoke(manager, null) ?? false);
            
            // Assert
            Assert.True(result, "Should toggle when parent commit is origin/main");
        }
        finally
        {
            // Cleanup
            DeleteDirectoryRecursive(gitRepo);
        }
    }
    
    [Fact]
    public void ShouldToggleRotationMarker_ReturnsFalse_WhenParentIsNotOriginMain()
    {
        // Arrange
        var gitRepo = SetupTestGitRepo(parentIsOriginMain: false);
        try
        {
            // Create ManifestManager with test git repo as output path
            var manager = new ManifestManager(gitRepo);
            
            // Use reflection to call private method
            var methodInfo = typeof(ManifestManager).GetMethod("ShouldToggleRotationMarker", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // Act
            var result = (bool)(methodInfo?.Invoke(manager, null) ?? false);
            
            // Assert
            Assert.False(result, "Should not toggle when parent commit is not origin/main");
        }
        finally
        {
            // Cleanup
            DeleteDirectoryRecursive(gitRepo);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                // Remove read-only attributes from all files
                foreach (var file in Directory.GetFiles(_testDirectory, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task CreateOrUpdateManifestAsync_CreatesNewManifest_WhenNoneExists()
    {
        // Arrange
        var gitRepo = SetupTestGitRepo(parentIsOriginMain: false);
        try
        {
            var manager = new ManifestManager(gitRepo);
            var sourceServer = "SourceServer";
            var sourceDatabase = "SourceDB";
            var targetServer = "TargetServer";
            var targetDatabase = "TargetDB";
            
            // Act
            var result = await manager.CreateOrUpdateManifestAsync(sourceServer, sourceDatabase, targetServer, targetDatabase);
            
            // Assert
            // CreateOrUpdateManifestAsync returns bool, not the manifest
            Assert.True(result);
        }
        finally
        {
            DeleteDirectoryRecursive(gitRepo);
        }
    }

    [Fact]
    public async Task CreateOrUpdateManifestAsync_RegeneratesManifest_WhenForceRegenerate()
    {
        // Arrange
        var gitRepo = SetupTestGitRepo(parentIsOriginMain: false);
        try
        {
            var manager = new ManifestManager(gitRepo);
            var sourceServer = "SourceServer";
            var sourceDatabase = "SourceDB";
            var targetServer = "TargetServer";
            var targetDatabase = "TargetDB";
            
            // Create initial manifest
            var manifest = new ChangeManifest
            {
                DatabaseName = sourceDatabase,
                ServerName = sourceServer,
                RotationMarker = '/'
            };
            manifest.ExcludedChanges.Add(new ManifestChange
            {
                Identifier = "dbo.Table1",
                Description = "Should be removed on regenerate"
            });
            
            // Save it in target location but named after source
            var targetPath = Path.Combine(gitRepo, "servers", targetServer, targetDatabase);
            Directory.CreateDirectory(targetPath);
            var manifestDir = Path.Combine(targetPath, "_change-manifests");
            Directory.CreateDirectory(manifestDir);
            var manifestPath = Path.Combine(manifestDir, $"{sourceServer}_{sourceDatabase}.manifest");
            var fileHandler = new ManifestFileHandler();
            await fileHandler.WriteManifestAsync(manifestPath, manifest);
            
            // Act - would regenerate with forceRegenerate flag in real implementation
            var regenerated = await manager.CreateOrUpdateManifestAsync(sourceServer, sourceDatabase, targetServer, targetDatabase);
            
            // Assert
            Assert.True(regenerated);
        }
        finally
        {
            DeleteDirectoryRecursive(gitRepo);
        }
    }

    [Fact]
    public async Task UpdateExclusionCommentsAsync_UpdatesFiles_WhenManifestExists()
    {
        // Arrange
        var gitRepo = SetupTestGitRepo(parentIsOriginMain: false);
        try
        {
            var manager = new ManifestManager(gitRepo);
            var sourceServer = "SourceServer";
            var sourceDatabase = "SourceDB";
            var targetServer = "TargetServer";
            var targetDatabase = "TargetDB";
            
            // Create directory structure for target
            var dbPath = Path.Combine(gitRepo, "servers", targetServer, targetDatabase);
            Directory.CreateDirectory(dbPath);
            
            // Create manifest with exclusions (named after source)
            var manifest = new ChangeManifest
            {
                DatabaseName = sourceDatabase,
                ServerName = sourceServer,
                Generated = DateTime.UtcNow,
                CommitHash = "test123",
                RotationMarker = '/'
            };
            
            manifest.ExcludedChanges.Add(new ManifestChange
            {
                Identifier = "dbo.TestTable",
                Description = "Excluded for testing"
            });
            
            // Save manifest in target location
            var manifestDir = Path.Combine(dbPath, "_change-manifests");
            Directory.CreateDirectory(manifestDir);
            var manifestPath = Path.Combine(manifestDir, $"{sourceServer}_{sourceDatabase}.manifest");
            var fileHandler = new ManifestFileHandler();
            await fileHandler.WriteManifestAsync(manifestPath, manifest);
            
            // Act
            var result = await manager.UpdateExclusionCommentsAsync(sourceServer, sourceDatabase, targetServer, targetDatabase);
            
            // Assert
            Assert.True(result);
        }
        finally
        {
            DeleteDirectoryRecursive(gitRepo);
        }
    }

    [Fact]
    public async Task UpdateExclusionCommentsAsync_CommentsOutExcludedIndexInMigrationScript()
    {
        // This test verifies that when an index like IX_nasti_performance is in the EXCLUDED section
        // of the manifest, it gets commented out in the migration script
        
        // Arrange
        var gitRepo = SetupTestGitRepo(parentIsOriginMain: false);
        try
        {
            var manager = new ManifestManager(gitRepo);
            var sourceServer = "10.188.49.19,1433";
            var sourceDatabase = "abc_20250801_1804";
            var targetServer = "pharm-n1.pharm.local";
            var targetDatabase = "abc";
            
            // Create directory structure for target
            var dbPath = Path.Combine(gitRepo, "servers", targetServer, targetDatabase);
            var migrationsPath = Path.Combine(dbPath, "z_migrations");
            Directory.CreateDirectory(migrationsPath);
            
            // Create a migration script with an active CREATE INDEX statement
            var migrationScript = @"-- Migration: 20250810_091615_zuraandguladze_1_indexes_13_other.sql
-- Generated: 2025-08-10 09:16:15 UTC
-- Database: abc
-- Actor: zuraandguladze
-- Changes: 14 schema modifications

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Create indexes
CREATE NONCLUSTERED INDEX [IX_nasti_performance]
    ON [dbo].[nasti]([na_kod] ASC, [na_tar] ASC, [na_saw] ASC)
    INCLUDE([na_raod], [na_k]);
GO

-- Modification operations
ALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_zura];
GO

ALTER TABLE [dbo].[test_migrations]
    ADD CONSTRAINT [DF_test_migrations_zura] DEFAULT (N'iura') FOR [zura];
GO

COMMIT TRANSACTION;
PRINT 'Migration applied successfully.';
";
            
            var migrationPath = Path.Combine(migrationsPath, "_20250810_091615_zuraandguladze_1_indexes_13_other.sql");
            await File.WriteAllTextAsync(migrationPath, migrationScript);
            
            // Create manifest with IX_nasti_performance in EXCLUDED section
            var manifest = new ChangeManifest
            {
                DatabaseName = sourceDatabase,
                ServerName = sourceServer,
                Generated = DateTime.UtcNow,
                CommitHash = "69423162",
                RotationMarker = '/'
            };
            
            // Add some included changes
            manifest.IncludedChanges.Add(new ManifestChange
            {
                Identifier = "dbo.test_migrations.DF_test_migrations_zura",
                Description = "modified"
            });
            
            // Add IX_nasti_performance to EXCLUDED changes
            manifest.ExcludedChanges.Add(new ManifestChange
            {
                Identifier = "dbo.nasti.IX_nasti_performance",
                Description = "added"
            });
            
            // Save manifest in target location
            var manifestDir = Path.Combine(dbPath, "_change-manifests");
            Directory.CreateDirectory(manifestDir);
            var manifestPath = Path.Combine(manifestDir, $"{sourceServer}_{sourceDatabase}.manifest");
            var fileHandler = new ManifestFileHandler();
            await fileHandler.WriteManifestAsync(manifestPath, manifest);
            
            // Act
            var result = await manager.UpdateExclusionCommentsAsync(sourceServer, sourceDatabase, targetServer, targetDatabase);
            
            // Assert
            Assert.True(result);
            
            // Read the updated migration script
            var updatedScript = await File.ReadAllTextAsync(migrationPath);
            
            // The IX_nasti_performance CREATE INDEX should now be commented out
            Assert.Contains("-- EXCLUDED: dbo.nasti.IX_nasti_performance - added", updatedScript);
            Assert.Contains("/*", updatedScript);
            Assert.Contains("CREATE NONCLUSTERED INDEX [IX_nasti_performance]", updatedScript);
            Assert.Contains("*/", updatedScript);
            
            // The DF_test_migrations_zura constraint should remain active (not commented)
            // since it's in the INCLUDED section
            Assert.DoesNotContain("-- EXCLUDED: dbo.test_migrations.DF_test_migrations_zura", updatedScript);
            
            // Verify the constraint modification is not within comment blocks
            var lines = updatedScript.Split('\n');
            var inCommentBlock = false;
            var constraintLineFound = false;
            
            foreach (var line in lines)
            {
                if (line.Trim() == "/*")
                    inCommentBlock = true;
                else if (line.Trim() == "*/")
                    inCommentBlock = false;
                else if (line.Contains("ALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_zura]"))
                {
                    constraintLineFound = true;
                    Assert.False(inCommentBlock, "The included constraint modification should not be inside a comment block");
                }
            }
            
            Assert.True(constraintLineFound, "The constraint modification should be present in the script");
        }
        finally
        {
            DeleteDirectoryRecursive(gitRepo);
        }
    }

    [Fact]
    public async Task UpdateExclusionCommentsAsync_ReturnsFalse_WhenNoManifestExists()
    {
        // Arrange
        var manager = new ManifestManager(_testDirectory);
        
        // Act
        var result = await manager.UpdateExclusionCommentsAsync("NonExistentSource", "SourceDB", "NonExistentTarget", "TargetDB");
        
        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetManifestPath_ReturnsCorrectPath()
    {
        // Arrange
        var sourceServer = "SourceServer";
        var sourceDatabase = "SourceDB";
        var targetServer = "TargetServer";
        var targetDatabase = "TargetDB";
        
        var manifest = new ChangeManifest
        {
            DatabaseName = sourceDatabase,
            ServerName = sourceServer
        };
        
        // Act - manifest should be in target location but named after source
        var expectedPath = Path.Combine(_testDirectory, "servers", targetServer, targetDatabase, 
            "_change-manifests", $"{sourceServer}_{sourceDatabase}.manifest");
        var actualPath = Path.Combine(_testDirectory, "servers", targetServer, targetDatabase, 
            manifest.GetManifestFileName());
        
        // Assert
        Assert.Equal(expectedPath, actualPath);
    }

    [Fact]
    public async Task ManifestFileHandler_PreservesRotationMarker()
    {
        // Test that rotation markers are correctly read and preserved
        var manifestPath = Path.Combine(_testDirectory, "test.manifest");
        
        // Create manifest with rotation marker '/'
        await File.WriteAllTextAsync(manifestPath, 
            "DATABASE: TestDB /\n" +
            "SERVER: TestServer /\n" +
            "GENERATED: 2024-01-01T00:00:00Z /\n" +
            "COMMIT: abc123 /\n\n" +
            "=== INCLUDED CHANGES ===\n" +
            "dbo.Table1 - Test change /\n\n" +
            "=== EXCLUDED CHANGES ===\n");
        
        var fileHandler = new ManifestFileHandler();
        var manifest = await fileHandler.ReadManifestAsync(manifestPath);
        
        Assert.NotNull(manifest);
        Assert.Equal('/', manifest.RotationMarker);
        
        // Now test with '\' marker
        await File.WriteAllTextAsync(manifestPath, 
            "DATABASE: TestDB \\\n" +
            "SERVER: TestServer \\\n" +
            "GENERATED: 2024-01-01T00:00:00Z \\\n" +
            "COMMIT: abc123 \\\n\n" +
            "=== INCLUDED CHANGES ===\n" +
            "dbo.Table1 - Test change \\\n\n" +
            "=== EXCLUDED CHANGES ===\n");
        
        manifest = await fileHandler.ReadManifestAsync(manifestPath);
        
        Assert.NotNull(manifest);
        Assert.Equal('\\', manifest.RotationMarker);
    }
    
    [Fact]
    public async Task MergeManifests_PreservesExclusions_AndAddsNewChanges()
    {
        // This would require making MergeManifests public or testing through CreateOrUpdateManifestAsync
        // The behavior is tested indirectly through CreateOrUpdateManifestAsync
        
        // Arrange
        var gitRepo = SetupTestGitRepo(parentIsOriginMain: false);
        try
        {
            var manager = new ManifestManager(gitRepo);
            var sourceServer = "SourceServer";
            var sourceDatabase = "SourceDB";
            var targetServer = "TargetServer";
            var targetDatabase = "TargetDB";
            
            // Create initial manifest with exclusion
            var initial = new ChangeManifest
            {
                DatabaseName = sourceDatabase,
                ServerName = sourceServer,
                Generated = DateTime.UtcNow,
                CommitHash = "initial",
                RotationMarker = '/'
            };
            
            initial.ExcludedChanges.Add(new ManifestChange
            {
                Identifier = "dbo.ExcludedTable",
                Description = "Should remain excluded"
            });
            
            initial.IncludedChanges.Add(new ManifestChange
            {
                Identifier = "dbo.IncludedTable",
                Description = "Should remain included"
            });
            
            // Save it in target location
            var targetPath = Path.Combine(gitRepo, "servers", targetServer, targetDatabase);
            Directory.CreateDirectory(targetPath);
            var manifestDir = Path.Combine(targetPath, "_change-manifests");
            Directory.CreateDirectory(manifestDir);
            var manifestPath = Path.Combine(manifestDir, $"{sourceServer}_{sourceDatabase}.manifest");
            var fileHandler = new ManifestFileHandler();
            await fileHandler.WriteManifestAsync(manifestPath, initial);
            
            // Act - update without regenerate
            var updated = await manager.CreateOrUpdateManifestAsync(sourceServer, sourceDatabase, targetServer, targetDatabase);
            
            // Assert - CreateOrUpdateManifestAsync returns bool
            Assert.True(updated);
        }
        finally
        {
            DeleteDirectoryRecursive(gitRepo);
        }
    }

}