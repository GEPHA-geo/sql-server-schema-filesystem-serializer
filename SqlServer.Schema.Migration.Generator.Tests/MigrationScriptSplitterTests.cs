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

            // Files are now placed directly in outputDir, not in a changes subdirectory
            var segmentFiles = Directory.GetFiles(outputDir, "*.sql");
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

            // Original script is no longer preserved in the new implementation
            // The split files are created directly without keeping the original
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
CREATE TABLE [dbo].[Products] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL
)
GO

CREATE VIEW [dbo].[vw_ProductList]
AS
SELECT Id, Name FROM Products
GO

CREATE PROCEDURE [dbo].[sp_GetProducts]
AS
BEGIN
    SELECT * FROM Products
END
GO

CREATE FUNCTION [dbo].[fn_ProductCount]()
RETURNS INT
AS
BEGIN
    RETURN (SELECT COUNT(*) FROM Products)
END
GO";

            var scriptPath = Path.Combine(tempDir, "_20250812_123456_test_multi.sql");
            await File.WriteAllTextAsync(scriptPath, migrationScript);

            var outputDir = Path.Combine(tempDir, "output");

            // Act
            await splitter.SplitMigrationScript(scriptPath, outputDir);

            // Assert
            Assert.True(Directory.Exists(outputDir), "Output directory should exist");

            // Files are now placed directly in outputDir, not in a changes subdirectory
            var segmentFiles = Directory.GetFiles(outputDir, "*.sql");
            _output.WriteLine($"Found {segmentFiles.Length} segment files");

            // Should have separate files for each object
            Assert.Equal(4, segmentFiles.Length);

            // Check for each object type
            Assert.Contains(segmentFiles, f => f.Contains("_table_dbo_Products"));
            Assert.Contains(segmentFiles, f => f.Contains("_view_dbo_vw_ProductList"));
            Assert.Contains(segmentFiles, f => f.Contains("_procedure_dbo_sp_GetProducts"));
            Assert.Contains(segmentFiles, f => f.Contains("_function_dbo_fn_ProductCount"));

            // Check manifest
            var manifestPath = Path.Combine(outputDir, "manifest.json");
            Assert.True(File.Exists(manifestPath), "Manifest file should exist");

            var manifestJson = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonDocument.Parse(manifestJson);

            var executionOrder = manifest.RootElement.GetProperty("executionOrder");
            Assert.Equal(4, executionOrder.GetArrayLength());

            // Verify execution order (tables should come before views/procedures that depend on them)
            var firstEntry = executionOrder[0];
            Assert.Equal("table", firstEntry.GetProperty("objectType").GetString());
            Assert.Equal("Products", firstEntry.GetProperty("objectName").GetString());
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