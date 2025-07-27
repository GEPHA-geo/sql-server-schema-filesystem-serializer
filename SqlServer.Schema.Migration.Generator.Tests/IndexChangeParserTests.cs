using Xunit;
using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class IndexChangeParserTests
{
    readonly IndexChangeParser _parser = new();
    
    [Fact]
    public void ParseIndexChange_WhenIndexAdded_ShouldReturnIndexAddChange()
    {
        // Arrange
        var entry = new DiffEntry
        {
            Path = "database/schemas/dbo/Tables/Customer/IDX_Customer_Email.sql",
            ChangeType = ChangeType.Added,
            NewContent = "CREATE NONCLUSTERED INDEX [IDX_Customer_Email] ON [dbo].[Customer] ([Email] ASC)"
        };
        
        // Act
        var change = _parser.ParseIndexChange(entry);
        
        // Assert
        Assert.NotNull(change);
        Assert.Equal("Index", change.ObjectType);
        Assert.Equal("dbo", change.Schema);
        Assert.Equal("Customer", change.TableName);
        Assert.Equal("IDX_Customer_Email", change.ObjectName);
        Assert.Equal(ChangeType.Added, change.ChangeType);
        Assert.Equal(entry.NewContent, change.NewDefinition);
    }
    
    [Fact]
    public void ParseIndexChange_WhenIndexDeleted_ShouldReturnIndexDeleteChange()
    {
        // Arrange
        var entry = new DiffEntry
        {
            Path = "database/schemas/dbo/Tables/Customer/IX_Customer_LastName.sql",
            ChangeType = ChangeType.Deleted,
            OldContent = "CREATE NONCLUSTERED INDEX [IX_Customer_LastName] ON [dbo].[Customer] ([LastName] ASC)"
        };
        
        // Act
        var change = _parser.ParseIndexChange(entry);
        
        // Assert
        Assert.NotNull(change);
        Assert.Equal("Index", change.ObjectType);
        Assert.Equal("dbo", change.Schema);
        Assert.Equal("Customer", change.TableName);
        Assert.Equal("IX_Customer_LastName", change.ObjectName);
        Assert.Equal(ChangeType.Deleted, change.ChangeType);
        Assert.Equal(entry.OldContent, change.OldDefinition);
    }
    
    [Fact]
    public void ParseIndexChange_WhenIndexModified_ShouldReturnIndexModifyChange()
    {
        // Arrange
        var entry = new DiffEntry
        {
            Path = "database/schemas/sales/Tables/Order/IDX_Order_Date.sql",
            ChangeType = ChangeType.Modified,
            OldContent = "CREATE NONCLUSTERED INDEX [IDX_Order_Date] ON [sales].[Order] ([OrderDate] ASC)",
            NewContent = "CREATE NONCLUSTERED INDEX [IDX_Order_Date] ON [sales].[Order] ([OrderDate] DESC)"
        };
        
        // Act
        var change = _parser.ParseIndexChange(entry);
        
        // Assert
        Assert.NotNull(change);
        Assert.Equal("Index", change.ObjectType);
        Assert.Equal("sales", change.Schema);
        Assert.Equal("Order", change.TableName);
        Assert.Equal("IDX_Order_Date", change.ObjectName);
        Assert.Equal(ChangeType.Modified, change.ChangeType);
        Assert.Equal(entry.OldContent, change.OldDefinition);
        Assert.Equal(entry.NewContent, change.NewDefinition);
    }
    
    
    [Fact]
    public void ParseIndexChange_WithComplexIndexDefinition_ShouldParseCorrectly()
    {
        // Arrange
        var entry = new DiffEntry
        {
            Path = "database/schemas/dbo/Tables/Order/IDX_Order_Complex.sql",
            ChangeType = ChangeType.Added,
            NewContent = @"CREATE NONCLUSTERED INDEX [IDX_Order_Complex] 
ON [dbo].[Order] (
    [CustomerId] ASC,
    [OrderDate] DESC,
    [Status] ASC
)
INCLUDE ([TotalAmount], [ShippingAddress])
WHERE ([Status] <> 'Cancelled')
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, 
      DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)"
        };
        
        // Act
        var change = _parser.ParseIndexChange(entry);
        
        // Assert
        Assert.NotNull(change);
        Assert.Equal("Index", change.ObjectType);
        Assert.Equal("IDX_Order_Complex", change.ObjectName);
        Assert.Contains("INCLUDE", change.NewDefinition);
        Assert.Contains("WHERE", change.NewDefinition);
    }
    
    [Fact]
    public void ParseIndexChange_WithInvalidPathButValidContent_ShouldExtractFromContent()
    {
        // Arrange
        var entry = new DiffEntry
        {
            Path = "database/invalid/path.sql",
            ChangeType = ChangeType.Added,
            NewContent = "CREATE INDEX [IX_Test] ON [dbo].[Test] ([Id])"
        };
        
        // Act
        var change = _parser.ParseIndexChange(entry);
        
        // Assert
        // Parser extracts from content when path doesn't match expected pattern
        Assert.NotNull(change);
        Assert.Equal("Index", change.ObjectType);
        Assert.Equal("IX_Test", change.ObjectName);
        Assert.Equal("dbo", change.Schema);
        Assert.Equal("Test", change.TableName);
    }
}