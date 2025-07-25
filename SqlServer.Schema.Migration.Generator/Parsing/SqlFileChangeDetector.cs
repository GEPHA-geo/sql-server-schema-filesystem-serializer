using System.Text.RegularExpressions;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Parsing;

public class SqlFileChangeDetector
{
    readonly TableChangeParser _tableParser = new();
    readonly IndexChangeParser _indexParser = new();

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
                    
                case "View":
                case "StoredProcedure":
                case "Function":
                    var objectChange = ParseObjectChange(entry, objectType);
                    if (objectChange != null)
                        changes.Add(objectChange);
                    break;
            }
        }
        
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
        // Extract schema from path (e.g., "database/dbo/Tables/...")
        var match = Regex.Match(filePath, @"[^/]+/([^/]+)/[^/]+/(.+)\.sql$");
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
}