using System.Text.RegularExpressions;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Parsing;

public class IndexChangeParser
{
    public SchemaChange? ParseIndexChange(DiffEntry entry)
    {
        var indexInfo = ExtractIndexInfo(entry.Path, entry.NewContent ?? entry.OldContent);
        if (indexInfo == null) return null;
        
        return new SchemaChange
        {
            ObjectType = "Index",
            Schema = indexInfo.Value.Schema,
            ObjectName = indexInfo.Value.IndexName,
            TableName = indexInfo.Value.TableName,
            ChangeType = entry.ChangeType,
            OldDefinition = entry.OldContent,
            NewDefinition = entry.NewContent
        };
    }

    (string Schema, string TableName, string IndexName)? ExtractIndexInfo(string filePath, string content)
    {
        // First, try to extract from CREATE INDEX statement in content
        // This is more reliable as it contains the actual index name
        var createMatch = Regex.Match(content, 
            @"CREATE\s+(?:UNIQUE\s+)?(?:CLUSTERED\s+|NONCLUSTERED\s+)?INDEX\s+\[?(\w+)\]?\s+ON\s+\[?(\w+)\]?\.\[?(\w+)\]?", 
            RegexOptions.IgnoreCase);
        
        if (createMatch.Success)
        {
            var indexName = createMatch.Groups[1].Value;
            var schema = createMatch.Groups[2].Value;
            var tableName = createMatch.Groups[3].Value;
            return (schema, tableName, indexName);
        }
        
        // Fallback: Extract from file path if content parsing fails
        // (e.g., "database/schemas/dbo/Tables/Customer/IDX_Customer_Email.sql")
        var pathMatch = Regex.Match(filePath, @"([^/]+)/schemas/([^/]+)/Tables/([^/]+)/((?:IDX_|IX_)[^.]+)\.sql$");
        if (pathMatch.Success)
        {
            var schema = pathMatch.Groups[2].Value;
            var tableName = pathMatch.Groups[3].Value;
            var indexName = pathMatch.Groups[4].Value;
            
            // Remove IDX_ prefix if present in filename
            if (indexName.StartsWith("IDX_", StringComparison.OrdinalIgnoreCase))
            {
                indexName = indexName.Substring(4);
            }
            
            return (schema, tableName, indexName);
        }
        
        return null;
    }
}