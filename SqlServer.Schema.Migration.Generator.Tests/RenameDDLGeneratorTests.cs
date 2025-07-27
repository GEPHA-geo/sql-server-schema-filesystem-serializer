using Xunit;
using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.Generation;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class RenameDDLGeneratorTests
{
    readonly RenameDDLGenerator _generator = new();
    
    [Fact]
    public void GenerateColumnRename_ShouldProduceCorrectSQL()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "Customer",
            ColumnName = "Email",
            Properties = new Dictionary<string, string>
            {
                ["IsRename"] = "true",
                ["OldName"] = "EmailAddress",
                ["RenameType"] = "Column"
            }
        };
        
        // Act
        var sql = _generator.GenerateRenameDDL(change);
        
        // Assert
        Assert.Equal("EXEC sp_rename '[dbo].[Customer].[EmailAddress]', 'Email', 'COLUMN';", sql);
    }
    
    [Fact]
    public void GenerateIndexRename_ShouldProduceCorrectSQL()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Index",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "IX_Customer_Email",
            Properties = new Dictionary<string, string>
            {
                ["IsRename"] = "true",
                ["OldName"] = "IDX_Customer_Email",
                ["RenameType"] = "Index"
            }
        };
        
        // Act
        var sql = _generator.GenerateRenameDDL(change);
        
        // Assert
        Assert.Equal("EXEC sp_rename '[dbo].[Customer].[IDX_Customer_Email]', 'IX_Customer_Email', 'INDEX';", sql);
    }
    
    [Fact]
    public void GenerateConstraintRename_ShouldProduceCorrectSQL()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            ObjectName = "FK_Customer_CountryId",
            Properties = new Dictionary<string, string>
            {
                ["IsRename"] = "true",
                ["OldName"] = "FK_Customer_Country",
                ["RenameType"] = "Constraint"
            }
        };
        
        // Act
        var sql = _generator.GenerateRenameDDL(change);
        
        // Assert
        Assert.Equal("EXEC sp_rename '[dbo].[FK_Customer_Country]', 'FK_Customer_CountryId', 'OBJECT';", sql);
    }
    
    [Fact]
    public void GenerateTriggerRename_ShouldProduceCorrectSQL()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Trigger",
            Schema = "dbo",
            ObjectName = "trg_Customer_Audit",
            Properties = new Dictionary<string, string>
            {
                ["IsRename"] = "true",
                ["OldName"] = "trg_CustomerAudit",
                ["RenameType"] = "Trigger"
            }
        };
        
        // Act
        var sql = _generator.GenerateRenameDDL(change);
        
        // Assert
        Assert.Equal("EXEC sp_rename '[dbo].[trg_CustomerAudit]', 'trg_Customer_Audit', 'OBJECT';", sql);
    }
    
    [Fact]
    public void GenerateRename_WithoutIsRenameFlag_ShouldReturnComment()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "Email",
            Properties = new Dictionary<string, string>()
        };
        
        // Act
        var sql = _generator.GenerateRenameDDL(change);
        
        // Assert
        Assert.StartsWith("-- Not a rename operation", sql);
    }
    
    [Fact]
    public void GenerateRename_WithoutOldName_ShouldReturnErrorComment()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "Email",
            Properties = new Dictionary<string, string>
            {
                ["IsRename"] = "true",
                ["RenameType"] = "Column"
            }
        };
        
        // Act
        var sql = _generator.GenerateRenameDDL(change);
        
        // Assert
        Assert.StartsWith("-- Missing old name", sql);
    }
    
    [Fact]
    public void GenerateRename_WithSpecialCharactersInNames_ShouldHandleCorrectly()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Column",
            Schema = "dbo",
            TableName = "Customer_Orders",
            ColumnName = "Order_Date",
            Properties = new Dictionary<string, string>
            {
                ["IsRename"] = "true",
                ["OldName"] = "OrderDate",
                ["RenameType"] = "Column"
            }
        };
        
        // Act
        var sql = _generator.GenerateRenameDDL(change);
        
        // Assert
        Assert.Equal("EXEC sp_rename '[dbo].[Customer_Orders].[OrderDate]', 'Order_Date', 'COLUMN';", sql);
    }
}