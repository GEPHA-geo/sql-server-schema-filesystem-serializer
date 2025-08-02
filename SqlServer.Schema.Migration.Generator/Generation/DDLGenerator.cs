using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Generation;

public class DDLGenerator
{
    readonly TableDDLGenerator _tableGenerator = new();
    readonly IndexDDLGenerator _indexGenerator = new();
    readonly RenameDDLGenerator _renameGenerator = new();
    
    // Store all changes for cross-referencing
    private List<SchemaChange>? _allChanges;
    
    public void SetAllChanges(List<SchemaChange> allChanges)
    {
        _allChanges = allChanges;
        _tableGenerator.SetAllChanges(allChanges);
    }

    public string GenerateDDL(SchemaChange change)
    {
        // Check if this is a rename operation
        if (change.Properties.TryGetValue("IsRename", out var isRename) && isRename == "true")
        {
            return _renameGenerator.GenerateRenameDDL(change);
        }
        
        // Skip DEFAULT constraints that will be handled inline with column creation
        if (ShouldSkipDefaultConstraint(change))
        {
            return $"-- DEFAULT constraint handled inline with column creation: {change.ObjectName}";
        }
        
        return change.ObjectType switch
        {
            "Table" => _tableGenerator.GenerateTableDDL(change),
            "Column" => _tableGenerator.GenerateColumnDDL(change),
            "Index" => _indexGenerator.GenerateIndexDDL(change),
            "Constraint" => GenerateConstraintDDL(change),
            "Trigger" => GenerateTriggerDDL(change),
            "View" => GenerateViewDDL(change),
            "StoredProcedure" => GenerateStoredProcedureDDL(change),
            "Function" => GenerateFunctionDDL(change),
            "ExtendedProperty" => GenerateExtendedPropertyDDL(change),
            _ => $"-- Unsupported object type: {change.ObjectType}"
        };
    }
    
    bool ShouldSkipDefaultConstraint(SchemaChange change)
    {
        if (change.ObjectType != "Constraint" || change.ChangeType != ChangeType.Added)
            return false;
            
        // Check if this is a DEFAULT constraint
        if (!change.ObjectName.StartsWith("DF_") && 
            !change.NewDefinition.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase))
            return false;
            
        // Check if there's a corresponding column being added
        if (_allChanges != null)
        {
            // Extract column name from the constraint definition
            var columnName = ExtractColumnNameFromConstraint(change.NewDefinition);
            if (!string.IsNullOrEmpty(columnName))
            {
                // Check if this column is being added with NOT NULL
                var columnChange = _allChanges.FirstOrDefault(c =>
                    c.ObjectType == "Column" &&
                    c.ChangeType == ChangeType.Added &&
                    c.TableName == change.TableName &&
                    c.Schema == change.Schema &&
                    c.ColumnName == columnName &&
                    c.NewDefinition.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase));
                    
                if (columnChange != null)
                {
                    // This DEFAULT constraint will be handled inline with the column
                    return true;
                }
            }
        }
        
        return false;
    }
    
    string ExtractColumnNameFromConstraint(string constraintDef)
    {
        // Extract column name from DEFAULT constraint
        // Example: ALTER TABLE [dbo].[Table] ADD CONSTRAINT [DF_Table_Column] DEFAULT ((0)) FOR [Column]
        var match = System.Text.RegularExpressions.Regex.Match(
            constraintDef,
            @"FOR\s+\[([^\]]+)\]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
        return match.Success ? match.Groups[1].Value : null;
    }

    string GenerateConstraintDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                // For DEFAULT constraints, drop any existing default on the column first
                if (IsDefaultConstraint(change))
                {
                    return GenerateDefaultConstraintWithDropExisting(change);
                }
                return change.NewDefinition;
                
            case ChangeType.Deleted:
                // Need to determine constraint type from the definition or name
                if (change.ObjectName.StartsWith("FK_"))
                    return $"ALTER TABLE [{change.Schema}].[{change.TableName}] DROP CONSTRAINT [{change.ObjectName}];";
                else if (change.ObjectName.StartsWith("PK_"))
                    return $"ALTER TABLE [{change.Schema}].[{change.TableName}] DROP CONSTRAINT [{change.ObjectName}];";
                else if (change.ObjectName.StartsWith("DF_"))
                    return $"ALTER TABLE [{change.Schema}].[{change.TableName}] DROP CONSTRAINT [{change.ObjectName}];";
                else
                    return $"-- Drop constraint: {change.ObjectName}";
                    
            case ChangeType.Modified:
                // Drop and recreate
                var dropDDL = GenerateConstraintDDL(new SchemaChange 
                { 
                    ObjectType = change.ObjectType,
                    Schema = change.Schema,
                    TableName = change.TableName,
                    ObjectName = change.ObjectName,
                    ChangeType = ChangeType.Deleted,
                    OldDefinition = change.OldDefinition
                });
                
                return $"{dropDDL}\nGO\n\n{change.NewDefinition}";
                
            default:
                return $"-- Unknown change type for constraint: {change.ObjectName}";
        }
    }

    bool IsDefaultConstraint(SchemaChange change)
    {
        return change.ObjectName.StartsWith("DF_") || 
               change.NewDefinition.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase);
    }
    
    string GenerateDefaultConstraintWithDropExisting(SchemaChange change)
    {
        // Extract column name from the constraint definition
        var columnName = ExtractColumnNameFromConstraint(change.NewDefinition);
        if (string.IsNullOrEmpty(columnName))
        {
            // If we can't extract column name, just return the original definition
            return change.NewDefinition;
        }
        
        var sb = new System.Text.StringBuilder();
        
        // Generate SQL to drop any existing DEFAULT constraint on this column
        sb.AppendLine($"-- Drop any existing DEFAULT constraint on column [{columnName}]");
        sb.AppendLine($"DECLARE @ConstraintName nvarchar(200)");
        sb.AppendLine($"SELECT @ConstraintName = dc.name");
        sb.AppendLine($"FROM sys.default_constraints dc");
        sb.AppendLine($"INNER JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id");
        sb.AppendLine($"WHERE dc.parent_object_id = OBJECT_ID(N'[{change.Schema}].[{change.TableName}]')");
        sb.AppendLine($"AND c.name = '{columnName}'");
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
                // Extract property details from the definition to create DROP statement
                // For now, return the full definition with a comment
                return $"-- TODO: Generate DROP extended property statement\n-- {change.NewDefinition}";
                
            case ChangeType.Modified:
                // Drop and recreate the extended property
                return $"-- TODO: Generate UPDATE extended property statement\n-- {change.NewDefinition}";
                
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
}