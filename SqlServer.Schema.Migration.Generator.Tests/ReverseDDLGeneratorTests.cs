using Xunit;
using SqlServer.Schema.Migration.Generator.Generation;
using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class ReverseDDLGeneratorTests
{
    readonly ReverseDDLGenerator _generator = new();

    [Fact]
    public void GenerateReverseDDL_TableAdded_GeneratesDropTable()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Table",
            Schema = "dbo",
            ObjectName = "TestTable",
            ChangeType = ChangeType.Added,
            NewDefinition = "CREATE TABLE [dbo].[TestTable] ([Id] INT IDENTITY(1,1) PRIMARY KEY);"
        };

        // Act
        var result = _generator.GenerateReverseDDL(change);

        // Assert
        Assert.Equal("DROP TABLE [dbo].[TestTable];", result);
    }

    [Fact]
    public void GenerateReverseDDL_TableDeleted_GeneratesCreateTable()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Table",
            Schema = "dbo",
            ObjectName = "TestTable",
            ChangeType = ChangeType.Deleted,
            OldDefinition = "CREATE TABLE [dbo].[TestTable] ([Id] INT IDENTITY(1,1) PRIMARY KEY);"
        };

        // Act
        var result = _generator.GenerateReverseDDL(change);

        // Assert
        Assert.Equal("CREATE TABLE [dbo].[TestTable] ([Id] INT IDENTITY(1,1) PRIMARY KEY);", result);
    }

    [Fact]
    public void GenerateReverseDDL_ColumnAdded_GeneratesDropColumn()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "TestTable",
            ColumnName = "TestColumn",
            ChangeType = ChangeType.Added,
            NewDefinition = "[TestColumn] VARCHAR(50) NULL"
        };

        // Act
        var result = _generator.GenerateReverseDDL(change);

        // Assert
        Assert.Equal("ALTER TABLE [dbo].[TestTable] DROP COLUMN [TestColumn];", result);
    }

    [Fact]
    public void GenerateReverseDDL_ColumnDeleted_GeneratesAddColumn()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "TestTable",
            ColumnName = "TestColumn",
            ChangeType = ChangeType.Deleted,
            OldDefinition = "[TestColumn] VARCHAR(50) NULL"
        };

        // Act
        var result = _generator.GenerateReverseDDL(change);

        // Assert
        Assert.Equal("ALTER TABLE [dbo].[TestTable] ADD [TestColumn] VARCHAR(50) NULL;", result);
    }

    [Fact]
    public void GenerateReverseDDL_ColumnModified_GeneratesAlterWithOldDefinition()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "TestTable",
            ColumnName = "TestColumn",
            ChangeType = ChangeType.Modified,
            OldDefinition = "[TestColumn] VARCHAR(50) NULL",
            NewDefinition = "[TestColumn] VARCHAR(100) NOT NULL"
        };

        // Act
        var result = _generator.GenerateReverseDDL(change);

        // Assert
        Assert.Equal("ALTER TABLE [dbo].[TestTable] ALTER COLUMN [TestColumn] VARCHAR(50) NULL;", result);
    }

    [Fact]
    public void GenerateReverseDDL_IndexAdded_GeneratesDropIndex()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Index",
            Schema = "dbo",
            TableName = "TestTable",
            ObjectName = "IX_TestTable_TestColumn",
            ChangeType = ChangeType.Added,
            NewDefinition = "CREATE INDEX [IX_TestTable_TestColumn] ON [dbo].[TestTable] ([TestColumn]);"
        };

        // Act
        var result = _generator.GenerateReverseDDL(change);

        // Assert
        Assert.Equal("DROP INDEX [IX_TestTable_TestColumn] ON [dbo].[TestTable];", result);
    }

    [Fact]
    public void GenerateReverseDDL_RenameColumn_ReversesRename()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "TestTable",
            ColumnName = "NewColumnName",
            ChangeType = ChangeType.Modified,
            Properties = new Dictionary<string, string>
            {
                { "IsRename", "true" },
                { "OldName", "OldColumnName" },
                { "RenameType", "Column" }
            }
        };

        // Act
        var result = _generator.GenerateReverseDDL(change);

        // Assert
        Assert.Equal("EXEC sp_rename '[dbo].[TestTable].[NewColumnName]', 'OldColumnName', 'COLUMN';", result);
    }

    [Fact]
    public void GenerateReverseDDL_ViewAdded_GeneratesDropView()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "View",
            Schema = "dbo",
            ObjectName = "TestView",
            ChangeType = ChangeType.Added,
            NewDefinition = "CREATE VIEW [dbo].[TestView] AS SELECT * FROM TestTable;"
        };

        // Act
        var result = _generator.GenerateReverseDDL(change);

        // Assert
        Assert.Equal("DROP VIEW [dbo].[TestView];", result);
    }

    [Fact]
    public void GenerateReverseDDL_StoredProcedureModified_GeneratesOldVersion()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "StoredProcedure",
            Schema = "dbo",
            ObjectName = "TestProc",
            ChangeType = ChangeType.Modified,
            OldDefinition = "CREATE PROCEDURE [dbo].[TestProc] AS BEGIN SELECT 1 END",
            NewDefinition = "CREATE PROCEDURE [dbo].[TestProc] AS BEGIN SELECT 2 END"
        };

        // Act
        var result = _generator.GenerateReverseDDL(change);

        // Assert
        Assert.Contains("DROP PROCEDURE [dbo].[TestProc];", result);
        Assert.Contains("CREATE PROCEDURE [dbo].[TestProc] AS BEGIN SELECT 1 END", result);
    }

    [Fact]
    public void GenerateReverseDDL_ConstraintAdded_GeneratesDropConstraint()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "TestTable",
            ObjectName = "FK_TestTable_RefTable",
            ChangeType = ChangeType.Added,
            NewDefinition = "ALTER TABLE [dbo].[TestTable] ADD CONSTRAINT [FK_TestTable_RefTable] FOREIGN KEY ([RefId]) REFERENCES [dbo].[RefTable] ([Id]);"
        };

        // Act
        var result = _generator.GenerateReverseDDL(change);

        // Assert
        Assert.Equal("ALTER TABLE [dbo].[TestTable] DROP CONSTRAINT [FK_TestTable_RefTable];", result);
    }
}