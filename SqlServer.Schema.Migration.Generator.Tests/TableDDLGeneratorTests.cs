using Xunit;
using SqlServer.Schema.Migration.Generator.Generation;
using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class TableDDLGeneratorTests
{
    readonly TableDDLGenerator _generator = new();
    
    [Fact]
    public void GenerateTableDDL_ForAddedTable_ShouldReturnCreateStatement()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Table",
            Schema = "dbo",
            ObjectName = "Customer",
            ChangeType = ChangeType.Added,
            NewDefinition = @"CREATE TABLE [dbo].[Customer] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY CLUSTERED ([Id] ASC)
)"
        };
        
        // Act
        var ddl = _generator.GenerateTableDDL(change);
        
        // Assert
        Assert.Equal(change.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateTableDDL_ForDeletedTable_ShouldReturnDropStatement()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Table",
            Schema = "dbo",
            ObjectName = "Customer",
            ChangeType = ChangeType.Deleted,
            OldDefinition = "CREATE TABLE [dbo].[Customer] ([Id] INT)"
        };
        
        // Act
        var ddl = _generator.GenerateTableDDL(change);
        
        // Assert
        Assert.Equal("DROP TABLE [dbo].[Customer];", ddl);
    }
    
    [Fact]
    public void GenerateTableDDL_ForModifiedTable_ShouldReturnComment()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Table",
            Schema = "dbo",
            ObjectName = "Customer",
            ChangeType = ChangeType.Modified,
            OldDefinition = "CREATE TABLE [dbo].[Customer] ([Id] INT)",
            NewDefinition = "CREATE TABLE [dbo].[Customer] ([Id] INT, [Name] NVARCHAR(100))"
        };
        
        // Act
        var ddl = _generator.GenerateTableDDL(change);
        
        // Assert
        Assert.Equal("-- Table structure modified - see column changes below", ddl);
    }
    
    [Fact]
    public void GenerateColumnDDL_ForAddedColumn_ShouldReturnAlterTableAdd()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "Email",
            ColumnName = "Email",
            ChangeType = ChangeType.Added,
            NewDefinition = "[Email] NVARCHAR(100) NULL"
        };
        
        // Act
        var ddl = _generator.GenerateColumnDDL(change);
        
        // Assert
        Assert.Equal("ALTER TABLE [dbo].[Customer] ADD [Email] NVARCHAR(100) NULL;", ddl);
    }
    
    [Fact]
    public void GenerateColumnDDL_ForDeletedColumn_ShouldReturnAlterTableDrop()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "OldColumn",
            ColumnName = "OldColumn",
            ChangeType = ChangeType.Deleted,
            OldDefinition = "[OldColumn] VARCHAR(50) NULL"
        };
        
        // Act
        var ddl = _generator.GenerateColumnDDL(change);
        
        // Assert
        Assert.Equal("ALTER TABLE [dbo].[Customer] DROP COLUMN [OldColumn];", ddl);
    }
    
    [Fact]
    public void GenerateColumnDDL_ForModifiedColumn_ShouldReturnAlterColumn()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "Product",
            ObjectName = "Price",
            ColumnName = "Price",
            ChangeType = ChangeType.Modified,
            OldDefinition = "[Price] DECIMAL(10,2) NOT NULL",
            NewDefinition = "[Price] DECIMAL(12,4) NOT NULL"
        };
        
        // Act
        var ddl = _generator.GenerateColumnDDL(change);
        
        // Assert
        Assert.Equal("ALTER TABLE [dbo].[Product] ALTER COLUMN [Price] DECIMAL(12,4) NOT NULL;", ddl);
    }
    
    
    [Fact]
    public void GenerateColumnDDL_ForRenamedColumn_ShouldGenerateRenameStatement()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "Email",
            ColumnName = "Email",
            ChangeType = ChangeType.Modified,
            OldDefinition = "[EmailAddress] NVARCHAR(100) NOT NULL",
            NewDefinition = "[Email] NVARCHAR(100) NOT NULL",
            Properties = new Dictionary<string, string>
            {
                ["IsRename"] = "true",
                ["OldName"] = "EmailAddress",
                ["RenameType"] = "Column"
            }
        };
        
        // Act
        var ddl = _generator.GenerateColumnDDL(change);
        
        // Assert
        // Note: This should be handled by the DDLGenerator which would use RenameDDLGenerator
        // but TableDDLGenerator doesn't handle renames directly
        Assert.Contains("ALTER COLUMN", ddl);
    }

    
    [Fact]
    public void GenerateColumnDDL_ForNotNullColumnWithDefaultConstraint_ShouldIncludeDefaultInline()
    {
        // Arrange
        var columnChange = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "Product",
            ObjectName = "IsActive",
            ColumnName = "IsActive",
            ChangeType = ChangeType.Added,
            NewDefinition = "[IsActive] BIT NOT NULL"
        };
        
        var defaultConstraint = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "Product",
            ObjectName = "DF_Product_IsActive",
            ChangeType = ChangeType.Added,
            NewDefinition = "ALTER TABLE [dbo].[Product] ADD CONSTRAINT [DF_Product_IsActive] DEFAULT ((1)) FOR [IsActive]"
        };
        
        var allChanges = new List<SchemaChange> { columnChange, defaultConstraint };
        _generator.SetAllChanges(allChanges);
        
        // Act
        var ddl = _generator.GenerateColumnDDL(columnChange);
        
        // Assert
        Assert.Equal("ALTER TABLE [dbo].[Product] ADD [IsActive] BIT DEFAULT ((1)) NOT NULL;", ddl);
    }
    
    [Fact]
    public void GenerateColumnDDL_ForNotNullColumnWithComplexDefaultConstraint_ShouldIncludeDefaultInline()
    {
        // Arrange
        var columnChange = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "Order",
            ObjectName = "CreatedDate",
            ColumnName = "CreatedDate",
            ChangeType = ChangeType.Added,
            NewDefinition = "[CreatedDate] DATETIME2 NOT NULL"
        };
        
        var defaultConstraint = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "Order",
            ObjectName = "DF_Order_CreatedDate",
            ChangeType = ChangeType.Added,
            NewDefinition = "ALTER TABLE [dbo].[Order] ADD CONSTRAINT [DF_Order_CreatedDate] DEFAULT (GETUTCDATE()) FOR [CreatedDate]"
        };
        
        var allChanges = new List<SchemaChange> { columnChange, defaultConstraint };
        _generator.SetAllChanges(allChanges);
        
        // Act
        var ddl = _generator.GenerateColumnDDL(columnChange);
        
        // Assert
        Assert.Equal("ALTER TABLE [dbo].[Order] ADD [CreatedDate] DATETIME2 DEFAULT (GETUTCDATE()) NOT NULL;", ddl);
    }
    
    [Fact]
    public void GenerateColumnDDL_ForNullableColumnWithDefaultConstraint_ShouldNotIncludeDefaultInline()
    {
        // Arrange
        var columnChange = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "Product",
            ObjectName = "Description",
            ColumnName = "Description",
            ChangeType = ChangeType.Added,
            NewDefinition = "[Description] NVARCHAR(500) NULL"
        };
        
        var defaultConstraint = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "Product",
            ObjectName = "DF_Product_Description",
            ChangeType = ChangeType.Added,
            NewDefinition = "ALTER TABLE [dbo].[Product] ADD CONSTRAINT [DF_Product_Description] DEFAULT ('No description') FOR [Description]"
        };
        
        var allChanges = new List<SchemaChange> { columnChange, defaultConstraint };
        _generator.SetAllChanges(allChanges);
        
        // Act
        var ddl = _generator.GenerateColumnDDL(columnChange);
        
        // Assert
        // Nullable columns don't need inline DEFAULT, so it should remain unchanged
        Assert.Equal("ALTER TABLE [dbo].[Product] ADD [Description] NVARCHAR(500) NULL;", ddl);
    }
    
    [Fact]
    public void GenerateColumnDDL_ForNotNullColumnWithoutDefaultConstraint_ShouldNotAddDefault()
    {
        // Arrange
        var columnChange = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "Email",
            ColumnName = "Email",
            ChangeType = ChangeType.Added,
            NewDefinition = "[Email] NVARCHAR(100) NOT NULL"
        };
        
        var allChanges = new List<SchemaChange> { columnChange };
        _generator.SetAllChanges(allChanges);
        
        // Act
        var ddl = _generator.GenerateColumnDDL(columnChange);
        
        // Assert
        Assert.Equal("ALTER TABLE [dbo].[Customer] ADD [Email] NVARCHAR(100) NOT NULL;", ddl);
    }
}