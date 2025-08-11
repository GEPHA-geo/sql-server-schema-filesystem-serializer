using Xunit;
using SqlServer.Schema.Migration.Generator.Generation;
using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Tests;

using System.Linq;

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

    
    [Fact]
    public void GenerateDDL_ForDefaultConstraint_WhenAddingNotNullColumn_ShouldSkipConstraint()
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
        
        var constraintChange = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "Product",
            ObjectName = "DF_Product_IsActive",
            ChangeType = ChangeType.Added,
            NewDefinition = "ALTER TABLE [dbo].[Product] ADD CONSTRAINT [DF_Product_IsActive] DEFAULT ((1)) FOR [IsActive]"
        };
        
        var allChanges = new List<SchemaChange> { columnChange, constraintChange };
        _generator.SetAllChanges(allChanges);
        
        // Act
        var ddl = _generator.GenerateDDL(constraintChange);
        
        // Assert
        Assert.Contains("DEFAULT constraint handled inline with column creation", ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForDefaultConstraint_WhenColumnNotBeingAdded_ShouldGenerateConstraint()
    {
        // Arrange
        var constraintChange = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "Product",
            ObjectName = "DF_Product_Description",
            ChangeType = ChangeType.Added,
            NewDefinition = "ALTER TABLE [dbo].[Product] ADD CONSTRAINT [DF_Product_Description] DEFAULT ('No description') FOR [Description]"
        };
        
        // No corresponding column being added
        var allChanges = new List<SchemaChange> { constraintChange };
        _generator.SetAllChanges(allChanges);
        
        // Act
        var ddl = _generator.GenerateDDL(constraintChange);
        
        // Assert
        // Now it should include drop logic for any existing default on the column
        Assert.Contains("Drop any existing DEFAULT constraint on column [Description]", ddl);
        Assert.Contains("sys.default_constraints", ddl);
        Assert.Contains(constraintChange.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForDefaultConstraint_WhenAddingNullableColumn_ShouldGenerateConstraint()
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
        
        var constraintChange = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "Product",
            ObjectName = "DF_Product_Description",
            ChangeType = ChangeType.Added,
            NewDefinition = "ALTER TABLE [dbo].[Product] ADD CONSTRAINT [DF_Product_Description] DEFAULT ('No description') FOR [Description]"
        };
        
        var allChanges = new List<SchemaChange> { columnChange, constraintChange };
        _generator.SetAllChanges(allChanges);
        
        // Act
        var ddl = _generator.GenerateDDL(constraintChange);
        
        // Assert
        // Nullable columns don't need inline DEFAULT, so constraint should be generated separately
        // But now with drop logic for any existing default on the column
        Assert.Contains("Drop any existing DEFAULT constraint on column [Description]", ddl);
        Assert.Contains("sys.default_constraints", ddl);
        Assert.Contains(constraintChange.NewDefinition, ddl);
    }

    
    [Fact]
    public void GenerateDDL_ForDefaultConstraint_WhenAddedSeparately_ShouldDropExistingDefaultFirst()
    {
        // Arrange
        var constraintChange = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "Product",
            ObjectName = "DF_Product_Status",
            ChangeType = ChangeType.Added,
            NewDefinition = "ALTER TABLE [dbo].[Product] ADD CONSTRAINT [DF_Product_Status] DEFAULT ('Active') FOR [Status]"
        };
        
        // No corresponding column being added - this is a standalone default constraint
        var allChanges = new List<SchemaChange> { constraintChange };
        _generator.SetAllChanges(allChanges);
        
        // Act
        var ddl = _generator.GenerateDDL(constraintChange);
        
        // Assert
        // Should contain logic to drop any existing default constraint on the column
        Assert.Contains("Drop any existing DEFAULT constraint on column [Status]", ddl);
        Assert.Contains("sys.default_constraints", ddl);
        Assert.Contains("@ConstraintName IS NOT NULL", ddl);
        Assert.Contains("DROP CONSTRAINT", ddl);
        Assert.Contains("Add new DEFAULT constraint", ddl);
        Assert.Contains(constraintChange.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForNonDefaultConstraint_ShouldNotAddDropLogic()
    {
        // Arrange
        var constraintChange = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "Order",
            ObjectName = "FK_Order_Customer",
            ChangeType = ChangeType.Added,
            NewDefinition = "ALTER TABLE [dbo].[Order] ADD CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id])"
        };
        
        var allChanges = new List<SchemaChange> { constraintChange };
        _generator.SetAllChanges(allChanges);
        
        // Act
        var ddl = _generator.GenerateDDL(constraintChange);
        
        // Assert
        // Foreign key constraint should not have drop logic
        Assert.DoesNotContain("sys.default_constraints", ddl);
        Assert.Equal(constraintChange.NewDefinition, ddl);
    }

    
    [Fact]
    public void GenerateDDL_ForDefaultConstraint_WithDifferentConstraintName_ShouldDropExistingAndAddNew()
    {
        // Arrange - scenario where we're adding DF_Product_Status but column might already have DF_Product_OldStatus
        var constraintChange = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "Product",
            ObjectName = "DF_Product_Status_New",
            ChangeType = ChangeType.Added,
            NewDefinition = "ALTER TABLE [dbo].[Product] ADD CONSTRAINT [DF_Product_Status_New] DEFAULT ('Pending') FOR [Status]"
        };
        
        var allChanges = new List<SchemaChange> { constraintChange };
        _generator.SetAllChanges(allChanges);
        
        // Act
        var ddl = _generator.GenerateDDL(constraintChange);
        
        // Assert
        // Should drop ANY existing default constraint on the Status column
        Assert.Contains("Drop any existing DEFAULT constraint on column [Status]", ddl);
        Assert.Contains("FROM sys.default_constraints dc", ddl);
        Assert.Contains("WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[Product]')", ddl);
        Assert.Contains("AND c.name = 'Status'", ddl);
        Assert.Contains("IF @ConstraintName IS NOT NULL", ddl);
        Assert.Contains("DROP CONSTRAINT", ddl);
        // And then add the new constraint
        Assert.Contains(constraintChange.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForDefaultConstraint_ExtractColumnName_HandlesComplexDefaults()
    {
        // Arrange - test various DEFAULT constraint formats
        var testCases = new[]
        {
            new
            {
                Definition = "ALTER TABLE [dbo].[Order] ADD CONSTRAINT [DF_Order_OrderDate] DEFAULT (GETDATE()) FOR [OrderDate]",
                ExpectedColumn = "OrderDate"
            },
            new
            {
                Definition = "ALTER TABLE [dbo].[Product] ADD CONSTRAINT [DF_Product_Price] DEFAULT ((0.00)) FOR [Price]",
                ExpectedColumn = "Price"
            },
            new
            {
                Definition = "ALTER TABLE [dbo].[Customer] ADD CONSTRAINT [DF_Customer_Country] DEFAULT (N'USA') FOR [Country]",
                ExpectedColumn = "Country"
            }
        };
        
        foreach (var testCase in testCases)
        {
            var constraintChange = new SchemaChange
            {
                ObjectType = "Constraint",
                Schema = "dbo",
                TableName = "TestTable",
                ObjectName = "DF_TestTable_Column",
                ChangeType = ChangeType.Added,
                NewDefinition = testCase.Definition
            };
            
            var allChanges = new List<SchemaChange> { constraintChange };
            _generator.SetAllChanges(allChanges);
            
            // Act
            var ddl = _generator.GenerateDDL(constraintChange);
            
            // Assert
            Assert.Contains($"Drop any existing DEFAULT constraint on column [{testCase.ExpectedColumn}]", ddl);
            Assert.Contains($"AND c.name = '{testCase.ExpectedColumn}'", ddl);
        }
    }

    
    [Fact]
    public void GenerateDDL_ForDefaultConstraint_ReplacingExistingWithDifferentName_ShouldGenerateCorrectScript()
    {
        // Arrange - Real-world scenario: Column 'Status' has DF_Product_StatusOld, we're adding DF_Product_Status
        var constraintChange = new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = "dbo",
            TableName = "Product",
            ObjectName = "DF_Product_Status",
            ChangeType = ChangeType.Added,
            NewDefinition = "ALTER TABLE [dbo].[Product] ADD CONSTRAINT [DF_Product_Status] DEFAULT ('Active') FOR [Status]"
        };
        
        var allChanges = new List<SchemaChange> { constraintChange };
        _generator.SetAllChanges(allChanges);
        
        // Act
        var ddl = _generator.GenerateDDL(constraintChange);
        
        // Assert
        // Verify the complete script structure
        var lines = ddl.Split('\n').Select(l => l.Trim()).ToList();
        
        // Should have comment explaining what we're doing
        Assert.Contains("-- Drop any existing DEFAULT constraint on column [Status]", ddl);
        
        // Should declare variable for constraint name
        Assert.Contains("DECLARE @ConstraintName nvarchar(200)", ddl);
        
        // Should query sys.default_constraints to find ANY default on Status column
        Assert.Contains("SELECT @ConstraintName = dc.name", ddl);
        Assert.Contains("FROM sys.default_constraints dc", ddl);
        Assert.Contains("INNER JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id", ddl);
        Assert.Contains("WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[Product]')", ddl);
        Assert.Contains("AND c.name = 'Status'", ddl);
        
        // Should conditionally drop the constraint if found
        Assert.Contains("IF @ConstraintName IS NOT NULL", ddl);
        Assert.Contains("EXEC('ALTER TABLE [dbo].[Product] DROP CONSTRAINT [' + @ConstraintName + ']')", ddl);
        
        // Should have GO statement to separate batches
        Assert.Contains("GO", ddl);
        
        // Should have comment for adding new constraint
        Assert.Contains("-- Add new DEFAULT constraint", ddl);
        
        // Should include the original constraint definition
        Assert.Contains(constraintChange.NewDefinition, ddl);
        
        // Verify the script will work even if:
        // 1. No existing default constraint exists (won't error)
        // 2. Existing constraint has different name like DF_Product_StatusOld
        // 3. Existing constraint has same name (will drop and recreate)
    }
    
    [Fact]
    public void GenerateDDL_ForDefaultConstraint_MultipleScenariosWithDifferentNames_ShouldHandleAll()
    {
        // Test multiple real-world scenarios
        var scenarios = new[]
        {
            new
            {
                Description = "Replacing auto-generated constraint name",
                Schema = "dbo",
                Table = "Customer",
                Column = "IsActive",
                OldConstraintName = "DF__Customer__IsActi__5EBF139D", // SQL Server auto-generated
                NewConstraintName = "DF_Customer_IsActive",
                NewDefault = "((1))"
            },
            new
            {
                Description = "Changing naming convention",
                Schema = "sales",
                Table = "Order",
                Column = "OrderDate",
                OldConstraintName = "Default_Order_OrderDate", // Old naming convention
                NewConstraintName = "DF_Order_OrderDate", // New naming convention
                NewDefault = "(GETUTCDATE())"
            },
            new
            {
                Description = "Fixing typo in constraint name",
                Schema = "dbo",
                Table = "Product",
                Column = "Price",
                OldConstraintName = "DF_Prodcut_Price", // Typo: Prodcut instead of Product
                NewConstraintName = "DF_Product_Price",
                NewDefault = "((0.00))"
            }
        };
        
        foreach (var scenario in scenarios)
        {
            // Arrange
            var constraintChange = new SchemaChange
            {
                ObjectType = "Constraint",
                Schema = scenario.Schema,
                TableName = scenario.Table,
                ObjectName = scenario.NewConstraintName,
                ChangeType = ChangeType.Added,
                NewDefinition = $"ALTER TABLE [{scenario.Schema}].[{scenario.Table}] ADD CONSTRAINT [{scenario.NewConstraintName}] DEFAULT {scenario.NewDefault} FOR [{scenario.Column}]"
            };
            
            var allChanges = new List<SchemaChange> { constraintChange };
            _generator.SetAllChanges(allChanges);
            
            // Act
            var ddl = _generator.GenerateDDL(constraintChange);
            
            // Assert - Verify it will drop ANY existing constraint on the column
            Assert.True(ddl.Contains($"Drop any existing DEFAULT constraint on column [{scenario.Column}]"), 
                $"Failed for scenario: {scenario.Description} - Missing drop comment");
            Assert.True(ddl.Contains($"WHERE dc.parent_object_id = OBJECT_ID(N'[{scenario.Schema}].[{scenario.Table}]')"),
                $"Failed for scenario: {scenario.Description} - Missing WHERE clause");
            Assert.True(ddl.Contains($"AND c.name = '{scenario.Column}'"),
                $"Failed for scenario: {scenario.Description} - Missing column name check");
            
            // The beauty of this approach is it doesn't need to know the old constraint name
            // It will find and drop whatever DEFAULT constraint exists on that column
        }
    }

    #region Extended Property Tests
    
    [Fact]
    public void GenerateDDL_ForExtendedProperty_Added_ShouldReturnAddStatement()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "ExtendedProperty",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "MS_Description",
            ChangeType = ChangeType.Added,
            NewDefinition = "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Customer information table', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer'"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Equal(change.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForExtendedProperty_Deleted_ShouldGenerateDropStatement()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "ExtendedProperty",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "MS_Description",
            ChangeType = ChangeType.Deleted,
            OldDefinition = "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Customer information table', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer'"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Contains("Drop extended property", ddl);
        Assert.Contains("sp_dropextendedproperty", ddl);
        Assert.Contains("@name = N'MS_Description'", ddl);
        Assert.Contains("@level0type = N'SCHEMA'", ddl);
        Assert.Contains("@level0name = N'dbo'", ddl);
        Assert.Contains("@level1type = N'TABLE'", ddl);
        Assert.Contains("@level1name = N'Customer'", ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForExtendedProperty_Modified_ShouldGenerateUpdateStatement()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "ExtendedProperty",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "MS_Description",
            ChangeType = ChangeType.Modified,
            OldDefinition = "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Customer information table', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer'",
            NewDefinition = "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Customer information and contact details', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer'"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        // Should only use sp_updateextendedproperty without any TRY/CATCH logic
        Assert.Contains("sp_updateextendedproperty", ddl);
        Assert.Contains("@value = N'Customer information and contact details'", ddl);
        Assert.DoesNotContain("BEGIN TRY", ddl);
        Assert.DoesNotContain("END TRY", ddl);
        Assert.DoesNotContain("BEGIN CATCH", ddl);
        Assert.DoesNotContain("END CATCH", ddl);
        Assert.DoesNotContain("ERROR_NUMBER", ddl);
        Assert.DoesNotContain("THROW", ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForExtendedProperty_ColumnLevel_ShouldHandleCorrectly()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "ExtendedProperty",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "Column_Description_Email",
            ChangeType = ChangeType.Added,
            NewDefinition = "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Customer email address', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'Email'"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Equal(change.NewDefinition, ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForExtendedProperty_DeleteColumnLevel_ShouldIncludeAllLevels()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "ExtendedProperty",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "Column_Description_Email",
            ChangeType = ChangeType.Deleted,
            OldDefinition = "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Customer email address', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'Email'"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Contains("sp_dropextendedproperty", ddl);
        Assert.Contains("@level2type = N'COLUMN'", ddl);
        Assert.Contains("@level2name = N'Email'", ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForExtendedProperty_InvalidDefinition_ShouldReturnCommentedError()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "ExtendedProperty",
            Schema = "dbo",
            TableName = "Customer",
            ObjectName = "MS_Description",
            ChangeType = ChangeType.Deleted,
            OldDefinition = "INVALID SQL STATEMENT"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.StartsWith("-- Could not parse extended property definition:", ddl);
        Assert.Contains("INVALID SQL STATEMENT", ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForExtendedProperty_Modify_ShouldUseUpdateOnly()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "ExtendedProperty",
            Schema = "dbo",
            TableName = "Product",
            ObjectName = "MS_Description",
            ChangeType = ChangeType.Modified,
            NewDefinition = "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Product catalog', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Product'"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        // Should only use sp_updateextendedproperty without any TRY/CATCH logic
        Assert.Contains("sp_updateextendedproperty", ddl);
        Assert.DoesNotContain("BEGIN TRY", ddl);
        Assert.DoesNotContain("END TRY", ddl);
        Assert.DoesNotContain("BEGIN CATCH", ddl);
        Assert.DoesNotContain("END CATCH", ddl);
        Assert.DoesNotContain("ERROR_NUMBER", ddl);
        Assert.DoesNotContain("sp_addextendedproperty", ddl);
        Assert.DoesNotContain("THROW", ddl);
    }
    
    [Fact]
    public void GenerateDDL_ForExtendedProperty_WithSpecialCharacters_ShouldPreserveValues()
    {
        // Arrange
        var change = new SchemaChange
        {
            ObjectType = "ExtendedProperty",
            Schema = "dbo",
            TableName = "Order",
            ObjectName = "MS_Description",
            ChangeType = ChangeType.Added,
            NewDefinition = "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Order''s details with special chars: [test] & symbols', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Order'"
        };
        
        // Act
        var ddl = _generator.GenerateDDL(change);
        
        // Assert
        Assert.Contains("Order''s details with special chars: [test] & symbols", ddl);
    }
    
    #endregion
}