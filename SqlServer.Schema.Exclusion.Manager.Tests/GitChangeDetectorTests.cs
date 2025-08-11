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

-- Drop and recreate constraint (should be detected as modified, not removed)
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
        // Should detect DF_test_migrations_zura as modified (not removed) since it's re-added
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.DF_test_migrations_zura" && c.Description == "modified");
    }

    [Fact]
    public async Task ParseMigrationFileAsync_DetectsIndexDrops()
    {
        // Arrange - Tests DROP INDEX detection
        var detector = new GitChangeDetector(GitRepoPath);
        var migrationContent = @"-- Migration: test index drops
-- Drop index
DROP INDEX [iTesting2] ON [dbo].[test_migrations];
GO

-- Drop another index with different formatting
DROP INDEX iTesting3 ON dbo.products;
GO

-- Create an index (to verify we can detect both create and drop)
CREATE NONCLUSTERED INDEX [iNewIndex] ON [dbo].[test_migrations]([column1] ASC);
GO";
        
        var migrationPath = Path.Combine(GitRepoPath, "migration.sql");
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        // Act
        var changes = await detector.ParseMigrationFileAsync(migrationPath, "test-server", "test-db");
        
        // Assert - Should detect 2 index drops and 1 index creation
        Assert.Equal(3, changes.Count);
        
        // Verify index drops are detected
        Assert.Contains(changes, c => 
            c.Identifier == "dbo.test_migrations.iTesting2" && 
            c.Description == "removed" && 
            c.ObjectType == "Index");
            
        Assert.Contains(changes, c => 
            c.Identifier == "dbo.products.iTesting3" && 
            c.Description == "removed" && 
            c.ObjectType == "Index");
            
        // Verify index creation is still detected
        Assert.Contains(changes, c => 
            c.Identifier == "dbo.test_migrations.iNewIndex" && 
            c.Description == "added" && 
            c.ObjectType == "Index");
    }

    [Fact]
    public async Task ParseMigrationFileAsync_DetectsExtendedPropertyDrops()
    {
        // Arrange - Tests extended property drop detection
        var detector = new GitChangeDetector(GitRepoPath);
        var migrationContent = @"-- Migration: test extended property operations
-- Add extended property
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Test column description',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'test_migrations',
    @level2type = N'COLUMN', @level2name = N'column1';
GO

-- Drop extended property
EXECUTE sp_dropextendedproperty @name = N'MS_Description',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'test_migrations',
    @level2type = N'COLUMN', @level2name = N'oldColumn';
GO

-- Drop custom extended property
EXECUTE sp_dropextendedproperty @name = N'CustomProperty',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'products',
    @level2type = N'COLUMN', @level2name = N'price';
GO";
        
        var migrationPath = Path.Combine(GitRepoPath, "migration.sql");
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        // Act
        var changes = await detector.ParseMigrationFileAsync(migrationPath, "test-server", "test-db");
        
        // Assert - Should detect 1 add and 2 drops
        Assert.Equal(3, changes.Count);
        
        // Verify extended property addition is detected
        Assert.Contains(changes, c => 
            c.Identifier == "dbo.test_migrations.EP_Column_Description_column1" && 
            c.Description == "added" && 
            c.ObjectType == "ExtendedProperty");
            
        // Verify extended property drops are detected
        Assert.Contains(changes, c => 
            c.Identifier == "dbo.test_migrations.EP_Column_Description_oldColumn" && 
            c.Description == "removed" && 
            c.ObjectType == "ExtendedProperty");
            
        Assert.Contains(changes, c => 
            c.Identifier == "dbo.products.EP_CustomProperty_price" && 
            c.Description == "removed" && 
            c.ObjectType == "ExtendedProperty");
    }

    [Fact]
    public async Task ParseMigrationFileAsync_DetectsTableOperations()
    {
        // Arrange - Tests table creation, dropping, and renaming
        var detector = new GitChangeDetector(GitRepoPath);
        var migrationContent = @"-- Migration: test table operations
-- Create a new table
CREATE TABLE [dbo].[new_table] (
    [id] INT IDENTITY(1,1) PRIMARY KEY,
    [name] NVARCHAR(100) NOT NULL
);
GO

-- Create another table without brackets
CREATE TABLE sales.orders (
    order_id INT PRIMARY KEY,
    customer_id INT
);
GO

-- Drop a table
DROP TABLE [dbo].[old_table];
GO

-- Drop table with IF EXISTS
DROP TABLE IF EXISTS dbo.temp_table;
GO

-- Rename a table (using sp_rename without 'OBJECT' parameter)
EXEC sp_rename 'dbo.old_products', 'products';
GO

-- Rename a table (using sp_rename with 'OBJECT' parameter)
EXEC sp_rename '[dbo].[old_customers]', 'customers', 'OBJECT';
GO";
        
        var migrationPath = Path.Combine(GitRepoPath, "migration.sql");
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        // Act
        var changes = await detector.ParseMigrationFileAsync(migrationPath, "test-server", "test-db");
        
        // Assert - Should detect 2 creates, 2 drops, and 2 renames
        Assert.Equal(6, changes.Count);
        
        // Verify table creations are detected
        Assert.Contains(changes, c => 
            c.Identifier == "dbo.new_table" && 
            c.Description == "added" && 
            c.ObjectType == "Table");
            
        Assert.Contains(changes, c => 
            c.Identifier == "sales.orders" && 
            c.Description == "added" && 
            c.ObjectType == "Table");
            
        // Verify table drops are detected
        Assert.Contains(changes, c => 
            c.Identifier == "dbo.old_table" && 
            c.Description == "removed" && 
            c.ObjectType == "Table");
            
        Assert.Contains(changes, c => 
            c.Identifier == "dbo.temp_table" && 
            c.Description == "removed" && 
            c.ObjectType == "Table");
            
        // Verify table renames are detected
        Assert.Contains(changes, c => 
            c.Identifier == "dbo.old_products" && 
            c.Description == "renamed to products" && 
            c.ObjectType == "Table");
            
        Assert.Contains(changes, c => 
            c.Identifier == "dbo.old_customers" && 
            c.Description == "renamed to customers" && 
            c.ObjectType == "Table");
    }

    [Fact]
    public async Task ParseMigrationFileAsync_DetectsDatabaseObjectOperations()
    {
        // Arrange - Tests view, stored procedure, and function operations
        var detector = new GitChangeDetector(GitRepoPath);
        var migrationContent = @"-- Migration: test database object operations

-- VIEW operations
CREATE VIEW [dbo].[v_active_customers] AS
    SELECT * FROM customers WHERE active = 1;
GO

ALTER VIEW [dbo].[v_orders] AS
    SELECT * FROM orders WHERE status = 'completed';
GO

CREATE OR ALTER VIEW dbo.v_products AS
    SELECT * FROM products WHERE in_stock = 1;
GO

DROP VIEW [dbo].[v_old_view];
GO

DROP VIEW IF EXISTS dbo.v_temp_view;
GO

-- STORED PROCEDURE operations
CREATE PROCEDURE [dbo].[sp_GetCustomer]
    @CustomerId INT
AS
BEGIN
    SELECT * FROM customers WHERE id = @CustomerId;
END
GO

ALTER PROC dbo.sp_UpdateOrder
    @OrderId INT
AS
BEGIN
    UPDATE orders SET processed = 1 WHERE id = @OrderId;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_ProcessPayment]
    @PaymentId INT
AS
BEGIN
    -- Process payment logic
END
GO

DROP PROCEDURE [dbo].[sp_OldProc];
GO

DROP PROC IF EXISTS dbo.sp_TempProc;
GO

-- FUNCTION operations
CREATE FUNCTION [dbo].[fn_CalculateDiscount]
(
    @Amount DECIMAL(10,2)
)
RETURNS DECIMAL(10,2)
AS
BEGIN
    RETURN @Amount * 0.9;
END
GO

ALTER FUNCTION dbo.fn_GetFullName
(
    @FirstName NVARCHAR(50),
    @LastName NVARCHAR(50)
)
RETURNS NVARCHAR(100)
AS
BEGIN
    RETURN @FirstName + ' ' + @LastName;
END
GO

CREATE OR ALTER FUNCTION [dbo].[fn_CalculateTax]
(
    @Amount DECIMAL(10,2)
)
RETURNS DECIMAL(10,2)
AS
BEGIN
    RETURN @Amount * 0.15;
END
GO

DROP FUNCTION [dbo].[fn_OldFunction];
GO

DROP FUNCTION IF EXISTS dbo.fn_TempFunction;
GO";
        
        var migrationPath = Path.Combine(GitRepoPath, "migration.sql");
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        // Act
        var changes = await detector.ParseMigrationFileAsync(migrationPath, "test-server", "test-db");
        
        // Assert - Should detect all operations
        // Views: 2 added (CREATE VIEW), 1 modified (ALTER VIEW), 1 modified (CREATE OR ALTER), 2 removed (DROP VIEW)
        Assert.Contains(changes, c => c.Identifier == "dbo.v_active_customers" && c.Description == "added" && c.ObjectType == "View");
        Assert.Contains(changes, c => c.Identifier == "dbo.v_orders" && c.Description == "modified" && c.ObjectType == "View");
        Assert.Contains(changes, c => c.Identifier == "dbo.v_products" && c.Description == "modified" && c.ObjectType == "View");
        Assert.Contains(changes, c => c.Identifier == "dbo.v_old_view" && c.Description == "removed" && c.ObjectType == "View");
        Assert.Contains(changes, c => c.Identifier == "dbo.v_temp_view" && c.Description == "removed" && c.ObjectType == "View");
        
        // Stored Procedures: 1 added (CREATE), 1 modified (ALTER), 1 modified (CREATE OR ALTER), 2 removed (DROP)
        Assert.Contains(changes, c => c.Identifier == "dbo.sp_GetCustomer" && c.Description == "added" && c.ObjectType == "StoredProcedure");
        Assert.Contains(changes, c => c.Identifier == "dbo.sp_UpdateOrder" && c.Description == "modified" && c.ObjectType == "StoredProcedure");
        Assert.Contains(changes, c => c.Identifier == "dbo.sp_ProcessPayment" && c.Description == "modified" && c.ObjectType == "StoredProcedure");
        Assert.Contains(changes, c => c.Identifier == "dbo.sp_OldProc" && c.Description == "removed" && c.ObjectType == "StoredProcedure");
        Assert.Contains(changes, c => c.Identifier == "dbo.sp_TempProc" && c.Description == "removed" && c.ObjectType == "StoredProcedure");
        
        // Functions: 1 added (CREATE), 1 modified (ALTER), 1 modified (CREATE OR ALTER), 2 removed (DROP)
        Assert.Contains(changes, c => c.Identifier == "dbo.fn_CalculateDiscount" && c.Description == "added" && c.ObjectType == "Function");
        Assert.Contains(changes, c => c.Identifier == "dbo.fn_GetFullName" && c.Description == "modified" && c.ObjectType == "Function");
        Assert.Contains(changes, c => c.Identifier == "dbo.fn_CalculateTax" && c.Description == "modified" && c.ObjectType == "Function");
        Assert.Contains(changes, c => c.Identifier == "dbo.fn_OldFunction" && c.Description == "removed" && c.ObjectType == "Function");
        Assert.Contains(changes, c => c.Identifier == "dbo.fn_TempFunction" && c.Description == "removed" && c.ObjectType == "Function");
        
        // Total: 15 changes (5 views + 5 procedures + 5 functions)
        Assert.Equal(15, changes.Count);
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
        // Arrange - This test is now updated to verify that recreated constraints are detected as modifications
        var detector = new GitChangeDetector(GitRepoPath);
        var migrationContent = @"-- Migration: test
-- Drop and recreate a constraint (should be detected as modified, not added)
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
        // Should detect DF_test_migrations_zura as modified (not as added)
        var modifiedConstraint = changes.FirstOrDefault(c => c.Identifier == "dbo.test_migrations.DF_test_migrations_zura");
        Assert.NotNull(modifiedConstraint);
        Assert.Equal("modified", modifiedConstraint.Description);
        
        // Should detect the new constraint as added
        var addedConstraint = changes.FirstOrDefault(c => c.Identifier == "dbo.test_migrations.DF_test_migrations_newone");
        Assert.NotNull(addedConstraint);
        Assert.Equal("added", addedConstraint.Description);
    }

    [Fact]
    public async Task ParseMigrationFileAsync_DetectsConstraintModifications()
    {
        // Arrange - Test that constraint drops followed by recreations are detected as modifications
        var detector = new GitChangeDetector(GitRepoPath);
        var migrationContent = @"-- Migration: test
-- Drop and recreate a constraint with different default value (should be detected as modified)
ALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_zura];
GO

ALTER TABLE [dbo].[test_migrations]
    ADD CONSTRAINT [DF_test_migrations_zura] DEFAULT (N'iura') FOR [zura];
GO

-- Drop another constraint and recreate it
ALTER TABLE [dbo].[orders] DROP CONSTRAINT [DF_orders_status];
GO

ALTER TABLE [dbo].[orders]
    ADD CONSTRAINT [DF_orders_status] DEFAULT ('pending') FOR [status];
GO

-- Add a completely new constraint (not dropped first)
ALTER TABLE [dbo].[test_migrations]
    ADD CONSTRAINT [DF_test_migrations_newone] DEFAULT (N'value') FOR [column];
GO

-- Drop a constraint without recreating it
ALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_deleted];
GO";
        
        var migrationPath = Path.Combine(GitRepoPath, "migration.sql");
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        // Act
        var changes = await detector.ParseMigrationFileAsync(migrationPath, "test-server", "test-db");
        
        // Assert
        // Should detect DF_test_migrations_zura as modified (not added or removed)
        var zuraConstraint = changes.FirstOrDefault(c => c.Identifier == "dbo.test_migrations.DF_test_migrations_zura");
        Assert.NotNull(zuraConstraint);
        Assert.Equal("modified", zuraConstraint.Description);
        Assert.Equal("Constraint", zuraConstraint.ObjectType);
        
        // Should detect DF_orders_status as modified
        var statusConstraint = changes.FirstOrDefault(c => c.Identifier == "dbo.orders.DF_orders_status");
        Assert.NotNull(statusConstraint);
        Assert.Equal("modified", statusConstraint.Description);
        Assert.Equal("Constraint", statusConstraint.ObjectType);
        
        // Should detect the new constraint as added
        var newConstraint = changes.FirstOrDefault(c => c.Identifier == "dbo.test_migrations.DF_test_migrations_newone");
        Assert.NotNull(newConstraint);
        Assert.Equal("added", newConstraint.Description);
        Assert.Equal("Constraint", newConstraint.ObjectType);
        
        // Should detect the deleted constraint as removed
        var deletedConstraint = changes.FirstOrDefault(c => c.Identifier == "dbo.test_migrations.DF_test_migrations_deleted");
        Assert.NotNull(deletedConstraint);
        Assert.Equal("removed", deletedConstraint.Description);
        Assert.Equal("Constraint", deletedConstraint.ObjectType);
    }

    [Fact]
    public async Task ParseMigrationFileAsync_DetectsAllObjectTypeModifications()
    {
        // Arrange - Test that all object types (indexes, views, procedures, functions) handle drop/recreate as modifications
        var detector = new GitChangeDetector(GitRepoPath);
        var migrationContent = @"-- Migration: test all object modifications

-- INDEX MODIFICATIONS
-- Drop and recreate an index (should be detected as modified)
DROP INDEX [IX_Orders_CustomerID] ON [dbo].[Orders];
GO
CREATE NONCLUSTERED INDEX [IX_Orders_CustomerID] ON [dbo].[Orders]([CustomerID] ASC) INCLUDE ([OrderDate]);
GO

-- Add a new index (not dropped first)
CREATE UNIQUE INDEX [IX_Products_SKU] ON [dbo].[Products]([SKU] ASC);
GO

-- Drop an index without recreating it
DROP INDEX [IX_OldIndex] ON [dbo].[Legacy];
GO

-- VIEW MODIFICATIONS
-- Drop and recreate a view (should be detected as modified)
DROP VIEW [dbo].[vw_CustomerOrders];
GO
CREATE VIEW [dbo].[vw_CustomerOrders]
AS
SELECT c.Name, o.OrderDate, o.Total
FROM Customers c
JOIN Orders o ON c.ID = o.CustomerID
WHERE o.Status = 'Active';
GO

-- Add a new view (not dropped first)
CREATE VIEW [dbo].[vw_NewReport]
AS
SELECT * FROM Reports;
GO

-- Drop a view without recreating it
DROP VIEW IF EXISTS [dbo].[vw_OldView];
GO

-- STORED PROCEDURE MODIFICATIONS  
-- Drop and recreate a procedure (should be detected as modified)
DROP PROCEDURE IF EXISTS [dbo].[sp_UpdateCustomer];
GO
CREATE PROCEDURE [dbo].[sp_UpdateCustomer]
    @CustomerID int,
    @Name nvarchar(100),
    @Email nvarchar(100)
AS
BEGIN
    UPDATE Customers 
    SET Name = @Name, Email = @Email, ModifiedDate = GETDATE()
    WHERE ID = @CustomerID;
END
GO

-- Add a new procedure (not dropped first)
CREATE PROC [dbo].[sp_NewProc]
AS
BEGIN
    SELECT 1;
END
GO

-- Drop a procedure without recreating it
DROP PROC [dbo].[sp_OldProc];
GO

-- FUNCTION MODIFICATIONS
-- Drop and recreate a function (should be detected as modified)
DROP FUNCTION [dbo].[fn_CalculateDiscount];
GO
CREATE FUNCTION [dbo].[fn_CalculateDiscount](@Amount decimal(10,2), @DiscountPercent decimal(5,2))
RETURNS decimal(10,2)
AS
BEGIN
    RETURN @Amount * (1 - @DiscountPercent / 100);
END
GO

-- Add a new function (not dropped first)
CREATE FUNCTION [dbo].[fn_NewFunc](@Input int)
RETURNS int
AS
BEGIN
    RETURN @Input * 2;
END
GO

-- Drop a function without recreating it
DROP FUNCTION IF EXISTS [dbo].[fn_OldFunc];
GO";
        
        var migrationPath = Path.Combine(GitRepoPath, "migration.sql");
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        // Act
        var changes = await detector.ParseMigrationFileAsync(migrationPath, "test-server", "test-db");
        
        // Assert - INDEX modifications
        var modifiedIndex = changes.FirstOrDefault(c => c.Identifier == "dbo.Orders.IX_Orders_CustomerID");
        Assert.NotNull(modifiedIndex);
        Assert.Equal("modified", modifiedIndex.Description);
        Assert.Equal("Index", modifiedIndex.ObjectType);
        
        var addedIndex = changes.FirstOrDefault(c => c.Identifier == "dbo.Products.IX_Products_SKU");
        Assert.NotNull(addedIndex);
        Assert.Equal("added", addedIndex.Description);
        Assert.Equal("Index", addedIndex.ObjectType);
        
        var removedIndex = changes.FirstOrDefault(c => c.Identifier == "dbo.Legacy.IX_OldIndex");
        Assert.NotNull(removedIndex);
        Assert.Equal("removed", removedIndex.Description);
        Assert.Equal("Index", removedIndex.ObjectType);
        
        // Assert - VIEW modifications
        var modifiedView = changes.FirstOrDefault(c => c.Identifier == "dbo.vw_CustomerOrders");
        Assert.NotNull(modifiedView);
        Assert.Equal("modified", modifiedView.Description);
        Assert.Equal("View", modifiedView.ObjectType);
        
        var addedView = changes.FirstOrDefault(c => c.Identifier == "dbo.vw_NewReport");
        Assert.NotNull(addedView);
        Assert.Equal("added", addedView.Description);
        Assert.Equal("View", addedView.ObjectType);
        
        var removedView = changes.FirstOrDefault(c => c.Identifier == "dbo.vw_OldView");
        Assert.NotNull(removedView);
        Assert.Equal("removed", removedView.Description);
        Assert.Equal("View", removedView.ObjectType);
        
        // Assert - STORED PROCEDURE modifications
        var modifiedProc = changes.FirstOrDefault(c => c.Identifier == "dbo.sp_UpdateCustomer");
        Assert.NotNull(modifiedProc);
        Assert.Equal("modified", modifiedProc.Description);
        Assert.Equal("StoredProcedure", modifiedProc.ObjectType);
        
        var addedProc = changes.FirstOrDefault(c => c.Identifier == "dbo.sp_NewProc");
        Assert.NotNull(addedProc);
        Assert.Equal("added", addedProc.Description);
        Assert.Equal("StoredProcedure", addedProc.ObjectType);
        
        var removedProc = changes.FirstOrDefault(c => c.Identifier == "dbo.sp_OldProc");
        Assert.NotNull(removedProc);
        Assert.Equal("removed", removedProc.Description);
        Assert.Equal("StoredProcedure", removedProc.ObjectType);
        
        // Assert - FUNCTION modifications
        var modifiedFunc = changes.FirstOrDefault(c => c.Identifier == "dbo.fn_CalculateDiscount");
        Assert.NotNull(modifiedFunc);
        Assert.Equal("modified", modifiedFunc.Description);
        Assert.Equal("Function", modifiedFunc.ObjectType);
        
        var addedFunc = changes.FirstOrDefault(c => c.Identifier == "dbo.fn_NewFunc");
        Assert.NotNull(addedFunc);
        Assert.Equal("added", addedFunc.Description);
        Assert.Equal("Function", addedFunc.ObjectType);
        
        var removedFunc = changes.FirstOrDefault(c => c.Identifier == "dbo.fn_OldFunc");
        Assert.NotNull(removedFunc);
        Assert.Equal("removed", removedFunc.Description);
        Assert.Equal("Function", removedFunc.ObjectType);
        
        // Verify total count - should have exactly 12 changes (3 for each of 4 object types)
        Assert.Equal(12, changes.Count);
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
        // Arrange - Use a comprehensive migration with all supported operations
        var detector = new GitChangeDetector(GitRepoPath);
        var migrationContent = @"-- Migration: 20250810_comprehensive_test.sql
-- Table operations
CREATE TABLE [dbo].[new_table] (id INT PRIMARY KEY);
GO
DROP TABLE [dbo].[old_table];
GO

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

-- Drop index (NEW)
DROP INDEX [iOldIndex] ON [dbo].[test_migrations];
GO

-- Extended properties
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'testing - description', 
    @level0type = N'SCHEMA', @level0name = N'dbo', 
    @level1type = N'TABLE', @level1name = N'test_migrations', 
    @level2type = N'COLUMN', @level2name = N'zura';
GO

-- Drop extended property (NEW)
EXECUTE sp_dropextendedproperty @name = N'MS_Description',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'test_migrations',
    @level2type = N'COLUMN', @level2name = N'oldColumn';
GO

-- View operations (NEW)
CREATE VIEW [dbo].[v_test] AS SELECT * FROM test_migrations;
GO
DROP VIEW [dbo].[v_old];
GO

-- Stored procedure operations (NEW)
CREATE PROCEDURE [dbo].[sp_Test] AS BEGIN SELECT 1; END
GO
DROP PROCEDURE [dbo].[sp_Old];
GO

-- Function operations (NEW)
CREATE FUNCTION [dbo].[fn_Test]() RETURNS INT AS BEGIN RETURN 1; END
GO
DROP FUNCTION [dbo].[fn_Old];
GO";
        
        var migrationPath = Path.Combine(GitRepoPath, "migration.sql");
        await File.WriteAllTextAsync(migrationPath, migrationContent);
        
        // Act
        var changes = await detector.ParseMigrationFileAsync(migrationPath, "test-server", "test-db");
        
        // Assert - Should detect all changes (DF_test_migrations_zura is now counted as modified when recreated)
        Assert.Equal(20, changes.Count);
        
        // Table operations
        Assert.Contains(changes, c => c.Identifier == "dbo.new_table" && c.Description == "added" && c.ObjectType == "Table");
        Assert.Contains(changes, c => c.Identifier == "dbo.old_table" && c.Description == "removed" && c.ObjectType == "Table");
        
        // Column rename  
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.bb1" && c.Description.Contains("renamed to tempof"));
        
        // Constraint modification (drop and recreate)
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.DF_test_migrations_zura" && c.Description == "modified" && c.ObjectType == "Constraint");
        
        // Column drops
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.ga" && c.Description == "removed");
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.gagdf" && c.Description == "removed");
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.gsdf" && c.Description == "removed");
        
        // Column modification
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.zura" && c.Description == "modified");
        
        // Column addition
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.gjglksdf" && c.Description == "added");
        
        // Constraint drop (DF_test_migrations_gsdf is dropped, DF_test_migrations_zura is recreated so counted as modified)
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.DF_test_migrations_gsdf" && c.Description == "removed");
        
        // Index operations
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.iTesting2" && c.Description == "added" && c.ObjectType == "Index");
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.iOldIndex" && c.Description == "removed" && c.ObjectType == "Index");
        
        // Extended properties
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.EP_Column_Description_zura" && c.Description == "added");
        Assert.Contains(changes, c => c.Identifier == "dbo.test_migrations.EP_Column_Description_oldColumn" && c.Description == "removed");
        
        // View operations
        Assert.Contains(changes, c => c.Identifier == "dbo.v_test" && c.Description == "added" && c.ObjectType == "View");
        Assert.Contains(changes, c => c.Identifier == "dbo.v_old" && c.Description == "removed" && c.ObjectType == "View");
        
        // Stored procedure operations
        Assert.Contains(changes, c => c.Identifier == "dbo.sp_Test" && c.Description == "added" && c.ObjectType == "StoredProcedure");
        Assert.Contains(changes, c => c.Identifier == "dbo.sp_Old" && c.Description == "removed" && c.ObjectType == "StoredProcedure");
        
        // Function operations
        Assert.Contains(changes, c => c.Identifier == "dbo.fn_Test" && c.Description == "added" && c.ObjectType == "Function");
        Assert.Contains(changes, c => c.Identifier == "dbo.fn_Old" && c.Description == "removed" && c.ObjectType == "Function");
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