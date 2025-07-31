using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Generation;

// Generates reverse DDL statements that undo the effects of forward DDL operations
// Used to create manual rollback scripts for migrations
public class ReverseDDLGenerator
{
    readonly TableDDLGenerator _tableGenerator = new();
    readonly IndexDDLGenerator _indexGenerator = new();
    readonly RenameDDLGenerator _renameGenerator = new();

    public string GenerateReverseDDL(SchemaChange change)
    {
        // Handle rename operations specially - reverse the direction
        if (change.Properties.TryGetValue("IsRename", out var isRename) && isRename == "true")
        {
            return GenerateReverseRenameDDL(change);
        }
        
        // For other operations, generate the inverse
        return change.ObjectType switch
        {
            "Table" => GenerateReverseTableDDL(change),
            "Column" => GenerateReverseColumnDDL(change),
            "Index" => GenerateReverseIndexDDL(change),
            "Constraint" => GenerateReverseConstraintDDL(change),
            "Trigger" => GenerateReverseTriggerDDL(change),
            "View" => GenerateReverseViewDDL(change),
            "StoredProcedure" => GenerateReverseStoredProcedureDDL(change),
            "Function" => GenerateReverseFunctionDDL(change),
            "ExtendedProperty" => GenerateReverseExtendedPropertyDDL(change),
            _ => $"-- Cannot generate reverse for unsupported object type: {change.ObjectType}"
        };
    }

    string GenerateReverseRenameDDL(SchemaChange change)
    {
        // For renames, swap old and new names
        if (!change.Properties.TryGetValue("OldName", out var oldName) || string.IsNullOrEmpty(oldName))
        {
            return $"-- Cannot reverse rename: missing original name for {change.ObjectName}";
        }

        // Create a reversed change object
        var reverseChange = new SchemaChange
        {
            ObjectType = change.ObjectType,
            Schema = change.Schema,
            TableName = change.TableName,
            ObjectName = change.ObjectType == "Column" ? oldName : oldName,
            ColumnName = change.ObjectType == "Column" ? oldName : null,
            ChangeType = change.ChangeType,
            Properties = new Dictionary<string, string>(change.Properties)
        };
        
        // Swap the names in properties
        if (change.ObjectType == "Column")
        {
            // For columns, swap: new becomes old, old becomes new
            reverseChange.ColumnName = oldName; // The old name is what we want to restore
            reverseChange.Properties["OldName"] = change.ColumnName ?? change.ObjectName; // Current name becomes the old name to reverse from
        }
        else
        {
            // For other objects, the current name becomes the old name
            reverseChange.ObjectName = oldName; // The old name is what we want to restore
            reverseChange.Properties["OldName"] = change.ObjectName; // Current name becomes the old name to reverse from
        }

        return _renameGenerator.GenerateRenameDDL(reverseChange);
    }

    string GenerateReverseTableDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                // Reverse of CREATE is DROP
                return $"DROP TABLE [{change.Schema}].[{change.ObjectName}];";
                
            case ChangeType.Deleted:
                // Reverse of DROP is CREATE - use the old definition
                return change.OldDefinition;
                
            case ChangeType.Modified:
                // Table modifications are handled by column changes
                return "-- Table modification reversal handled by column changes";
                
            default:
                return $"-- Cannot reverse unknown change type for table: {change.ObjectName}";
        }
    }

    string GenerateReverseColumnDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                // Reverse of ADD COLUMN is DROP COLUMN
                return $"ALTER TABLE [{change.Schema}].[{change.TableName}] DROP COLUMN [{change.ColumnName}];";
                
            case ChangeType.Deleted:
                // Reverse of DROP COLUMN is ADD COLUMN - use old definition
                return $"ALTER TABLE [{change.Schema}].[{change.TableName}] ADD {change.OldDefinition};";
                
            case ChangeType.Modified:
                // Reverse of ALTER COLUMN - use old definition
                var oldColumnDef = ExtractColumnDefinition(change.OldDefinition);
                return $"ALTER TABLE [{change.Schema}].[{change.TableName}] ALTER COLUMN {oldColumnDef};";
                
            default:
                return $"-- Cannot reverse unknown change type for column: {change.ColumnName}";
        }
    }

    string GenerateReverseIndexDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                // Reverse of CREATE INDEX is DROP INDEX
                return $"DROP INDEX [{change.ObjectName}] ON [{change.Schema}].[{change.TableName}];";
                
            case ChangeType.Deleted:
                // Reverse of DROP INDEX is CREATE INDEX - use old definition
                return change.OldDefinition;
                
            case ChangeType.Modified:
                // Reverse of index modification - recreate old version
                return change.OldDefinition;
                
            default:
                return $"-- Cannot reverse unknown change type for index: {change.ObjectName}";
        }
    }

    string GenerateReverseConstraintDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                // Reverse of ADD CONSTRAINT is DROP CONSTRAINT
                return $"ALTER TABLE [{change.Schema}].[{change.TableName}] DROP CONSTRAINT [{change.ObjectName}];";
                
            case ChangeType.Deleted:
                // Reverse of DROP CONSTRAINT is ADD CONSTRAINT - use old definition
                return change.OldDefinition;
                
            case ChangeType.Modified:
                // Reverse of constraint modification - recreate old version
                return change.OldDefinition;
                
            default:
                return $"-- Cannot reverse unknown change type for constraint: {change.ObjectName}";
        }
    }

    string GenerateReverseViewDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                // Reverse of CREATE VIEW is DROP VIEW
                return $"DROP VIEW [{change.Schema}].[{change.ObjectName}];";
                
            case ChangeType.Deleted:
                // Reverse of DROP VIEW is CREATE VIEW - use old definition
                return change.OldDefinition;
                
            case ChangeType.Modified:
                // Reverse of view modification - recreate old version
                return $"DROP VIEW [{change.Schema}].[{change.ObjectName}];\nGO\n\n{change.OldDefinition}";
                
            default:
                return $"-- Cannot reverse unknown change type for view: {change.ObjectName}";
        }
    }

    string GenerateReverseStoredProcedureDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                // Reverse of CREATE PROCEDURE is DROP PROCEDURE
                return $"DROP PROCEDURE [{change.Schema}].[{change.ObjectName}];";
                
            case ChangeType.Deleted:
                // Reverse of DROP PROCEDURE is CREATE PROCEDURE - use old definition
                return change.OldDefinition;
                
            case ChangeType.Modified:
                // Reverse of procedure modification - recreate old version
                return $"DROP PROCEDURE [{change.Schema}].[{change.ObjectName}];\nGO\n\n{change.OldDefinition}";
                
            default:
                return $"-- Cannot reverse unknown change type for stored procedure: {change.ObjectName}";
        }
    }

    string GenerateReverseFunctionDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                // Reverse of CREATE FUNCTION is DROP FUNCTION
                return $"DROP FUNCTION [{change.Schema}].[{change.ObjectName}];";
                
            case ChangeType.Deleted:
                // Reverse of DROP FUNCTION is CREATE FUNCTION - use old definition
                return change.OldDefinition;
                
            case ChangeType.Modified:
                // Reverse of function modification - recreate old version
                return $"DROP FUNCTION [{change.Schema}].[{change.ObjectName}];\nGO\n\n{change.OldDefinition}";
                
            default:
                return $"-- Cannot reverse unknown change type for function: {change.ObjectName}";
        }
    }

    string GenerateReverseTriggerDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                // Reverse of CREATE TRIGGER is DROP TRIGGER
                return $"DROP TRIGGER [{change.Schema}].[{change.ObjectName}];";
                
            case ChangeType.Deleted:
                // Reverse of DROP TRIGGER is CREATE TRIGGER - use old definition
                return change.OldDefinition;
                
            case ChangeType.Modified:
                // Reverse of trigger modification - recreate old version
                return $"DROP TRIGGER [{change.Schema}].[{change.ObjectName}];\nGO\n\n{change.OldDefinition}";
                
            default:
                return $"-- Cannot reverse unknown change type for trigger: {change.ObjectName}";
        }
    }
    
    string GenerateReverseExtendedPropertyDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                // TODO: Parse the sp_addextendedproperty call to generate sp_dropextendedproperty
                return $"-- TODO: Generate sp_dropextendedproperty for added property\n-- Original: {change.NewDefinition}";
                
            case ChangeType.Deleted:
                // Reverse of DROP is ADD - use old definition
                return change.OldDefinition;
                
            case ChangeType.Modified:
                // TODO: Generate sp_updateextendedproperty to restore old value
                return $"-- TODO: Generate sp_updateextendedproperty to restore old value\n-- Original: {change.OldDefinition}";
                
            default:
                return $"-- Cannot reverse unknown change type for extended property: {change.ObjectName}";
        }
    }

    // Helper method to extract column definition - copied from TableDDLGenerator
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