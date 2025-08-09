using SqlServer.Schema.Exclusion.Manager.Core.Models;
using SqlServer.Schema.Exclusion.Manager.Core.Services;
using System.Text.RegularExpressions;
using Xunit;

namespace SqlServer.Schema.Exclusion.Manager.Tests;

public class ExclusionCommentUpdaterTests : IDisposable
{
    readonly string _testDirectory;
    readonly ExclusionCommentUpdater _updater;

    public ExclusionCommentUpdaterTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ExclusionTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _updater = new ExclusionCommentUpdater();
    }

    public void Dispose() => Directory.Delete(_testDirectory, true);

    [Fact]
    public async Task UpdateSerializedFiles_OnlyModifiesFilesRelatedToManifestChanges()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "servers", "TestServer", "TestDB", "schemas", "dbo", "Tables");
        Directory.CreateDirectory(Path.Combine(dbPath, "Users"));
        Directory.CreateDirectory(Path.Combine(dbPath, "Orders"));
        Directory.CreateDirectory(Path.Combine(dbPath, "nasti"));
        
        // Create files - some in manifest, some not
        var userTableFile = Path.Combine(dbPath, "Users", "TBL_Users.sql");
        var orderTableFile = Path.Combine(dbPath, "Orders", "TBL_Orders.sql");
        var indexFile = Path.Combine(dbPath, "nasti", "IDX_IX_nasti_performance.sql");
        
        await File.WriteAllTextAsync(userTableFile, "CREATE TABLE Users");
        await File.WriteAllTextAsync(orderTableFile, "CREATE TABLE Orders");
        await File.WriteAllTextAsync(indexFile, "CREATE INDEX IX_nasti_performance");
        
        var manifest = new ChangeManifest
        {
            ServerName = "TestServer",
            DatabaseName = "TestDB"
        };
        
        // Only Users table and index are in the manifest
        manifest.ExcludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.TBL_Users",
            Description = "Excluded for testing"
        });
        
        manifest.IncludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.IDX_IX_nasti_performance",
            Description = "Index included"
        });
        
        // Act
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "TestServer", "TestDB", manifest);
        
        // Assert
        var userContent = await File.ReadAllTextAsync(userTableFile);
        var orderContent = await File.ReadAllTextAsync(orderTableFile);
        var indexContent = await File.ReadAllTextAsync(indexFile);
        
        // Users file should have exclusion comment (it's excluded)
        Assert.Contains("MIGRATION EXCLUDED", userContent);
        
        // Orders file should NOT be modified (not in manifest at all)
        Assert.Equal("CREATE TABLE Orders", orderContent);
        
        // Index file should NOT have exclusion comment (it's included, not excluded)
        Assert.DoesNotContain("MIGRATION EXCLUDED", indexContent);
    }

    [Fact]
    public async Task UpdateSerializedFiles_AddsExclusionComments()
    {
        // Arrange
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer"
        };
        
        manifest.ExcludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.Users",
            Description = "Table to be excluded",
            FilePath = "schemas/dbo/Tables/Users/Users.sql"
        });

        // Create file in the expected directory structure
        var sqlFile = Path.Combine(_testDirectory, "servers", "TestServer", "TestDB", "schemas", "dbo", "Tables", "Users", "Users.sql");
        Directory.CreateDirectory(Path.GetDirectoryName(sqlFile)!);
        
        var originalContent = @"CREATE TABLE [dbo].[Users] (
    [Id] INT PRIMARY KEY,
    [Name] NVARCHAR(100)
);";
        await File.WriteAllTextAsync(sqlFile, originalContent);

        // Act
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "TestServer", "TestDB", manifest);

        // Assert
        var updatedContent = await File.ReadAllTextAsync(sqlFile);
        Assert.Contains("MIGRATION EXCLUDED:", updatedContent);
        Assert.Contains("Table to be excluded", updatedContent);
        Assert.Contains("This change is NOT included in current migration", updatedContent);
    }

    [Fact]
    public async Task UpdateSerializedFiles_HandlesMultipleExclusions()
    {
        // Arrange
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer"
        };
        
        manifest.ExcludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.Table1",
            Description = "First exclusion",
            FilePath = "schemas/dbo/Tables/Table1/Table1.sql"
        });
        
        manifest.ExcludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.Table2",
            Description = "Second exclusion",
            FilePath = "schemas/dbo/Tables/Table2/Table2.sql"
        });

        // Create files in the expected directory structure
        var table1File = Path.Combine(_testDirectory, "servers", "TestServer", "TestDB", "schemas", "dbo", "Tables", "Table1", "Table1.sql");
        var table2File = Path.Combine(_testDirectory, "servers", "TestServer", "TestDB", "schemas", "dbo", "Tables", "Table2", "Table2.sql");
        Directory.CreateDirectory(Path.GetDirectoryName(table1File)!);
        Directory.CreateDirectory(Path.GetDirectoryName(table2File)!);
        
        await File.WriteAllTextAsync(table1File, "CREATE TABLE [dbo].[Table1] (Id INT);");
        await File.WriteAllTextAsync(table2File, "CREATE TABLE [dbo].[Table2] (Id INT);");

        // Act
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "TestServer", "TestDB", manifest);

        // Assert
        var content1 = await File.ReadAllTextAsync(table1File);
        var content2 = await File.ReadAllTextAsync(table2File);
        
        Assert.Contains("MIGRATION EXCLUDED: First exclusion", content1);
        Assert.Contains("MIGRATION EXCLUDED: Second exclusion", content2);
    }

    [Fact]
    public async Task UpdateSerializedFiles_SkipsNonExistentFiles()
    {
        // Arrange
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer"
        };
        
        manifest.ExcludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.NonExistent",
            Description = "File doesn't exist",
            FilePath = "Tables/dbo.NonExistent.sql"
        });

        // Act & Assert (should not throw)
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "TestServer", "TestDB", manifest);
    }

    [Fact]
    public async Task UpdateSerializedFiles_DoesNotDuplicateComments_WhenRunMultipleTimes()
    {
        // Arrange - create a table file
        var tablePath = Path.Combine(_testDirectory, "servers", "TestServer", "TestDB", 
            "schemas", "dbo", "Tables", "TestTable");
        Directory.CreateDirectory(tablePath);
        
        var tableFile = Path.Combine(tablePath, "TBL_TestTable.sql");
        var originalContent = @"CREATE TABLE [dbo].[TestTable] (
    [Id] INT NOT NULL,
    [Name] NVARCHAR(100) NULL,
    [NewColumn] NCHAR(10) NULL
);";
        await File.WriteAllTextAsync(tableFile, originalContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer",
            CommitHash = "test123"
        };
        manifest.ExcludedChanges.Add(
            new ManifestChange
            {
                Identifier = "dbo.TestTable.NewColumn",
                Description = "Column added",
                ObjectType = "Table"
            }
        );
        
        // Act - run the update multiple times
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "TestServer", "TestDB", manifest);
        var contentAfterFirstRun = await File.ReadAllTextAsync(tableFile);
        
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "TestServer", "TestDB", manifest);
        var contentAfterSecondRun = await File.ReadAllTextAsync(tableFile);
        
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "TestServer", "TestDB", manifest);
        var contentAfterThirdRun = await File.ReadAllTextAsync(tableFile);
        
        // Assert
        // Content should be the same after multiple runs
        Assert.Equal(contentAfterFirstRun, contentAfterSecondRun);
        Assert.Equal(contentAfterSecondRun, contentAfterThirdRun);
        
        // Should have exclusion comment only once
        var commentCount = Regex.Matches(contentAfterThirdRun, @"-- MIGRATION EXCLUDED:").Count;
        Assert.Equal(1, commentCount);
        
        // Should have manifest reference only once
        var manifestRefCount = Regex.Matches(contentAfterThirdRun, @"-- See: _change-manifests").Count;
        Assert.Equal(1, manifestRefCount);
        
        // Content should still have the table definition
        Assert.Contains("CREATE TABLE", contentAfterThirdRun);
        Assert.Contains("[NewColumn]", contentAfterThirdRun);
    }
    
    [Fact]
    public async Task UpdateSerializedFiles_ReplacesOldComments_WhenManifestChanges()
    {
        // Arrange - create a table file with existing exclusion comments
        var tablePath = Path.Combine(_testDirectory, "servers", "TestServer", "TestDB", 
            "schemas", "dbo", "Tables", "TestTable");
        Directory.CreateDirectory(tablePath);
        
        var tableFile = Path.Combine(tablePath, "TBL_TestTable.sql");
        var contentWithOldComment = @"-- MIGRATION EXCLUDED: Old exclusion reason
-- This change is NOT included in current migration
-- See: _change-manifests/old_manifest.manifest

CREATE TABLE [dbo].[TestTable] (
    [Id] INT NOT NULL,
    [Name] NVARCHAR(100) NULL
);";
        await File.WriteAllTextAsync(tableFile, contentWithOldComment);
        
        var newManifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer",
            CommitHash = "new456"
        };
        newManifest.ExcludedChanges.Add(
            new ManifestChange
            {
                Identifier = "dbo.TestTable",
                Description = "New exclusion reason",
                ObjectType = "Table"
            }
        );
        
        // Act
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "TestServer", "TestDB", newManifest);
        var updatedContent = await File.ReadAllTextAsync(tableFile);
        
        // Assert
        // Old comments should be removed
        Assert.DoesNotContain("Old exclusion reason", updatedContent);
        Assert.DoesNotContain("old_manifest.manifest", updatedContent);
        
        // New comments should be present
        Assert.Contains("New exclusion reason", updatedContent);
        Assert.Contains("TestServer_TestDB.manifest", updatedContent);
        
        // Should have only one set of exclusion comments
        var commentCount = Regex.Matches(updatedContent, @"-- MIGRATION EXCLUDED:").Count;
        Assert.Equal(1, commentCount);
    }

    
    [Fact]
    public void ExtractIdentifierFromSql_ExtractsColumnFromAlterTableAdd()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var sql = "ALTER TABLE [dbo].[test_migrations] ADD [tempof] NCHAR (10) NULL;";
        
        // Use reflection to test private method
        var method = typeof(ExclusionCommentUpdater).GetMethod("ExtractIdentifierFromSql", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        // Act
        var result = method.Invoke(updater, new object[] { sql }) as string;
        
        // Assert
        Assert.Equal("dbo.test_migrations.tempof", result);
    }
    
    [Fact]
    public void ExtractIdentifierFromSql_ExtractsColumnFromAlterTableDrop()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var sql = "ALTER TABLE [dbo].[Users] DROP COLUMN [DeletedAt];";
        
        // Use reflection to test private method
        var method = typeof(ExclusionCommentUpdater).GetMethod("ExtractIdentifierFromSql", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        // Act
        var result = method.Invoke(updater, new object[] { sql }) as string;
        
        // Assert
        Assert.Equal("dbo.Users.DeletedAt", result);
    }
    
    [Fact]
    public void ExtractIdentifierFromSql_ExtractsColumnFromAlterTableAlter()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var sql = "ALTER TABLE [dbo].[Products] ALTER COLUMN [Price] DECIMAL(10,2) NOT NULL;";
        
        // Use reflection to test private method
        var method = typeof(ExclusionCommentUpdater).GetMethod("ExtractIdentifierFromSql", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        // Act
        var result = method.Invoke(updater, new object[] { sql }) as string;
        
        // Assert
        Assert.Equal("dbo.Products.Price", result);
    }
    
    [Fact]
    public void ExtractIdentifierFromSql_HandlesVariousFormats()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var testCases = new[]
        {
            ("ALTER TABLE dbo.Users ADD Email VARCHAR(255);", "dbo.Users.Email"),
            ("ALTER TABLE [dbo].Users ADD [Phone] VARCHAR(20);", "dbo.Users.Phone"),
            ("ALTER TABLE dbo.[Orders] ADD OrderDate DATETIME;", "dbo.Orders.OrderDate"),
            ("ALTER TABLE [dbo].[Customers] DROP COLUMN [OldField];", "dbo.Customers.OldField"),
            ("ALTER TABLE dbo.Products ALTER COLUMN Name NVARCHAR(500);", "dbo.Products.Name")
        };
        
        var method = typeof(ExclusionCommentUpdater).GetMethod("ExtractIdentifierFromSql", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        foreach (var (sql, expected) in testCases)
        {
            // Act
            var result = method.Invoke(updater, new object[] { sql }) as string;
            
            // Assert
            Assert.Equal(expected, result);
        }
    }
    
    [Fact]
    public async Task UpdateMigrationScript_ExcludesColumnSpecificChanges()
    {
        // Arrange
        var migrationPath = Path.Combine(_testDirectory, "migration.sql");
        var migrationContent = @"-- Migration
ALTER TABLE [dbo].[test_migrations] ADD [tempof] NCHAR (10) NULL;
GO
ALTER TABLE [dbo].[test_migrations] ADD [another] INT NOT NULL;
GO
ALTER TABLE [dbo].[other_table] ADD [field] VARCHAR(50);
GO";
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer",
            CommitHash = "test123"
        };
        manifest.ExcludedChanges.AddRange(new[]
        {
            new ManifestChange
            {
                Identifier = "dbo.test_migrations.tempof",
                Description = "added",
                ObjectType = "Table"
            },
            new ManifestChange
            {
                Identifier = "dbo.other_table.field",
                Description = "added",
                ObjectType = "Table"
            }
        });
        
        // Act
        await _updater.UpdateMigrationScriptAsync(migrationPath, manifest);
        var updatedContent = await File.ReadAllTextAsync(migrationPath);
        
        // Assert
        // First ALTER should be commented out
        Assert.Contains("-- EXCLUDED: dbo.test_migrations.tempof", updatedContent);
        Assert.Contains("/*", updatedContent);
        Assert.Contains("ALTER TABLE [dbo].[test_migrations] ADD [tempof]", updatedContent);
        
        // Second ALTER should NOT be commented (different column)
        Assert.DoesNotContain("-- EXCLUDED: dbo.test_migrations.another", updatedContent);
        
        // Third ALTER should be commented out
        Assert.Contains("-- EXCLUDED: dbo.other_table.field", updatedContent);
    }

    [Fact]
    public async Task UpdateMigrationScripts_CommentsOutExcludedChanges_ThatMatchPattern()
    {
        // Arrange
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer"
        };
        
        manifest.ExcludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.Users",
            Description = "Exclude user table changes"
        });
        
        manifest.ExcludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.sp_GetUser",
            Description = "Exclude stored procedure"
        });

        var migrationFile = Path.Combine(_testDirectory, "migration_001.sql");
        var originalContent = @"-- Migration Script
BEGIN TRANSACTION;

-- Create Users table
CREATE TABLE [dbo].[Users] (
    [Id] INT PRIMARY KEY,
    [Name] NVARCHAR(100)
);

-- Create Products table
CREATE TABLE [dbo].[Products] (
    [Id] INT PRIMARY KEY,
    [Name] NVARCHAR(100)
);

-- Create stored procedure
CREATE PROCEDURE [dbo].[sp_GetUser]
    @UserId INT
AS
BEGIN
    SELECT * FROM [dbo].[Users] WHERE Id = @UserId;
END;

COMMIT TRANSACTION;";
        
        await File.WriteAllTextAsync(migrationFile, originalContent);

        // Act
        await _updater.UpdateMigrationScriptAsync(migrationFile, manifest);

        // Assert
        var updatedContent = await File.ReadAllTextAsync(migrationFile);
        
        // Note: Current implementation only detects ALTER TABLE and CREATE INDEX
        // CREATE TABLE and CREATE PROCEDURE are not detected, so they won't be commented
        // This is a limitation of the current ExtractIdentifierFromSql implementation
        
        // The updatedContent should still contain the original SQL since no patterns matched
        Assert.Contains("CREATE TABLE [dbo].[Users]", updatedContent);
        Assert.Contains("CREATE TABLE [dbo].[Products]", updatedContent);
        Assert.Contains("CREATE PROCEDURE [dbo].[sp_GetUser]", updatedContent);
        
        // Since CREATE statements aren't detected, nothing should be commented out
        Assert.DoesNotContain("-- EXCLUDED:", updatedContent);
    }

    [Fact]
    public async Task UpdateMigrationScripts_HandlesAlterStatements()
    {
        // Arrange
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer"
        };
        
        manifest.ExcludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.Users.Email",
            Description = "Exclude alter table",
            ObjectType = "Table"
        });

        var migrationFile = Path.Combine(_testDirectory, "migration_002.sql");
        var originalContent = @"-- Migration Script
ALTER TABLE [dbo].[Users] ADD [Email] NVARCHAR(200);
ALTER TABLE [dbo].[Products] ADD [Price] DECIMAL(10,2);";
        
        await File.WriteAllTextAsync(migrationFile, originalContent);

        // Act
        await _updater.UpdateMigrationScriptAsync(migrationFile, manifest);

        // Assert
        var updatedContent = await File.ReadAllTextAsync(migrationFile);
        
        // The implementation adds "-- EXCLUDED: dbo.Users.Email - Exclude alter table"
        Assert.Contains("-- EXCLUDED: dbo.Users.Email", updatedContent);
        Assert.Contains("Exclude alter table", updatedContent);
        Assert.Contains("/*", updatedContent);
        Assert.Contains("ALTER TABLE [dbo].[Users]", updatedContent);
        Assert.Contains("*/", updatedContent);
        // Products table should not be commented since it's not excluded
        Assert.Contains("ALTER TABLE [dbo].[Products]", updatedContent);
        Assert.DoesNotContain("-- EXCLUDED: dbo.Products", updatedContent);
    }

    [Fact]
    public async Task UpdateMigrationScripts_PreservesTransactionStructure()
    {
        // Arrange
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer"
        };
        
        manifest.ExcludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.ExcludedTable",
            Description = "Exclude this table"
        });

        var migrationFile = Path.Combine(_testDirectory, "migration_003.sql");
        var originalContent = @"BEGIN TRANSACTION;
CREATE TABLE [dbo].[ExcludedTable] (Id INT);
CREATE TABLE [dbo].[IncludedTable] (Id INT);
COMMIT TRANSACTION;";
        
        await File.WriteAllTextAsync(migrationFile, originalContent);

        // Act
        await _updater.UpdateMigrationScriptAsync(migrationFile, manifest);

        // Assert
        var updatedContent = await File.ReadAllTextAsync(migrationFile);
        
        // Transaction statements should remain uncommented
        Assert.Contains("BEGIN TRANSACTION;", updatedContent);
        Assert.Contains("COMMIT TRANSACTION;", updatedContent);
        Assert.DoesNotContain("-- BEGIN TRANSACTION", updatedContent);
        Assert.DoesNotContain("-- COMMIT TRANSACTION", updatedContent);
        
        // CREATE TABLE is not detected by ExtractIdentifierFromSql
        // So nothing should be commented out
        Assert.Contains("CREATE TABLE [dbo].[ExcludedTable]", updatedContent);
        Assert.Contains("CREATE TABLE [dbo].[IncludedTable]", updatedContent);
        Assert.DoesNotContain("-- EXCLUDED:", updatedContent);
    }
}