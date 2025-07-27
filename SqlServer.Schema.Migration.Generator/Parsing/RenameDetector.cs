using System.Text.RegularExpressions;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Parsing;

public class RenameDetector
{
    // Detects renames by matching dropped objects with added objects that have identical definitions
    public List<SchemaChange> DetectRenames(List<SchemaChange> changes)
    {
        var result = new List<SchemaChange>();
        var processedChanges = new HashSet<SchemaChange>();
        
        // Group changes by type for efficient matching
        var changesByType = changes.GroupBy(c => c.ObjectType).ToDictionary(g => g.Key, g => g.ToList());
        
        foreach (var objectType in changesByType.Keys)
        {
            var typeChanges = changesByType[objectType];
            var deletedObjects = typeChanges.Where(c => c.ChangeType == ChangeType.Deleted).ToList();
            var addedObjects = typeChanges.Where(c => c.ChangeType == ChangeType.Added).ToList();
            
            // Try to match deleted objects with added objects
            foreach (var deleted in deletedObjects)
            {
                if (processedChanges.Contains(deleted)) continue;
                
                var renamed = objectType switch
                {
                    "Column" => FindRenamedColumn(deleted, addedObjects, processedChanges),
                    "Index" => FindRenamedIndex(deleted, addedObjects, processedChanges),
                    "Constraint" => FindRenamedConstraint(deleted, addedObjects, processedChanges),
                    "Trigger" => FindRenamedTrigger(deleted, addedObjects, processedChanges),
                    _ => null
                };
                
                if (renamed != null)
                {
                    result.Add(renamed);
                    processedChanges.Add(deleted);
                }
                else
                {
                    // Not a rename, keep original delete operation
                    result.Add(deleted);
                    processedChanges.Add(deleted);
                }
            }
            
            // Add remaining added objects that weren't matched as renames
            foreach (var added in addedObjects)
            {
                if (!processedChanges.Contains(added))
                {
                    result.Add(added);
                    processedChanges.Add(added);
                }
            }
            
            // Add all modified objects as-is
            var modifiedObjects = typeChanges.Where(c => c.ChangeType == ChangeType.Modified);
            foreach (var modified in modifiedObjects)
            {
                result.Add(modified);
                processedChanges.Add(modified);
            }
        }
        
        return result;
    }
    
    SchemaChange? FindRenamedColumn(SchemaChange deleted, List<SchemaChange> addedColumns, HashSet<SchemaChange> processed)
    {
        // Columns must be in the same table
        var candidates = addedColumns.Where(a => 
            a.Schema == deleted.Schema && 
            a.TableName == deleted.TableName &&
            !processed.Contains(a)).ToList();
        
        foreach (var added in candidates)
        {
            if (AreColumnsEquivalent(deleted.OldDefinition, added.NewDefinition))
            {
                processed.Add(added);
                return new SchemaChange
                {
                    ObjectType = "Column",
                    Schema = deleted.Schema,
                    TableName = deleted.TableName,
                    ObjectName = added.ColumnName, // New name
                    ColumnName = added.ColumnName,
                    ChangeType = ChangeType.Modified,
                    OldDefinition = deleted.OldDefinition,
                    NewDefinition = added.NewDefinition,
                    Properties = new Dictionary<string, string>
                    {
                        ["IsRename"] = "true",
                        ["OldName"] = deleted.ColumnName,
                        ["RenameType"] = "Column"
                    }
                };
            }
        }
        
        return null;
    }
    
    SchemaChange? FindRenamedIndex(SchemaChange deleted, List<SchemaChange> addedIndexes, HashSet<SchemaChange> processed)
    {
        // Indexes must be on the same table
        var candidates = addedIndexes.Where(a => 
            a.Schema == deleted.Schema && 
            a.TableName == deleted.TableName &&
            !processed.Contains(a)).ToList();
        
        foreach (var added in candidates)
        {
            if (AreIndexesEquivalent(deleted.OldDefinition, added.NewDefinition, deleted.ObjectName, added.ObjectName))
            {
                processed.Add(added);
                return new SchemaChange
                {
                    ObjectType = "Index",
                    Schema = deleted.Schema,
                    TableName = deleted.TableName,
                    ObjectName = added.ObjectName, // New name
                    ChangeType = ChangeType.Modified,
                    OldDefinition = deleted.OldDefinition,
                    NewDefinition = added.NewDefinition,
                    Properties = new Dictionary<string, string>
                    {
                        ["IsRename"] = "true",
                        ["OldName"] = deleted.ObjectName,
                        ["RenameType"] = "Index"
                    }
                };
            }
        }
        
        return null;
    }
    
    SchemaChange? FindRenamedConstraint(SchemaChange deleted, List<SchemaChange> addedConstraints, HashSet<SchemaChange> processed)
    {
        // For table-level constraints
        var candidates = addedConstraints.Where(a => 
            a.Schema == deleted.Schema &&
            !processed.Contains(a)).ToList();
        
        // If table name is available, filter by it
        if (!string.IsNullOrEmpty(deleted.TableName))
        {
            candidates = candidates.Where(a => a.TableName == deleted.TableName).ToList();
        }
        
        foreach (var added in candidates)
        {
            if (AreConstraintsEquivalent(deleted.OldDefinition, added.NewDefinition, deleted.ObjectName, added.ObjectName))
            {
                processed.Add(added);
                return new SchemaChange
                {
                    ObjectType = "Constraint",
                    Schema = deleted.Schema,
                    TableName = deleted.TableName,
                    ObjectName = added.ObjectName, // New name
                    ChangeType = ChangeType.Modified,
                    OldDefinition = deleted.OldDefinition,
                    NewDefinition = added.NewDefinition,
                    Properties = new Dictionary<string, string>
                    {
                        ["IsRename"] = "true",
                        ["OldName"] = deleted.ObjectName,
                        ["RenameType"] = "Constraint"
                    }
                };
            }
        }
        
        return null;
    }
    
    SchemaChange? FindRenamedTrigger(SchemaChange deleted, List<SchemaChange> addedTriggers, HashSet<SchemaChange> processed)
    {
        // Triggers must be on the same table
        var candidates = addedTriggers.Where(a => 
            a.Schema == deleted.Schema && 
            a.TableName == deleted.TableName &&
            !processed.Contains(a)).ToList();
        
        foreach (var added in candidates)
        {
            if (AreTriggersEquivalent(deleted.OldDefinition, added.NewDefinition, deleted.ObjectName, added.ObjectName))
            {
                processed.Add(added);
                return new SchemaChange
                {
                    ObjectType = "Trigger",
                    Schema = deleted.Schema,
                    TableName = deleted.TableName,
                    ObjectName = added.ObjectName, // New name
                    ChangeType = ChangeType.Modified,
                    OldDefinition = deleted.OldDefinition,
                    NewDefinition = added.NewDefinition,
                    Properties = new Dictionary<string, string>
                    {
                        ["IsRename"] = "true",
                        ["OldName"] = deleted.ObjectName,
                        ["RenameType"] = "Trigger"
                    }
                };
            }
        }
        
        return null;
    }
    
    bool AreColumnsEquivalent(string oldDef, string newDef)
    {
        // Normalize column definitions for comparison
        var oldNormalized = NormalizeColumnDefinition(oldDef);
        var newNormalized = NormalizeColumnDefinition(newDef);
        
        // Extract data type and constraints from both definitions
        var oldParts = ParseColumnDefinition(oldNormalized);
        var newParts = ParseColumnDefinition(newNormalized);
        
        // Compare data type, nullability, and other properties (excluding name)
        return oldParts.DataType == newParts.DataType &&
               oldParts.IsNullable == newParts.IsNullable &&
               oldParts.DefaultValue == newParts.DefaultValue &&
               oldParts.IsIdentity == newParts.IsIdentity;
    }
    
    bool AreIndexesEquivalent(string oldDef, string newDef, string oldName, string newName)
    {
        // Replace the old index name with the new one in the old definition
        var normalizedOld = oldDef.Replace($"[{oldName}]", $"[{newName}]");
        
        // Use regex with word boundaries to avoid partial replacements
        normalizedOld = Regex.Replace(normalizedOld, $@"\b{Regex.Escape(oldName)}\b", newName);
        
        // Normalize whitespace and compare
        normalizedOld = Regex.Replace(normalizedOld, @"\s+", " ").Trim();
        var normalizedNew = Regex.Replace(newDef, @"\s+", " ").Trim();
        
        return normalizedOld.Equals(normalizedNew, StringComparison.OrdinalIgnoreCase);
    }
    
    bool AreConstraintsEquivalent(string oldDef, string newDef, string oldName, string newName)
    {
        // Replace the old constraint name with the new one
        // First replace the bracketed version, then use word boundaries for unbracketed version
        var normalizedOld = oldDef.Replace($"[{oldName}]", $"[{newName}]");
        
        // Use regex with word boundaries to avoid partial replacements
        normalizedOld = Regex.Replace(normalizedOld, $@"\b{Regex.Escape(oldName)}\b", newName);
        
        // Normalize whitespace and compare
        normalizedOld = Regex.Replace(normalizedOld, @"\s+", " ").Trim();
        var normalizedNew = Regex.Replace(newDef, @"\s+", " ").Trim();
        
        return normalizedOld.Equals(normalizedNew, StringComparison.OrdinalIgnoreCase);
    }
    
    bool AreTriggersEquivalent(string oldDef, string newDef, string oldName, string newName)
    {
        // Replace the old trigger name with the new one
        var normalizedOld = oldDef.Replace($"[{oldName}]", $"[{newName}]");
        
        // Use regex with word boundaries to avoid partial replacements
        normalizedOld = Regex.Replace(normalizedOld, $@"\b{Regex.Escape(oldName)}\b", newName);
        
        // Normalize whitespace and compare
        normalizedOld = Regex.Replace(normalizedOld, @"\s+", " ").Trim();
        var normalizedNew = Regex.Replace(newDef, @"\s+", " ").Trim();
        
        return normalizedOld.Equals(normalizedNew, StringComparison.OrdinalIgnoreCase);
    }
    
    string NormalizeColumnDefinition(string definition)
    {
        // Remove extra whitespace and normalize
        return Regex.Replace(definition.Trim(), @"\s+", " ");
    }
    
    ColumnDefinitionParts ParseColumnDefinition(string definition)
    {
        var parts = new ColumnDefinitionParts();
        
        // Extract column name and the rest
        var match = Regex.Match(definition, @"^\[?(\w+)\]?\s+(.+)$");
        if (!match.Success) return parts;
        
        parts.ColumnName = match.Groups[1].Value;
        var rest = match.Groups[2].Value;
        
        // Extract data type (including precision/scale)
        var dataTypeMatch = Regex.Match(rest, @"^(\w+(?:\s*\([^)]+\))?)");
        if (dataTypeMatch.Success)
        {
            parts.DataType = dataTypeMatch.Groups[1].Value.Trim();
            rest = rest.Substring(dataTypeMatch.Length).Trim();
        }
        
        // Check for IDENTITY
        if (Regex.IsMatch(rest, @"\bIDENTITY\b", RegexOptions.IgnoreCase))
        {
            parts.IsIdentity = true;
        }
        
        // Check for NULL/NOT NULL
        if (Regex.IsMatch(rest, @"\bNOT\s+NULL\b", RegexOptions.IgnoreCase))
        {
            parts.IsNullable = false;
        }
        else if (Regex.IsMatch(rest, @"\bNULL\b", RegexOptions.IgnoreCase))
        {
            parts.IsNullable = true;
        }
        
        // Extract DEFAULT constraint
        var defaultMatch = Regex.Match(rest, @"\bDEFAULT\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
        if (defaultMatch.Success)
        {
            parts.DefaultValue = defaultMatch.Groups[1].Value.Trim();
        }
        
        return parts;
    }
    
    class ColumnDefinitionParts
    {
        public string ColumnName { get; set; } = "";
        public string DataType { get; set; } = "";
        public bool IsNullable { get; set; } = true;
        public bool IsIdentity { get; set; }
        public string? DefaultValue { get; set; }
    }
}