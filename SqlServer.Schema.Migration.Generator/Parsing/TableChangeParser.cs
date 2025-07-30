using System.Text.RegularExpressions;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Parsing;

public class TableChangeParser
{
    public List<SchemaChange> ParseTableChanges(DiffEntry entry)
    {
        var changes = new List<SchemaChange>();
        
        if (entry.ChangeType == ChangeType.Added)
        {
            // New table - return single change for entire table
            var tableInfo = ExtractTableInfo(entry.Path, entry.NewContent);
            if (tableInfo != null)
            {
                changes.Add(new SchemaChange
                {
                    ObjectType = "Table",
                    Schema = tableInfo.Value.Schema,
                    ObjectName = tableInfo.Value.TableName,
                    ChangeType = ChangeType.Added,
                    NewDefinition = entry.NewContent
                });
            }
        }
        else if (entry.ChangeType == ChangeType.Deleted)
        {
            // Table deleted
            var tableInfo = ExtractTableInfo(entry.Path, entry.OldContent);
            if (tableInfo != null)
            {
                changes.Add(new SchemaChange
                {
                    ObjectType = "Table",
                    Schema = tableInfo.Value.Schema,
                    ObjectName = tableInfo.Value.TableName,
                    ChangeType = ChangeType.Deleted,
                    OldDefinition = entry.OldContent
                });
            }
        }
        else if (entry.ChangeType == ChangeType.Modified)
        {
            // Analyze column changes
            var tableInfo = ExtractTableInfo(entry.Path, entry.NewContent);
            if (tableInfo != null)
            {
                var oldColumns = ExtractColumns(entry.OldContent);
                var newColumns = ExtractColumns(entry.NewContent);
                
                // Find added columns
                foreach (var newCol in newColumns)
                {
                    if (!oldColumns.Any(c => c.Name == newCol.Name))
                    {
                        changes.Add(new SchemaChange
                        {
                            ObjectType = "Column",
                            Schema = tableInfo.Value.Schema,
                            TableName = tableInfo.Value.TableName,
                            ObjectName = newCol.Name,
                            ColumnName = newCol.Name,
                            ChangeType = ChangeType.Added,
                            NewDefinition = newCol.Definition,
                            Properties = new Dictionary<string, string> { ["DataType"] = newCol.DataType }
                        });
                    }
                }
                
                // Find deleted columns
                foreach (var oldCol in oldColumns)
                {
                    if (!newColumns.Any(c => c.Name == oldCol.Name))
                    {
                        changes.Add(new SchemaChange
                        {
                            ObjectType = "Column",
                            Schema = tableInfo.Value.Schema,
                            TableName = tableInfo.Value.TableName,
                            ObjectName = oldCol.Name,
                            ColumnName = oldCol.Name,
                            ChangeType = ChangeType.Deleted,
                            OldDefinition = oldCol.Definition
                        });
                    }
                }
                
                // Find modified columns
                foreach (var newCol in newColumns)
                {
                    var oldCol = oldColumns.FirstOrDefault(c => c.Name == newCol.Name);
                    if (oldCol != null)
                    {
                        // Normalize both definitions for comparison to avoid false positives from whitespace/formatting differences
                        var normalizedOld = NormalizeColumnDefinition(oldCol.Definition);
                        var normalizedNew = NormalizeColumnDefinition(newCol.Definition);
                        
                        if (normalizedOld != normalizedNew)
                        {
                            changes.Add(new SchemaChange
                            {
                                ObjectType = "Column",
                                Schema = tableInfo.Value.Schema,
                                TableName = tableInfo.Value.TableName,
                                ObjectName = newCol.Name,
                                ColumnName = newCol.Name,
                                ChangeType = ChangeType.Modified,
                                OldDefinition = oldCol.Definition,
                                NewDefinition = newCol.Definition,
                                Properties = new Dictionary<string, string> { ["DataType"] = newCol.DataType }
                            });
                        }
                    }
                }
            }
        }
        
        return changes;
    }

    (string Schema, string TableName)? ExtractTableInfo(string filePath, string content)
    {
        // Extract from file path
        var pathMatch = Regex.Match(filePath, @"([^/]+)/schemas/([^/]+)/Tables/([^/]+)/TBL_");
        if (pathMatch.Success)
        {
            var schema = pathMatch.Groups[2].Value;
            var tableName = pathMatch.Groups[3].Value;
            return (schema, tableName);
        }
        
        // Try to extract from CREATE TABLE statement
        var createMatch = Regex.Match(content, @"CREATE\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (createMatch.Success)
        {
            return (createMatch.Groups[1].Value, createMatch.Groups[2].Value);
        }
        
        return null;
    }

    List<ColumnInfo> ExtractColumns(string tableDefinition)
    {
        var columns = new List<ColumnInfo>();
        
        // Find the column definitions section
        var match = Regex.Match(tableDefinition, @"CREATE\s+TABLE[^(]+\((.*?)\n\s*(?:CONSTRAINT|PRIMARY|\)|;)", 
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        
        if (match.Success)
        {
            var columnsSection = match.Groups[1].Value;
            
            // Split by commas but respect nested parentheses
            var columnDefs = SplitColumns(columnsSection);
            
            foreach (var colDef in columnDefs)
            {
                var trimmed = colDef.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase))
                    continue;
                    
                // Extract column name and data type
                var colMatch = Regex.Match(trimmed, @"^\[?(\w+)\]?\s+(\w+(?:\s*\([^)]+\))?)", RegexOptions.IgnoreCase);
                if (colMatch.Success)
                {
                    columns.Add(new ColumnInfo
                    {
                        Name = colMatch.Groups[1].Value,
                        DataType = colMatch.Groups[2].Value,
                        Definition = trimmed
                    });
                }
            }
        }
        
        return columns;
    }

    List<string> SplitColumns(string columnsSection)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var parenDepth = 0;
        
        foreach (char c in columnsSection)
        {
            if (c == '(') parenDepth++;
            else if (c == ')') parenDepth--;
            else if (c == ',' && parenDepth == 0)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }
            
            current.Append(c);
        }
        
        if (current.Length > 0)
            result.Add(current.ToString());
            
        return result;
    }

    // Normalizes column definitions to avoid false positives in change detection due to whitespace/formatting differences
    string NormalizeColumnDefinition(string definition)
    {
        // Remove extra whitespace and normalize
        return Regex.Replace(definition.Trim(), @"\s+", " ");
    }

    class ColumnInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public string Definition { get; set; } = string.Empty;
    }
}