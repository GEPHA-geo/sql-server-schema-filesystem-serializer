using Xunit;
using SqlServer.Schema.Migration.Generator;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class SqlProjectBuilderTests : IDisposable
{
    readonly string _testDirectory;
    readonly SqlProjectBuilder _builder;

    public SqlProjectBuilderTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"SqlProjectTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _builder = new SqlProjectBuilder();
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
    public async Task CreateSqlProject_NoSqlFiles_ReturnsEmptyString()
    {
        // Arrange
        var schemaPath = Path.Combine(_testDirectory, "schema");
        var outputDir = Path.Combine(_testDirectory, "output");
        Directory.CreateDirectory(schemaPath);

        // Act
        var projectPath = await _builder.CreateSqlProject(schemaPath, outputDir, "TestProject");

        // Assert
        Assert.Equal(string.Empty, projectPath);
    }

    [Fact]
    public async Task CreateSqlProject_WithSqlFiles_CreatesProject()
    {
        // Arrange
        var schemaPath = Path.Combine(_testDirectory, "schema");
        var outputDir = Path.Combine(_testDirectory, "output");
        Directory.CreateDirectory(schemaPath);
        
        // Create test SQL files
        await File.WriteAllTextAsync(
            Path.Combine(schemaPath, "schema.sql"),
            "CREATE SCHEMA [dbo];");
        await File.WriteAllTextAsync(
            Path.Combine(schemaPath, "table.sql"),
            "CREATE TABLE [dbo].[TestTable] (Id INT);");

        // Act
        var projectPath = await _builder.CreateSqlProject(schemaPath, outputDir, "TestProject");

        // Assert
        Assert.NotEmpty(projectPath);
        Assert.True(File.Exists(projectPath));
        Assert.EndsWith(".sqlproj", projectPath);
    }

    [Fact]
    public async Task CreateSqlProject_PreservesDirectoryStructure()
    {
        // Arrange
        var schemaPath = Path.Combine(_testDirectory, "schema");
        var outputDir = Path.Combine(_testDirectory, "output");
        var tablesDir = Path.Combine(schemaPath, "tables", "dbo");
        Directory.CreateDirectory(tablesDir);
        
        await File.WriteAllTextAsync(
            Path.Combine(tablesDir, "TestTable.sql"),
            "CREATE TABLE [dbo].[TestTable] (Id INT);");

        // Act
        var projectPath = await _builder.CreateSqlProject(schemaPath, outputDir, "TestProject");

        // Assert
        var projectDir = Path.GetDirectoryName(projectPath)!;
        var copiedFile = Path.Combine(projectDir, "tables", "dbo", "TestTable.sql");
        Assert.True(File.Exists(copiedFile));
    }

    [Fact]
    public async Task CreateSqlProject_RemovesExcludedComments()
    {
        // Arrange
        var schemaPath = Path.Combine(_testDirectory, "schema");
        var outputDir = Path.Combine(_testDirectory, "output");
        Directory.CreateDirectory(schemaPath);
        
        var sqlWithExclusion = @"-- EXCLUDED: dbo.TestTable
-- This object is excluded from deployment
-- Remove this comment to include the object

CREATE TABLE [dbo].[TestTable] (Id INT);";
        
        await File.WriteAllTextAsync(
            Path.Combine(schemaPath, "table.sql"),
            sqlWithExclusion);

        // Act
        var projectPath = await _builder.CreateSqlProject(schemaPath, outputDir, "TestProject");

        // Assert
        var projectDir = Path.GetDirectoryName(projectPath)!;
        var copiedFile = Path.Combine(projectDir, "table.sql");
        var content = await File.ReadAllTextAsync(copiedFile);
        
        Assert.DoesNotContain("-- EXCLUDED:", content);
        Assert.Contains("CREATE TABLE", content);
    }

    [Fact]
    public async Task CreateSqlProject_RemovesUseStatements()
    {
        // Arrange
        var schemaPath = Path.Combine(_testDirectory, "schema");
        var outputDir = Path.Combine(_testDirectory, "output");
        Directory.CreateDirectory(schemaPath);
        
        var sqlWithUse = @"USE [MyDatabase];
GO
CREATE TABLE [dbo].[TestTable] (Id INT);";
        
        await File.WriteAllTextAsync(
            Path.Combine(schemaPath, "table.sql"),
            sqlWithUse);

        // Act
        var projectPath = await _builder.CreateSqlProject(schemaPath, outputDir, "TestProject");

        // Assert
        var projectDir = Path.GetDirectoryName(projectPath)!;
        var copiedFile = Path.Combine(projectDir, "table.sql");
        var content = await File.ReadAllTextAsync(copiedFile);
        
        Assert.DoesNotContain("USE [MyDatabase]", content);
        Assert.Contains("CREATE TABLE", content);
    }

    [Fact]
    public async Task CreateSqlProject_OrdersFilesByDependency()
    {
        // Arrange
        var schemaPath = Path.Combine(_testDirectory, "schema");
        var outputDir = Path.Combine(_testDirectory, "output");
        
        // Create files that should be ordered
        var dirs = new[]
        {
            Path.Combine(schemaPath, "schemas"),
            Path.Combine(schemaPath, "tables"),
            Path.Combine(schemaPath, "views"),
            Path.Combine(schemaPath, "procedures")
        };
        
        foreach (var dir in dirs)
            Directory.CreateDirectory(dir);
        
        await File.WriteAllTextAsync(Path.Combine(schemaPath, "procedures", "sp_test.sql"), "CREATE PROCEDURE sp_test AS SELECT 1;");
        await File.WriteAllTextAsync(Path.Combine(schemaPath, "views", "vw_test.sql"), "CREATE VIEW vw_test AS SELECT 1;");
        await File.WriteAllTextAsync(Path.Combine(schemaPath, "tables", "tbl_test.sql"), "CREATE TABLE tbl_test (Id INT);");
        await File.WriteAllTextAsync(Path.Combine(schemaPath, "schemas", "schema.sql"), "CREATE SCHEMA test;");

        // Act
        var projectPath = await _builder.CreateSqlProject(schemaPath, outputDir, "TestProject");

        // Assert
        Assert.NotEmpty(projectPath);
        
        // Read project file and verify order
        var projectContent = await File.ReadAllTextAsync(projectPath);
        var schemaIndex = projectContent.IndexOf("schema.sql");
        var tableIndex = projectContent.IndexOf("tbl_test.sql");
        var viewIndex = projectContent.IndexOf("vw_test.sql");
        var procIndex = projectContent.IndexOf("sp_test.sql");
        
        // Schemas should come before tables, tables before views, views before procedures
        Assert.True(schemaIndex < tableIndex, "Schema should be before table");
        Assert.True(tableIndex < viewIndex, "Table should be before view");
        Assert.True(viewIndex < procIndex, "View should be before procedure");
    }

    [Fact]
    public async Task CreateSqlProject_GeneratesValidXml()
    {
        // Arrange
        var schemaPath = Path.Combine(_testDirectory, "schema");
        var outputDir = Path.Combine(_testDirectory, "output");
        Directory.CreateDirectory(schemaPath);
        
        await File.WriteAllTextAsync(
            Path.Combine(schemaPath, "test.sql"),
            "SELECT 1;");

        // Act
        var projectPath = await _builder.CreateSqlProject(schemaPath, outputDir, "TestProject");

        // Assert
        var projectContent = await File.ReadAllTextAsync(projectPath);
        
        // Verify XML structure
        Assert.Contains("<?xml version=\"1.0\" encoding=\"utf-8\"?>", projectContent);
        Assert.Contains("<Project DefaultTargets=\"Build\"", projectContent);
        Assert.Contains("<Name>TestProject</Name>", projectContent);
        Assert.Contains("<Build Include=", projectContent);
        Assert.Contains("</Project>", projectContent);
    }
}