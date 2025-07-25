using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Generation;

public class TableDDLGenerator
{
    public string GenerateTableDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                return change.NewDefinition;
                
            case ChangeType.Deleted:
                return $"DROP TABLE [{change.Schema}].[{change.ObjectName}];";
                
            case ChangeType.Modified:
                // For table modifications, we should not drop and recreate
                // This should be handled by column-level changes
                return "-- Table structure modified - see column changes below";
                
            default:
                return $"-- Unknown change type for table: {change.ObjectName}";
        }
    }
    
    public string GenerateColumnDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                return $"ALTER TABLE [{change.Schema}].[{change.TableName}] ADD {change.NewDefinition};";
                
            case ChangeType.Deleted:
                return $"ALTER TABLE [{change.Schema}].[{change.TableName}] DROP COLUMN [{change.ColumnName}];";
                
            case ChangeType.Modified:
                // Extract just the column definition part
                var columnDef = ExtractColumnDefinition(change.NewDefinition);
                return $"ALTER TABLE [{change.Schema}].[{change.TableName}] ALTER COLUMN {columnDef};";
                
            default:
                return $"-- Unknown change type for column: {change.ColumnName}";
        }
    }

    string ExtractColumnDefinition(string fullDefinition)
    {
        // Remove common constraints that can't be part of ALTER COLUMN
        var cleaned = fullDefinition
            .Replace("IDENTITY", "")
            .Replace("NOT FOR REPLICATION", "")
            .Trim();
            
        // Extract column name and core definition
        var parts = cleaned.Split(' ', 2);
        if (parts.Length >= 2)
        {
            var columnName = parts[0].Trim('[', ']');
            var dataType = parts[1];
            
            // Determine NULL/NOT NULL
            if (dataType.Contains("NOT NULL"))
            {
                dataType = dataType.Replace("NOT NULL", "").Trim() + " NOT NULL";
            }
            else if (dataType.Contains("NULL"))
            {
                dataType = dataType.Replace("NULL", "").Trim() + " NULL";
            }
            
            return $"[{columnName}] {dataType}";
        }
        
        return fullDefinition;
    }
}