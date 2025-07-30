using Xunit;
using SqlServer.Schema.Migration.Generator.Generation;
using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class IndexDDLGeneratorTests
{
    readonly IndexDDLGenerator _generator = new();
    
    [Fact]
    public void GenerateIndexDDL_ForAddedIndex_ShouldReturnCreateStatement()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Index",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "IDX_Customer_Email",
            ChangeType = ChangeType.Added,
            NewDefinition = "CREATE NONCLUSTERED INDEX [IDX_Customer_Email] ON [dbo].[Customer] ([Email] ASC)"
        };
        
        // Act
        var ddl = _generator.GenerateIndexDDL(change);
        
        // Assert
        Assert.Equal(change.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateIndexDDL_ForDeletedIndex_ShouldReturnDropStatement()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Index",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "IX_Customer_Name",
            ChangeType = ChangeType.Deleted,
            OldDefinition = "CREATE NONCLUSTERED INDEX [IX_Customer_Name] ON [dbo].[Customer] ([Name] ASC)"
        };
        
        // Act
        var ddl = _generator.GenerateIndexDDL(change);
        
        // Assert
        Assert.Equal("DROP INDEX [IX_Customer_Name] ON [dbo].[Customer];", ddl);
    }
    
    [Fact]
    public void GenerateIndexDDL_ForModifiedIndex_ShouldReturnDropAndCreate()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Index",
            Schema = "dbo",
            TableName = "Product",
            ObjectName = "IX_Product_Category",
            ChangeType = ChangeType.Modified,
            OldDefinition = "CREATE NONCLUSTERED INDEX [IX_Product_Category] ON [dbo].[Product] ([CategoryId] ASC)",
            NewDefinition = "CREATE NONCLUSTERED INDEX [IX_Product_Category] ON [dbo].[Product] ([CategoryId] ASC, [Name] ASC)"
        };
        
        // Act
        var ddl = _generator.GenerateIndexDDL(change);
        
        // Assert
        Assert.Contains("DROP INDEX [IX_Product_Category] ON [dbo].[Product];", ddl);
        Assert.Contains("GO", ddl);
        Assert.Contains(change.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateIndexDDL_ForComplexIndex_ShouldHandleCorrectly()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Index",
            Schema = "sales",
            TableName = "Order",
            ObjectName = "IX_Order_Complex",
            ChangeType = ChangeType.Added,
            NewDefinition = @"CREATE NONCLUSTERED INDEX [IX_Order_Complex] 
ON [sales].[Order] (
    [CustomerId] ASC,
    [OrderDate] DESC
)
INCLUDE ([TotalAmount], [Status])
WHERE ([Status] <> 'Cancelled')
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF)"
        };
        
        // Act
        var ddl = _generator.GenerateIndexDDL(change);
        
        // Assert
        Assert.Equal(change.NewDefinition, ddl);
        Assert.Contains("INCLUDE", ddl);
        Assert.Contains("WHERE", ddl);
        Assert.Contains("WITH", ddl);
    }
    
    [Fact]
    public void GenerateIndexDDL_ForUniqueIndex_ShouldHandleCorrectly()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Index",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "UQ_Customer_Email",
            ChangeType = ChangeType.Added,
            NewDefinition = "CREATE UNIQUE NONCLUSTERED INDEX [UQ_Customer_Email] ON [dbo].[Customer] ([Email] ASC)"
        };
        
        // Act
        var ddl = _generator.GenerateIndexDDL(change);
        
        // Assert
        Assert.Equal(change.NewDefinition, ddl);
        Assert.Contains("UNIQUE", ddl);
    }
    
    [Fact]
    public void GenerateIndexDDL_ForClusteredIndex_ShouldHandleCorrectly()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Index",
            Schema = "dbo",
            TableName = "OrderItem",
            ObjectName = "IX_OrderItem_Clustered",
            ChangeType = ChangeType.Added,
            NewDefinition = "CREATE CLUSTERED INDEX [IX_OrderItem_Clustered] ON [dbo].[OrderItem] ([OrderId] ASC, [ProductId] ASC)"
        };
        
        // Act
        var ddl = _generator.GenerateIndexDDL(change);
        
        // Assert
        Assert.Equal(change.NewDefinition, ddl);
        Assert.Contains("CLUSTERED", ddl);
    }
    
    [Fact]
    public void GenerateIndexDDL_WithUnknownChangeType_ShouldReturnComment()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Index",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "IX_Customer_Test",
            ChangeType = (ChangeType)999 // Invalid change type
        };
        
        // Act
        var ddl = _generator.GenerateIndexDDL(change);
        
        // Assert
        Assert.StartsWith("-- Unknown change type for index:", ddl);
        Assert.Contains("IX_Customer_Test", ddl);
    }
    
    [Fact]
    public void GenerateIndexDDL_ForRenamedIndex_ShouldNotHandleDirectly()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Index",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "IX_Customer_Email",
            ChangeType = ChangeType.Modified,
            OldDefinition = "CREATE NONCLUSTERED INDEX [IDX_Customer_Email] ON [dbo].[Customer] ([Email] ASC)",
            NewDefinition = "CREATE NONCLUSTERED INDEX [IX_Customer_Email] ON [dbo].[Customer] ([Email] ASC)",
            Properties = new Dictionary<string, string>
            {
                ["IsRename"] = "true",
                ["OldName"] = "IDX_Customer_Email",
                ["RenameType"] = "Index"
            }
        };
        
        // Act
        var ddl = _generator.GenerateIndexDDL(change);
        
        // Assert
        // IndexDDLGenerator doesn't handle renames - it will treat as drop/create
        Assert.Contains("DROP INDEX", ddl);
        Assert.Contains(change.NewDefinition, ddl);
    }
}