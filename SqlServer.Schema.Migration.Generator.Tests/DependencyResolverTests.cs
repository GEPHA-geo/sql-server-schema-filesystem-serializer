using Xunit;
using SqlServer.Schema.Migration.Generator.Generation;
using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class DependencyResolverTests
{
    readonly DependencyResolver _resolver = new();
    
    [Fact]
    public void OrderChanges_WithEmptyList_ShouldReturnEmptyList()
    {
        // Arrange
        var changes = new List<SchemaChange>();
        
        // Act
        var ordered = _resolver.OrderChanges(changes);
        
        // Assert
        Assert.Empty(ordered);
    }
    
    [Fact]
    public void OrderChanges_WithForeignKeyConstraints_ShouldDropFKsFirst()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Column",
                ChangeType = ChangeType.Deleted,
                ObjectName = "CustomerId"
            },
            new SchemaChange
            {
                ObjectType = "Constraint",
                ChangeType = ChangeType.Deleted,
                ObjectName = "FK_Order_Customer"
            },
            new SchemaChange
            {
                ObjectType = "Constraint",
                ChangeType = ChangeType.Deleted,
                ObjectName = "CHK_Order_Total"
            }
        };
        
        // Act
        var ordered = _resolver.OrderChanges(changes);
        
        // Assert
        Assert.Equal("FK_Order_Customer", ordered[0].ObjectName);
        Assert.Equal("CHK_Order_Total", ordered[1].ObjectName);
        Assert.Equal("CustomerId", ordered[2].ObjectName);
    }
    
    [Fact]
    public void OrderChanges_WithTableCreationAndDeletion_ShouldOrderCorrectly()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Table",
                ChangeType = ChangeType.Added,
                ObjectName = "NewTable"
            },
            new SchemaChange
            {
                ObjectType = "Table",
                ChangeType = ChangeType.Deleted,
                ObjectName = "OldTable"
            }
        };
        
        // Act
        var ordered = _resolver.OrderChanges(changes);
        
        // Assert
        Assert.Equal("OldTable", ordered[0].ObjectName); // Drops come first
        Assert.Equal("NewTable", ordered[1].ObjectName); // Creates come after
    }
    
    [Fact]
    public void OrderChanges_WithCompleteScenario_ShouldFollowCorrectOrder()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            // Various creates
            new SchemaChange { ObjectType = "Table", ChangeType = ChangeType.Added, ObjectName = "NewTable" },
            new SchemaChange { ObjectType = "Column", ChangeType = ChangeType.Added, ObjectName = "NewColumn" },
            new SchemaChange { ObjectType = "Index", ChangeType = ChangeType.Added, ObjectName = "IX_NewIndex" },
            new SchemaChange { ObjectType = "Constraint", ChangeType = ChangeType.Added, ObjectName = "FK_NewFK" },
            new SchemaChange { ObjectType = "Constraint", ChangeType = ChangeType.Added, ObjectName = "CHK_NewCheck" },
            new SchemaChange { ObjectType = "View", ChangeType = ChangeType.Added, ObjectName = "NewView" },
            
            // Various drops
            new SchemaChange { ObjectType = "View", ChangeType = ChangeType.Deleted, ObjectName = "OldView" },
            new SchemaChange { ObjectType = "Constraint", ChangeType = ChangeType.Deleted, ObjectName = "FK_OldFK" },
            new SchemaChange { ObjectType = "Constraint", ChangeType = ChangeType.Deleted, ObjectName = "PK_OldPK" },
            new SchemaChange { ObjectType = "Index", ChangeType = ChangeType.Deleted, ObjectName = "IX_OldIndex" },
            new SchemaChange { ObjectType = "Column", ChangeType = ChangeType.Deleted, ObjectName = "OldColumn" },
            new SchemaChange { ObjectType = "Table", ChangeType = ChangeType.Deleted, ObjectName = "OldTable" },
            
            // Modifications
            new SchemaChange { ObjectType = "Column", ChangeType = ChangeType.Modified, ObjectName = "ModifiedColumn" }
        };
        
        // Act
        var ordered = _resolver.OrderChanges(changes);
        
        // Assert
        var orderNames = ordered.Select(c => c.ObjectName).ToList();
        
        // Verify drop order
        Assert.True(orderNames.IndexOf("FK_OldFK") < orderNames.IndexOf("PK_OldPK"));
        Assert.True(orderNames.IndexOf("PK_OldPK") < orderNames.IndexOf("IX_OldIndex"));
        Assert.True(orderNames.IndexOf("IX_OldIndex") < orderNames.IndexOf("OldColumn"));
        Assert.True(orderNames.IndexOf("OldColumn") < orderNames.IndexOf("OldView"));
        Assert.True(orderNames.IndexOf("OldView") < orderNames.IndexOf("OldTable"));
        
        // Verify create order
        Assert.True(orderNames.IndexOf("NewTable") < orderNames.IndexOf("NewColumn"));
        Assert.True(orderNames.IndexOf("NewColumn") < orderNames.IndexOf("ModifiedColumn"));
        Assert.True(orderNames.IndexOf("ModifiedColumn") < orderNames.IndexOf("IX_NewIndex"));
        Assert.True(orderNames.IndexOf("IX_NewIndex") < orderNames.IndexOf("CHK_NewCheck"));
        Assert.True(orderNames.IndexOf("CHK_NewCheck") < orderNames.IndexOf("FK_NewFK"));
        Assert.True(orderNames.IndexOf("FK_NewFK") < orderNames.IndexOf("NewView"));
    }
    
    [Fact]
    public void OrderChanges_WithMultipleForeignKeys_ShouldDropAllFKsFirst()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange { ObjectType = "Index", ChangeType = ChangeType.Deleted, ObjectName = "IX_Test" },
            new SchemaChange { ObjectType = "Constraint", ChangeType = ChangeType.Deleted, ObjectName = "FK_Order_Customer" },
            new SchemaChange { ObjectType = "Constraint", ChangeType = ChangeType.Deleted, ObjectName = "DF_Default" },
            new SchemaChange { ObjectType = "Constraint", ChangeType = ChangeType.Deleted, ObjectName = "FK_OrderItem_Order" },
            new SchemaChange { ObjectType = "Constraint", ChangeType = ChangeType.Deleted, ObjectName = "FK_OrderItem_Product" }
        };
        
        // Act
        var ordered = _resolver.OrderChanges(changes);
        
        // Assert
        // All FKs should come first
        Assert.Equal("FK_Order_Customer", ordered[0].ObjectName);
        Assert.Equal("FK_OrderItem_Order", ordered[1].ObjectName);
        Assert.Equal("FK_OrderItem_Product", ordered[2].ObjectName);
        Assert.Equal("DF_Default", ordered[3].ObjectName);
        Assert.Equal("IX_Test", ordered[4].ObjectName);
    }
    
    [Fact]
    public void OrderChanges_WithOnlyModifications_ShouldMaintainOrder()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange { ObjectType = "Column", ChangeType = ChangeType.Modified, ObjectName = "Column1" },
            new SchemaChange { ObjectType = "Column", ChangeType = ChangeType.Modified, ObjectName = "Column2" },
            new SchemaChange { ObjectType = "Column", ChangeType = ChangeType.Modified, ObjectName = "Column3" }
        };
        
        // Act
        var ordered = _resolver.OrderChanges(changes);
        
        // Assert
        Assert.Equal(3, ordered.Count);
        Assert.All(ordered, c => Assert.Equal(ChangeType.Modified, c.ChangeType));
    }
    
    [Fact]
    public void OrderChanges_WithProceduresAndFunctions_ShouldOrderCorrectly()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange { ObjectType = "Function", ChangeType = ChangeType.Added, ObjectName = "fn_New" },
            new SchemaChange { ObjectType = "StoredProcedure", ChangeType = ChangeType.Added, ObjectName = "sp_New" },
            new SchemaChange { ObjectType = "Function", ChangeType = ChangeType.Deleted, ObjectName = "fn_Old" },
            new SchemaChange { ObjectType = "StoredProcedure", ChangeType = ChangeType.Deleted, ObjectName = "sp_Old" },
            new SchemaChange { ObjectType = "Table", ChangeType = ChangeType.Added, ObjectName = "NewTable" },
            new SchemaChange { ObjectType = "Table", ChangeType = ChangeType.Deleted, ObjectName = "OldTable" }
        };
        
        // Act
        var ordered = _resolver.OrderChanges(changes);
        
        // Assert
        var orderNames = ordered.Select(c => c.ObjectName).ToList();
        
        // Drops: procedures/functions before tables
        Assert.True(orderNames.IndexOf("fn_Old") < orderNames.IndexOf("OldTable"));
        Assert.True(orderNames.IndexOf("sp_Old") < orderNames.IndexOf("OldTable"));
        
        // Creates: tables before procedures/functions
        Assert.True(orderNames.IndexOf("NewTable") < orderNames.IndexOf("fn_New"));
        Assert.True(orderNames.IndexOf("NewTable") < orderNames.IndexOf("sp_New"));
    }
    
    [Fact]
    public void OrderChanges_PreservesAllChanges()
    {
        // Arrange
        var changes = new List<SchemaChange>();
        for (int i = 0; i < 10; i++)
        {
            changes.Add(new SchemaChange 
            { 
                ObjectType = "Column", 
                ChangeType = i % 2 == 0 ? ChangeType.Added : ChangeType.Deleted,
                ObjectName = $"Column{i}"
            });
        }
        
        // Act
        var ordered = _resolver.OrderChanges(changes);
        
        // Assert
        Assert.Equal(changes.Count, ordered.Count);
        Assert.All(changes, c => Assert.Contains(ordered, o => o.ObjectName == c.ObjectName));
    }
}