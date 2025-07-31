using Xunit;
using SqlServer.Schema.Migration.Generator.Generation;
using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;
using System.Collections.Generic;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class ReverseMigrationBuilderTests
{
    readonly ReverseMigrationBuilder _builder = new();

    [Fact]
    public void BuildReverseMigration_GeneratesProperHeader()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Table",
                Schema = "dbo",
                ObjectName = "TestTable",
                ChangeType = ChangeType.Added
            }
        };

        // Act
        var result = _builder.BuildReverseMigration(changes, "TestDB", "testuser");

        // Assert
        Assert.Contains("-- REVERSE Migration:", result);
        Assert.Contains("-- Database: TestDB", result);
        Assert.Contains("-- Actor: testuser", result);
        Assert.Contains("-- WARNING: This is a MANUAL ROLLBACK script", result);
        Assert.Contains("-- It is NOT tracked in DatabaseMigrationHistory", result);
        Assert.Contains("SET XACT_ABORT ON;", result);
        Assert.Contains("BEGIN TRANSACTION;", result);
        Assert.Contains("COMMIT TRANSACTION;", result);
    }

    [Fact]
    public void BuildReverseMigration_OrdersOperationsCorrectly()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            // This will be a DROP in reverse
            new SchemaChange
            {
                ObjectType = "Table",
                Schema = "dbo",
                ObjectName = "TestTable",
                ChangeType = ChangeType.Added,
                NewDefinition = "CREATE TABLE [dbo].[TestTable] ([Id] INT);"
            },
            // This will be a CREATE in reverse
            new SchemaChange
            {
                ObjectType = "Table",
                Schema = "dbo",
                ObjectName = "OldTable",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "CREATE TABLE [dbo].[OldTable] ([Id] INT);"
            }
        };

        // Act
        var result = _builder.BuildReverseMigration(changes, "TestDB");

        // Assert
        // Verify that DROPs (reverse of CREATEs) come before CREATEs (reverse of DROPs)
        var dropIndex = result.IndexOf("DROP TABLE [dbo].[TestTable];");
        var createIndex = result.IndexOf("CREATE TABLE [dbo].[OldTable]");
        Assert.True(dropIndex > 0, "DROP statement should exist");
        Assert.True(createIndex > 0, "CREATE statement should exist");
        Assert.True(dropIndex < createIndex, "DROP should come before CREATE in reverse migration");
    }

    [Fact]
    public void BuildReverseMigration_HandlesRenameOperations()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "TestTable",
                ColumnName = "NewName",
                ChangeType = ChangeType.Modified,
                Properties = new Dictionary<string, string>
                {
                    { "IsRename", "true" },
                    { "OldName", "OldName" },
                    { "RenameType", "Column" }
                }
            }
        };

        // Act
        var result = _builder.BuildReverseMigration(changes, "TestDB");

        // Assert
        Assert.Contains("-- Reversing RENAME operations", result);
        Assert.Contains("sp_rename", result);
    }

    [Fact]
    public void BuildReverseMigration_IncludesManualHistoryUpdateNote()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Table",
                Schema = "dbo",
                ObjectName = "TestTable",
                ChangeType = ChangeType.Added
            }
        };

        // Act
        var result = _builder.BuildReverseMigration(changes, "TestDB");

        // Assert
        Assert.Contains("-- If you want to manually track this rollback:", result);
        Assert.Contains("-- DELETE FROM [dbo].[DatabaseMigrationHistory]", result);
        Assert.Contains("-- WHERE [MigrationId] =", result);
    }

    [Fact]
    public void BuildReverseMigration_GroupsOperationsByType()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Table",
                Schema = "dbo",
                ObjectName = "TestTable1",
                ChangeType = ChangeType.Added
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "ExistingTable",
                ColumnName = "NewColumn",
                ChangeType = ChangeType.Added
            },
            new SchemaChange
            {
                ObjectType = "Index",
                Schema = "dbo",
                TableName = "ExistingTable",
                ObjectName = "IX_Test",
                ChangeType = ChangeType.Modified,
                OldDefinition = "CREATE INDEX [IX_Test] ON [dbo].[ExistingTable] ([Col1]);"
            }
        };

        // Act
        var result = _builder.BuildReverseMigration(changes, "TestDB");

        // Assert
        Assert.Contains("-- Reversing CREATE operations (DROP)", result);
        Assert.Contains("-- Reversing MODIFICATION operations", result);
    }

    [Fact]
    public void BuildReverseMigration_GeneratesDescriptionFromChanges()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange { ObjectType = "Table", ChangeType = ChangeType.Added },
            new SchemaChange { ObjectType = "Table", ChangeType = ChangeType.Added },
            new SchemaChange { ObjectType = "Index", ChangeType = ChangeType.Modified },
            new SchemaChange { ObjectType = "Column", ChangeType = ChangeType.Added }
        };

        // Act
        var result = _builder.BuildReverseMigration(changes, "TestDB");

        // Assert
        // The filename should contain counts like "2tables_1indexes_1other"
        Assert.Contains("2tables", result);
        Assert.Contains("1indexes", result);
    }
}