using Xunit;
using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class TableChangeParserTests
{
    readonly TableChangeParser _parser = new();
    
    [Fact]
    public void ParseTableChanges_WhenTableAdded_ShouldReturnSingleTableAddChange()
    {
        // Arrange
        var entry = new DiffEntry
        {
            Path = "database/schemas/dbo/Tables/Customer/TBL_Customer.sql",
            ChangeType = ChangeType.Added,
            NewContent = @"CREATE TABLE [dbo].[Customer] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Email] NVARCHAR(100) NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY CLUSTERED ([Id] ASC)
);"
        };
        
        // Act
        var changes = _parser.ParseTableChanges(entry);
        
        // Assert
        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal("Table", change.ObjectType);
        Assert.Equal("dbo", change.Schema);
        Assert.Equal("Customer", change.ObjectName);
        Assert.Equal(ChangeType.Added, change.ChangeType);
        Assert.Equal(entry.NewContent, change.NewDefinition);
    }
    
    [Fact]
    public void ParseTableChanges_WhenTableDeleted_ShouldReturnSingleTableDeleteChange()
    {
        // Arrange
        var entry = new DiffEntry
        {
            Path = "database/schemas/dbo/Tables/Customer/TBL_Customer.sql",
            ChangeType = ChangeType.Deleted,
            OldContent = @"CREATE TABLE [dbo].[Customer] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL
);"
        };
        
        // Act
        var changes = _parser.ParseTableChanges(entry);
        
        // Assert
        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal("Table", change.ObjectType);
        Assert.Equal("dbo", change.Schema);
        Assert.Equal("Customer", change.ObjectName);
        Assert.Equal(ChangeType.Deleted, change.ChangeType);
        Assert.Equal(entry.OldContent, change.OldDefinition);
    }
    
    [Fact]
    public void ParseTableChanges_WhenTableModified_ShouldReturnColumnChanges()
    {
        // Arrange
        var entry = new DiffEntry
        {
            Path = "database/schemas/dbo/Tables/Customer/TBL_Customer.sql",
            ChangeType = ChangeType.Modified,
            OldContent = @"CREATE TABLE [dbo].[Customer] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [OldColumn] VARCHAR(50) NULL
);",
            NewContent = @"CREATE TABLE [dbo].[Customer] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Email] NVARCHAR(100) NULL,
    [Phone] VARCHAR(20) NULL
);"
        };
        
        // Act
        var changes = _parser.ParseTableChanges(entry);
        
        // Assert
        Assert.Equal(3, changes.Count); // 1 deleted, 2 added
        
        // Check deleted column
        var deletedColumn = changes.FirstOrDefault(c => c.ColumnName == "OldColumn");
        Assert.NotNull(deletedColumn);
        Assert.Equal("Column", deletedColumn.ObjectType);
        Assert.Equal(ChangeType.Deleted, deletedColumn.ChangeType);
        Assert.Equal("[OldColumn] VARCHAR(50) NULL", deletedColumn.OldDefinition);
        
        // Check added columns
        var emailColumn = changes.FirstOrDefault(c => c.ColumnName == "Email");
        Assert.NotNull(emailColumn);
        Assert.Equal("Column", emailColumn.ObjectType);
        Assert.Equal(ChangeType.Added, emailColumn.ChangeType);
        Assert.Equal("[Email] NVARCHAR(100) NULL", emailColumn.NewDefinition);
        
        var phoneColumn = changes.FirstOrDefault(c => c.ColumnName == "Phone");
        Assert.NotNull(phoneColumn);
        Assert.Equal("Column", phoneColumn.ObjectType);
        Assert.Equal(ChangeType.Added, phoneColumn.ChangeType);
        Assert.Equal("[Phone] VARCHAR(20) NULL", phoneColumn.NewDefinition);
    }
    
    [Fact]
    public void ParseTableChanges_WhenColumnModified_ShouldDetectModification()
    {
        // Arrange
        var entry = new DiffEntry
        {
            Path = "database/schemas/dbo/Tables/Product/TBL_Product.sql",
            ChangeType = ChangeType.Modified,
            OldContent = @"CREATE TABLE [dbo].[Product] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Price] DECIMAL(10,2) NOT NULL
);",
            NewContent = @"CREATE TABLE [dbo].[Product] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Price] DECIMAL(12,4) NOT NULL
);"
        };
        
        // Act
        var changes = _parser.ParseTableChanges(entry);
        
        // Assert
        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal("Column", change.ObjectType);
        Assert.Equal("Price", change.ColumnName);
        Assert.Equal(ChangeType.Modified, change.ChangeType);
        Assert.Equal("[Price] DECIMAL(10,2) NOT NULL", change.OldDefinition);
        Assert.Equal("[Price] DECIMAL(12,4) NOT NULL", change.NewDefinition);
    }
    
    
    [Fact]
    public void ParseTableChanges_WhenColumnHasOnlyWhitespaceDifferences_ShouldNotDetectAsModified()
    {
        // Arrange
        var entry = new DiffEntry
        {
            Path = "servers/server1/db1/schemas/dbo/Tables/TestTable/TBL_TestTable.sql",
            ChangeType = ChangeType.Modified,
            OldContent = @"CREATE TABLE [dbo].[TestTable] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name]   NVARCHAR(50)    NULL,
    [test] NCHAR (10) NULL
);",
            NewContent = @"CREATE TABLE [dbo].[TestTable] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name]   NVARCHAR(50)    NULL,
    [test]  NCHAR (10) NULL
);"
        };
        
        // Act
        var changes = _parser.ParseTableChanges(entry);
        
        // Assert
        Assert.Empty(changes); // No changes should be detected as only whitespace differs
    }

    [Fact]
    public void ParseTableChanges_WithNoChanges_ShouldReturnEmptyList()
    {
        // Arrange
        var entry = new DiffEntry
        {
            Path = "database/schemas/dbo/Tables/Customer/TBL_Customer.sql",
            ChangeType = ChangeType.Modified,
            OldContent = @"CREATE TABLE [dbo].[Customer] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL
);",
            NewContent = @"CREATE TABLE [dbo].[Customer] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL
);"
        };
        
        // Act
        var changes = _parser.ParseTableChanges(entry);
        
        // Assert
        Assert.Empty(changes);
    }
}