using Xunit;
using SqlServer.Schema.Migration.Generator;
using SqlServer.Schema.Migration.Generator.Generation;
using SqlServer.Schema.Migration.Generator.Parsing;
using System.Collections.Generic;

namespace SqlServer.Schema.Migration.Generator.Tests.IntegrationTests;

public class NotNullDefaultConstraintTests
{
    [Fact]
    public void MigrationScriptBuilder_WithNotNullColumnAndDefaultConstraint_ShouldGenerateInlineDefault()
    {
        // Arrange
        var changes = new List<SchemaChange>
        {
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "IsActive",
                ColumnName = "IsActive",
                ChangeType = GitIntegration.ChangeType.Added,
                NewDefinition = "[IsActive] BIT NOT NULL"
            },
            new SchemaChange
            {
                ObjectType = "Constraint",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "DF_Product_IsActive",
                ChangeType = GitIntegration.ChangeType.Added,
                NewDefinition = "ALTER TABLE [dbo].[Product] ADD CONSTRAINT [DF_Product_IsActive] DEFAULT ((1)) FOR [IsActive]"
            },
            new SchemaChange
            {
                ObjectType = "Column",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "CreatedDate",
                ColumnName = "CreatedDate",
                ChangeType = GitIntegration.ChangeType.Added,
                NewDefinition = "[CreatedDate] DATETIME2 NOT NULL"
            },
            new SchemaChange
            {
                ObjectType = "Constraint",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "DF_Product_CreatedDate",
                ChangeType = GitIntegration.ChangeType.Added,
                NewDefinition = "ALTER TABLE [dbo].[Product] ADD CONSTRAINT [DF_Product_CreatedDate] DEFAULT (GETUTCDATE()) FOR [CreatedDate]"
            }
        };
        
        var scriptBuilder = new MigrationScriptBuilder();
        
        // Act
        var migrationScript = scriptBuilder.BuildMigration(changes, "TestDB", "test-user");
        
        // Assert
        // Verify that the column additions include DEFAULT inline
        Assert.Contains("ALTER TABLE [dbo].[Product] ADD [IsActive] BIT DEFAULT ((1)) NOT NULL;", migrationScript);
        Assert.Contains("ALTER TABLE [dbo].[Product] ADD [CreatedDate] DATETIME2 DEFAULT (GETUTCDATE()) NOT NULL;", migrationScript);
        
        // Verify that the DEFAULT constraints are marked as handled inline
        Assert.Contains("DEFAULT constraint handled inline with column creation: DF_Product_IsActive", migrationScript);
        Assert.Contains("DEFAULT constraint handled inline with column creation: DF_Product_CreatedDate", migrationScript);
    }
    
    [Fact]
    public void MigrationScriptBuilder_WithNullableColumnAndDefaultConstraint_ShouldGenerateSeparately()
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
                ChangeType = GitIntegration.ChangeType.Added,
                NewDefinition = "[Description] NVARCHAR(500) NULL"
            },
            new SchemaChange
            {
                ObjectType = "Constraint",
                Schema = "dbo",
                TableName = "Product",
                ObjectName = "DF_Product_Description",
                ChangeType = GitIntegration.ChangeType.Added,
                NewDefinition = "ALTER TABLE [dbo].[Product] ADD CONSTRAINT [DF_Product_Description] DEFAULT ('No description') FOR [Description]"
            }
        };
        
        var scriptBuilder = new MigrationScriptBuilder();
        
        // Act
        var migrationScript = scriptBuilder.BuildMigration(changes, "TestDB", "test-user");
        
        // Assert
        // Verify that nullable column is added without DEFAULT
        Assert.Contains("ALTER TABLE [dbo].[Product] ADD [Description] NVARCHAR(500) NULL;", migrationScript);
        
        // Verify that the DEFAULT constraint is generated separately
        Assert.Contains("ALTER TABLE [dbo].[Product] ADD CONSTRAINT [DF_Product_Description] DEFAULT ('No description') FOR [Description]", migrationScript);
        
        // Should NOT contain the inline handled message
        Assert.DoesNotContain("DEFAULT constraint handled inline with column creation: DF_Product_Description", migrationScript);
    }
}