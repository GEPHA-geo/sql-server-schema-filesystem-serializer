using System.Text.RegularExpressions;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Parsing;

public class SqlFileChangeDetector
{
    readonly TableChangeParser _tableParser = new();
    readonly IndexChangeParser _indexParser = new();
    readonly RenameDetector _renameDetector = new();

    public List<SchemaChange> AnalyzeChanges(string outputPath, List<DiffEntry> diffEntries)
    {
        var changes = new List<SchemaChange>();
        
        foreach (var entry in diffEntries)
        {
            var objectType = DetermineObjectType(entry.Path);
            Console.WriteLine($"Processing: {entry.Path} - Type: {objectType} - Change: {entry.ChangeType}");
            
            switch (objectType)
            {
                case "Table":
                    var tableChanges = _tableParser.ParseTableChanges(entry);
                    changes.AddRange(tableChanges);
                    break;
                    
                case "Index":
                    var indexChange = _indexParser.ParseIndexChange(entry);
                    if (indexChange != null)
                    {
                        Console.WriteLine($"Parsed index change: {indexChange.ObjectName} ({indexChange.ChangeType})");
                        changes.Add(indexChange);
                    }
                    break;
                    
                case "Constraint":
                    var constraintChange = ParseConstraintChange(entry);
                    if (constraintChange != null)
                        changes.Add(constraintChange);
                    break;
                    
                case "Trigger":
                    var triggerChange = ParseTriggerChange(entry);
                    if (triggerChange != null)
                        changes.Add(triggerChange);
                    break;
                    
                case "ExtendedProperty":
                    var extendedPropertyChange = ParseExtendedPropertyChange(entry);
                    if (extendedPropertyChange != null)
                        changes.Add(extendedPropertyChange);
                    break;
                    
                case "View":
                case "StoredProcedure":
                case "Function":
                    var objectChange = ParseObjectChange(entry, objectType);
                    if (objectChange != null)
                        changes.Add(objectChange);
                    break;
            }
        }
        
        // Apply rename detection to identify rename operations
        changes = _renameDetector.DetectRenames(changes);
        
        return changes;
    }

    string DetermineObjectType(string filePath)
    {
        if (filePath.Contains("/Tables/") && filePath.Contains("/TBL_"))
            return "Table";
        if (filePath.Contains("/Tables/") && (filePath.Contains("/IDX_") || filePath.Contains("/IX_")))
            return "Index";
        if (filePath.Contains("/Tables/") && (filePath.Contains("/FK_") || filePath.Contains("/PK_") || filePath.Contains("/DF_") || filePath.Contains("/CHK_")))
            return "Constraint";
        if (filePath.Contains("/Tables/") && filePath.Contains("/trg_"))
            return "Trigger";
        if (filePath.Contains("/Tables/") && filePath.Contains("/EP_"))
            return "ExtendedProperty";
        if (filePath.Contains("/Views/"))
            return "View";
        if (filePath.Contains("/StoredProcedures/"))
            return "StoredProcedure";
        if (filePath.Contains("/Functions/"))
            return "Function";
            
        return "Unknown";
    }

    SchemaChange? ParseConstraintChange(DiffEntry entry)
    {
        var schemaObjectName = ExtractSchemaAndObjectName(entry.Path);
        if (schemaObjectName == null) return null;
        
        return new SchemaChange
        {
            ObjectType = "Constraint",
            Schema = schemaObjectName.Value.Schema,
            ObjectName = schemaObjectName.Value.ObjectName,
            ChangeType = entry.ChangeType,
            OldDefinition = entry.OldContent,
            NewDefinition = entry.NewContent
        };
    }

    SchemaChange? ParseTriggerChange(DiffEntry entry)
    {
        var triggerInfo = ExtractTriggerInfo(entry.Path);
        if (triggerInfo == null) return null;
        
        return new SchemaChange
        {
            ObjectType = "Trigger",
            Schema = triggerInfo.Value.Schema,
            ObjectName = triggerInfo.Value.TriggerName,
            TableName = triggerInfo.Value.TableName,
            ChangeType = entry.ChangeType,
            OldDefinition = entry.OldContent,
            NewDefinition = entry.NewContent
        };
    }
    
    SchemaChange? ParseExtendedPropertyChange(DiffEntry entry)
    {
        var extPropInfo = ExtractExtendedPropertyInfo(entry.Path);
        if (extPropInfo == null) return null;
        
        return new SchemaChange
        {
            ObjectType = "ExtendedProperty",
            Schema = extPropInfo.Value.Schema,
            ObjectName = extPropInfo.Value.PropertyName,
            TableName = extPropInfo.Value.TableName,
            ChangeType = entry.ChangeType,
            OldDefinition = entry.OldContent,
            NewDefinition = entry.NewContent
        };
    }

    SchemaChange? ParseObjectChange(DiffEntry entry, string objectType)
    {
        var schemaObjectName = ExtractSchemaAndObjectName(entry.Path);
        if (schemaObjectName == null) return null;
        
        return new SchemaChange
        {
            ObjectType = objectType,
            Schema = schemaObjectName.Value.Schema,
            ObjectName = schemaObjectName.Value.ObjectName,
            ChangeType = entry.ChangeType,
            OldDefinition = entry.OldContent,
            NewDefinition = entry.NewContent
        };
    }

    (string Schema, string ObjectName)? ExtractSchemaAndObjectName(string filePath)
    {
        // Extract schema from path (e.g., "database/schemas/dbo/Tables/...")
        var match = Regex.Match(filePath, @"[^/]+/schemas/([^/]+)/[^/]+/(.+)\.sql$");
        if (match.Success)
        {
            var schema = match.Groups[1].Value;
            var fileName = match.Groups[2].Value;
            
            // Remove prefixes like TBL_, IDX_, FK_, etc.
            var objectName = Regex.Replace(fileName, @"^(TBL_|IDX_|IX_|FK_|PK_|DF_|CHK_)", "");
            
            // For constraints in table folders, extract table name from path
            if (filePath.Contains("/Tables/") && !fileName.StartsWith("TBL_"))
            {
                var tableMatch = Regex.Match(filePath, @"/Tables/([^/]+)/");
                if (tableMatch.Success)
                {
                    objectName = fileName; // Keep full constraint name
                }
            }
            
            return (schema, objectName);
        }
        
        return null;
    }

    (string Schema, string TableName, string TriggerName)? ExtractTriggerInfo(string filePath)
    {
        // Extract from file path (e.g., "database/schemas/dbo/Tables/Customer/trg_Customer_audit.sql")
        var match = Regex.Match(filePath, @"[^/]+/schemas/([^/]+)/Tables/([^/]+)/(trg_[^.]+)\.sql$");
        if (match.Success)
        {
            var schema = match.Groups[1].Value;
            var tableName = match.Groups[2].Value;
            var triggerName = match.Groups[3].Value;
            return (schema, tableName, triggerName);
        }
        
        return null;
    }
    
    (string Schema, string TableName, string PropertyName)? ExtractExtendedPropertyInfo(string filePath)
    {
        // Extract from file path (e.g., "database/schemas/dbo/Tables/Customer/EP_Column_Description_CustomerName.sql")
        var match = Regex.Match(filePath, @"[^/]+/schemas/([^/]+)/Tables/([^/]+)/(EP_[^.]+)\.sql$");
        if (match.Success)
        {
            var schema = match.Groups[1].Value;
            var tableName = match.Groups[2].Value;
            var propertyName = match.Groups[3].Value;
            return (schema, tableName, propertyName);
        }
        
        return null;
    }
}