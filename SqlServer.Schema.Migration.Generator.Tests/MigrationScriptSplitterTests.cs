using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class MigrationScriptSplitterTests
{
    private readonly ITestOutputHelper _output;

    public MigrationScriptSplitterTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SplitMigrationScript_WithTableRecreation_GroupsAllOperationsTogether()
    {
        // Arrange
        var splitter = new MigrationScriptSplitter();
        var tempDir = Path.Combine(Path.GetTempPath(), $"MigrationSplitterTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var migrationScript = @"
-- Drop foreign key before table modification
ALTER TABLE [dbo].[Orders] DROP CONSTRAINT [FK_Orders_Customer]
GO

-- Recreate Customer table with new structure
CREATE TABLE [dbo].[tmp_ms_xx_Customer] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Email] NVARCHAR(255) NOT NULL
)
GO

-- Migrate data
SET IDENTITY_INSERT [dbo].[tmp_ms_xx_Customer] ON
INSERT INTO [dbo].[tmp_ms_xx_Customer] ([Id], [Name])
SELECT [Id], [Name] FROM [dbo].[Customer]
SET IDENTITY_INSERT [dbo].[tmp_ms_xx_Customer] OFF
GO

-- Replace old table
DROP TABLE [dbo].[Customer]
GO

EXEC sp_rename '[dbo].[tmp_ms_xx_Customer]', 'Customer'
GO

-- Add constraints
ALTER TABLE [dbo].[Customer] ADD CONSTRAINT [PK_Customer] PRIMARY KEY ([Id])
GO

CREATE INDEX [IX_Customer_Email] ON [dbo].[Customer] ([Email])
GO

-- Restore foreign key
ALTER TABLE [dbo].[Orders] ADD CONSTRAINT [FK_Orders_Customer] 
    FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([Id])
GO

-- Update stored procedure
ALTER PROCEDURE [dbo].[sp_GetCustomers]
AS
BEGIN
    SELECT Id, Name, Email FROM Customer
END
GO";

            var scriptPath = Path.Combine(tempDir, "_20250812_123456_test_update.sql");
            await File.WriteAllTextAsync(scriptPath, migrationScript);
            
            var outputDir = Path.Combine(tempDir, "output");
            
            // Act
            await splitter.SplitMigrationScript(scriptPath, outputDir);
            
            // Assert
            Assert.True(Directory.Exists(outputDir), "Output directory should exist");
            
            var changesDir = Path.Combine(outputDir, "changes");
            Assert.True(Directory.Exists(changesDir), "Changes directory should exist");
            
            var segmentFiles = Directory.GetFiles(changesDir, "*.sql");
            _output.WriteLine($"Found {segmentFiles.Length} segment files");
            
            // Should have files for Customer table, Orders table, and sp_GetCustomers procedure
            Assert.True(segmentFiles.Length >= 2, "Should have at least 2 segments");
            
            // Check that Customer table file exists and contains all related operations
            var customerFile = segmentFiles.FirstOrDefault(f => f.Contains("_table_dbo_Customer"));
            Assert.NotNull(customerFile);
            
            var customerContent = await File.ReadAllTextAsync(customerFile!);
            _output.WriteLine($"Customer file content length: {customerContent.Length}");
            
            // Verify all Customer-related operations are in the same file
            Assert.Contains("tmp_ms_xx_Customer", customerContent);
            Assert.Contains("DROP TABLE [dbo].[Customer]", customerContent);
            Assert.Contains("sp_rename", customerContent);
            Assert.Contains("PK_Customer", customerContent);
            Assert.Contains("IX_Customer_Email", customerContent);
            
            // Check manifest file
            var manifestPath = Path.Combine(outputDir, "manifest.json");
            Assert.True(File.Exists(manifestPath), "Manifest file should exist");
            
            var manifestJson = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonDocument.Parse(manifestJson);
            
            Assert.Equal("1.0", manifest.RootElement.GetProperty("version").GetString());
            Assert.True(manifest.RootElement.GetProperty("totalSegments").GetInt32() > 0);
            
            // Check that original script was preserved
            var originalScriptPath = Path.Combine(outputDir, "migration.sql");
            Assert.True(File.Exists(originalScriptPath), "Original migration script should be preserved");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
    
    [Fact]
    public async Task SplitMigrationScript_WithMultipleObjects_CreatesSeparateFiles()
    {
        // Arrange
        var splitter = new MigrationScriptSplitter();
        var tempDir = Path.Combine(Path.GetTempPath(), $"MigrationSplitterTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var migrationScript = @"
CREATE VIEW [dbo].[vw_CustomerOrders]
AS
SELECT c.Name, o.OrderDate
FROM Customer c
JOIN Orders o ON c.Id = o.CustomerId
GO

CREATE FUNCTION [dbo].[fn_CalculateTotal]
(@OrderId INT)
RETURNS DECIMAL(10,2)
AS
BEGIN
    RETURN 100.00
END
GO

ALTER TABLE [dbo].[Products] ADD [Description] NVARCHAR(500)
GO";

            var scriptPath = Path.Combine(tempDir, "_20250812_123456_test_multiple.sql");
            await File.WriteAllTextAsync(scriptPath, migrationScript);
            
            var outputDir = Path.Combine(tempDir, "output");
            
            // Act
            await splitter.SplitMigrationScript(scriptPath, outputDir);
            
            // Assert
            var changesDir = Path.Combine(outputDir, "changes");
            var segmentFiles = Directory.GetFiles(changesDir, "*.sql");
            
            // Should have separate files for view, function, and table
            Assert.Equal(3, segmentFiles.Length);
            
            // Check for specific object type files
            Assert.True(segmentFiles.Any(f => f.Contains("_view_")), "Should have a view file");
            Assert.True(segmentFiles.Any(f => f.Contains("_function_")), "Should have a function file");
            Assert.True(segmentFiles.Any(f => f.Contains("_table_")), "Should have a table file");
            
            // Verify file naming follows the pattern
            foreach (var file in segmentFiles)
            {
                var filename = Path.GetFileName(file);
                Assert.Matches(@"^\d{3}_\w+_\w+_\w+\.sql$", filename);
            }
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
    
    [Fact]
    public async Task ReconstructMigration_RebuildsOriginalScript()
    {
        // Arrange
        var splitter = new MigrationScriptSplitter();
        var tempDir = Path.Combine(Path.GetTempPath(), $"MigrationSplitterTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var migrationScript = @"CREATE TABLE [dbo].[TestTable] (Id INT)
GO

CREATE VIEW [dbo].[TestView] AS SELECT * FROM TestTable
GO";

            var scriptPath = Path.Combine(tempDir, "_20250812_123456_test_reconstruct.sql");
            await File.WriteAllTextAsync(scriptPath, migrationScript);
            
            var outputDir = Path.Combine(tempDir, "output");
            
            // Act - Split
            await splitter.SplitMigrationScript(scriptPath, outputDir);
            
            // Act - Reconstruct
            var reconstructed = await splitter.ReconstructMigration(outputDir);
            
            // Assert
            Assert.NotNull(reconstructed);
            Assert.Contains("CREATE TABLE [dbo].[TestTable]", reconstructed);
            Assert.Contains("CREATE VIEW [dbo].[TestView]", reconstructed);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}