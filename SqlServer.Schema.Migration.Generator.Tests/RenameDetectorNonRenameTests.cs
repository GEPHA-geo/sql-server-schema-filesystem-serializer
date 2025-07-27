using Xunit;
using Xunit.Abstractions;
using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class RenameDetectorNonRenameTests
{
    readonly RenameDetector _detector = new();
    readonly ITestOutputHelper _output;
    
    public RenameDetectorNonRenameTests(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public void DetectRenames_WhenColumnDataTypesDiffer_ShouldNotDetectAsRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "Age",
                ColumnName = "Age",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "[Age] INT NOT NULL"
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "CustomerAge",
                ColumnName = "CustomerAge",
                ChangeType = ChangeType.Added,
                NewDefinition = "[CustomerAge] BIGINT NOT NULL" // Different data type
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, change => Assert.False(change.Properties.ContainsKey("IsRename")));
        Assert.Contains(result, c => c.ChangeType == ChangeType.Deleted && c.ColumnName == "Age");
        Assert.Contains(result, c => c.ChangeType == ChangeType.Added && c.ColumnName == "CustomerAge");
    }
    
    [Fact]
    public void DetectRenames_WhenColumnNullabilityDiffers_ShouldNotDetectAsRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "Description",
                ColumnName = "Description",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "[Description] NVARCHAR(500) NOT NULL"
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "ProductDescription",
                ColumnName = "ProductDescription",
                ChangeType = ChangeType.Added,
                NewDefinition = "[ProductDescription] NVARCHAR(500) NULL" // Different nullability
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, change => Assert.False(change.Properties.ContainsKey("IsRename")));
    }
    
    [Fact]
    public void DetectRenames_WhenIndexColumnsListDiffers_ShouldNotDetectAsRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Index",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "IX_Customer_Email",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "CREATE NONCLUSTERED INDEX [IX_Customer_Email] ON [dbo].[Customer] ([Email] ASC)"
            },
            new SchemaChange
            {
                ObjectType = "Index",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "IX_Customer_EmailName",
                ChangeType = ChangeType.Added,
                NewDefinition = "CREATE NONCLUSTERED INDEX [IX_Customer_EmailName] ON [dbo].[Customer] ([Email] ASC, [Name] ASC)" // Different columns
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, change => Assert.False(change.Properties.ContainsKey("IsRename")));
    }
    
    [Fact]
    public void DetectRenames_WhenIndexTypesDiffer_ShouldNotDetectAsRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Index",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "IX_Product_Code",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "CREATE NONCLUSTERED INDEX [IX_Product_Code] ON [dbo].[Product] ([Code] ASC)"
            },
            new SchemaChange
            {
                ObjectType = "Index",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "IX_Product_ProductCode",
                ChangeType = ChangeType.Added,
                NewDefinition = "CREATE CLUSTERED INDEX [IX_Product_ProductCode] ON [dbo].[Product] ([Code] ASC)" // CLUSTERED vs NONCLUSTERED
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, change => Assert.False(change.Properties.ContainsKey("IsRename")));
    }
    
    [Fact]
    public void DetectRenames_WhenConstraintDefinitionsDiffer_ShouldNotDetectAsRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Constraint",
                Schema = "dbo",
                TableName = "Order",
                ObjectName = "FK_Order_Customer",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "ALTER TABLE [dbo].[Order] ADD CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id])"
            },
            new SchemaChange
            {
                ObjectType = "Constraint",
                Schema = "dbo",
                TableName = "Order",
                ObjectName = "FK_Order_CustomerId",
                ChangeType = ChangeType.Added,
                NewDefinition = "ALTER TABLE [dbo].[Order] ADD CONSTRAINT [FK_Order_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]) ON DELETE CASCADE" // Added CASCADE
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, change => Assert.False(change.Properties.ContainsKey("IsRename")));
    }
    
    [Fact]
    public void DetectRenames_WhenChangesInDifferentTables_ShouldNotDetectAsRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "Email",
                ColumnName = "Email",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "[Email] NVARCHAR(100) NOT NULL"
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Employee", // Different table
                ObjectName = "EmailAddress",
                ColumnName = "EmailAddress",
                ChangeType = ChangeType.Added,
                NewDefinition = "[EmailAddress] NVARCHAR(100) NOT NULL"
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, change => Assert.False(change.Properties.ContainsKey("IsRename")));
    }
    
    [Fact]
    public void DetectRenames_WhenChangesInDifferentSchemas_ShouldNotDetectAsRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "Name",
                ColumnName = "Name",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "[Name] NVARCHAR(200) NOT NULL"
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "sales", // Different schema
                TableName = "Product",
                ObjectName = "ProductName",
                ColumnName = "ProductName",
                ChangeType = ChangeType.Added,
                NewDefinition = "[ProductName] NVARCHAR(200) NOT NULL"
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, change => Assert.False(change.Properties.ContainsKey("IsRename")));
    }
    
    [Fact]
    public void DetectRenames_WithDefaultValueDifference_ShouldNotDetectAsRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Settings",
                ObjectName = "IsActive",
                ColumnName = "IsActive",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "[IsActive] BIT NOT NULL DEFAULT (1)"
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Settings",
                ObjectName = "IsEnabled",
                ColumnName = "IsEnabled",
                ChangeType = ChangeType.Added,
                NewDefinition = "[IsEnabled] BIT NOT NULL DEFAULT (0)" // Different default value
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, change => Assert.False(change.Properties.ContainsKey("IsRename")));
    }
    
    [Fact]
    public void DetectRenames_WhenTriggerEventsAreDifferent_ShouldNotDetectAsRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Trigger",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "trg_Customer_Audit",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "CREATE TRIGGER [trg_Customer_Audit] ON [dbo].[Customer] AFTER INSERT AS BEGIN /* audit logic */ END"
            },
            new SchemaChange
            {
                ObjectType = "Trigger",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "trg_CustomerAudit",
                ChangeType = ChangeType.Added,
                NewDefinition = "CREATE TRIGGER [trg_CustomerAudit] ON [dbo].[Customer] AFTER INSERT, UPDATE AS BEGIN /* audit logic */ END" // Added UPDATE event
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, change => Assert.False(change.Properties.ContainsKey("IsRename")));
    }
    
    [Fact]
    public void DetectRenames_WithMixedRenamesAndRealChanges_ShouldOnlyDetectTrueRenames()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            // True rename - same definition
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "FirstName",
                ColumnName = "FirstName",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "[FirstName] NVARCHAR(50) NOT NULL"
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "GivenName",
                ColumnName = "GivenName",
                ChangeType = ChangeType.Added,
                NewDefinition = "[GivenName] NVARCHAR(50) NOT NULL"
            },
            // Not a rename - different data type
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "Age",
                ColumnName = "Age",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "[Age] TINYINT NULL"
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "CustomerAge",
                ColumnName = "CustomerAge",
                ChangeType = ChangeType.Added,
                NewDefinition = "[CustomerAge] INT NULL"
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        _output.WriteLine($"Result count: {result.Count}");
        foreach (var change in result)
        {
            _output.WriteLine($"{change.ObjectType} {change.ObjectName} ({change.ChangeType}) - IsRename: {change.Properties.ContainsKey("IsRename")}");
        }
        
        // Assert
        Assert.Equal(3, result.Count); // 1 rename + 2 separate add/delete
        
        var renameCount = result.Count(c => c.Properties.ContainsKey("IsRename") && c.Properties["IsRename"] == "true");
        Assert.Equal(1, renameCount);
        
        var rename = result.First(c => c.Properties.ContainsKey("IsRename"));
        Assert.Equal("GivenName", rename.ColumnName);
        Assert.Equal("FirstName", rename.Properties["OldName"]);
    }
    
    [Fact]
    public void DetectRenames_WhenIdentitySpecificationsDiffer_ShouldNotDetectAsRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Order",
                ObjectName = "OrderNumber",
                ColumnName = "OrderNumber",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "[OrderNumber] INT NOT NULL"
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Order",
                ObjectName = "OrderId",
                ColumnName = "OrderId",
                ChangeType = ChangeType.Added,
                NewDefinition = "[OrderId] INT IDENTITY(1,1) NOT NULL" // Added IDENTITY
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, change => Assert.False(change.Properties.ContainsKey("IsRename")));
    }
    
    [Fact]
    public void DetectRenames_WhenUniqueConstraintsDiffer_ShouldNotDetectAsRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Index",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "IX_Product_Code",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "CREATE NONCLUSTERED INDEX [IX_Product_Code] ON [dbo].[Product] ([Code] ASC)"
            },
            new SchemaChange
            {
                ObjectType = "Index",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "UQ_Product_Code",
                ChangeType = ChangeType.Added,
                NewDefinition = "CREATE UNIQUE NONCLUSTERED INDEX [UQ_Product_Code] ON [dbo].[Product] ([Code] ASC)" // Added UNIQUE
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, change => Assert.False(change.Properties.ContainsKey("IsRename")));
    }
}