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

    #region Extended Property Reverse Tests
    
    [Fact]
    public void GenerateReverseDDL_ExtendedPropertyAdded_GeneratesDropStatement()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "ExtendedProperty",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "MS_Description",
            ChangeType = ChangeType.Added,
            NewDefinition = "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Customer information table', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer'"
        };

        // Act
        var result = _generator.GenerateReverseDDL(change);

        // Assert
        Assert.Contains("Drop extended property", result);
        Assert.Contains("sp_dropextendedproperty", result);
        Assert.Contains("@name = N'MS_Description'", result);
        Assert.Contains("@level0type = N'SCHEMA'", result);
        Assert.Contains("@level0name = N'dbo'", result);
        Assert.Contains("@level1type = N'TABLE'", result);
        Assert.Contains("@level1name = N'Customer'", result);
    }
    
    [Fact]
    public void GenerateReverseDDL_ExtendedPropertyDeleted_GeneratesAddStatement()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "ExtendedProperty",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "MS_Description",
            ChangeType = ChangeType.Deleted,
            OldDefinition = "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Customer information table', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer'"
        };

        // Act
        var result = _generator.GenerateReverseDDL(change);

        // Assert
        Assert.Equal(change.OldDefinition, result);
    }
    
    [Fact]
    public void GenerateReverseDDL_ExtendedPropertyModified_GeneratesUpdateWithOldValue()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "ExtendedProperty",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "MS_Description",
            ChangeType = ChangeType.Modified,
            OldDefinition = "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Customer information table', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer'",
            NewDefinition = "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Customer information and contact details', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer'"
        };

        // Act
        var result = _generator.GenerateReverseDDL(change);

        // Assert
        Assert.Contains("Update extended property to restore old value", result);
        Assert.Contains("sp_updateextendedproperty", result);
        Assert.Contains("@value = N'Customer information table'", result); // Old value
        Assert.Contains("BEGIN TRY", result);
        Assert.Contains("END TRY", result);
        Assert.Contains("BEGIN CATCH", result);
        Assert.Contains("IF ERROR_NUMBER() = 15217", result); // Property does not exist
    }
    
    [Fact]
    public void GenerateReverseDDL_ExtendedPropertyColumnLevel_ShouldHandleAllLevels()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "ExtendedProperty",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "Column_Description_Email",
            ChangeType = ChangeType.Added,
            NewDefinition = "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Customer email address', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'Email'"
        };

        // Act
        var result = _generator.GenerateReverseDDL(change);

        // Assert
        Assert.Contains("sp_dropextendedproperty", result);
        Assert.Contains("@level2type = N'COLUMN'", result);
        Assert.Contains("@level2name = N'Email'", result);
    }
    
    [Fact]
    public void GenerateReverseDDL_ExtendedPropertyInvalidDefinition_ReturnsErrorComment()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "ExtendedProperty",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "MS_Description",
            ChangeType = ChangeType.Added,
            NewDefinition = "INVALID SQL STATEMENT"
        };

        // Act
        var result = _generator.GenerateReverseDDL(change);

        // Assert
        Assert.StartsWith("-- Could not parse extended property definition:", result);
        Assert.Contains("INVALID SQL STATEMENT", result);
    }
    
    #endregion
}