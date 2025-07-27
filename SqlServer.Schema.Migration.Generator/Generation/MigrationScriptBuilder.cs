using System.Text;
using SqlServer.Schema.Migration.Generator.Parsing;

namespace SqlServer.Schema.Migration.Generator.Generation;

public class MigrationScriptBuilder
{
    readonly DDLGenerator _ddlGenerator = new();
    readonly DependencyResolver _dependencyResolver = new();

    public string BuildMigration(List<SchemaChange> changes, string databaseName)
    {
        var sb = new StringBuilder();
        
        // Add migration header
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var migrationId = $"{timestamp}_{GenerateMigrationName(changes)}";
        
        sb.AppendLine($"-- Migration: {migrationId}.sql");
        sb.AppendLine($"-- MigrationId: {migrationId}");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"-- Database: {databaseName}");
        sb.AppendLine($"-- Changes: {changes.Count} schema modifications");
        sb.AppendLine();
        sb.AppendLine("SET XACT_ABORT ON;");
        sb.AppendLine("BEGIN TRANSACTION;");
        sb.AppendLine();
        
        // Check if migration already applied
        sb.AppendLine("-- Check if migration already applied");
        sb.AppendLine($"IF EXISTS (SELECT 1 FROM [dbo].[DatabaseMigrationHistory] WHERE [MigrationId] = '{migrationId}')");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    PRINT 'Migration already applied. Skipping.';");
        sb.AppendLine("    RETURN;");
        sb.AppendLine("END");
        sb.AppendLine();
        
        try
        {
            // Order changes by dependencies
            var orderedChanges = _dependencyResolver.OrderChanges(changes);
            
            // Group changes by type for better organization
            // Separate rename operations from other modifications
            var renameOperations = orderedChanges.Where(c => 
                c.Properties.TryGetValue("IsRename", out var isRename) && isRename == "true").ToList();
            var dropOperations = orderedChanges.Where(c => 
                c.ChangeType == GitIntegration.ChangeType.Deleted && 
                (!c.Properties.TryGetValue("IsRename", out var isRename) || isRename != "true")).ToList();
            var createOperations = orderedChanges.Where(c => 
                c.ChangeType == GitIntegration.ChangeType.Added && 
                (!c.Properties.TryGetValue("IsRename", out var isRename2) || isRename2 != "true")).ToList();
            var alterOperations = orderedChanges.Where(c => 
                c.ChangeType == GitIntegration.ChangeType.Modified && 
                (!c.Properties.TryGetValue("IsRename", out var isRename3) || isRename3 != "true")).ToList();
            
            // Process renames first (they are the safest operations)
            if (renameOperations.Any())
            {
                sb.AppendLine("-- Rename operations");
                foreach (var change in renameOperations)
                {
                    sb.AppendLine(_ddlGenerator.GenerateDDL(change));
                    sb.AppendLine("GO");
                    sb.AppendLine();
                }
            }
            
            // Process drops second (in reverse dependency order)
            if (dropOperations.Any())
            {
                sb.AppendLine("-- Drop operations");
                foreach (var change in dropOperations)
                {
                    sb.AppendLine(_ddlGenerator.GenerateDDL(change));
                    sb.AppendLine("GO");
                    sb.AppendLine();
                }
            }
            
            // Process modifications
            if (alterOperations.Any())
            {
                sb.AppendLine("-- Modification operations");
                foreach (var change in alterOperations)
                {
                    sb.AppendLine(_ddlGenerator.GenerateDDL(change));
                    sb.AppendLine("GO");
                    sb.AppendLine();
                }
            }
            
            // Process creates last
            if (createOperations.Any())
            {
                sb.AppendLine("-- Create operations");
                foreach (var change in createOperations)
                {
                    Console.WriteLine($"Generating CREATE for: {change.ObjectType} {change.ObjectName}");
                    sb.AppendLine(_ddlGenerator.GenerateDDL(change));
                    sb.AppendLine("GO");
                    sb.AppendLine();
                }
            }
            
            // Record migration in history table BEFORE committing
            sb.AppendLine("-- Record this migration as applied");
            sb.AppendLine($"INSERT INTO [dbo].[DatabaseMigrationHistory] ([MigrationId], [Filename], [Checksum], [Status])");
            sb.AppendLine($"VALUES ('{migrationId}', '{migrationId}.sql', HASHBYTES('SHA2_256', CAST('{migrationId}' AS NVARCHAR(MAX))), 'Success');");
            sb.AppendLine("GO");
            sb.AppendLine();
            
            sb.AppendLine("COMMIT TRANSACTION;");
            sb.AppendLine("PRINT 'Migration applied successfully.'");
        }
        catch
        {
            sb.AppendLine("ROLLBACK TRANSACTION;");
            throw;
        }
        
        return sb.ToString();
    }

    string GenerateMigrationName(List<SchemaChange> changes)
    {
        var summary = new List<string>();
        
        var tables = changes.Where(c => c.ObjectType == "Table").Count();
        var columns = changes.Where(c => c.ObjectType == "Column").Count();
        var indexes = changes.Where(c => c.ObjectType == "Index").Count();
        var others = changes.Count - tables - columns - indexes;
        
        if (tables > 0) summary.Add($"{tables}tables");
        if (columns > 0) summary.Add($"{columns}columns");
        if (indexes > 0) summary.Add($"{indexes}indexes");
        if (others > 0) summary.Add($"{others}other");
        
        return string.Join("_", summary);
    }
}