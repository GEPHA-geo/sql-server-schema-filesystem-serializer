-- Migration: 00000000_000000_create_migration_history_table.sql
-- MigrationId: 00000000_000000_create_migration_history_table
-- Description: Bootstrap migration - creates the migration tracking table
-- Author: System
-- Date: System Bootstrap

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DatabaseMigrationHistory' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[DatabaseMigrationHistory] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [MigrationId] NVARCHAR(100) NOT NULL UNIQUE,
        [Filename] NVARCHAR(500) NOT NULL,
        [AppliedDate] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [Checksum] NVARCHAR(64) NOT NULL,
        [Status] NVARCHAR(50) NOT NULL,
        [ExecutionTime] INT NULL,
        [ErrorMessage] NVARCHAR(MAX) NULL
    );
    
    -- Self-register this bootstrap migration
    INSERT INTO [dbo].[DatabaseMigrationHistory] 
        ([MigrationId], [Filename], [Checksum], [Status], [ExecutionTime])
    VALUES 
        ('00000000_000000_create_migration_history_table', 
         '00000000_000000_create_migration_history_table.sql',
         'BOOTSTRAP', 
         'Success', 
         0);
END
GO