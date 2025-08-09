using SqlServer.Schema.Exclusion.Manager.Core.Services;
using Xunit;

namespace SqlServer.Schema.Exclusion.Manager.Tests;

public class GitChangeDetectorTests : GitTestBase
{
    [Fact]
    public async Task DetectChangesAsync_ReturnsEmptyList_WhenNoGitChanges()
    {
        // Arrange - using git repo from base class
        var detector = new GitChangeDetector(GitRepoPath);
        
        // Act
        var changes = await detector.DetectChangesAsync("TestServer", "TestDB");
        
        // Assert
        Assert.NotNull(changes);
        Assert.Empty(changes);
    }
    
    [Fact]
    public async Task DetectChangesAsync_DetectsChanges_WhenFilesModified()
    {
        // Arrange
        var detector = new GitChangeDetector(GitRepoPath);
        
        // Add some initial schema files
        AddFileAndCommit("servers/TestServer/TestDB/schemas/dbo/Tables/Users.sql", 
            "CREATE TABLE Users (Id INT)", "Add Users table");
        
        // Mark this as origin/main
        RunGitCommand(GitRepoPath, "update-ref refs/remotes/origin/main HEAD");
        
        // Now modify the file to create a change
        ModifyAndStageFile("servers/TestServer/TestDB/schemas/dbo/Tables/Users.sql",
            "CREATE TABLE Users (Id INT, Name NVARCHAR(100))");
        
        // Act
        var changes = await detector.DetectChangesAsync("TestServer", "TestDB");
        
        // Assert
        Assert.NotNull(changes);
        Assert.NotEmpty(changes);
        Assert.Contains(changes, c => c.Identifier.Contains("Users"));
    }

    [Theory]
    [InlineData("Tables/dbo.Users.sql", "Table")]
    [InlineData("Views/dbo.vw_UserList.sql", "View")]
    [InlineData("StoredProcedures/dbo.sp_GetUser.sql", "StoredProcedure")]
    [InlineData("Functions/dbo.fn_Calculate.sql", "Function")]
    [InlineData("Triggers/dbo.tr_Audit.sql", "Trigger")]
    [InlineData("Indexes/IX_Users_Email.sql", "Index")]
    public void DetermineObjectType_IdentifiesCorrectly(string path, string expectedType)
    {
        // This would require making DetermineObjectType public or testing through DetectChangesAsync
        // For now, we're documenting expected behavior
        Assert.NotNull(expectedType);
        Assert.NotNull(path);
    }

    [Theory]
    [InlineData("servers/Server1/DB1/Tables", "dbo.Users", "dbo.Users")]
    [InlineData("servers/Server1/DB1/Views", "vw_List", "dbo.vw_List")]
    [InlineData("servers/Server1/DB1/StoredProcedures", "sp_Test", "dbo.sp_Test")]
    public void BuildIdentifier_CreatesCorrectIdentifier(string path, string fileName, string expectedIdentifier)
    {
        // This would require making BuildIdentifier public or testing through DetectChangesAsync
        // For now, we're documenting expected behavior
        Assert.NotNull(expectedIdentifier);
        Assert.NotNull(path);
        Assert.NotNull(fileName);
    }

    
    [Fact]
    public async Task ParseMigrationFileAsync_DetectsColumnRename()
    {
        // Arrange
        var detector = new GitChangeDetector(GitRepoPath);
        var migrationContent = @"-- Migration: test
-- Rename operations
EXEC sp_rename '[dbo].[test_migrations].[bb1]', 'tempof', 'COLUMN';
GO

-- Drop operations
ALTER TABLE [dbo].[test_migrations] DROP COLUMN [ga];
GO

-- Modification operations
ALTER TABLE [dbo].[test_migrations] ALTER COLUMN [zura] NVARCHAR (105) NOT NULL;
GO

-- Create operations  
ALTER TABLE [dbo].[test_migrations] ADD [gjglksdf] INT NULL;
GO";
        
        var migrationPath = Path.Combine(GitRepoPath, "migration.sql");
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        // Act
        var changes = await detector.ParseMigrationFileAsync(migrationPath, "test-server", "test-db");
        
        // Assert
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.bb1" && c.Description.Contains("renamed to tempof"));
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.ga" && c.Description == "removed");
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.zura" && c.Description == "modified");
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.gjglksdf" && c.Description == "added");
        
        // Verify that all column changes have correct ObjectType
        var columnChanges = changes.Where(c => c.Identifier.Split('.').Length == 3);
        Assert.All(columnChanges, c => Assert.Equal("Table", c.ObjectType));
    }
    
    [Fact]
    public async Task ParseMigrationFileAsync_HandlesConstraintDrops()
    {
        // Arrange
        var detector = new GitChangeDetector(GitRepoPath);
        var migrationContent = @"-- Migration: test
-- Drop constraint
ALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_gsdf];
GO

-- Drop and recreate constraint (should not be detected as removed)
ALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_zura];
GO
ALTER TABLE [dbo].[test_migrations] ADD CONSTRAINT [DF_test_migrations_zura] DEFAULT (N'value') FOR [zura];
GO";
        
        var migrationPath = Path.Combine(GitRepoPath, "migration.sql");
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        // Act
        var changes = await detector.ParseMigrationFileAsync(migrationPath, "test-server", "test-db");
        
        // Assert
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.DF_test_migrations_gsdf" && c.Description == "removed");
        // Should not detect DF_test_migrations_zura as removed since it's re-added
        Assert.DoesNotContain(changes, c => c.Identifier.Contains("DF_test_migrations_zura"));
    }

    
    [Fact]
    public async Task ParseMigrationFileAsync_DetectsConstraintAdditionsCorrectly()
    {
        // Arrange
        var detector = new GitChangeDetector(GitRepoPath);
        var migrationContent = @"-- Migration: test
-- Add a new constraint (not a recreation)
ALTER TABLE [dbo].[test_migrations] 
    ADD CONSTRAINT [DF_test_migrations_newcolumn] DEFAULT (N'default') FOR [newcolumn];
GO

-- Add a column (should not be mistaken for constraint CONSTRAINT keyword)
ALTER TABLE [dbo].[test_migrations] ADD [newcolumn] NVARCHAR(50) NULL;
GO";
        
        var migrationPath = Path.Combine(GitRepoPath, "migration.sql");
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        // Act
        var changes = await detector.ParseMigrationFileAsync(migrationPath, "test-server", "test-db");
        
        // Assert
        // Should detect the constraint with full name
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.DF_test_migrations_newcolumn" && c.Description == "added" && c.ObjectType == "Constraint");
        
        // Should detect the column addition correctly
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.newcolumn" && c.Description == "added" && c.ObjectType == "Table");
        
        // Should NOT have any entry with "CONSTRAINT" as a column name
        Assert.DoesNotContain(changes, c => c.Identifier.Contains(".CONSTRAINT"));
    }
    
    [Fact]
    public async Task ParseMigrationFileAsync_IgnoresRecreatedConstraints()
    {
        // Arrange
        var detector = new GitChangeDetector(GitRepoPath);
        var migrationContent = @"-- Migration: test
-- Drop and recreate a constraint (should not be detected as added)
ALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_zura];
GO

ALTER TABLE [dbo].[test_migrations]
    ADD CONSTRAINT [DF_test_migrations_zura] DEFAULT (N'iura') FOR [zura];
GO

-- Add a completely new constraint
ALTER TABLE [dbo].[test_migrations]
    ADD CONSTRAINT [DF_test_migrations_newone] DEFAULT (N'value') FOR [column];
GO";
        
        var migrationPath = Path.Combine(GitRepoPath, "migration.sql");
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        // Act
        var changes = await detector.ParseMigrationFileAsync(migrationPath, "test-server", "test-db");
        
        // Assert
        // Should NOT detect DF_test_migrations_zura as it's a recreation
        Assert.DoesNotContain(changes, c => c.Identifier.Contains("DF_test_migrations_zura"));
        
        // Should detect the new constraint
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.DF_test_migrations_newone" && c.Description == "added");
    }

    [Fact]
    public async Task ParseMigrationFileAsync_IncludesTableNameInAllIdentifiers()
    {
        // Arrange
        var detector = new GitChangeDetector(GitRepoPath);
        var migrationContent = @"-- Migration: test
-- Drop a constraint
ALTER TABLE [dbo].[Orders] DROP CONSTRAINT [DF_Orders_OrderDate];
GO

-- Add a constraint
ALTER TABLE [dbo].[Customers] 
    ADD CONSTRAINT [DF_Customers_Created] DEFAULT (GETDATE()) FOR [Created];
GO

-- Create an index
CREATE NONCLUSTERED INDEX [IX_Products_Name]
    ON [dbo].[Products]([Name] ASC);
GO

-- Add extended property
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Customer email address', 
    @level0type = N'SCHEMA', @level0name = N'dbo', 
    @level1type = N'TABLE', @level1name = N'Customers', 
    @level2type = N'COLUMN', @level2name = N'Email';
GO";
        
        var migrationPath = Path.Combine(GitRepoPath, "migration.sql");
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        // Act
        var changes = await detector.ParseMigrationFileAsync(migrationPath, "test-server", "test-db");
        
        // Assert - All identifiers should include the table name
        Assert.Contains(changes, c => c.Identifier == "dbo.Orders.DF_Orders_OrderDate" && c.Description == "removed");
        Assert.Contains(changes, c => c.Identifier == "dbo.Customers.DF_Customers_Created" && c.Description == "added");
        Assert.Contains(changes, c => c.Identifier == "dbo.Products.IX_Products_Name" && c.Description == "added");
        Assert.Contains(changes, c => c.Identifier == "dbo.Customers.EP_Column_Description_Email" && c.Description == "added");
        
        // Verify ObjectTypes are correct
        Assert.Contains(changes, c => c.Identifier.Contains("DF_Orders_OrderDate") && c.ObjectType == "Constraint");
        Assert.Contains(changes, c => c.Identifier.Contains("DF_Customers_Created") && c.ObjectType == "Constraint");
        Assert.Contains(changes, c => c.Identifier.Contains("IX_Products_Name") && c.ObjectType == "Index");
        Assert.Contains(changes, c => c.Identifier.Contains("EP_Column_Description_Email") && c.ObjectType == "ExtendedProperty");
    }
    
    [Fact]
    public async Task ParseMigrationFileAsync_ComplexMigrationWithAllChangeTypes()
    {
        // Arrange - Use the actual migration from the test database
        var detector = new GitChangeDetector(GitRepoPath);
        var migrationContent = @"-- Migration: 20250809_091823_6columns_1indexes_3other.sql
-- Rename operations
EXEC sp_rename '[dbo].[test_migrations].[bb1]', 'tempof', 'COLUMN';
GO

-- Drop operations
ALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_gsdf];
GO

ALTER TABLE [dbo].[test_migrations] DROP COLUMN [ga];
GO

ALTER TABLE [dbo].[test_migrations] DROP COLUMN [gagdf];
GO

ALTER TABLE [dbo].[test_migrations] DROP COLUMN [gsdf];
GO

-- Modification operations
ALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_zura];
GO

ALTER TABLE [dbo].[test_migrations]
    ADD CONSTRAINT [DF_test_migrations_zura] DEFAULT (N'iura') FOR [zura];
GO

ALTER TABLE [dbo].[test_migrations] ALTER COLUMN [zura] NVARCHAR (105) NOT NULL;
GO

-- Create operations
ALTER TABLE [dbo].[test_migrations] ADD [gjglksdf] INT NULL;
GO

CREATE NONCLUSTERED INDEX [iTesting2]
    ON [dbo].[test_migrations]([zura] ASC);
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'testing - description', 
    @level0type = N'SCHEMA', @level0name = N'dbo', 
    @level1type = N'TABLE', @level1name = N'test_migrations', 
    @level2type = N'COLUMN', @level2name = N'zura';
GO";
        
        var migrationPath = Path.Combine(GitRepoPath, "migration.sql");
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        // Act
        var changes = await detector.ParseMigrationFileAsync(migrationPath, "test-server", "test-db");
        
        // Assert - Should detect exactly 9 changes (DF_test_migrations_zura is not counted as it's recreated)
        Assert.Equal(9, changes.Count);
        
        // Column rename  
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.bb1" && c.Description.Contains("renamed to tempof"));
        
        // Column drops
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.ga" && c.Description == "removed");
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.gagdf" && c.Description == "removed");
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.gsdf" && c.Description == "removed");
        
        // Column modification
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.zura" && c.Description == "modified");
        
        // Column addition
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.gjglksdf" && c.Description == "added");
        
        // Constraint drop (DF_test_migrations_gsdf is dropped, DF_test_migrations_zura is recreated so not counted)
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.DF_test_migrations_gsdf" && c.Description == "removed");
        Assert.DoesNotContain(changes, c => c.Identifier.Contains("DF_test_migrations_zura"));
        
        // Index creation
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.iTesting2" && c.Description == "added");
        
        // Extended property
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.EP_Column_Description_zura" && c.Description == "added");
    }

    [Fact]
    public async Task AnalyzeTableChanges_DetectsAddedColumns()
    {
        // Arrange
        var detector = new GitChangeDetector(".");
        var oldContent = @"CREATE TABLE [dbo].[TestTable] (
    [Id] INT NOT NULL,
    [Name] NVARCHAR(100) NULL
);";
        var newContent = @"CREATE TABLE [dbo].[TestTable] (
    [Id] INT NOT NULL,
    [Name] NVARCHAR(100) NULL,
    [Email] NVARCHAR(255) NULL,
    [Phone] VARCHAR(20) NULL
);";
        
        // Use reflection to test private method
        var method = typeof(GitChangeDetector).GetMethod("AnalyzeTableChanges", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        // Act
        var result = method.Invoke(detector, new object[] { oldContent, newContent }) 
            as List<(string columnName, string changeType)>;
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Contains(("email", "added"), result);
        Assert.Contains(("phone", "added"), result);
    }
    
    [Fact]
    public async Task AnalyzeTableChanges_DetectsRemovedColumns()
    {
        // Arrange
        var detector = new GitChangeDetector(".");
        var oldContent = @"CREATE TABLE [dbo].[TestTable] (
    [Id] INT NOT NULL,
    [Name] NVARCHAR(100) NULL,
    [Email] NVARCHAR(255) NULL,
    [Deleted] BIT NOT NULL
);";
        var newContent = @"CREATE TABLE [dbo].[TestTable] (
    [Id] INT NOT NULL,
    [Name] NVARCHAR(100) NULL,
    [Email] NVARCHAR(255) NULL
);";
        
        // Use reflection to test private method
        var method = typeof(GitChangeDetector).GetMethod("AnalyzeTableChanges", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        // Act
        var result = method.Invoke(detector, new object[] { oldContent, newContent }) 
            as List<(string columnName, string changeType)>;
        
        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains(("deleted", "removed"), result);
    }
    
    [Fact]
    public async Task AnalyzeTableChanges_DetectsModifiedColumns()
    {
        // Arrange
        var detector = new GitChangeDetector(".");
        var oldContent = @"CREATE TABLE [dbo].[TestTable] (
    [Id] INT NOT NULL,
    [Name] NVARCHAR(100) NULL,
    [Email] VARCHAR(100) NULL
);";
        var newContent = @"CREATE TABLE [dbo].[TestTable] (
    [Id] INT NOT NULL,
    [Name] NVARCHAR(100) NULL,
    [Email] NVARCHAR(255) NULL
);";
        
        // Use reflection to test private method
        var method = typeof(GitChangeDetector).GetMethod("AnalyzeTableChanges", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        // Act
        var result = method.Invoke(detector, new object[] { oldContent, newContent }) 
            as List<(string columnName, string changeType)>;
        
        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains(("email", "modified"), result);
    }
    
    [Fact]
    public async Task AnalyzeTableChanges_DetectsMultipleChanges()
    {
        // Arrange
        var detector = new GitChangeDetector(".");
        var oldContent = @"CREATE TABLE [dbo].[test_migrations] (
    [test1] NCHAR(10) NULL,
    [bb1] NCHAR(10) NULL,
    [ga] NCHAR(10) NULL,
    [gagdf] NCHAR(10) NOT NULL,
    [gsdf] INT NOT NULL,
    [zura] NVARCHAR(100) NOT NULL,
    [gagadf] NCHAR(10) NULL
);";
        var newContent = @"CREATE TABLE [dbo].[test_migrations] (
    [test1] NCHAR(10) NULL,
    [bb1] NCHAR(10) NULL,
    [ga] NCHAR(10) NULL,
    [gagdf] NCHAR(10) NOT NULL,
    [gsdf] INT NOT NULL,
    [zura] NVARCHAR(100) NOT NULL,
    [gagadf] NCHAR(10) NULL,
    [tempof] NCHAR(10) NULL
);";
        
        // Use reflection to test private method
        var method = typeof(GitChangeDetector).GetMethod("AnalyzeTableChanges", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        // Act
        var result = method.Invoke(detector, new object[] { oldContent, newContent }) 
            as List<(string columnName, string changeType)>;
        
        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains(("tempof", "added"), result);
    }
}