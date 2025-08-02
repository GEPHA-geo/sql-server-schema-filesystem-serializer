using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Generation;

public class TableDDLGenerator
{
    // Store reference to all changes for cross-referencing
    private List<SchemaChange>? _allChanges;
    
    public void SetAllChanges(List<SchemaChange> allChanges)
    {
        _allChanges = allChanges;
    }
    
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
                // Check if this is a NOT NULL column that needs a DEFAULT constraint
                var columnDef = change.NewDefinition;
                if (IsNotNullColumn(columnDef) && _allChanges != null)
                {
                    // Look for a DEFAULT constraint for this column
                    var defaultConstraint = FindDefaultConstraintForColumn(change);
                    if (defaultConstraint != null)
                    {
                        // Extract the DEFAULT value from the constraint definition
                        var defaultValue = ExtractDefaultValue(defaultConstraint.NewDefinition);
                        if (!string.IsNullOrEmpty(defaultValue))
                        {
                            // Inject the DEFAULT clause into the column definition
                            columnDef = InjectDefaultIntoColumnDef(columnDef, defaultValue);
                        }
                    }
                }
                return $"ALTER TABLE [{change.Schema}].[{change.TableName}] ADD {columnDef};";
                
            case ChangeType.Deleted:
                return $"ALTER TABLE [{change.Schema}].[{change.TableName}] DROP COLUMN [{change.ColumnName}];";
                
            case ChangeType.Modified:
                // Extract just the column definition part
                var columnDefMod = ExtractColumnDefinition(change.NewDefinition);
                return $"ALTER TABLE [{change.Schema}].[{change.TableName}] ALTER COLUMN {columnDefMod};";
                
            default:
                return $"-- Unknown change type for column: {change.ColumnName}";
        }
    }
    
    bool IsNotNullColumn(string columnDefinition)
    {
        return columnDefinition.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase);
    }
    
    SchemaChange? FindDefaultConstraintForColumn(SchemaChange columnChange)
    {
        if (_allChanges == null) return null;
        
        // Look for a DEFAULT constraint that references this column
        // Default constraints typically have names like DF_TableName_ColumnName
        var expectedConstraintPattern = $"DF_{columnChange.TableName}_{columnChange.ColumnName}";
        
        return _allChanges.FirstOrDefault(c => 
            c.ObjectType == "Constraint" &&
            c.ChangeType == ChangeType.Added &&
            c.TableName == columnChange.TableName &&
            c.Schema == columnChange.Schema &&
            (c.ObjectName.Contains(columnChange.ColumnName, StringComparison.OrdinalIgnoreCase) ||
             c.NewDefinition.Contains($"[{columnChange.ColumnName}]", StringComparison.OrdinalIgnoreCase)) &&
            c.NewDefinition.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase));
    }
    
    string? ExtractDefaultValue(string constraintDefinition)
    {
        // Extract DEFAULT value from constraint definition
        // Example: ALTER TABLE [dbo].[Table] ADD CONSTRAINT [DF_Table_Column] DEFAULT ((0)) FOR [Column]
        // The regex needs to handle nested parentheses like ((0)) or (GETDATE())
        var match = System.Text.RegularExpressions.Regex.Match(
            constraintDefinition, 
            @"DEFAULT\s+(\([^)]+\)|\(\([^)]+\)\))", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (match.Success)
        {
            var value = match.Groups[1].Value.Trim();
            // Fix unbalanced parentheses - count opening and closing
            int openCount = value.Count(c => c == '(');
            int closeCount = value.Count(c => c == ')');
            
            while (closeCount < openCount)
            {
                value += ")";
                closeCount++;
            }
            
            return value;
        }
        
        return null;
    }
    
    string InjectDefaultIntoColumnDef(string columnDef, string defaultValue)
    {
        // Inject DEFAULT clause before any constraints
        // Example: [Column] INT NOT NULL -> [Column] INT DEFAULT (0) NOT NULL
        
        // Remove trailing semicolon if present
        columnDef = columnDef.TrimEnd(';').Trim();
        
        // defaultValue already contains parentheses from ExtractDefaultValue
        
        // Find the position after data type and before/after NULL/NOT NULL
        if (columnDef.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase))
        {
            var notNullIndex = columnDef.LastIndexOf("NOT NULL", StringComparison.OrdinalIgnoreCase);
            return columnDef.Substring(0, notNullIndex) + $"DEFAULT {defaultValue} " + columnDef.Substring(notNullIndex);
        }
        else
        {
            // Append at the end
            return $"{columnDef} DEFAULT {defaultValue}";
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