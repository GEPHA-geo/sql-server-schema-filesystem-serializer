using SqlServer.Schema.Migration.Generator.Parsing;

namespace SqlServer.Schema.Migration.Generator.Generation;

public class RenameDDLGenerator
{
    public string GenerateRenameDDL(SchemaChange change)
    {
        // Check if this is a rename operation
        if (!change.Properties.TryGetValue("IsRename", out var isRename) || isRename != "true")
        {
            return $"-- Not a rename operation for {change.ObjectName}";
        }
        
        if (!change.Properties.TryGetValue("OldName", out var oldName) || string.IsNullOrEmpty(oldName))
        {
            return $"-- Missing old name for rename operation on {change.ObjectName}";
        }
        
        if (!change.Properties.TryGetValue("RenameType", out var renameType))
        {
            renameType = change.ObjectType; // Fallback to object type
        }
        
        return renameType switch
        {
            "Column" => GenerateColumnRename(change, oldName),
            "Index" => GenerateIndexRename(change, oldName),
            "Constraint" => GenerateConstraintRename(change, oldName),
            "Trigger" => GenerateTriggerRename(change, oldName),
            _ => $"-- Unsupported rename type: {renameType}"
        };
    }
    
    string GenerateColumnRename(SchemaChange change, string oldName)
    {
        // SQL Server sp_rename for columns
        // EXEC sp_rename 'schema.table.old_column', 'new_column', 'COLUMN'
        return $"EXEC sp_rename '[{change.Schema}].[{change.TableName}].[{oldName}]', '{change.ColumnName}', 'COLUMN';";
    }
    
    string GenerateIndexRename(SchemaChange change, string oldName)
    {
        // SQL Server sp_rename for indexes
        // EXEC sp_rename 'schema.table.old_index', 'new_index', 'INDEX'
        return $"EXEC sp_rename '[{change.Schema}].[{change.TableName}].[{oldName}]', '{change.ObjectName}', 'INDEX';";
    }
    
    string GenerateConstraintRename(SchemaChange change, string oldName)
    {
        // SQL Server sp_rename for constraints (using OBJECT type)
        // Constraints are schema-scoped objects
        return $"EXEC sp_rename '[{change.Schema}].[{oldName}]', '{change.ObjectName}', 'OBJECT';";
    }
    
    string GenerateTriggerRename(SchemaChange change, string oldName)
    {
        // SQL Server sp_rename for triggers (using OBJECT type)
        // Triggers are schema-scoped objects
        return $"EXEC sp_rename '[{change.Schema}].[{oldName}]', '{change.ObjectName}', 'OBJECT';";
    }
}