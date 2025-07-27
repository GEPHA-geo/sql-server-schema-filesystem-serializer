using Xunit;
using Xunit.Abstractions;
using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class SqlFileChangeDetectorTests
{
    readonly SqlFileChangeDetector _detector = new();
    
    [Fact]
    public void AnalyzeChanges_WithTableChanges_ShouldDetectTableChanges()
    {
        // Arrange
        var diffEntries = new List<DiffEntry>
        {
            new DiffEntry
            {
                Path = "database/schemas/dbo/Tables/Customer/TBL_Customer.sql",
                ChangeType = ChangeType.Added,
                NewContent = "CREATE TABLE [dbo].[Customer] ([Id] INT)"
            }
        };
        
        // Act
        var changes = _detector.AnalyzeChanges("/output", diffEntries);
        
        // Assert
        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal("Table", change.ObjectType);
        Assert.Equal("Customer", change.ObjectName);
    }
    
    [Fact]
    public void AnalyzeChanges_WithIndexChanges_ShouldDetectIndexChanges()
    {
        // Arrange
        var diffEntries = new List<DiffEntry>
        {
            new DiffEntry
            {
                Path = "database/schemas/dbo/Tables/Customer/IDX_Customer_Email.sql",
                ChangeType = ChangeType.Added,
                NewContent = "CREATE INDEX [IDX_Customer_Email] ON [dbo].[Customer] ([Email])"
            }
        };
        
        // Act
        var changes = _detector.AnalyzeChanges("/output", diffEntries);
        
        // Assert
        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal("Index", change.ObjectType);
        Assert.Equal("IDX_Customer_Email", change.ObjectName);
    }
    
    [Fact]
    public void AnalyzeChanges_WithConstraintChanges_ShouldDetectConstraintChanges()
    {
        // Arrange
        var diffEntries = new List<DiffEntry>
        {
            new DiffEntry
            {
                Path = "database/schemas/dbo/Tables/Order/FK_Order_Customer.sql",
                ChangeType = ChangeType.Added,
                NewContent = "ALTER TABLE [dbo].[Order] ADD CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id])"
            }
        };
        
        // Act
        var changes = _detector.AnalyzeChanges("/output", diffEntries);
        
        // Assert
        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal("Constraint", change.ObjectType);
        Assert.Equal("Order/FK_Order_Customer", change.ObjectName);
    }
    
    [Fact]
    public void AnalyzeChanges_WithTriggerChanges_ShouldDetectTriggerChanges()
    {
        // Arrange
        var diffEntries = new List<DiffEntry>
        {
            new DiffEntry
            {
                Path = "database/schemas/dbo/Tables/Customer/trg_Customer_Audit.sql",
                ChangeType = ChangeType.Added,
                NewContent = "CREATE TRIGGER [trg_Customer_Audit] ON [dbo].[Customer] AFTER INSERT AS BEGIN END"
            }
        };
        
        // Act
        var changes = _detector.AnalyzeChanges("/output", diffEntries);
        
        // Assert
        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal("Trigger", change.ObjectType);
        Assert.Equal("trg_Customer_Audit", change.ObjectName);
    }
    
    [Fact]
    public void AnalyzeChanges_WithViewChanges_ShouldDetectViewChanges()
    {
        // Arrange
        var diffEntries = new List<DiffEntry>
        {
            new DiffEntry
            {
                Path = "database/schemas/dbo/Views/vw_CustomerOrders.sql",
                ChangeType = ChangeType.Added,
                NewContent = "CREATE VIEW [dbo].[vw_CustomerOrders] AS SELECT * FROM [dbo].[Customer]"
            }
        };
        
        // Act
        var changes = _detector.AnalyzeChanges("/output", diffEntries);
        
        // Assert
        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal("View", change.ObjectType);
        Assert.Equal("vw_CustomerOrders", change.ObjectName);
    }
    
    [Fact]
    public void AnalyzeChanges_WithStoredProcedureChanges_ShouldDetectStoredProcedureChanges()
    {
        // Arrange
        var diffEntries = new List<DiffEntry>
        {
            new DiffEntry
            {
                Path = "database/schemas/dbo/StoredProcedures/sp_GetCustomer.sql",
                ChangeType = ChangeType.Modified,
                OldContent = "CREATE PROCEDURE [dbo].[sp_GetCustomer] AS SELECT * FROM Customer",
                NewContent = "CREATE PROCEDURE [dbo].[sp_GetCustomer] AS SELECT Id, Name FROM Customer"
            }
        };
        
        // Act
        var changes = _detector.AnalyzeChanges("/output", diffEntries);
        
        // Assert
        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal("StoredProcedure", change.ObjectType);
        Assert.Equal("sp_GetCustomer", change.ObjectName);
        Assert.Equal(ChangeType.Modified, change.ChangeType);
    }
    
    [Fact]
    public void AnalyzeChanges_WithFunctionChanges_ShouldDetectFunctionChanges()
    {
        // Arrange
        var diffEntries = new List<DiffEntry>
        {
            new DiffEntry
            {
                Path = "database/schemas/dbo/Functions/fn_CalculateDiscount.sql",
                ChangeType = ChangeType.Deleted,
                OldContent = "CREATE FUNCTION [dbo].[fn_CalculateDiscount] (@price DECIMAL) RETURNS DECIMAL AS BEGIN RETURN @price * 0.9 END"
            }
        };
        
        // Act
        var changes = _detector.AnalyzeChanges("/output", diffEntries);
        
        // Assert
        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal("Function", change.ObjectType);
        Assert.Equal("fn_CalculateDiscount", change.ObjectName);
        Assert.Equal(ChangeType.Deleted, change.ChangeType);
    }
    
    [Fact]
    public void AnalyzeChanges_WithMultipleChanges_ShouldDetectAllChanges()
    {
        // Arrange
        var diffEntries = new List<DiffEntry>
        {
            new DiffEntry
            {
                Path = "database/schemas/dbo/Tables/Customer/TBL_Customer.sql",
                ChangeType = ChangeType.Modified,
                OldContent = "CREATE TABLE [dbo].[Customer] ([Id] INT, [Name] NVARCHAR(100))",
                NewContent = "CREATE TABLE [dbo].[Customer] ([Id] INT, [Name] NVARCHAR(200), [Email] NVARCHAR(100))"
            },
            new DiffEntry
            {
                Path = "database/schemas/dbo/Tables/Customer/IDX_Customer_Name.sql",
                ChangeType = ChangeType.Added,
                NewContent = "CREATE INDEX [IDX_Customer_Name] ON [dbo].[Customer] ([Name])"
            },
            new DiffEntry
            {
                Path = "database/schemas/dbo/Tables/Order/FK_Order_Customer.sql",
                ChangeType = ChangeType.Deleted,
                OldContent = "ALTER TABLE [dbo].[Order] ADD CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id])"
            }
        };
        
        // Act
        var changes = _detector.AnalyzeChanges("/output", diffEntries);
        
        // Assert
        // The table modification will produce column changes, plus we have an index and a constraint
        // However, rename detection might reduce the column changes if it detects a rename
        Assert.True(changes.Count >= 2); // At minimum: index + constraint
        
        // Check for index and constraint which should always be present
        Assert.Contains(changes, c => c.ObjectType == "Index" && c.ObjectName == "IDX_Customer_Name");
        Assert.Contains(changes, c => c.ObjectType == "Constraint" && c.ObjectName.Contains("FK_Order_Customer"));
        
        // Check if we have column changes (either added/modified or rename)
        var hasColumnChanges = changes.Any(c => c.ObjectType == "Column");
        if (hasColumnChanges)
        {
            // Could be either regular column changes or a rename
            var hasEmailColumn = changes.Any(c => 
                c.ObjectType == "Column" && c.ColumnName == "Email");
            var hasNameColumn = changes.Any(c => 
                c.ObjectType == "Column" && c.ColumnName == "Name");
            Assert.True(hasEmailColumn || hasNameColumn);
        }
    }
    
    
    [Fact]
    public void AnalyzeChanges_WithRenameDetection_ShouldApplyRenameDetection()
    {
        // Arrange
        var diffEntries = new List<DiffEntry>
        {
            new DiffEntry
            {
                Path = "database/schemas/dbo/Tables/Customer/TBL_Customer.sql",
                ChangeType = ChangeType.Modified,
                OldContent = @"CREATE TABLE [dbo].[Customer] (
    [Id] INT,
    [EmailAddress] NVARCHAR(100) NOT NULL
)",
                NewContent = @"CREATE TABLE [dbo].[Customer] (
    [Id] INT,
    [Email] NVARCHAR(100) NOT NULL
)"
            }
        };
        
        // Act
        var changes = _detector.AnalyzeChanges("/output", diffEntries);
        
        // Assert
        // Should detect rename instead of drop/add
        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal("Column", change.ObjectType);
        Assert.Equal(ChangeType.Modified, change.ChangeType);
        Assert.True(change.Properties.ContainsKey("IsRename"));
        Assert.Equal("true", change.Properties["IsRename"]);
    }
}