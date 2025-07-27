using Xunit;
using Xunit.Abstractions;
using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;
using SqlServer.Schema.Migration.Generator.Generation;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class RenameDetectorTests
{
    readonly RenameDetector _detector = new();
    readonly DDLGenerator _ddlGenerator = new();
    readonly ITestOutputHelper _output;
    
    public RenameDetectorTests(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public void DetectColumnRename_WhenColumnDroppedAndAddedWithSameDefinition_ShouldDetectRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "EmailAddress",
                ColumnName = "EmailAddress",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "[EmailAddress] NVARCHAR(100) NOT NULL"
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "Email",
                ColumnName = "Email",
                ChangeType = ChangeType.Added,
                NewDefinition = "[Email] NVARCHAR(100) NOT NULL"
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Single(result);
        var change = result[0];
        Assert.Equal(ChangeType.Modified, change.ChangeType);
        Assert.Equal("Email", change.ColumnName);
        Assert.True(change.Properties.ContainsKey("IsRename"));
        Assert.Equal("true", change.Properties["IsRename"]);
        Assert.Equal("EmailAddress", change.Properties["OldName"]);
        Assert.Equal("Column", change.Properties["RenameType"]);
    }
    
    [Fact]
    public void DetectColumnRename_WhenDefinitionsDiffer_ShouldNotDetectRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "EmailAddress",
                ColumnName = "EmailAddress",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "[EmailAddress] NVARCHAR(100) NOT NULL"
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "Email",
                ColumnName = "Email",
                ChangeType = ChangeType.Added,
                NewDefinition = "[Email] NVARCHAR(200) NOT NULL" // Different size
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, change => Assert.False(change.Properties.ContainsKey("IsRename")));
    }
    
    [Fact]
    public void DetectIndexRename_WhenIndexDefinitionSameExceptName_ShouldDetectRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Index",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "IDX_Customer_Email",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "CREATE NONCLUSTERED INDEX [IDX_Customer_Email] ON [dbo].[Customer] ([Email] ASC)"
            },
            new SchemaChange
            {
                ObjectType = "Index",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "IX_Customer_Email",
                ChangeType = ChangeType.Added,
                NewDefinition = "CREATE NONCLUSTERED INDEX [IX_Customer_Email] ON [dbo].[Customer] ([Email] ASC)"
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Single(result);
        var change = result[0];
        Assert.Equal("IX_Customer_Email", change.ObjectName);
        Assert.Equal("true", change.Properties["IsRename"]);
        Assert.Equal("IDX_Customer_Email", change.Properties["OldName"]);
    }
    
    [Fact]
    public void DetectConstraintRename_ForeignKey_ShouldDetectRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Constraint",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "FK_Customer_Country",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "ALTER TABLE [dbo].[Customer] ADD CONSTRAINT [FK_Customer_Country] FOREIGN KEY ([CountryId]) REFERENCES [dbo].[Country] ([Id])"
            },
            new SchemaChange
            {
                ObjectType = "Constraint",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "FK_Customer_CountryId",
                ChangeType = ChangeType.Added,
                NewDefinition = "ALTER TABLE [dbo].[Customer] ADD CONSTRAINT [FK_Customer_CountryId] FOREIGN KEY ([CountryId]) REFERENCES [dbo].[Country] ([Id])"
            }
        };
        
        _output.WriteLine($"Input changes count: {changes.Count}");
        foreach (var change in changes)
        {
            _output.WriteLine($"  {change.ObjectType} {change.ObjectName} ({change.ChangeType})");
            _output.WriteLine($"    Schema: {change.Schema}, Table: {change.TableName}");
            _output.WriteLine($"    OldDef: {change.OldDefinition}");
            _output.WriteLine($"    NewDef: {change.NewDefinition}");
        }
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        _output.WriteLine($"\nOutput changes count: {result.Count}");
        foreach (var change in result)
        {
            _output.WriteLine($"  {change.ObjectType} {change.ObjectName} ({change.ChangeType})");
            if (change.Properties.TryGetValue("IsRename", out var isRename))
            {
                _output.WriteLine($"    IsRename: {isRename}");
                if (isRename == "true" && change.Properties.TryGetValue("OldName", out var oldName))
                {
                    _output.WriteLine($"    OldName: {oldName}");
                }
            }
        }
        
        // Assert
        Assert.Single(result);
        Assert.Equal("true", result[0].Properties["IsRename"]);
    }
    
    [Fact]
    public void DetectTriggerRename_ShouldDetectRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Trigger",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "trg_CustomerAudit",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "CREATE TRIGGER [trg_CustomerAudit] ON [dbo].[Customer] AFTER INSERT AS BEGIN /* logic */ END"
            },
            new SchemaChange
            {
                ObjectType = "Trigger",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "trg_Customer_Audit",
                ChangeType = ChangeType.Added,
                NewDefinition = "CREATE TRIGGER [trg_Customer_Audit] ON [dbo].[Customer] AFTER INSERT AS BEGIN /* logic */ END"
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Single(result);
        Assert.Equal("true", result[0].Properties["IsRename"]);
    }
    
    [Fact]
    public void MixedOperations_ShouldCorrectlyIdentifyRenamesAndRealChanges()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            // Column rename
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "ProductName",
                ColumnName = "ProductName",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "[ProductName] NVARCHAR(200) NOT NULL"
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "Name",
                ColumnName = "Name",
                ChangeType = ChangeType.Added,
                NewDefinition = "[Name] NVARCHAR(200) NOT NULL"
            },
            // Real column addition
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "Description",
                ColumnName = "Description",
                ChangeType = ChangeType.Added,
                NewDefinition = "[Description] NVARCHAR(MAX) NULL"
            },
            // Real column deletion
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "LegacyCode",
                ColumnName = "LegacyCode",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "[LegacyCode] VARCHAR(50) NULL"
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Equal(3, result.Count); // 1 rename (counts as 1) + 1 add + 1 delete
        
        var rename = result.First(c => c.Properties.ContainsKey("IsRename"));
        Assert.Equal("Name", rename.ColumnName);
        Assert.Equal("ProductName", rename.Properties["OldName"]);
        
        var realAdd = result.First(c => c.ColumnName == "Description");
        Assert.Equal(ChangeType.Added, realAdd.ChangeType);
        Assert.False(realAdd.Properties.ContainsKey("IsRename"));
        
        var realDelete = result.First(c => c.ColumnName == "LegacyCode");
        Assert.Equal(ChangeType.Deleted, realDelete.ChangeType);
        Assert.False(realDelete.Properties.ContainsKey("IsRename"));
    }
    
    [Fact]
    public void GenerateRenameDDL_ForColumn_ShouldGenerateCorrectSQL()
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
            Properties = new Dictionary<string, string>
            {
                ["IsRename"] = "true",
                ["OldName"] = "EmailAddress",
                ["RenameType"] = "Column"
            }
        };
        
        // Act
        var ddl = _ddlGenerator.GenerateDDL(change);
        
        // Assert
        Assert.Equal("EXEC sp_rename '[dbo].[Customer].[EmailAddress]', 'Email', 'COLUMN';", ddl);
    }
    
    [Fact]
    public void GenerateRenameDDL_ForIndex_ShouldGenerateCorrectSQL()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Index",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "IX_Customer_Email",
            ChangeType = ChangeType.Modified,
            Properties = new Dictionary<string, string>
            {
                ["IsRename"] = "true",
                ["OldName"] = "IDX_Customer_Email",
                ["RenameType"] = "Index"
            }
        };
        
        // Act
        var ddl = _ddlGenerator.GenerateDDL(change);
        
        // Assert
        Assert.Equal("EXEC sp_rename '[dbo].[Customer].[IDX_Customer_Email]', 'IX_Customer_Email', 'INDEX';", ddl);
    }
    
    [Fact]
    public void MultipleRenames_InSameTable_ShouldAllBeDetected()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            // First column rename
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
            // Second column rename
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "LastName",
                ColumnName = "LastName",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "[LastName] NVARCHAR(50) NOT NULL"
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Customer",
                ObjectName = "FamilyName",
                ColumnName = "FamilyName",
                ChangeType = ChangeType.Added,
                NewDefinition = "[FamilyName] NVARCHAR(50) NOT NULL"
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, change => 
        {
            Assert.Equal("true", change.Properties["IsRename"]);
            Assert.Equal(ChangeType.Modified, change.ChangeType);
        });
    }
    
    [Fact]
    public void ColumnWithIdentity_ShouldStillDetectRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Order",
                ObjectName = "OrderID",
                ColumnName = "OrderID",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "[OrderID] INT IDENTITY(1,1) NOT NULL"
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Order",
                ObjectName = "OrderId",
                ColumnName = "OrderId",
                ChangeType = ChangeType.Added,
                NewDefinition = "[OrderId] INT IDENTITY(1,1) NOT NULL"
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Single(result);
        Assert.Equal("true", result[0].Properties["IsRename"]);
    }
    
    [Fact]
    public void ColumnWithDefaultConstraint_ShouldDetectRename()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "Created",
                ColumnName = "Created",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "[Created] DATETIME NOT NULL DEFAULT (GETDATE())"
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "CreatedDate",
                ColumnName = "CreatedDate",
                ChangeType = ChangeType.Added,
                NewDefinition = "[CreatedDate] DATETIME NOT NULL DEFAULT (GETDATE())"
            }
        };
        
        // Act
        var result = _detector.DetectRenames(changes);
        
        // Assert
        Assert.Single(result);
        Assert.Equal("true", result[0].Properties["IsRename"]);
    }
}