#!/usr/bin/env dotnet-script
#r "nuget: xunit, 2.4.1"
#load "SqlServer.Schema.Exclusion.Manager.Core/Models/ManifestChange.cs"
#load "SqlServer.Schema.Exclusion.Manager.Core/Services/GitChangeDetector.cs"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// Test the complex migration
var migrationContent = @"-- Migration: 20250810_comprehensive_test.sql
-- Table operations
CREATE TABLE [dbo].[new_table] (id INT PRIMARY KEY);
GO
DROP TABLE [dbo].[old_table];
GO

-- Rename operations
EXEC sp_rename '[dbo].[test_migrations].[bb1]', 'tempof', 'COLUMN';
GO

-- Drop operations
ALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_gsdf];
GO

ALTER TABLE [dbo].[test_migrations] DROP COLUMN [ga];
GO

ALTER TABLE [dbo].[test_migrations] DROP COLUMN [gagdf];
GO

ALTER TABLE [dbo].[test_migrations] DROP COLUMN [gsdf];
GO

-- Modification operations
ALTER TABLE [dbo].[test_migrations] DROP CONSTRAINT [DF_test_migrations_zura];
GO

ALTER TABLE [dbo].[test_migrations]
    ADD CONSTRAINT [DF_test_migrations_zura] DEFAULT (N'iura') FOR [zura];
GO

ALTER TABLE [dbo].[test_migrations] ALTER COLUMN [zura] NVARCHAR (105) NOT NULL;
GO

-- Create operations
ALTER TABLE [dbo].[test_migrations] ADD [gjglksdf] INT NULL;
GO

CREATE NONCLUSTERED INDEX [iTesting2]
    ON [dbo].[test_migrations]([zura] ASC);
GO

-- Drop index (NEW)
DROP INDEX [iOldIndex] ON [dbo].[test_migrations];
GO

-- Extended properties
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'testing - description', 
    @level0type = N'SCHEMA', @level0name = N'dbo', 
    @level1type = N'TABLE', @level1name = N'test_migrations', 
    @level2type = N'COLUMN', @level2name = N'zura';
GO

-- Drop extended property (NEW)
EXECUTE sp_dropextendedproperty @name = N'MS_Description',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'test_migrations',
    @level2type = N'COLUMN', @level2name = N'oldColumn';
GO

-- View operations (NEW)
CREATE VIEW [dbo].[v_test] AS SELECT * FROM test_migrations;
GO
DROP VIEW [dbo].[v_old];
GO

-- Stored procedure operations (NEW)
CREATE PROCEDURE [dbo].[sp_Test] AS BEGIN SELECT 1; END
GO
DROP PROCEDURE [dbo].[sp_Old];
GO

-- Function operations (NEW)
CREATE FUNCTION [dbo].[fn_Test]() RETURNS INT AS BEGIN RETURN 1; END
GO
DROP FUNCTION [dbo].[fn_Old];
GO";

var tempPath = Path.GetTempFileName();
await File.WriteAllTextAsync(tempPath, migrationContent);

var detector = new GitChangeDetector("/tmp");
var changes = await detector.ParseMigrationFileAsync(tempPath, "test-server", "test-db");

Console.WriteLine($"Total changes detected: {changes.Count} (expected 20)");
Console.WriteLine("\nDetected changes:");
foreach (var change in changes.OrderBy(c => c.ObjectType).ThenBy(c => c.Identifier))
{
    Console.WriteLine($"  {change.ObjectType,-20} {change.Identifier,-50} {change.Description}");
}

// Check what might be missing
Console.WriteLine("\nExpected items:");
var expected = new[] {
    "Table: dbo.new_table - added",
    "Table: dbo.old_table - removed",
    "Column rename: dbo.test_migrations.bb1 - renamed to tempof",
    "Constraint: dbo.test_migrations.DF_test_migrations_gsdf - removed",
    "Column: dbo.test_migrations.ga - removed",
    "Column: dbo.test_migrations.gagdf - removed",
    "Column: dbo.test_migrations.gsdf - removed",
    "Column: dbo.test_migrations.zura - modified",
    "Column: dbo.test_migrations.gjglksdf - added",
    "Index: dbo.test_migrations.iTesting2 - added",
    "Index: dbo.test_migrations.iOldIndex - removed",
    "ExtProp: dbo.test_migrations.EP_Column_Description_zura - added",
    "ExtProp: dbo.test_migrations.EP_Column_Description_oldColumn - removed",
    "View: dbo.v_test - added",
    "View: dbo.v_old - removed",
    "Proc: dbo.sp_Test - added",
    "Proc: dbo.sp_Old - removed",
    "Func: dbo.fn_Test - added",
    "Func: dbo.fn_Old - removed",
    "Note: DF_test_migrations_zura should NOT be counted (dropped and recreated)"
};

Console.WriteLine("\nExpected (20 items, DF_test_migrations_zura not counted):");
foreach (var exp in expected)
{
    Console.WriteLine($"  {exp}");
}

File.Delete(tempPath);