using SqlServer.Schema.Exclusion.Manager.Core.Models;
using SqlServer.Schema.Exclusion.Manager.Core.Services;
using System;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace SqlServer.Schema.Exclusion.Manager.Tests;

public class ExclusionCommentUpdaterTests : IDisposable
{
    readonly string _testDirectory;
    readonly ExclusionCommentUpdater _updater;
    readonly ITestOutputHelper _output;

    public ExclusionCommentUpdaterTests(ITestOutputHelper output)
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ExclusionTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _updater = new ExclusionCommentUpdater();
        _output = output;
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
        
        // Create tables with columns
        await File.WriteAllTextAsync(userTableFile, @"CREATE TABLE [dbo].[Users] (
    [Id] INT PRIMARY KEY,
    [Name] NVARCHAR(100)
);");
        await File.WriteAllTextAsync(orderTableFile, "CREATE TABLE Orders");
        await File.WriteAllTextAsync(indexFile, "CREATE INDEX IX_nasti_performance");
        
        var manifest = new ChangeManifest
        {
            ServerName = "TestServer",
            DatabaseName = "TestDB"
        };
        
        // Add column exclusion for Users table
        manifest.ExcludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.Users.Name",  // Column exclusion
            Description = "Column excluded for testing"
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
        
        // Users file should have inline comment for the excluded column
        Assert.Contains("[Name] NVARCHAR(100)  -- EXCLUDED:", userContent);
        
        // Orders file should NOT be modified (not in manifest at all)
        Assert.Equal("CREATE TABLE Orders", orderContent);
        
        // Index file should NOT have exclusion comment (it's included, not excluded)
        Assert.DoesNotContain("EXCLUDED", indexContent);
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
        
        // Change to column exclusion instead of table exclusion
        manifest.ExcludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.Users.Name",  // Column exclusion
            Description = "Column to be excluded",
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
        // Should have inline comment for column, not header comments
        Assert.Contains("[Name] NVARCHAR(100)  -- EXCLUDED:", updatedContent);
        Assert.Contains("Column to be excluded", updatedContent);
        // Should NOT have header comments
        Assert.DoesNotContain("-- MIGRATION EXCLUDED:", updatedContent);
        Assert.DoesNotContain("This change is NOT included in current migration", updatedContent);
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
        
        // Change to column exclusions
        manifest.ExcludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.Table1.Name",
            Description = "First exclusion",
            FilePath = "schemas/dbo/Tables/Table1/Table1.sql"
        });
        
        manifest.ExcludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.Table2.Email",
            Description = "Second exclusion",
            FilePath = "schemas/dbo/Tables/Table2/Table2.sql"
        });

        // Create files in the expected directory structure
        var table1File = Path.Combine(_testDirectory, "servers", "TestServer", "TestDB", "schemas", "dbo", "Tables", "Table1", "Table1.sql");
        var table2File = Path.Combine(_testDirectory, "servers", "TestServer", "TestDB", "schemas", "dbo", "Tables", "Table2", "Table2.sql");
        Directory.CreateDirectory(Path.GetDirectoryName(table1File)!);
        Directory.CreateDirectory(Path.GetDirectoryName(table2File)!);
        
        // Create files with columns
        await File.WriteAllTextAsync(table1File, @"CREATE TABLE [dbo].[Table1] (
    [Id] INT,
    [Name] NVARCHAR(100)
);");
        await File.WriteAllTextAsync(table2File, @"CREATE TABLE [dbo].[Table2] (
    [Id] INT,
    [Email] NVARCHAR(200)
);");

        // Act
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "TestServer", "TestDB", manifest);

        // Assert
        var content1 = await File.ReadAllTextAsync(table1File);
        var content2 = await File.ReadAllTextAsync(table2File);
        
        // Should have inline comments for columns
        Assert.Contains("[Name] NVARCHAR(100)  -- EXCLUDED: First exclusion", content1);
        Assert.Contains("[Email] NVARCHAR(200)  -- EXCLUDED: Second exclusion", content2);
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
        
        // Should have exclusion comment only once (inline for column exclusions)
        var commentCount = Regex.Matches(contentAfterThirdRun, @"-- EXCLUDED:").Count;
        Assert.Equal(1, commentCount);
        
        // For column exclusions with inline comments, there's no manifest reference in header
        // So we should not find any "-- See:" references
        var manifestRefCount = Regex.Matches(contentAfterThirdRun, @"-- See: _change-manifests").Count;
        Assert.Equal(0, manifestRefCount);
        
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
        // Change to column exclusion
        newManifest.ExcludedChanges.Add(
            new ManifestChange
            {
                Identifier = "dbo.TestTable.Name",  // Column exclusion
                Description = "New exclusion reason",
                ObjectType = "Table"
            }
        );
        
        // Act
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "TestServer", "TestDB", newManifest);
        var updatedContent = await File.ReadAllTextAsync(tableFile);
        
        // Assert
        // Old header comments should be removed
        Assert.DoesNotContain("Old exclusion reason", updatedContent);
        Assert.DoesNotContain("old_manifest.manifest", updatedContent);
        
        // New inline comment should be present for the column
        Assert.Contains("[Name] NVARCHAR(100) NULL  -- EXCLUDED: New exclusion reason", updatedContent);
        
        // Should have no header comments
        Assert.DoesNotContain("-- MIGRATION EXCLUDED:", updatedContent);
        Assert.DoesNotContain("TestServer_TestDB.manifest", updatedContent);
        
        // Should have only one inline exclusion comment
        var commentCount = Regex.Matches(updatedContent, @"-- EXCLUDED:").Count;
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
    public void ExtractIdentifierFromSql_ExtractsColumnRename()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var testCases = new[]
        {
            ("EXEC sp_rename '[dbo].[test_migrations].[bb1]', 'tempof', 'COLUMN';", "dbo.test_migrations.bb1"),
            ("sp_rename 'dbo.Users.OldName', 'NewName', 'COLUMN'", "dbo.Users.OldName"),
            ("EXEC sp_rename '[dbo].[Orders].[Date]', 'OrderDate', 'COLUMN';", "dbo.Orders.Date")
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
    public void ExtractIdentifierFromSql_ExtractsConstraintAddition()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var testCases = new[]
        {
            ("ALTER TABLE [dbo].[test_migrations] ADD CONSTRAINT [DF_test_migrations_zura] DEFAULT (N'iura') FOR [zura];", "dbo.test_migrations.DF_test_migrations_zura"),
            ("ALTER TABLE dbo.Orders ADD CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId) REFERENCES Customers(Id);", "dbo.Orders.FK_Orders_Customers"),
            ("ALTER TABLE [dbo].[Products] ADD CONSTRAINT [CK_Products_Price] CHECK (Price > 0);", "dbo.Products.CK_Products_Price")
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
    public void ExtractIdentifierFromSql_ExtractsConstraintDrop()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var testCases = new[]
        {
            ("ALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_gsdf];", "dbo.test_migrations.DF_test_migrations_gsdf"),
            ("ALTER TABLE dbo.Orders DROP CONSTRAINT FK_Orders_Customers;", "dbo.Orders.FK_Orders_Customers"),
            ("ALTER TABLE [dbo].[Products] DROP CONSTRAINT [CK_Products_Price];", "dbo.Products.CK_Products_Price")
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
    public void ExtractIdentifierFromSql_ExtractsIndexCreation()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var testCases = new[]
        {
            ("CREATE NONCLUSTERED INDEX [iTesting2] ON [dbo].[test_migrations]([zura] ASC);", "dbo.test_migrations.iTesting2"),
            ("CREATE INDEX IX_Users_Email ON dbo.Users(Email);", "dbo.Users.IX_Users_Email"),
            ("CREATE UNIQUE INDEX [UQ_Products_SKU] ON [dbo].[Products]([SKU] ASC);", "dbo.Products.UQ_Products_SKU"),
            ("CREATE CLUSTERED INDEX IX_Orders_Date ON dbo.Orders(OrderDate DESC);", "dbo.Orders.IX_Orders_Date"),
            // Add test case for multi-line CREATE INDEX with INCLUDE clause (like IX_nasti_performance)
            (@"CREATE NONCLUSTERED INDEX [IX_nasti_performance]
    ON [dbo].[nasti]([na_kod] ASC, [na_tar] ASC, [na_saw] ASC)
    INCLUDE([na_raod], [na_k]);", "dbo.nasti.IX_nasti_performance")
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
    public void ExtractIdentifierFromSql_ExtractsIndexDrop()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var testCases = new[]
        {
            ("DROP INDEX [iTesting2] ON [dbo].[test_migrations];", "dbo.test_migrations.iTesting2"),
            ("DROP INDEX IX_Users_Email ON dbo.Users;", "dbo.Users.IX_Users_Email"),
            ("DROP INDEX [IX_Orders_Date] ON [sales].[Orders];", "sales.Orders.IX_Orders_Date")
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
    public void ExtractIdentifierFromSql_ExtractsExtendedProperty()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var testCases = new[]
        {
            (@"EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'testing - description', 
                @level0type = N'SCHEMA', @level0name = N'dbo', 
                @level1type = N'TABLE', @level1name = N'test_migrations', 
                @level2type = N'COLUMN', @level2name = N'zura';", "dbo.test_migrations.EP_Column_Description_zura"),
            (@"sp_addextendedproperty @name = 'MS_Description', @value = 'Customer email', 
                @level0type = 'SCHEMA', @level0name = 'dbo', 
                @level1type = 'TABLE', @level1name = 'Customers', 
                @level2type = 'COLUMN', @level2name = 'Email';", "dbo.Customers.EP_Column_Description_Email")
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
    public void ExtractIdentifierFromSql_ExtractsDropExtendedProperty()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var testCases = new[]
        {
            (@"EXECUTE sp_dropextendedproperty @name = N'MS_Description',
                @level0type = N'SCHEMA', @level0name = N'dbo',
                @level1type = N'TABLE', @level1name = N'test_migrations',
                @level2type = N'COLUMN', @level2name = N'oldColumn';", "dbo.test_migrations.EP_Column_Description_oldColumn"),
            (@"sp_dropextendedproperty @name = 'CustomProperty',
                @level0type = 'SCHEMA', @level0name = 'dbo',
                @level1type = 'TABLE', @level1name = 'products',
                @level2type = 'COLUMN', @level2name = 'price';", "dbo.products.EP_CustomProperty_price")
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
    public void ExtractIdentifierFromSql_ExtractsTableOperations()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var testCases = new[]
        {
            ("CREATE TABLE [dbo].[new_table] (id INT);", "dbo.new_table"),
            ("DROP TABLE [dbo].[old_table];", "dbo.old_table"),
            ("DROP TABLE IF EXISTS dbo.temp_table;", "dbo.temp_table"),
            ("EXEC sp_rename 'dbo.old_products', 'products';", "dbo.old_products"),
            ("EXEC sp_rename '[dbo].[old_customers]', 'customers', 'OBJECT';", "dbo.old_customers")
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
    public void ExtractIdentifierFromSql_ExtractsViewOperations()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var testCases = new[]
        {
            ("CREATE VIEW [dbo].[v_active_customers] AS SELECT * FROM customers;", "dbo.v_active_customers"),
            ("ALTER VIEW [dbo].[v_orders] AS SELECT * FROM orders;", "dbo.v_orders"),
            ("DROP VIEW [dbo].[v_old_view];", "dbo.v_old_view"),
            ("DROP VIEW IF EXISTS dbo.v_temp_view;", "dbo.v_temp_view"),
            ("CREATE OR ALTER VIEW dbo.v_products AS SELECT * FROM products;", "dbo.v_products")
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
    public void ExtractIdentifierFromSql_ExtractsProcedureOperations()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var testCases = new[]
        {
            ("CREATE PROCEDURE [dbo].[sp_GetCustomer] AS BEGIN SELECT 1; END", "dbo.sp_GetCustomer"),
            ("ALTER PROC dbo.sp_UpdateOrder AS BEGIN UPDATE orders SET processed = 1; END", "dbo.sp_UpdateOrder"),
            ("DROP PROCEDURE [dbo].[sp_OldProc];", "dbo.sp_OldProc"),
            ("DROP PROC IF EXISTS dbo.sp_TempProc;", "dbo.sp_TempProc"),
            ("CREATE OR ALTER PROCEDURE [dbo].[sp_ProcessPayment] AS BEGIN RETURN; END", "dbo.sp_ProcessPayment")
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
    public void ExtractIdentifierFromSql_ExtractsFunctionOperations()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var testCases = new[]
        {
            ("CREATE FUNCTION [dbo].[fn_CalculateDiscount](@Amount DECIMAL) RETURNS DECIMAL AS BEGIN RETURN @Amount * 0.9; END", "dbo.fn_CalculateDiscount"),
            ("ALTER FUNCTION dbo.fn_GetFullName(@First NVARCHAR(50)) RETURNS NVARCHAR(100) AS BEGIN RETURN @First; END", "dbo.fn_GetFullName"),
            ("DROP FUNCTION [dbo].[fn_OldFunction];", "dbo.fn_OldFunction"),
            ("DROP FUNCTION IF EXISTS dbo.fn_TempFunction;", "dbo.fn_TempFunction"),
            ("CREATE OR ALTER FUNCTION [dbo].[fn_CalculateTax](@Amount DECIMAL) RETURNS DECIMAL AS BEGIN RETURN @Amount * 0.15; END", "dbo.fn_CalculateTax")
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
    public void ExtractIdentifierFromSql_DoesNotConfuseConstraintKeyword()
    {
        // Arrange
        var updater = new ExclusionCommentUpdater();
        var method = typeof(ExclusionCommentUpdater).GetMethod("ExtractIdentifierFromSql", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        // Test that ADD column doesn't match when CONSTRAINT keyword is present
        var sql1 = "ALTER TABLE [dbo].[test_migrations] ADD CONSTRAINT [DF_test] DEFAULT (0) FOR [col];";
        var result1 = method.Invoke(updater, new object[] { sql1 }) as string;
        Assert.Equal("dbo.test_migrations.DF_test", result1);
        Assert.NotEqual("dbo.test_migrations.CONSTRAINT", result1);
        
        // Test that ADD column DOES match when CONSTRAINT is not present
        var sql2 = "ALTER TABLE [dbo].[test_migrations] ADD [newcol] INT NULL;";
        var result2 = method.Invoke(updater, new object[] { sql2 }) as string;
        Assert.Equal("dbo.test_migrations.newcol", result2);
    }
    
    [Fact]
    public async Task UpdateMigrationScript_CommentsOutAllExcludedOperations()
    {
        // Comprehensive test for all SQL operation types
        var migrationPath = Path.Combine(_testDirectory, "migration_comprehensive.sql");
        var migrationContent = @"-- Migration: comprehensive test
-- Rename operations
EXEC sp_rename '[dbo].[test_migrations].[bb1]', 'tempof', 'COLUMN';
GO

-- Drop operations
ALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_gsdf];
GO

ALTER TABLE [dbo].[test_migrations] DROP COLUMN [ga];
GO

-- Modification operations
ALTER TABLE [dbo].[test_migrations] ALTER COLUMN [zura] NVARCHAR (105) NOT NULL;
GO

-- Create operations
ALTER TABLE [dbo].[test_migrations] ADD [gjglksdf] INT NULL;
GO

ALTER TABLE [dbo].[test_migrations] ADD CONSTRAINT [DF_test_migrations_zura] DEFAULT (N'iura') FOR [zura];
GO

CREATE NONCLUSTERED INDEX [iTesting2] ON [dbo].[test_migrations]([zura] ASC);
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'testing - description', 
    @level0type = N'SCHEMA', @level0name = N'dbo', 
    @level1type = N'TABLE', @level1name = N'test_migrations', 
    @level2type = N'COLUMN', @level2name = N'zura';
GO";
        
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer",
            CommitHash = "test123"
        };
        
        // Exclude all operations
        manifest.ExcludedChanges.AddRange(new[]
        {
            new ManifestChange { Identifier = "dbo.test_migrations.bb1", Description = "renamed to tempof" },
            new ManifestChange { Identifier = "dbo.test_migrations.DF_test_migrations_gsdf", Description = "removed" },
            new ManifestChange { Identifier = "dbo.test_migrations.ga", Description = "removed" },
            new ManifestChange { Identifier = "dbo.test_migrations.zura", Description = "modified" },
            new ManifestChange { Identifier = "dbo.test_migrations.gjglksdf", Description = "added" },
            new ManifestChange { Identifier = "dbo.test_migrations.DF_test_migrations_zura", Description = "added" },
            new ManifestChange { Identifier = "dbo.test_migrations.iTesting2", Description = "added" },
            new ManifestChange { Identifier = "dbo.test_migrations.EP_Column_Description_zura", Description = "added" }
        });
        
        // Act
        await _updater.UpdateMigrationScriptAsync(migrationPath, manifest);
        var updatedContent = await File.ReadAllTextAsync(migrationPath);
        
        // Assert - All operations should be commented out
        Assert.Contains("-- EXCLUDED: dbo.test_migrations.bb1", updatedContent);
        Assert.True(updatedContent.Contains("/*\nEXEC sp_rename") || 
                    updatedContent.Contains("/*\r\nEXEC sp_rename"),
                    "Should contain commented out EXEC sp_rename");
        
        Assert.Contains("-- EXCLUDED: dbo.test_migrations.DF_test_migrations_gsdf", updatedContent);
        Assert.True(updatedContent.Contains("/*\nALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT") || 
                    updatedContent.Contains("/*\r\nALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT"),
                    "Should contain commented out DROP CONSTRAINT");
        
        Assert.Contains("-- EXCLUDED: dbo.test_migrations.ga", updatedContent);
        Assert.True(updatedContent.Contains("/*\nALTER TABLE [dbo].[test_migrations] DROP COLUMN [ga]") || 
                    updatedContent.Contains("/*\r\nALTER TABLE [dbo].[test_migrations] DROP COLUMN [ga]"),
                    "Should contain commented out DROP COLUMN [ga]");
        
        Assert.Contains("-- EXCLUDED: dbo.test_migrations.zura", updatedContent);
        Assert.True(updatedContent.Contains("/*\nALTER TABLE [dbo].[test_migrations] ALTER COLUMN [zura]") || 
                    updatedContent.Contains("/*\r\nALTER TABLE [dbo].[test_migrations] ALTER COLUMN [zura]"),
                    "Should contain commented out ALTER COLUMN [zura]");
        
        Assert.Contains("-- EXCLUDED: dbo.test_migrations.gjglksdf", updatedContent);
        Assert.True(updatedContent.Contains("/*\nALTER TABLE [dbo].[test_migrations] ADD [gjglksdf]") || 
                    updatedContent.Contains("/*\r\nALTER TABLE [dbo].[test_migrations] ADD [gjglksdf]"),
                    "Should contain commented out ADD [gjglksdf]");
        
        Assert.Contains("-- EXCLUDED: dbo.test_migrations.DF_test_migrations_zura", updatedContent);
        Assert.True(updatedContent.Contains("/*\nALTER TABLE [dbo].[test_migrations] ADD CONSTRAINT [DF_test_migrations_zura]") || 
                    updatedContent.Contains("/*\r\nALTER TABLE [dbo].[test_migrations] ADD CONSTRAINT [DF_test_migrations_zura]"),
                    "Should contain commented out ADD CONSTRAINT [DF_test_migrations_zura]");
        
        Assert.Contains("-- EXCLUDED: dbo.test_migrations.iTesting2", updatedContent);
        Assert.True(updatedContent.Contains("/*\nCREATE NONCLUSTERED INDEX") || 
                    updatedContent.Contains("/*\r\nCREATE NONCLUSTERED INDEX"),
                    "Should contain commented out CREATE NONCLUSTERED INDEX");
        
        Assert.Contains("-- EXCLUDED: dbo.test_migrations.EP_Column_Description_zura", updatedContent);
        Assert.True(updatedContent.Contains("/*\nEXECUTE sp_addextendedproperty") || 
                    updatedContent.Contains("/*\r\nEXECUTE sp_addextendedproperty"),
                    "Should contain commented out EXECUTE sp_addextendedproperty");
    }
    
    [Fact]
    public async Task UpdateMigrationScript_SelectiveExclusion()
    {
        // Test that only specified operations are excluded
        var migrationPath = Path.Combine(_testDirectory, "migration_selective.sql");
        var migrationContent = @"-- Migration: selective test
EXEC sp_rename '[dbo].[test_migrations].[bb1]', 'tempof', 'COLUMN';
GO
ALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_gsdf];
GO
ALTER TABLE [dbo].[test_migrations] DROP COLUMN [ga];
GO
ALTER TABLE [dbo].[test_migrations] ALTER COLUMN [zura] NVARCHAR (105) NOT NULL;
GO
CREATE NONCLUSTERED INDEX [iTesting2] ON [dbo].[test_migrations]([zura] ASC);
GO";
        
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer",
            CommitHash = "test123"
        };
        
        // Only exclude rename and index
        manifest.ExcludedChanges.AddRange(new[]
        {
            new ManifestChange { Identifier = "dbo.test_migrations.bb1", Description = "renamed to tempof" },
            new ManifestChange { Identifier = "dbo.test_migrations.iTesting2", Description = "added" }
        });
        
        // Act
        await _updater.UpdateMigrationScriptAsync(migrationPath, manifest);
        var updatedContent = await File.ReadAllTextAsync(migrationPath);
        
        // Assert - Only rename and index should be commented
        Assert.Contains("-- EXCLUDED: dbo.test_migrations.bb1", updatedContent);
        Assert.True(updatedContent.Contains("/*\nEXEC sp_rename") || 
                    updatedContent.Contains("/*\r\nEXEC sp_rename"),
                    "Should contain commented out EXEC sp_rename");
        
        Assert.Contains("-- EXCLUDED: dbo.test_migrations.iTesting2", updatedContent);
        Assert.True(updatedContent.Contains("/*\nCREATE NONCLUSTERED INDEX") || 
                    updatedContent.Contains("/*\r\nCREATE NONCLUSTERED INDEX"),
                    "Should contain commented out CREATE NONCLUSTERED INDEX");
        
        // These should NOT be commented
        Assert.DoesNotContain("-- EXCLUDED: dbo.test_migrations.DF_test_migrations_gsdf", updatedContent);
        Assert.DoesNotContain("-- EXCLUDED: dbo.test_migrations.ga", updatedContent);
        Assert.DoesNotContain("-- EXCLUDED: dbo.test_migrations.zura", updatedContent);
        Assert.False(updatedContent.Contains("/*\nALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT") ||
                     updatedContent.Contains("/*\r\nALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT"),
                     "Should not contain commented out DROP CONSTRAINT");
        Assert.False(updatedContent.Contains("/*\nALTER TABLE [dbo].[test_migrations] DROP COLUMN") ||
                     updatedContent.Contains("/*\r\nALTER TABLE [dbo].[test_migrations] DROP COLUMN"),
                     "Should not contain commented out DROP COLUMN");
        Assert.False(updatedContent.Contains("/*\nALTER TABLE [dbo].[test_migrations] ALTER COLUMN") ||
                     updatedContent.Contains("/*\r\nALTER TABLE [dbo].[test_migrations] ALTER COLUMN"),
                     "Should not contain commented out ALTER COLUMN");
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
GO

COMMIT TRANSACTION;\";
        
        await File.WriteAllTextAsync(migrationFile, originalContent);

        // Act
        await _updater.UpdateMigrationScriptAsync(migrationFile, manifest);

        // Assert
        var updatedContent = await File.ReadAllTextAsync(migrationFile);
        
        // Now that CREATE TABLE and CREATE PROCEDURE are detected, they should be commented out
        
        // Users table should be excluded
        Assert.Contains("-- EXCLUDED: dbo.Users", updatedContent);
        Assert.Contains("/*", updatedContent);
        Assert.Contains("CREATE TABLE [dbo].[Users]", updatedContent);
        
        // Products table should NOT be excluded (not in manifest)
        Assert.DoesNotContain("-- EXCLUDED: dbo.Products", updatedContent);
        Assert.Contains("CREATE TABLE [dbo].[Products]", updatedContent);
        
        // Stored procedure should be excluded
        Assert.Contains("-- EXCLUDED: dbo.sp_GetUser", updatedContent);
        Assert.Contains("CREATE PROCEDURE [dbo].[sp_GetUser]", updatedContent);
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
        
        // CREATE TABLE is now detected by ExtractIdentifierFromSql
        // ExcludedTable should be commented out
        Assert.Contains("-- EXCLUDED: dbo.ExcludedTable", updatedContent);
        Assert.Contains("/*", updatedContent);
        Assert.Contains("CREATE TABLE [dbo].[ExcludedTable]", updatedContent);
        
        // IncludedTable should NOT be commented out
        Assert.DoesNotContain("-- EXCLUDED: dbo.IncludedTable", updatedContent);
        Assert.DoesNotContain("/*\nCREATE TABLE [dbo].[IncludedTable]", updatedContent);
    }

    [Fact]
    public async Task UpdateSerializedFiles_AddsInlineCommentsForColumnExclusions()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "servers", "testserver", "testdb");
        var tablePath = Path.Combine(dbPath, "schemas", "dbo", "Tables", "Users");
        Directory.CreateDirectory(tablePath);
        
        var tableContent = @"CREATE TABLE [dbo].[Users] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Email] NVARCHAR(100) NOT NULL,
    [Name] NVARCHAR(50) NULL,
    [CreatedDate] DATETIME2 NOT NULL
);";
        var tableSqlPath = Path.Combine(tablePath, "TBL_Users.sql");
        await File.WriteAllTextAsync(tableSqlPath, tableContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "testdb",
            ServerName = "testserver",
            Generated = DateTime.UtcNow,
            CommitHash = "abc123"
        };
        manifest.ExcludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Users.Email",
            Description = "added",
            ObjectType = "Table"
        });
        manifest.ExcludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Users.Name",
            Description = "modified",
            ObjectType = "Table"
        });
        
        // Act
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "testserver", "testdb", manifest);
        
        // Assert
        var modifiedContent = await File.ReadAllTextAsync(tableSqlPath);
        
        // Should contain inline comments for excluded columns (commas come before comments)
        Assert.Contains("[Email] NVARCHAR(100) NOT NULL,  -- EXCLUDED: added", modifiedContent);
        Assert.Contains("[Name] NVARCHAR(50) NULL,  -- EXCLUDED: modified", modifiedContent);
        
        // Should NOT contain inline comments for non-excluded columns
        Assert.DoesNotContain("[Id].*-- EXCLUDED", modifiedContent);
        Assert.DoesNotContain("[CreatedDate].*-- EXCLUDED", modifiedContent);
    }
    
    [Fact]
    public async Task UpdateSerializedFiles_RemovesInlineCommentsWhenColumnsIncluded()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "servers", "testserver", "testdb");
        var tablePath = Path.Combine(dbPath, "schemas", "dbo", "Tables", "Users");
        Directory.CreateDirectory(tablePath);
        
        // Start with content that has inline comments
        var tableContent = @"CREATE TABLE [dbo].[Users] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Email] NVARCHAR(100) NOT NULL,  -- EXCLUDED: added
    [Name] NVARCHAR(50) NULL,  -- EXCLUDED: modified
    [CreatedDate] DATETIME2 NOT NULL
);";
        var tableSqlPath = Path.Combine(tablePath, "TBL_Users.sql");
        await File.WriteAllTextAsync(tableSqlPath, tableContent);
        
        // Manifest with Email now included, Name still excluded
        var manifest = new ChangeManifest
        {
            DatabaseName = "testdb",
            ServerName = "testserver",
            Generated = DateTime.UtcNow,
            CommitHash = "abc123"
        };
        manifest.IncludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Users.Email",
            Description = "added",
            ObjectType = "Table"
        });
        manifest.ExcludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Users.Name",
            Description = "modified",
            ObjectType = "Table"
        });
        
        // Act
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "testserver", "testdb", manifest);
        
        // Assert
        var modifiedContent = await File.ReadAllTextAsync(tableSqlPath);
        
        // Email comment should be removed (check the specific line)
        var emailLine = modifiedContent.Split('\n').FirstOrDefault(l => l.Contains("[Email]")) ?? "";
        Assert.DoesNotContain("-- EXCLUDED", emailLine);
        Assert.Contains("[Email] NVARCHAR(100) NOT NULL", modifiedContent);
        
        // Name comment should remain
        Assert.Contains("[Name] NVARCHAR(50) NULL,  -- EXCLUDED: modified", modifiedContent);
    }
    
    [Fact]
    public async Task UpdateMigrationScript_ExcludesAndIncludesChangesCorrectly()
    {
        // Arrange
        var migrationPath = Path.Combine(_testDirectory, "servers", "testserver", "testdb", "z_migrations", "_20250809_test.sql");
        Directory.CreateDirectory(Path.GetDirectoryName(migrationPath)!);
        
        var migrationContent = @"BEGIN TRANSACTION;
ALTER TABLE [dbo].[Users] ADD [Email] NVARCHAR(100) NOT NULL;
GO
ALTER TABLE [dbo].[Users] ADD [Name] NVARCHAR(50) NULL;
GO
CREATE INDEX [IDX_Users_Email] ON [dbo].[Users]([Email]);
GO
COMMIT TRANSACTION;";
        
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "testdb",
            ServerName = "testserver",
            Generated = DateTime.UtcNow,
            CommitHash = "abc123"
        };
        manifest.ExcludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Users.Email",
            Description = "added"
        });
        manifest.IncludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Users.Name",
            Description = "added"
        });
        manifest.ExcludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Users.IDX_Users_Email",  // Index identifiers include table name
            Description = "added"
        });
        
        // Act
        await _updater.UpdateMigrationScriptAsync(migrationPath, manifest);
        
        // Assert
        var modifiedContent = await File.ReadAllTextAsync(migrationPath);
        
        // Email column should be excluded
        Assert.Contains("-- EXCLUDED: dbo.Users.Email", modifiedContent);
        Assert.Contains("/*", modifiedContent);
        Assert.Contains("ALTER TABLE [dbo].[Users] ADD [Email]", modifiedContent);
        Assert.Contains("*/", modifiedContent);
        
        // Name column should NOT be excluded
        Assert.DoesNotContain("-- EXCLUDED: dbo.Users.Name", modifiedContent);
        Assert.DoesNotContain("/*\nALTER TABLE [dbo].[Users] ADD [Name]", modifiedContent);
        
        // Index should be excluded
        Assert.Contains("-- EXCLUDED: dbo.Users.IDX_Users_Email", modifiedContent);
    }
    
    [Fact]
    public async Task UpdateMigrationScript_ReIncludesPreviouslyExcludedChanges()
    {
        // Arrange
        var migrationPath = Path.Combine(_testDirectory, "servers", "testserver", "testdb", "z_migrations", "_20250809_test.sql");
        Directory.CreateDirectory(Path.GetDirectoryName(migrationPath)!);
        
        // Start with an already excluded change
        var migrationContent = @"BEGIN TRANSACTION;
-- EXCLUDED: dbo.Users.Email - added
-- Source: test.manifest
/*
ALTER TABLE [dbo].[Users] ADD [Email] NVARCHAR(100) NOT NULL;
GO
*/
ALTER TABLE [dbo].[Users] ADD [Name] NVARCHAR(50) NULL;
GO
COMMIT TRANSACTION;";
        
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        // Manifest with Email now included
        var manifest = new ChangeManifest
        {
            DatabaseName = "testdb",
            ServerName = "testserver",
            Generated = DateTime.UtcNow,
            CommitHash = "abc123"
        };
        manifest.IncludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Users.Email",
            Description = "added"
        });
        manifest.IncludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Users.Name",
            Description = "added"
        });
        
        // Act
        await _updater.UpdateMigrationScriptAsync(migrationPath, manifest);
        
        // Assert
        var modifiedContent = await File.ReadAllTextAsync(migrationPath);
        
        // Email should be re-included
        Assert.Contains("-- dbo.Users.Email - Now included in migration", modifiedContent);
        Assert.DoesNotContain("-- EXCLUDED: dbo.Users.Email", modifiedContent);
        Assert.DoesNotContain("/*", modifiedContent);
        Assert.Contains("ALTER TABLE [dbo].[Users] ADD [Email] NVARCHAR(100) NOT NULL;", modifiedContent);
        
        // Name should remain included
        Assert.Contains("ALTER TABLE [dbo].[Users] ADD [Name] NVARCHAR(50) NULL;", modifiedContent);
    }
    
    [Fact]
    public async Task UpdateSerializedFiles_HandlesAllExcludedThenAllIncluded()
    {
        // Test scenario: Exclude all changes, then include all back
        var dbPath = Path.Combine(_testDirectory, "servers", "testserver", "testdb");
        var tablePath = Path.Combine(dbPath, "schemas", "dbo", "Tables", "Orders");
        Directory.CreateDirectory(tablePath);
        
        var tableContent = @"CREATE TABLE [dbo].[Orders] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [CustomerId] INT NOT NULL,
    [OrderDate] DATETIME2 NOT NULL,
    [Total] DECIMAL(10,2) NULL
);";
        var tableSqlPath = Path.Combine(tablePath, "TBL_Orders.sql");
        await File.WriteAllTextAsync(tableSqlPath, tableContent);
        
        // First, exclude all columns
        var manifestExcludeAll = new ChangeManifest
        {
            DatabaseName = "testdb",
            ServerName = "testserver",
            Generated = DateTime.UtcNow,
            CommitHash = "abc123"
        };
        manifestExcludeAll.ExcludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Orders.CustomerId",
            Description = "added",
            ObjectType = "Table"
        });
        manifestExcludeAll.ExcludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Orders.OrderDate",
            Description = "added",
            ObjectType = "Table"
        });
        manifestExcludeAll.ExcludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Orders.Total",
            Description = "added",
            ObjectType = "Table"
        });
        
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "testserver", "testdb", manifestExcludeAll);
        
        var excludedContent = await File.ReadAllTextAsync(tableSqlPath);
        Assert.Contains("[CustomerId] INT NOT NULL,  -- EXCLUDED: added", excludedContent);
        Assert.Contains("[OrderDate] DATETIME2 NOT NULL,  -- EXCLUDED: added", excludedContent);
        Assert.Contains("[Total] DECIMAL(10,2) NULL  -- EXCLUDED: added", excludedContent);
        
        // Now include all back
        var manifestIncludeAll = new ChangeManifest
        {
            DatabaseName = "testdb",
            ServerName = "testserver",
            Generated = DateTime.UtcNow,
            CommitHash = "abc123"
        };
        manifestIncludeAll.IncludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Orders.CustomerId",
            Description = "added",
            ObjectType = "Table"
        });
        manifestIncludeAll.IncludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Orders.OrderDate",
            Description = "added",
            ObjectType = "Table"
        });
        manifestIncludeAll.IncludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Orders.Total",
            Description = "added",
            ObjectType = "Table"
        });
        
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "testserver", "testdb", manifestIncludeAll);
        
        var includedContent = await File.ReadAllTextAsync(tableSqlPath);
        Assert.DoesNotContain("-- EXCLUDED:", includedContent);
        Assert.Contains("[CustomerId] INT NOT NULL", includedContent);
        Assert.Contains("[OrderDate] DATETIME2 NOT NULL", includedContent);
        Assert.Contains("[Total] DECIMAL(10,2) NULL", includedContent);
    }
    
    [Fact]
    public async Task UpdateSerializedFiles_ExcludedTakesPrecedenceWhenInBothSections()
    {
        // Test the edge case where a change appears in both INCLUDED and EXCLUDED sections
        var dbPath = Path.Combine(_testDirectory, "servers", "testserver", "testdb");
        var tablePath = Path.Combine(dbPath, "schemas", "dbo", "Tables", "Products");
        Directory.CreateDirectory(tablePath);
        
        var tableContent = @"CREATE TABLE [dbo].[Products] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Price] DECIMAL(10,2) NOT NULL
);";
        var tableSqlPath = Path.Combine(tablePath, "TBL_Products.sql");
        await File.WriteAllTextAsync(tableSqlPath, tableContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "testdb",
            ServerName = "testserver",
            Generated = DateTime.UtcNow,
            CommitHash = "abc123"
        };
        
        // Add Price to BOTH included and excluded - excluded should take precedence
        manifest.IncludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Products.Price",
            Description = "added",
            ObjectType = "Table"
        });
        manifest.ExcludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Products.Price",
            Description = "added",
            ObjectType = "Table"
        });
        
        // Name only in included
        manifest.IncludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Products.Name",
            Description = "added",
            ObjectType = "Table"
        });
        
        // Act
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "testserver", "testdb", manifest);
        
        // Assert
        var modifiedContent = await File.ReadAllTextAsync(tableSqlPath);
        
        // Price should be EXCLUDED (excluded takes precedence), and it's the last column so no comma
        Assert.Contains("[Price] DECIMAL(10,2) NOT NULL  -- EXCLUDED: added", modifiedContent);
        
        // Name should NOT be excluded (only in included)
        Assert.DoesNotContain("-- EXCLUDED", modifiedContent.Split('\n').FirstOrDefault(l => l.Contains("[Name]")) ?? "");
        Assert.Contains("[Name] NVARCHAR(100) NOT NULL", modifiedContent);
    }

    [Fact]
    public async Task UpdateSerializedFiles_OnlyAddsInlineCommentsForColumns_NoHeaderComments()
    {
        // Test that table files only get inline comments for columns, no header comments
        var dbPath = Path.Combine(_testDirectory, "servers", "testserver", "testdb");
        var tablePath = Path.Combine(dbPath, "schemas", "dbo", "Tables", "test_migrations");
        Directory.CreateDirectory(tablePath);
        
        var tableContent = @"CREATE TABLE [dbo].[test_migrations] (
    [test1]    NCHAR (10)     NULL,
    [zura]     NVARCHAR (105) NOT NULL,
    [gagadf]   NCHAR (10)     NULL,
    [tempof]   NCHAR (10)     NULL,
    [gjglksdf] INT            NULL
);";
        var tableSqlPath = Path.Combine(tablePath, "TBL_test_migrations.sql");
        await File.WriteAllTextAsync(tableSqlPath, tableContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "testdb",
            ServerName = "testserver",
            Generated = DateTime.UtcNow,
            CommitHash = "abc123"
        };
        
        // Add various types of exclusions
        manifest.ExcludedChanges.AddRange(new[]
        {
            // Actual columns - should get inline comments
            new ManifestChange { Identifier = "dbo.test_migrations.zura", Description = "modified", ObjectType = "Table" },
            new ManifestChange { Identifier = "dbo.test_migrations.gjglksdf", Description = "added", ObjectType = "Table" },
            
            // Non-column items - should NOT add any comments to table file
            new ManifestChange { Identifier = "dbo.test_migrations.DF_test_migrations_gsdf", Description = "removed", ObjectType = "Constraint" },
            new ManifestChange { Identifier = "dbo.test_migrations.iTesting2", Description = "added", ObjectType = "Index" },
            new ManifestChange { Identifier = "dbo.test_migrations.EP_Column_Description_zura", Description = "added", ObjectType = "ExtendedProperty" },
            
            // Columns that don't exist in table (removed) - should NOT add any comments
            new ManifestChange { Identifier = "dbo.test_migrations.ga", Description = "removed", ObjectType = "Table" },
            new ManifestChange { Identifier = "dbo.test_migrations.deletedColumn", Description = "removed", ObjectType = "Table" },
            new ManifestChange { Identifier = "dbo.test_migrations.gsdf", Description = "removed", ObjectType = "Table" }
        });
        
        // Act
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "testserver", "testdb", manifest);
        
        // Assert
        var modifiedContent = await File.ReadAllTextAsync(tableSqlPath);
        
        // Should have inline comments for existing columns (with proper comma placement)
        Assert.Contains("[zura]     NVARCHAR (105) NOT NULL,  -- EXCLUDED: modified", modifiedContent);
        // gjglksdf is the last column, so no comma
        Assert.Contains("[gjglksdf] INT            NULL  -- EXCLUDED: added", modifiedContent);
        
        // Should NOT have any header comments
        Assert.DoesNotContain("-- MIGRATION EXCLUDED:", modifiedContent);
        Assert.DoesNotContain("-- This change is NOT included", modifiedContent);
        Assert.DoesNotContain("-- See:", modifiedContent);
        
        // Should NOT have comments for non-existent columns (check more specifically)
        // These columns are completely removed, so they shouldn't have EXCLUDED comments
        Assert.DoesNotContain("[ga]", modifiedContent);  // Column ga should not exist at all
        Assert.DoesNotContain("-- EXCLUDED: removed", modifiedContent);  // No "removed" comments
        Assert.DoesNotContain("[gsdf]", modifiedContent);  // Column gsdf should not exist at all
        
        // The existing column gagadf should NOT have a comment (it's not in exclusions for removal - there's a typo in test)
        // Actually, gagdf is marked as removed but the column name in table is gagadf - they don't match
        Assert.Contains("[gagadf]   NCHAR (10)     NULL", modifiedContent);
        Assert.DoesNotContain("[gagadf]   NCHAR (10)     NULL  -- EXCLUDED", modifiedContent);
        
        // Should NOT have comments for constraints, indexes, or extended properties
        Assert.DoesNotContain("DF_test_migrations_gsdf", modifiedContent);
        Assert.DoesNotContain("iTesting2", modifiedContent);
        Assert.DoesNotContain("EP_Column_Description", modifiedContent);
        
        // Should start with CREATE TABLE (no header comments)
        Assert.StartsWith("CREATE TABLE", modifiedContent.Trim());
    }
    
    [Fact]
    public async Task UpdateSerializedFiles_PreservesCommasInColumnDefinitions()
    {
        // Test that commas are properly preserved when adding inline comments
        var dbPath = Path.Combine(_testDirectory, "servers", "testserver", "testdb");
        var tablePath = Path.Combine(dbPath, "schemas", "dbo", "Tables", "Products");
        Directory.CreateDirectory(tablePath);
        
        var tableContent = @"CREATE TABLE [dbo].[Products] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Price] DECIMAL(10,2) NOT NULL,
    [Stock] INT NULL
);";
        var tableSqlPath = Path.Combine(tablePath, "TBL_Products.sql");
        await File.WriteAllTextAsync(tableSqlPath, tableContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "testdb",
            ServerName = "testserver",
            Generated = DateTime.UtcNow,
            CommitHash = "abc123"
        };
        
        manifest.ExcludedChanges.AddRange(new[]
        {
            new ManifestChange { Identifier = "dbo.Products.Name", Description = "modified", ObjectType = "Table" },
            new ManifestChange { Identifier = "dbo.Products.Price", Description = "added", ObjectType = "Table" }
        });
        
        // Act
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "testserver", "testdb", manifest);
        
        // Assert
        var modifiedContent = await File.ReadAllTextAsync(tableSqlPath);
        
        // Debug output
        _output.WriteLine("Original content:");
        _output.WriteLine(tableContent);
        _output.WriteLine("");
        _output.WriteLine("Modified content:");
        _output.WriteLine(modifiedContent);
        _output.WriteLine("");
        _output.WriteLine($"Content changed: {modifiedContent != tableContent}");
        
        // Check each line individually for debugging
        var lines = modifiedContent.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            _output.WriteLine($"Line {i}: '{lines[i]}'");
            if (lines[i].Contains("[Name]"))
                _output.WriteLine($"  Name line found, has comma at end: {lines[i].TrimEnd().EndsWith(",")}");
            if (lines[i].Contains("[Price]"))
                _output.WriteLine($"  Price line found, has comma at end: {lines[i].TrimEnd().EndsWith(",")}");
        }
        
        // Commas should be preserved at the end of the line, not inside the comment
        Assert.Contains("[Name] NVARCHAR(100) NOT NULL,  -- EXCLUDED: modified", modifiedContent);
        Assert.Contains("[Price] DECIMAL(10,2) NOT NULL,  -- EXCLUDED: added", modifiedContent);
        
        // Last column should not have a comma
        Assert.Contains("[Stock] INT NULL\n)", modifiedContent);
    }
    
    [Fact]
    public async Task UpdateSerializedFiles_MultipleRunsDoNotDuplicateComments()
    {
        // Test that running the update multiple times doesn't create duplicate comments
        var dbPath = Path.Combine(_testDirectory, "servers", "testserver", "testdb");
        var tablePath = Path.Combine(dbPath, "schemas", "dbo", "Tables", "Orders");
        Directory.CreateDirectory(tablePath);
        
        var tableContent = @"CREATE TABLE [dbo].[Orders] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [CustomerId] INT NOT NULL,
    [OrderDate] DATETIME2 NOT NULL
);";
        var tableSqlPath = Path.Combine(tablePath, "TBL_Orders.sql");
        await File.WriteAllTextAsync(tableSqlPath, tableContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "testdb",
            ServerName = "testserver",
            Generated = DateTime.UtcNow,
            CommitHash = "abc123"
        };
        
        manifest.ExcludedChanges.AddRange(new[]
        {
            new ManifestChange { Identifier = "dbo.Orders.CustomerId", Description = "added", ObjectType = "Table" },
            new ManifestChange { Identifier = "dbo.Orders.DF_Orders_OrderDate", Description = "removed", ObjectType = "Constraint" },
            new ManifestChange { Identifier = "dbo.Orders.IX_Orders_Date", Description = "added", ObjectType = "Index" }
        });
        
        // Act - Run multiple times
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "testserver", "testdb", manifest);
        var firstRun = await File.ReadAllTextAsync(tableSqlPath);
        
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "testserver", "testdb", manifest);
        var secondRun = await File.ReadAllTextAsync(tableSqlPath);
        
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "testserver", "testdb", manifest);
        var thirdRun = await File.ReadAllTextAsync(tableSqlPath);
        
        // Assert
        Assert.Equal(firstRun, secondRun);
        Assert.Equal(secondRun, thirdRun);
        
        // Should have exactly one inline comment for CustomerId
        var customerIdCommentCount = Regex.Matches(secondRun, @"-- EXCLUDED: added").Count;
        Assert.Equal(1, customerIdCommentCount);
        
        // Should have no header comments
        Assert.DoesNotContain("-- MIGRATION EXCLUDED:", secondRun);
        
        // Should start with CREATE TABLE
        Assert.StartsWith("CREATE TABLE", secondRun.Trim());
    }
    
    [Fact]
    public async Task UpdateSerializedFiles_HandlesRemovedColumns_NoCommentsAdded()
    {
        // Test that removed columns don't get any comments in the table file
        var dbPath = Path.Combine(_testDirectory, "servers", "testserver", "testdb");
        var tablePath = Path.Combine(dbPath, "schemas", "dbo", "Tables", "Customers");
        Directory.CreateDirectory(tablePath);
        
        // Current table without the removed columns
        var tableContent = @"CREATE TABLE [dbo].[Customers] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL
);";
        var tableSqlPath = Path.Combine(tablePath, "TBL_Customers.sql");
        await File.WriteAllTextAsync(tableSqlPath, tableContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "testdb",
            ServerName = "testserver",
            Generated = DateTime.UtcNow,
            CommitHash = "abc123"
        };
        
        // These columns were removed - they don't exist in the current table
        manifest.ExcludedChanges.AddRange(new[]
        {
            new ManifestChange { Identifier = "dbo.Customers.Email", Description = "removed", ObjectType = "Table" },
            new ManifestChange { Identifier = "dbo.Customers.Phone", Description = "removed", ObjectType = "Table" },
            new ManifestChange { Identifier = "dbo.Customers.Address", Description = "removed", ObjectType = "Table" }
        });
        
        // Act
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "testserver", "testdb", manifest);
        
        // Assert
        var modifiedContent = await File.ReadAllTextAsync(tableSqlPath);
        
        // Should be unchanged - no comments added for removed columns
        Assert.Equal(tableContent, modifiedContent);
        
        // Should not contain any references to removed columns
        Assert.DoesNotContain("Email", modifiedContent);
        Assert.DoesNotContain("Phone", modifiedContent);
        Assert.DoesNotContain("Address", modifiedContent);
        Assert.DoesNotContain("-- EXCLUDED:", modifiedContent);
        Assert.DoesNotContain("-- MIGRATION EXCLUDED:", modifiedContent);
    }
    
    [Fact]
    public async Task UpdateSerializedFiles_MixedScenario_OnlyColumnsGetInlineComments()
    {
        // Comprehensive test with all types of changes
        var dbPath = Path.Combine(_testDirectory, "servers", "testserver", "testdb");
        var tablePath = Path.Combine(dbPath, "schemas", "dbo", "Tables", "test_migrations");
        Directory.CreateDirectory(tablePath);
        
        var tableContent = @"CREATE TABLE [dbo].[test_migrations] (
    [test1]    NCHAR (10)     NULL,
    [zura]     NVARCHAR (105) NOT NULL,
    [gagadf]   NCHAR (10)     NULL,
    [tempof]   NCHAR (10)     NULL,
    [gjglksdf] INT            NULL
);";
        var tableSqlPath = Path.Combine(tablePath, "TBL_test_migrations.sql");
        await File.WriteAllTextAsync(tableSqlPath, tableContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "testdb",
            ServerName = "testserver",
            Generated = DateTime.UtcNow,
            CommitHash = "abc123"
        };
        
        // All 9 changes from the actual scenario
        manifest.ExcludedChanges.AddRange(new[]
        {
            new ManifestChange { Identifier = "dbo.test_migrations.bb1", Description = "renamed to tempof", ObjectType = "Table" },
            new ManifestChange { Identifier = "dbo.test_migrations.zura", Description = "modified", ObjectType = "Table" },
            new ManifestChange { Identifier = "dbo.test_migrations.iTesting2", Description = "added", ObjectType = "Index" },
            new ManifestChange { Identifier = "dbo.test_migrations.gjglksdf", Description = "added", ObjectType = "Table" },
            new ManifestChange { Identifier = "dbo.test_migrations.ga", Description = "removed", ObjectType = "Table" },
            new ManifestChange { Identifier = "dbo.test_migrations.deletedColumn", Description = "removed", ObjectType = "Table" },
            new ManifestChange { Identifier = "dbo.test_migrations.gsdf", Description = "removed", ObjectType = "Table" },
            new ManifestChange { Identifier = "dbo.test_migrations.DF_test_migrations_gsdf", Description = "removed", ObjectType = "Constraint" },
            new ManifestChange { Identifier = "dbo.test_migrations.EP_Column_Description_zura", Description = "added", ObjectType = "ExtendedProperty" }
        });
        
        // Act
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "testserver", "testdb", manifest);
        
        // Assert
        var modifiedContent = await File.ReadAllTextAsync(tableSqlPath);
        
        // Should have inline comments ONLY for existing columns (with proper comma placement)
        Assert.Contains("[zura]     NVARCHAR (105) NOT NULL,  -- EXCLUDED: modified", modifiedContent);
        // gjglksdf is the last column, so no comma
        Assert.Contains("[gjglksdf] INT            NULL  -- EXCLUDED: added", modifiedContent);
        
        // bb1 was renamed to tempof, but bb1 doesn't exist in current table
        Assert.DoesNotContain("bb1", modifiedContent);
        
        // Should NOT have any header comments
        var lines = modifiedContent.Split('\n');
        Assert.StartsWith("CREATE TABLE", lines[0].Trim());
        Assert.DoesNotContain("-- MIGRATION EXCLUDED:", modifiedContent);
        Assert.DoesNotContain("-- This change is NOT included", modifiedContent);
        Assert.DoesNotContain("-- See:", modifiedContent);
        
        // Should NOT have any references to non-column objects
        Assert.DoesNotContain("iTesting2", modifiedContent);
        Assert.DoesNotContain("DF_test_migrations_gsdf", modifiedContent);
        Assert.DoesNotContain("EP_Column_Description", modifiedContent);
        
        // Should NOT have any references to removed columns
        Assert.DoesNotContain("ga", modifiedContent.Replace("gagadf", "")); // Exclude gagadf when checking for ga
        Assert.DoesNotContain("gagdf", modifiedContent);
        Assert.DoesNotContain("gsdf", modifiedContent);
    }
    
    [Fact]
    public async Task UpdateMigrationScript_ExcludesDropIndexOperations()
    {
        // Test specifically for DROP INDEX exclusion - the original issue
        var migrationPath = Path.Combine(_testDirectory, "migration_drop_index.sql");
        var migrationContent = @"-- Migration: test drop index
-- Drop operations
DROP INDEX [iTesting2] ON [dbo].[test_migrations];
GO

DROP INDEX [IX_Users_Email] ON [dbo].[Users];
GO

-- Other operations
ALTER TABLE [dbo].[test_migrations] ADD [newColumn] INT NULL;
GO

CREATE INDEX [IX_NewIndex] ON [dbo].[test_migrations]([newColumn]);
GO";
        
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer",
            CommitHash = "test123"
        };
        
        // Exclude the DROP INDEX operations
        manifest.ExcludedChanges.AddRange(new[]
        {
            new ManifestChange { Identifier = "dbo.test_migrations.iTesting2", Description = "removed" },
            new ManifestChange { Identifier = "dbo.Users.IX_Users_Email", Description = "removed" }
        });
        
        // Act
        await _updater.UpdateMigrationScriptAsync(migrationPath, manifest);
        var updatedContent = await File.ReadAllTextAsync(migrationPath);
        
        // Assert - DROP INDEX operations should be commented out
        Assert.Contains("-- EXCLUDED: dbo.test_migrations.iTesting2 - removed", updatedContent);
        // Check for both Unix and Windows line endings
        Assert.True(updatedContent.Contains("/*\nDROP INDEX [iTesting2]") || 
                    updatedContent.Contains("/*\r\nDROP INDEX [iTesting2]") ||
                    updatedContent.Contains("/*" + Environment.NewLine + "DROP INDEX [iTesting2]"),
                    "Should contain commented out DROP INDEX [iTesting2]");
        
        Assert.Contains("-- EXCLUDED: dbo.Users.IX_Users_Email - removed", updatedContent);
        // Check for both Unix and Windows line endings
        Assert.True(updatedContent.Contains("/*\nDROP INDEX [IX_Users_Email]") || 
                    updatedContent.Contains("/*\r\nDROP INDEX [IX_Users_Email]") ||
                    updatedContent.Contains("/*" + Environment.NewLine + "DROP INDEX [IX_Users_Email]"),
                    "Should contain commented out DROP INDEX [IX_Users_Email]");
        
        // Other operations should NOT be commented
        Assert.DoesNotContain("-- EXCLUDED: dbo.test_migrations.newColumn", updatedContent);
        // Check that these are not commented with any line ending style
        Assert.False(updatedContent.Contains("/*\nALTER TABLE [dbo].[test_migrations] ADD [newColumn]") ||
                     updatedContent.Contains("/*\r\nALTER TABLE [dbo].[test_migrations] ADD [newColumn]"),
                     "Should not contain commented out ALTER TABLE ADD");
        Assert.False(updatedContent.Contains("/*\nCREATE INDEX [IX_NewIndex]") ||
                     updatedContent.Contains("/*\r\nCREATE INDEX [IX_NewIndex]"),
                     "Should not contain commented out CREATE INDEX");
    }

    [Fact]
    public async Task UpdateMigrationScript_ExcludesConstraintModification_BothDropAndAdd()
    {
        // Test that when a constraint modification is excluded, BOTH DROP and ADD statements are commented out
        var migrationPath = Path.Combine(_testDirectory, "migration_constraint_mod.sql");
        var migrationContent = @"-- Migration: test constraint modification
-- Modification operations
ALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_zura];
GO

ALTER TABLE [dbo].[test_migrations]
    ADD CONSTRAINT [DF_test_migrations_zura] DEFAULT (N'iura') FOR [zura];
GO

-- Other operations
ALTER TABLE [dbo].[test_migrations] ADD [newColumn] INT NULL;
GO";
        
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer",
            CommitHash = "test123"
        };
        
        // Exclude the constraint modification
        manifest.ExcludedChanges.Add(
            new ManifestChange { Identifier = "dbo.test_migrations.DF_test_migrations_zura", Description = "modified" }
        );
        
        // Act
        await _updater.UpdateMigrationScriptAsync(migrationPath, manifest);
        var updatedContent = await File.ReadAllTextAsync(migrationPath);
        
        // Assert - BOTH DROP and ADD CONSTRAINT should be commented out
        Assert.Contains("-- EXCLUDED: dbo.test_migrations.DF_test_migrations_zura - modified", updatedContent);
        
        // Check that DROP CONSTRAINT is commented
        Assert.True(updatedContent.Contains("/*\nALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_zura]") || 
                    updatedContent.Contains("/*\r\nALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_zura]"),
                    "DROP CONSTRAINT should be commented out");
        
        // Check that ADD CONSTRAINT is ALSO commented
        var addConstraintCommented = 
            updatedContent.Contains("/*\nALTER TABLE [dbo].[test_migrations]\n    ADD CONSTRAINT [DF_test_migrations_zura]") ||
            updatedContent.Contains("/*\r\nALTER TABLE [dbo].[test_migrations]\r\n    ADD CONSTRAINT [DF_test_migrations_zura]") ||
            updatedContent.Contains("/*\nALTER TABLE [dbo].[test_migrations]\r\n    ADD CONSTRAINT [DF_test_migrations_zura]");
            
        Assert.True(addConstraintCommented, "ADD CONSTRAINT should also be commented out");
        
        // The newColumn should NOT be commented (it's not excluded)
        Assert.DoesNotContain("/*\nALTER TABLE [dbo].[test_migrations] ADD [newColumn]", updatedContent);
        Assert.DoesNotContain("/*\r\nALTER TABLE [dbo].[test_migrations] ADD [newColumn]", updatedContent);
    }
    
    [Fact]
    public async Task UpdateMigrationScript_ExcludedTakesPrecedenceInMigrationToo()
    {
        // Test that excluded takes precedence in migration scripts as well
        var migrationPath = Path.Combine(_testDirectory, "servers", "testserver", "testdb", "z_migrations", "_20250809_test.sql");
        Directory.CreateDirectory(Path.GetDirectoryName(migrationPath)!);
        
        var migrationContent = @"BEGIN TRANSACTION;
ALTER TABLE [dbo].[Products] ADD [Price] DECIMAL(10,2) NOT NULL;
GO
ALTER TABLE [dbo].[Products] ADD [Name] NVARCHAR(100) NOT NULL;
GO
COMMIT TRANSACTION;";
        
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "testdb",
            ServerName = "testserver",
            Generated = DateTime.UtcNow,
            CommitHash = "abc123"
        };
        
        // Add Price to BOTH sections
        manifest.IncludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Products.Price",
            Description = "added"
        });
        manifest.ExcludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Products.Price",
            Description = "added"
        });
        
        // Name only in included
        manifest.IncludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Products.Name",
            Description = "added"
        });
        
        // Act
        await _updater.UpdateMigrationScriptAsync(migrationPath, manifest);
        
        // Assert
        var modifiedContent = await File.ReadAllTextAsync(migrationPath);
        
        // Price should be excluded
        Assert.Contains("-- EXCLUDED: dbo.Products.Price", modifiedContent);
        Assert.Contains("/*", modifiedContent);
        
        // Name should NOT be excluded
        Assert.DoesNotContain("-- EXCLUDED: dbo.Products.Name", modifiedContent);
    }
    
    [Fact]
    public async Task UpdateSerializedFiles_CommaPlacementCorrect()
    {
        // Test the exact scenario from PR #172 - commas should be outside comments
        var dbPath = Path.Combine(_testDirectory, "servers", "testserver", "testdb");
        var tablePath = Path.Combine(dbPath, "schemas", "dbo", "Tables", "test_migrations");
        Directory.CreateDirectory(tablePath);
        
        var tableContent = @"CREATE TABLE [dbo].[test_migrations] (
    [test1]    NCHAR (10)     NULL,
    [zura]     NVARCHAR (105) NOT NULL,
    [gagadf]   NCHAR (10)     NULL,
    [tempof]   NCHAR (10)     NULL,
    [gjglksdf] INT            NULL,
    [gggassdf] NVARCHAR (50)  NULL,
    [ggggg]    NCHAR (10)     NULL
);";
        var tableSqlPath = Path.Combine(tablePath, "TBL_test_migrations.sql");
        await File.WriteAllTextAsync(tableSqlPath, tableContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "testdb",
            ServerName = "testserver",
            Generated = DateTime.UtcNow,
            CommitHash = "abc123"
        };
        
        // Exclude the last two columns as in PR #172
        manifest.ExcludedChanges.AddRange(new[]
        {
            new ManifestChange { Identifier = "dbo.test_migrations.gggassdf", Description = "added", ObjectType = "Table" },
            new ManifestChange { Identifier = "dbo.test_migrations.ggggg", Description = "added", ObjectType = "Table" }
        });
        
        // Act
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "testserver", "testdb", manifest);
        
        // Assert
        var modifiedContent = await File.ReadAllTextAsync(tableSqlPath);
        
        // The second-to-last column should have comma AFTER the definition, BEFORE the comment
        Assert.Contains("[gggassdf] NVARCHAR (50)  NULL,  -- EXCLUDED: added", modifiedContent);
        
        // The last column should NOT have a comma
        Assert.Contains("[ggggg]    NCHAR (10)     NULL  -- EXCLUDED: added", modifiedContent);
        Assert.DoesNotContain("[ggggg]    NCHAR (10)     NULL,", modifiedContent);
        
        // Verify the overall structure is preserved
        Assert.Contains("CREATE TABLE [dbo].[test_migrations]", modifiedContent);
        Assert.EndsWith(");", modifiedContent.Trim());
    }
    
    [Fact]
    public async Task UpdateSerializedFiles_HandlesIndexExclusions()
    {
        // Test index exclusion comments
        var dbPath = Path.Combine(_testDirectory, "servers", "testserver", "testdb");
        var indexPath = Path.Combine(dbPath, "schemas", "dbo", "Tables", "Users");
        Directory.CreateDirectory(indexPath);
        
        var indexContent = @"CREATE NONCLUSTERED INDEX [IDX_Users_Email]
    ON [dbo].[Users]([Email] ASC);";
        var indexSqlPath = Path.Combine(indexPath, "IDX_Users_Email.sql");
        await File.WriteAllTextAsync(indexSqlPath, indexContent);
        
        var manifest = new ChangeManifest
        {
            DatabaseName = "testdb",
            ServerName = "testserver",
            Generated = DateTime.UtcNow,
            CommitHash = "abc123"
        };
        manifest.ExcludedChanges.Add(new ManifestChange 
        { 
            Identifier = "dbo.Users.IDX_Users_Email",  // Index identifiers include table name
            Description = "added",
            ObjectType = "Index"
        });
        
        // Act
        await _updater.UpdateSerializedFilesAsync(_testDirectory, "testserver", "testdb", manifest);
        
        // Assert
        var modifiedContent = await File.ReadAllTextAsync(indexSqlPath);
        
        // Index files don't get comments in the new implementation (only columns in table files get inline comments)
        // So the index file should remain unchanged
        Assert.Equal(indexContent, modifiedContent);
    }
}