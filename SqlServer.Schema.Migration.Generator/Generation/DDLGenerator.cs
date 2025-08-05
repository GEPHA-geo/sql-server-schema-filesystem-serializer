using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace SqlServer.Schema.Migration.Generator.Generation;

public class DDLGenerator
{
    readonly TableDDLGenerator _tableGenerator = new();
    readonly IndexDDLGenerator _indexGenerator = new();
    readonly RenameDDLGenerator _renameGenerator = new();
    
    // Store all changes for checking duplicate DEFAULT constraints
    List<SchemaChange> _allChanges = new List<SchemaChange>();
    
    public void SetAllChanges(List<SchemaChange> changes)
    {
        _allChanges = changes;
        _tableGenerator.SetAllChanges(changes);
    }
    
    public string GenerateDDL(SchemaChange change, List<SchemaChange> allChanges = null)
    {
        // Check if this is a rename operation regardless of object type
        if (change.Properties != null && 
            change.Properties.TryGetValue("IsRename", out var isRename) && 
            isRename == "true")
        {
            return _renameGenerator.GenerateRenameDDL(change);
        }
        
        return change.ObjectType switch
        {
            "Table" => _tableGenerator.GenerateTableDDL(change),
            "Column" => _tableGenerator.GenerateColumnDDL(change),
            "Index" => _indexGenerator.GenerateIndexDDL(change),
            "Constraint" => GenerateConstraintDDL(change),
            "View" => GenerateViewDDL(change),
            "StoredProcedure" => GenerateStoredProcedureDDL(change),
            "Function" => GenerateFunctionDDL(change),
            "ExtendedProperty" => GenerateExtendedPropertyDDL(change),
            "Trigger" => GenerateTriggerDDL(change),
            "Rename" => _renameGenerator.GenerateRenameDDL(change),
            _ => $"-- Unsupported object type: {change.ObjectType}"
        };
    }
    
    public string GenerateBatchSeparator(SchemaChange currentChange, SchemaChange nextChange = null)
    {
        // Most DDL statements need GO separator
        if (RequiresBatchSeparator(currentChange.ObjectType))
        {
            return "GO\n";
        }
        
        return "";
    }
    
    bool RequiresBatchSeparator(string objectType) =>
        objectType is "Table" or "View" or "StoredProcedure" or "Function" or "Trigger" or "Index";
    
    bool ShouldSkipDefaultConstraint(SchemaChange change)
    {
        if (change.ObjectType != "Constraint" || !IsDefaultConstraint(change))
            return false;
            
        // Extract column name from the constraint definition
        var columnName = ExtractColumnNameFromConstraint(change.NewDefinition);
        if (string.IsNullOrEmpty(columnName))
            return false;
            
        // Check if this table has a NOT NULL column being added with a DEFAULT constraint
        var columnChanges = _allChanges.Where(c => 
            c.ObjectType == "Column" && 
            c.Schema == change.Schema && 
            c.TableName == change.TableName &&
            c.ColumnName == columnName &&
            c.ChangeType == ChangeType.Added).ToList();
            
        foreach (var columnChange in columnChanges)
        {
            // Check if the column is NOT NULL
            if (columnChange.NewDefinition.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase))
            {
                return true; // Skip this constraint as it should be handled inline with column creation
            }
        }
        
        return false;
    }
    
    string ExtractColumnNameFromConstraint(string constraintDefinition)
    {
        // Match pattern: ALTER TABLE ... ADD CONSTRAINT ... DEFAULT ... FOR [ColumnName]
        var match = Regex.Match(constraintDefinition, @"FOR\s+\[([^\]]+)\]", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        
        return null;
    }
    
    string GenerateConstraintDDL(SchemaChange change)
    {
        // Skip DEFAULT constraints that are already handled inline with NOT NULL columns
        if (ShouldSkipDefaultConstraint(change))
        {
            return $"-- DEFAULT constraint handled inline with column creation: {change.ObjectName}";
        }
        
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                if (IsDefaultConstraint(change))
                {
                    return GenerateDefaultConstraintWithDropExisting(change);
                }
                return change.NewDefinition;
                
            case ChangeType.Deleted:
                // Extract constraint name from the definition
                var constraintName = ExtractConstraintName(change.OldDefinition ?? change.ObjectName);
                if (!string.IsNullOrEmpty(constraintName))
                {
                    return $"ALTER TABLE [{change.Schema}].[{change.TableName}] DROP CONSTRAINT [{constraintName}];";
                }
                // If we have ObjectName, use it directly
                if (!string.IsNullOrEmpty(change.ObjectName))
                {
                    return $"ALTER TABLE [{change.Schema}].[{change.TableName}] DROP CONSTRAINT [{change.ObjectName}];";
                }
                return $"-- Could not extract constraint name from: {change.OldDefinition}";
                
            case ChangeType.Modified:
                // For constraints, we typically need to drop and recreate
                var oldConstraintName = ExtractConstraintName(change.OldDefinition);
                if (!string.IsNullOrEmpty(oldConstraintName))
                {
                    return $"ALTER TABLE [{change.Schema}].[{change.TableName}] DROP CONSTRAINT [{oldConstraintName}];\nGO\n\n{change.NewDefinition}";
                }
                return $"-- Could not extract constraint name for modification\n{change.NewDefinition}";
                
            default:
                return $"-- Unknown change type for constraint: {change.ObjectName}";
        }
    }
    
    bool IsDefaultConstraint(SchemaChange change) => 
        change.NewDefinition?.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase) == true &&
        !change.NewDefinition.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) &&
        !change.NewDefinition.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase);
    
    string GenerateDefaultConstraintWithDropExisting(SchemaChange change)
    {
        // Extract column name from the constraint definition
        var columnName = ExtractColumnNameFromConstraint(change.NewDefinition);
        if (string.IsNullOrEmpty(columnName))
        {
            // If we can't extract the column name, just return the original
            return change.NewDefinition;
        }
        
        // Generate a script that drops any existing default constraint on the column first
        var sb = new StringBuilder();
        sb.AppendLine($"-- Drop any existing DEFAULT constraint on column [{columnName}]");
        sb.AppendLine($"DECLARE @ConstraintName nvarchar(200)");
        sb.AppendLine($"SELECT @ConstraintName = dc.name");
        sb.AppendLine($"FROM sys.default_constraints dc");
        sb.AppendLine($"INNER JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id");
        sb.AppendLine($"WHERE dc.parent_object_id = OBJECT_ID(N'[{change.Schema}].[{change.TableName}]')");
        sb.AppendLine($"  AND c.name = '{columnName}'");
        sb.AppendLine();
        sb.AppendLine($"IF @ConstraintName IS NOT NULL");
        sb.AppendLine($"    EXEC('ALTER TABLE [{change.Schema}].[{change.TableName}] DROP CONSTRAINT [' + @ConstraintName + ']')");
        sb.AppendLine($"GO");
        sb.AppendLine();
        sb.AppendLine($"-- Add new DEFAULT constraint");
        sb.AppendLine(change.NewDefinition);
        
        return sb.ToString();
    }

    string GenerateViewDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                return change.NewDefinition;
                
            case ChangeType.Deleted:
                return $"DROP VIEW [{change.Schema}].[{change.ObjectName}];";
                
            case ChangeType.Modified:
                return $"DROP VIEW [{change.Schema}].[{change.ObjectName}];\nGO\n\n{change.NewDefinition}";
                
            default:
                return $"-- Unknown change type for view: {change.ObjectName}";
        }
    }

    string GenerateStoredProcedureDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                return change.NewDefinition;
                
            case ChangeType.Deleted:
                return $"DROP PROCEDURE [{change.Schema}].[{change.ObjectName}];";
                
            case ChangeType.Modified:
                return $"DROP PROCEDURE [{change.Schema}].[{change.ObjectName}];\nGO\n\n{change.NewDefinition}";
                
            default:
                return $"-- Unknown change type for stored procedure: {change.ObjectName}";
        }
    }

    string GenerateFunctionDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                return change.NewDefinition;
                
            case ChangeType.Deleted:
                return $"DROP FUNCTION [{change.Schema}].[{change.ObjectName}];";
                
            case ChangeType.Modified:
                return $"DROP FUNCTION [{change.Schema}].[{change.ObjectName}];\nGO\n\n{change.NewDefinition}";
                
            default:
                return $"-- Unknown change type for function: {change.ObjectName}";
        }
    }
    
    string GenerateExtendedPropertyDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                // Extended properties are created using their full definition (sp_addextendedproperty)
                return change.NewDefinition;
                
            case ChangeType.Deleted:
                // Parse the old definition to extract property details and generate drop statement
                return GenerateDropExtendedProperty(change.OldDefinition ?? change.NewDefinition);
                
            case ChangeType.Modified:
                // Parse the new definition to extract property details and generate update statement
                return GenerateUpdateExtendedProperty(change.NewDefinition);
                
            default:
                return $"-- Unknown change type for extended property: {change.ObjectName}";
        }
    }
    
    string GenerateTriggerDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                return change.NewDefinition;
                
            case ChangeType.Deleted:
                return $"DROP TRIGGER [{change.Schema}].[{change.ObjectName}];";
                
            case ChangeType.Modified:
                return $"DROP TRIGGER [{change.Schema}].[{change.ObjectName}];\nGO\n\n{change.NewDefinition}";
                
            default:
                return $"-- Unknown change type for trigger: {change.ObjectName}";
        }
    }
    
    string ExtractConstraintName(string constraintDefinition)
    {
        if (string.IsNullOrEmpty(constraintDefinition))
            return null;
            
        // If it's already just a constraint name (no SQL), return it
        if (!constraintDefinition.Contains(" ") && !constraintDefinition.Contains("["))
            return constraintDefinition;
            
        // Try different patterns to extract constraint name
        var patterns = new[]
        {
            @"ADD\s+CONSTRAINT\s+\[([^\]]+)\]",
            @"ADD\s+CONSTRAINT\s+([^\s]+)",
            @"CONSTRAINT\s+\[([^\]]+)\]",
            @"CONSTRAINT\s+([^\s]+)"
        };
        
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(constraintDefinition, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        
        return null;
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
        sb.AppendLine("-- Update extended property (drop and recreate if update fails)");
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