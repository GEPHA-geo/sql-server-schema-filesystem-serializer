using System.Text;
using SqlServer.Schema.Migration.Generator.Parsing;

namespace SqlServer.Schema.Migration.Generator.Generation;

// Builds reverse migration scripts that undo forward migrations
// These scripts are for manual recovery and are not tracked in DatabaseMigrationHistory
public class ReverseMigrationBuilder
{
    readonly ReverseDDLGenerator _reverseDdlGenerator = new();
    readonly DependencyResolver _dependencyResolver = new();

    public string BuildReverseMigration(List<SchemaChange> changes, string databaseName, string? actor = null)
    {
        var sb = new StringBuilder();
        
        // Add reverse migration header
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var migrationId = $"{timestamp}_{GenerateMigrationName(changes)}";
        
        sb.AppendLine($"-- REVERSE Migration: {migrationId}_reverse.sql");
        sb.AppendLine($"-- Original MigrationId: {migrationId}");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"-- Database: {databaseName}");
        sb.AppendLine($"-- Actor: {actor ?? "unknown"}");
        sb.AppendLine($"-- Changes: {changes.Count} schema modifications to reverse");
        sb.AppendLine();
        sb.AppendLine("-- WARNING: This is a MANUAL ROLLBACK script");
        sb.AppendLine("-- It is NOT tracked in DatabaseMigrationHistory");
        sb.AppendLine("-- Use with caution and review before execution");
        sb.AppendLine();
        sb.AppendLine("SET XACT_ABORT ON;");
        sb.AppendLine("BEGIN TRANSACTION;");
        sb.AppendLine();
        
        try
        {
            // Order changes by dependencies (forward order)
            var orderedChanges = _dependencyResolver.OrderChanges(changes);
            
            // Reverse the operations order for rollback
            // Creates become drops, drops become creates, etc.
            var reverseOrderedChanges = new List<SchemaChange>();
            
            // Group changes by type but in reverse order for processing
            var renameOperations = orderedChanges.Where(c => 
                c.Properties.TryGetValue("IsRename", out var isRename) && isRename == "true").ToList();
            var createOperations = orderedChanges.Where(c => 
                c.ChangeType == GitIntegration.ChangeType.Added && 
                (!c.Properties.TryGetValue("IsRename", out var isRename2) || isRename2 != "true")).ToList();
            var alterOperations = orderedChanges.Where(c => 
                c.ChangeType == GitIntegration.ChangeType.Modified && 
                (!c.Properties.TryGetValue("IsRename", out var isRename3) || isRename3 != "true")).ToList();
            var dropOperations = orderedChanges.Where(c => 
                c.ChangeType == GitIntegration.ChangeType.Deleted && 
                (!c.Properties.TryGetValue("IsRename", out var isRename) || isRename != "true")).ToList();
            
            // For reverse migration, process in reverse order:
            // 1. Reverse creates (which become drops)
            // 2. Reverse modifications
            // 3. Reverse drops (which become creates)
            // 4. Reverse renames last
            
            if (createOperations.Any())
            {
                sb.AppendLine("-- Reversing CREATE operations (DROP)");
                // Process creates in reverse order for proper dependency handling
                foreach (var change in createOperations.AsEnumerable().Reverse())
                {
                    sb.AppendLine(_reverseDdlGenerator.GenerateReverseDDL(change));
                    sb.AppendLine("GO");
                    sb.AppendLine();
                }
            }
            
            if (alterOperations.Any())
            {
                sb.AppendLine("-- Reversing MODIFICATION operations");
                foreach (var change in alterOperations)
                {
                    sb.AppendLine(_reverseDdlGenerator.GenerateReverseDDL(change));
                    sb.AppendLine("GO");
                    sb.AppendLine();
                }
            }
            
            if (dropOperations.Any())
            {
                sb.AppendLine("-- Reversing DROP operations (CREATE)");
                // Process drops in forward order since they become creates
                foreach (var change in dropOperations)
                {
                    sb.AppendLine(_reverseDdlGenerator.GenerateReverseDDL(change));
                    sb.AppendLine("GO");
                    sb.AppendLine();
                }
            }
            
            if (renameOperations.Any())
            {
                sb.AppendLine("-- Reversing RENAME operations");
                // Process renames in reverse order
                foreach (var change in renameOperations.AsEnumerable().Reverse())
                {
                    sb.AppendLine(_reverseDdlGenerator.GenerateReverseDDL(change));
                    sb.AppendLine("GO");
                    sb.AppendLine();
                }
            }
            
            // Add note about manual history update if needed
            sb.AppendLine("-- If you want to manually track this rollback:");
            sb.AppendLine("-- DELETE FROM [dbo].[DatabaseMigrationHistory]");
            sb.AppendLine($"-- WHERE [MigrationId] = '{migrationId}';");
            sb.AppendLine();
            
            sb.AppendLine("COMMIT TRANSACTION;");
            sb.AppendLine("PRINT 'Reverse migration applied successfully.';");
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