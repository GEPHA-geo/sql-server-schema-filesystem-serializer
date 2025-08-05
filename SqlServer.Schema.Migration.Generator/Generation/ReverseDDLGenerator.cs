using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

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
                // Reverse of ADD is DROP - parse the definition to generate drop statement
                return GenerateDropExtendedProperty(change.NewDefinition);
                
            case ChangeType.Deleted:
                // Reverse of DROP is ADD - use old definition
                return change.OldDefinition;
                
            case ChangeType.Modified:
                // Reverse of UPDATE is UPDATE with old value - parse old definition to generate update
                return GenerateUpdateExtendedProperty(change.OldDefinition);
                
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

    // Helper methods for parsing extended properties
    string GenerateDropExtendedProperty(string propertyDefinition)
    {
        var details = ParseExtendedPropertyCall(propertyDefinition);
        if (details == null)
            return $"-- Could not parse extended property definition: {propertyDefinition}";
            
        var sb = new StringBuilder();
        sb.AppendLine("-- Drop extended property");
        sb.Append("EXEC sys.sp_dropextendedproperty");
        sb.Append($" @name = N'{details.Value.Name}'");
        
        if (!string.IsNullOrEmpty(details.Value.Level0Type))
        {
            sb.Append($", @level0type = N'{details.Value.Level0Type}'");
            sb.Append($", @level0name = N'{details.Value.Level0Name}'");
        }
        
        if (!string.IsNullOrEmpty(details.Value.Level1Type))
        {
            sb.Append($", @level1type = N'{details.Value.Level1Type}'");
            sb.Append($", @level1name = N'{details.Value.Level1Name}'");
        }
        
        if (!string.IsNullOrEmpty(details.Value.Level2Type))
        {
            sb.Append($", @level2type = N'{details.Value.Level2Type}'");
            sb.Append($", @level2name = N'{details.Value.Level2Name}'");
        }
        
        sb.AppendLine(";");
        return sb.ToString();
    }
    
    string GenerateUpdateExtendedProperty(string propertyDefinition)
    {
        var details = ParseExtendedPropertyCall(propertyDefinition);
        if (details == null)
            return $"-- Could not parse extended property definition: {propertyDefinition}";
            
        // Replace sp_addextendedproperty with sp_updateextendedproperty
        var updateDefinition = Regex.Replace(propertyDefinition, 
            @"sp_addextendedproperty", 
            "sp_updateextendedproperty", 
            RegexOptions.IgnoreCase);
            
        var sb = new StringBuilder();
        sb.AppendLine("-- Update extended property to restore old value");
        sb.AppendLine("BEGIN TRY");
        sb.AppendLine($"    {updateDefinition}");
        sb.AppendLine("END TRY");
        sb.AppendLine("BEGIN CATCH");
        sb.AppendLine("    IF ERROR_NUMBER() = 15217 -- Property does not exist");
        sb.AppendLine("    BEGIN");
        sb.AppendLine($"        {propertyDefinition}");
        sb.AppendLine("    END");
        sb.AppendLine("    ELSE");
        sb.AppendLine("    BEGIN");
        sb.AppendLine("        THROW;");
        sb.AppendLine("    END");
        sb.AppendLine("END CATCH");
        
        return sb.ToString();
    }
    
    (string Name, string Value, string Level0Type, string Level0Name, string Level1Type, string Level1Name, string Level2Type, string Level2Name)? ParseExtendedPropertyCall(string definition)
    {
        // Parse the extended property call to extract all parameters
        var nameMatch = Regex.Match(definition, @"@name\s*=\s*N?'([^']+)'", RegexOptions.IgnoreCase);
        var valueMatch = Regex.Match(definition, @"@value\s*=\s*N?'([^']+)'", RegexOptions.IgnoreCase);
        
        if (!nameMatch.Success)
            return null;
            
        var result = (
            Name: nameMatch.Groups[1].Value,
            Value: valueMatch.Success ? valueMatch.Groups[1].Value : "",
            Level0Type: ExtractParameter(definition, "level0type"),
            Level0Name: ExtractParameter(definition, "level0name"),
            Level1Type: ExtractParameter(definition, "level1type"),
            Level1Name: ExtractParameter(definition, "level1name"),
            Level2Type: ExtractParameter(definition, "level2type"),
            Level2Name: ExtractParameter(definition, "level2name")
        );
        
        return result;
    }
    
    string ExtractParameter(string definition, string parameterName)
    {
        var match = Regex.Match(definition, $@"@{parameterName}\s*=\s*N?'([^']+)'", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "";
    }
}