using Xunit;
using SqlServer.Schema.Migration.Generator.Generation;
using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class MigrationScriptBuilderTests
{
    readonly MigrationScriptBuilder _builder = new();
    
    [Fact]
    public void BuildMigration_WithNoChanges_ShouldGenerateEmptyMigration()
    {
        // Arrange
        var changes = new List<SchemaChange>();
        var databaseName = "TestDB";
        
        // Act
        var script = _builder.BuildMigration(changes, databaseName);
        
        // Assert
        Assert.Contains("SET XACT_ABORT ON", script);
        Assert.Contains("BEGIN TRANSACTION", script);
        Assert.Contains($"Database: {databaseName}", script);
        Assert.Contains("Changes: 0 schema modifications", script);
        Assert.Contains("COMMIT TRANSACTION", script);
    }
    
    [Fact]
    public void BuildMigration_WithTableAddition_ShouldGenerateCreateTable()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Table",
                Schema = "dbo",
                ObjectName = "Customer",
                ChangeType = ChangeType.Added,
                NewDefinition = "CREATE TABLE [dbo].[Customer] ([Id] INT)"
            }
        };
        var databaseName = "TestDB";
        
        // Act
        var script = _builder.BuildMigration(changes, databaseName);
        
        // Assert
        Assert.Contains("-- Create operations", script);
        Assert.Contains("CREATE TABLE [dbo].[Customer] ([Id] INT)", script);
        Assert.Contains("GO", script);
    }
    
    [Fact]
    public void BuildMigration_WithMixedOperations_ShouldOrderCorrectly()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            // Create operation
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ColumnName = "Email",
                ChangeType = ChangeType.Added,
                NewDefinition = "[Email] NVARCHAR(100)"
            },
            // Delete operation
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ColumnName = "OldColumn",
                ChangeType = ChangeType.Deleted
            },
            // Modify operation
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ColumnName = "Name",
                ChangeType = ChangeType.Modified,
                OldDefinition = "[Name] NVARCHAR(50)",
                NewDefinition = "[Name] NVARCHAR(100)"
            }
        };
        var databaseName = "TestDB";
        
        // Act
        var script = _builder.BuildMigration(changes, databaseName);
        
        // Assert
        // Verify order: drops first, then modifications, then creates
        var dropIndex = script.IndexOf("-- Drop operations");
        var modifyIndex = script.IndexOf("-- Modification operations");
        var createIndex = script.IndexOf("-- Create operations");
        
        Assert.True(dropIndex < modifyIndex);
        Assert.True(modifyIndex < createIndex);
    }
    
    [Fact]
    public void BuildMigration_WithRenameOperations_ShouldProcessRenamesFirst()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            // Regular drop
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ColumnName = "OldColumn",
                ChangeType = ChangeType.Deleted
            },
            // Rename operation
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ColumnName = "Email",
                ChangeType = ChangeType.Modified,
                Properties = new Dictionary<string, string>
                {
                    ["IsRename"] = "true",
                    ["OldName"] = "EmailAddress",
                    ["RenameType"] = "Column"
                }
            }
        };
        var databaseName = "TestDB";
        
        // Act
        var script = _builder.BuildMigration(changes, databaseName);
        
        // Assert
        // Verify renames come before drops
        var renameIndex = script.IndexOf("-- Rename operations");
        var dropIndex = script.IndexOf("-- Drop operations");
        
        Assert.True(renameIndex > 0);
        Assert.True(renameIndex < dropIndex);
        Assert.Contains("EXEC sp_rename", script);
    }
    
    [Fact]
    public void BuildMigration_ShouldIncludeMigrationHeader()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Table",
                Schema = "dbo",
                ObjectName = "Test",
                ChangeType = ChangeType.Added,
                NewDefinition = "CREATE TABLE [dbo].[Test] ([Id] INT)"
            }
        };
        var databaseName = "TestDB";
        
        // Act
        var script = _builder.BuildMigration(changes, databaseName);
        
        // Assert
        Assert.Contains("-- Migration:", script);
        Assert.Contains("-- MigrationId:", script);
        Assert.Contains("-- Generated:", script);
        Assert.Contains("-- Database: TestDB", script);
        Assert.Contains("-- Changes: 1 schema modifications", script);
    }
    
    [Fact]
    public void BuildMigration_ShouldIncludeMigrationHistoryCheck()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Table",
                Schema = "dbo",
                ObjectName = "Test",
                ChangeType = ChangeType.Added,
                NewDefinition = "CREATE TABLE [dbo].[Test] ([Id] INT)"
            }
        };
        var databaseName = "TestDB";
        
        // Act
        var script = _builder.BuildMigration(changes, databaseName);
        
        // Assert
        Assert.Contains("-- Check if migration already applied", script);
        Assert.Contains("IF EXISTS (SELECT 1 FROM [dbo].[DatabaseMigrationHistory]", script);
        Assert.Contains("PRINT 'Migration already applied. Skipping.'", script);
        Assert.Contains("RETURN;", script);
    }
    
    [Fact]
    public void BuildMigration_ShouldRecordMigrationInHistory()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Table",
                Schema = "dbo",
                ObjectName = "Test",
                ChangeType = ChangeType.Added,
                NewDefinition = "CREATE TABLE [dbo].[Test] ([Id] INT)"
            }
        };
        var databaseName = "TestDB";
        
        // Act
        var script = _builder.BuildMigration(changes, databaseName);
        
        // Assert
        Assert.Contains("-- Record this migration as applied", script);
        Assert.Contains("INSERT INTO [dbo].[DatabaseMigrationHistory]", script);
        Assert.Contains("[MigrationId], [Filename], [Checksum], [Status]", script);
        Assert.Contains("'Success'", script);
    }
    
    [Fact]
    public void GenerateMigrationName_WithVariousChanges_ShouldCreateDescriptiveName()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange { ObjectType = "Table", ChangeType = ChangeType.Added },
            new SchemaChange { ObjectType = "Table", ChangeType = ChangeType.Deleted },
            new SchemaChange { ObjectType = "Column", ChangeType = ChangeType.Added },
            new SchemaChange { ObjectType = "Column", ChangeType = ChangeType.Modified },
            new SchemaChange { ObjectType = "Index", ChangeType = ChangeType.Added },
            new SchemaChange { ObjectType = "View", ChangeType = ChangeType.Added }
        };
        var databaseName = "TestDB";
        
        // Act
        var script = _builder.BuildMigration(changes, databaseName);
        
        // Assert
        // The migration name should include counts
        Assert.Contains("2tables", script); // 2 table changes
        Assert.Contains("2columns", script); // 2 column changes
        Assert.Contains("1indexes", script); // 1 index change
        Assert.Contains("1other", script); // 1 view change
    }
    
    [Fact]
    public void BuildMigration_ShouldWrapInTransaction()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Table",
                Schema = "dbo",
                ObjectName = "Test",
                ChangeType = ChangeType.Added,
                NewDefinition = "CREATE TABLE [dbo].[Test] ([Id] INT)"
            }
        };
        var databaseName = "TestDB";
        
        // Act
        var script = _builder.BuildMigration(changes, databaseName);
        
        // Assert
        Assert.Contains("SET XACT_ABORT ON", script);
        Assert.Contains("BEGIN TRANSACTION", script);
        Assert.Contains("COMMIT TRANSACTION", script);
        Assert.Contains("PRINT 'Migration applied successfully.'", script);
    }
}