using Xunit;
using SqlServer.Schema.Migration.Generator.Generation;
using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class DDLGeneratorTests
{
    readonly DDLGenerator _generator = new();
    
    [Fact]
    public void GenerateDDL_ForTable_ShouldUseTableDDLGenerator()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Table",
            Schema = "dbo",
            ObjectName = "Customer",
            ChangeType = ChangeType.Added,
            NewDefinition = "CREATE TABLE [dbo].[Customer] ([Id] INT)"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Equal(change.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForColumn_ShouldUseTableDDLGenerator()
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
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Equal("ALTER TABLE [dbo].[Customer] ADD [Email] NVARCHAR(100) NULL;", ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForIndex_ShouldUseIndexDDLGenerator()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Index",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "IX_Customer_Email",
            ChangeType = ChangeType.Added,
            NewDefinition = "CREATE INDEX [IX_Customer_Email] ON [dbo].[Customer] ([Email])"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Equal(change.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForRename_ShouldUseRenameDDLGenerator()
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
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Equal("EXEC sp_rename '[dbo].[Customer].[EmailAddress]', 'Email', 'COLUMN';", ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForConstraint_ShouldGenerateConstraintDDL()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "Order",
            ObjectName = "FK_Order_Customer",
            ChangeType = ChangeType.Added,
            NewDefinition = "ALTER TABLE [dbo].[Order] ADD CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id])"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Equal(change.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForDeletedForeignKey_ShouldGenerateDropConstraint()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "Order",
            ObjectName = "FK_Order_Customer",
            ChangeType = ChangeType.Deleted
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Equal("ALTER TABLE [dbo].[Order] DROP CONSTRAINT [FK_Order_Customer];", ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForTrigger_ShouldGenerateTriggerDDL()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Trigger",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "trg_Customer_Audit",
            ChangeType = ChangeType.Added,
            NewDefinition = "CREATE TRIGGER [trg_Customer_Audit] ON [dbo].[Customer] AFTER INSERT AS BEGIN END"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Equal(change.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForView_ShouldGenerateViewDDL()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "View",
            Schema = "dbo",
            ObjectName = "vw_CustomerOrders",
            ChangeType = ChangeType.Added,
            NewDefinition = "CREATE VIEW [dbo].[vw_CustomerOrders] AS SELECT * FROM [dbo].[Customer]"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Equal(change.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForStoredProcedure_ShouldGenerateStoredProcedureDDL()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "StoredProcedure",
            Schema = "dbo",
            ObjectName = "sp_GetCustomer",
            ChangeType = ChangeType.Modified,
            OldDefinition = "CREATE PROCEDURE [dbo].[sp_GetCustomer] AS SELECT * FROM Customer",
            NewDefinition = "CREATE PROCEDURE [dbo].[sp_GetCustomer] AS SELECT Id, Name FROM Customer"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Contains("DROP PROCEDURE [dbo].[sp_GetCustomer];", ddl);
        Assert.Contains("GO", ddl);
        Assert.Contains(change.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForFunction_ShouldGenerateFunctionDDL()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Function",
            Schema = "dbo",
            ObjectName = "fn_Calculate",
            ChangeType = ChangeType.Deleted
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Equal("DROP FUNCTION [dbo].[fn_Calculate];", ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForUnsupportedType_ShouldReturnComment()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "UnknownType",
            Schema = "dbo",
            ObjectName = "SomeObject",
            ChangeType = ChangeType.Added
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.StartsWith("-- Unsupported object type:", ddl);
        Assert.Contains("UnknownType", ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForModifiedConstraint_ShouldDropAndRecreate()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "Order",
            ObjectName = "FK_Order_Customer",
            ChangeType = ChangeType.Modified,
            OldDefinition = "ALTER TABLE [dbo].[Order] ADD CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id])",
            NewDefinition = "ALTER TABLE [dbo].[Order] ADD CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]) ON DELETE CASCADE"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Contains("DROP CONSTRAINT [FK_Order_Customer];", ddl);
        Assert.Contains("GO", ddl);
        Assert.Contains(change.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForCheckConstraint_ShouldHandleCorrectly()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "Product",
            ObjectName = "CHK_Product_Price",
            ChangeType = ChangeType.Added,
            NewDefinition = "ALTER TABLE [dbo].[Product] ADD CONSTRAINT [CHK_Product_Price] CHECK ([Price] > 0)"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Equal(change.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForDefaultConstraint_ShouldHandleCorrectly()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "DF_Customer_Created",
            ChangeType = ChangeType.Deleted
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Equal("ALTER TABLE [dbo].[Customer] DROP CONSTRAINT [DF_Customer_Created];", ddl);
    }

    [Fact]
    public void GenerateDDL_ForDefaultConstraintFromTableFolder_ShouldGenerateProperDropStatement()
    {
        // Arrange - simulating the constraint parsed from er_pac_zg_koef_cvl/DF_er_pac_zg_koef_cvl_axali_sveti
        var change = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "er_pac_zg_koef_cvl",
            ObjectName = "DF_er_pac_zg_koef_cvl_axali_sveti",
            ChangeType = ChangeType.Deleted
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Equal("ALTER TABLE [dbo].[er_pac_zg_koef_cvl] DROP CONSTRAINT [DF_er_pac_zg_koef_cvl_axali_sveti];", ddl);
        // Should NOT be a comment like "-- Drop constraint: er_pac_zg_koef_cvl/DF_er_pac_zg_koef_cvl_axali_sveti"
        Assert.DoesNotContain("--", ddl);
    }

    [Fact]
    public void MigrationScriptBuilder_ShouldExplicitlyDropDefaultConstraintsBeforeColumns()
    {
        // Arrange
        var builder = new MigrationScriptBuilder();
        var changes = new List<SchemaChange>
        {
            // Column being dropped
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "er_pac_zg_koef_cvl",
                ColumnName = "axali_sveti",
                ChangeType = ChangeType.Deleted
            },
            // Default constraint for the column being dropped
            new SchemaChange
            {
                ObjectType = "Constraint",
                Schema = "dbo",
                TableName = "er_pac_zg_koef_cvl",
                ObjectName = "DF_er_pac_zg_koef_cvl_axali_sveti",
                ChangeType = ChangeType.Deleted,
                OldDefinition = "ALTER TABLE [dbo].[er_pac_zg_koef_cvl] ADD CONSTRAINT [DF_er_pac_zg_koef_cvl_axali_sveti] DEFAULT ('') FOR [axali_sveti];"
            }
        };
        
        // Act
        var migration = builder.BuildMigration(changes, "TestDB", "test-user");
        
        // Assert
        // Should contain both the constraint drop AND the column drop
        Assert.Contains("DROP CONSTRAINT [DF_er_pac_zg_koef_cvl_axali_sveti]", migration);
        Assert.Contains("DROP COLUMN [axali_sveti]", migration);
        
        // Verify constraint is dropped before column
        var constraintDropIndex = migration.IndexOf("DROP CONSTRAINT [DF_er_pac_zg_koef_cvl_axali_sveti]");
        var columnDropIndex = migration.IndexOf("DROP COLUMN [axali_sveti]");
        Assert.True(constraintDropIndex < columnDropIndex, "Constraint should be dropped before column");
    }
}