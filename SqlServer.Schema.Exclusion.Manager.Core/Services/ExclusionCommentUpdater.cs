using SqlServer.Schema.Exclusion.Manager.Core.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlServer.Schema.Exclusion.Manager.Core.Services;

public class ExclusionCommentUpdater
{
    private readonly string[] _exclusionCommentPatterns = new[]
    {
        @"^\s*--\s*EXCLUDED FROM MIGRATION:.*\r?\n?",
        @"^\s*--\s*MIGRATION EXCLUDED:.*\r?\n?",
        @"^\s*--\s*This change is NOT included in current migration.*\r?\n?",
        @"^\s*--\s*Reason: Defined in _change-manifests/.*\r?\n?",
        @"^\s*--\s*See: _change-manifests/.*\r?\n?"
    };
    
    public async Task UpdateSerializedFilesAsync(string outputPath, string serverName, string databaseName, ChangeManifest manifest)
    {
        var dbPath = Path.Combine(outputPath, "servers", serverName, databaseName);
        if (!Directory.Exists(dbPath))
            return;
            
        // Only update files that are related to changes in the manifest
        // This includes both included and excluded changes
        var allChanges = manifest.IncludedChanges.Concat(manifest.ExcludedChanges).ToList();
        
        foreach (var change in allChanges)
        {
            // Find the file related to this change
            var possiblePaths = GetPossibleFilePaths(dbPath, change.Identifier);
            
            foreach (var filePath in possiblePaths)
            {
                if (File.Exists(filePath))
                {
                    await UpdateFileCommentsAsync(filePath, manifest);
                }
            }
        }
    }

    private List<string> GetPossibleFilePaths(string dbPath, string identifier)
    {
        var paths = new List<string>();
        
        // Parse identifier (e.g., "dbo.TableName" or "dbo.IDX_IndexName")
        var parts = identifier.Split('.');
        if (parts.Length < 2)
            return paths;
            
        var schema = parts[0];
        var objectName = parts[1];
        
        // Determine object type and build paths
        if (objectName.StartsWith("IDX_", StringComparison.OrdinalIgnoreCase))
        {
            // Index - could be in Tables folder
            var indexName = objectName.Substring(4); // Remove IDX_ prefix
            
            // Look for index files in all table folders
            var tablesPath = Path.Combine(dbPath, "schemas", schema, "Tables");
            if (Directory.Exists(tablesPath))
            {
                var indexFiles = Directory.GetFiles(tablesPath, $"*{indexName}*.sql", SearchOption.AllDirectories);
                paths.AddRange(indexFiles);
            }
        }
        else if (objectName.StartsWith("TBL_", StringComparison.OrdinalIgnoreCase))
        {
            // Table
            var tableName = objectName.Substring(4); // Remove TBL_ prefix
            var tablePath = Path.Combine(dbPath, "schemas", schema, "Tables", tableName, $"{objectName}.sql");
            paths.Add(tablePath);
        }
        else if (objectName.StartsWith("DF_", StringComparison.OrdinalIgnoreCase) || 
                 objectName.StartsWith("FK_", StringComparison.OrdinalIgnoreCase) ||
                 objectName.StartsWith("PK_", StringComparison.OrdinalIgnoreCase) ||
                 objectName.StartsWith("UQ_", StringComparison.OrdinalIgnoreCase))
        {
            // Constraint - find in Tables folders
            var constraintPath = Path.Combine(dbPath, "schemas", schema, "Tables");
            if (Directory.Exists(constraintPath))
            {
                var constraintFiles = Directory.GetFiles(constraintPath, $"*{objectName}*.sql", SearchOption.AllDirectories);
                paths.AddRange(constraintFiles);
            }
        }
        else if (objectName.StartsWith("sp_", StringComparison.OrdinalIgnoreCase))
        {
            // Stored procedure
            var spPath = Path.Combine(dbPath, "schemas", schema, "StoredProcedures", $"{objectName}.sql");
            paths.Add(spPath);
        }
        else if (objectName.StartsWith("fn_", StringComparison.OrdinalIgnoreCase))
        {
            // Function
            var fnPath = Path.Combine(dbPath, "schemas", schema, "Functions", $"{objectName}.sql");
            paths.Add(fnPath);
        }
        else if (objectName.StartsWith("vw_", StringComparison.OrdinalIgnoreCase))
        {
            // View
            var vwPath = Path.Combine(dbPath, "schemas", schema, "Views", $"{objectName}.sql");
            paths.Add(vwPath);
        }
        else
        {
            // Try common locations
            var commonPaths = new[]
            {
                Path.Combine(dbPath, "schemas", schema, "Tables", objectName, $"{objectName}.sql"),
                Path.Combine(dbPath, "schemas", schema, "Tables", objectName, $"TBL_{objectName}.sql"),
                Path.Combine(dbPath, "schemas", schema, "Views", $"{objectName}.sql"),
                Path.Combine(dbPath, "schemas", schema, "StoredProcedures", $"{objectName}.sql"),
                Path.Combine(dbPath, "schemas", schema, "Functions", $"{objectName}.sql")
            };
            paths.AddRange(commonPaths);
        }
        
        return paths;
    }
    
    public async Task UpdateMigrationScriptAsync(string migrationFilePath, ChangeManifest manifest)
    {
        if (!File.Exists(migrationFilePath))
            return;
            
        var lines = await File.ReadAllLinesAsync(migrationFilePath);
        var output = new List<string>();
        var i = 0;
        
        while (i < lines.Length)
        {
            var line = lines[i];
            
            // Check if this is an active SQL statement that should be excluded
            var identifier = ExtractIdentifierFromSql(line);
            if (identifier != null && manifest.ExcludedChanges.Any(c => c.Identifier == identifier))
            {
                // Comment out this change
                var change = manifest.ExcludedChanges.First(c => c.Identifier == identifier);
                output.Add($"-- EXCLUDED: {identifier} - {change.Description}");
                output.Add($"-- Source: {manifest.GetManifestFileName()}");
                output.Add("/*");
                output.Add(line);
                
                // Continue reading until we find GO
                i++;
                while (i < lines.Length)
                {
                    output.Add(lines[i]);
                    if (lines[i].Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    i++;
                }
                output.Add("*/");
            }
            else if (line.Contains("-- EXCLUDED:") || line.Contains("-- MIGRATION EXCLUDED:"))
            {
                // This is already an excluded block - check if it should stay excluded
                var blockLines = new List<string> { line };
                
                // Read the entire excluded block
                i++;
                while (i < lines.Length && !lines[i].Trim().Equals("*/"))
                {
                    blockLines.Add(lines[i]);
                    i++;
                }
                if (i < lines.Length && lines[i].Trim().Equals("*/"))
                {
                    blockLines.Add(lines[i]);
                }
                
                // Extract identifier from the comment
                var excludedIdentifier = ExtractIdentifierFromComment(line);
                if (excludedIdentifier != null && manifest.ExcludedChanges.Any(c => c.Identifier == excludedIdentifier))
                {
                    // Keep it excluded
                    output.AddRange(blockLines);
                }
                else if (excludedIdentifier != null)
                {
                    // Un-exclude it - extract the SQL from the comment block
                    output.Add($"-- {excludedIdentifier} - Now included in migration");
                    
                    var inSql = false;
                    foreach (var blockLine in blockLines)
                    {
                        if (blockLine.Trim() == "/*")
                        {
                            inSql = true;
                            continue;
                        }
                        if (blockLine.Trim() == "*/")
                        {
                            break;
                        }
                        if (inSql && !blockLine.StartsWith("--"))
                        {
                            output.Add(blockLine);
                        }
                    }
                }
                else
                {
                    // Can't determine, keep as is
                    output.AddRange(blockLines);
                }
            }
            else
            {
                output.Add(line);
            }
            
            i++;
        }
        
        await File.WriteAllLinesAsync(migrationFilePath, output);
    }
    
    private async Task UpdateFileCommentsAsync(string filePath, ChangeManifest manifest)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var originalContent = content;
        
        // Remove existing exclusion comments and clean up extra blank lines
        foreach (var pattern in _exclusionCommentPatterns)
        {
            content = Regex.Replace(content, pattern, "", RegexOptions.Multiline);
        }
        
        // Clean up any consecutive blank lines left after removing comments
        content = Regex.Replace(content, @"(\r?\n){3,}", "\n\n", RegexOptions.Multiline);
        
        // Remove leading blank lines
        content = Regex.Replace(content, @"^\s*\n+", "", RegexOptions.Singleline);
        
        // Add new exclusion comments based on manifest
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var relativePath = GetRelativePath(filePath);
        
        foreach (var excludedChange in manifest.ExcludedChanges)
        {
            if (IsChangeRelatedToFile(excludedChange, relativePath, fileName))
            {
                content = AddExclusionComment(content, excludedChange, manifest.GetManifestFileName());
            }
        }
        
        if (content != originalContent)
        {
            await File.WriteAllTextAsync(filePath, content);
        }
    }
    
    private bool IsChangeRelatedToFile(ManifestChange change, string filePath, string fileName)
    {
        // Simple matching based on identifier
        var parts = change.Identifier.Split('.');
        if (parts.Length >= 2)
        {
            var objectName = parts[^1];
            return fileName.Equals(objectName, StringComparison.OrdinalIgnoreCase) ||
                   filePath.Contains(objectName, StringComparison.OrdinalIgnoreCase);
        }
        
        return false;
    }
    
    private string AddExclusionComment(string content, ManifestChange change, string manifestFileName)
    {
        // For table files, add comments near column definitions
        if (change.ObjectType == "Table" && change.Identifier.Count(c => c == '.') == 2)
        {
            // Column change
            var columnName = change.Identifier.Split('.')[^1];
            var pattern = $@"(\[\s*{columnName}\s*\][^\r\n]+)";
            var replacement = $@"    -- MIGRATION EXCLUDED: {change.Description}
    -- This change is NOT included in current migration
    -- See: {manifestFileName}
$1";
            
            return Regex.Replace(content, pattern, replacement, RegexOptions.IgnoreCase);
        }
        
        // For other objects, add comment at the beginning
        var lines = content.Split('\n').ToList();
        lines.Insert(0, $"-- MIGRATION EXCLUDED: {change.Description}");
        lines.Insert(1, $"-- This change is NOT included in current migration");
        lines.Insert(2, $"-- See: {manifestFileName}");
        lines.Insert(3, "");
        
        return string.Join('\n', lines);
    }
    
    private string GetRelativePath(string fullPath)
    {
        var serversIndex = fullPath.IndexOf("servers");
        return serversIndex >= 0 ? fullPath.Substring(serversIndex) : fullPath;
    }
    
    private string? ExtractIdentifierFromComment(string comment)
    {
        var match = Regex.Match(comment, @"--\s*EXCLUDED:\s*([^\s]+)");
        return match.Success ? match.Groups[1].Value : null;
    }
    
    private string? ExtractIdentifierFromSql(string sql)
    {
        // Try to extract object identifier from SQL statement
        var alterTableMatch = Regex.Match(sql, @"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (alterTableMatch.Success)
        {
            return $"{alterTableMatch.Groups[1].Value}.{alterTableMatch.Groups[2].Value}";
        }
        
        // Match CREATE INDEX with optional schema and brackets
        var createIndexMatch = Regex.Match(sql, @"CREATE\s+(?:UNIQUE\s+|NONCLUSTERED\s+|CLUSTERED\s+)?INDEX\s+\[?([^\]]+)\]?", RegexOptions.IgnoreCase);
        if (createIndexMatch.Success)
        {
            var indexName = createIndexMatch.Groups[1].Value.Trim();
            // Check if it starts with IDX_ already, if not add it
            if (!indexName.StartsWith("IDX_", StringComparison.OrdinalIgnoreCase))
            {
                indexName = $"IDX_{indexName}";
            }
            return $"dbo.{indexName}";
        }
        
        // Match DROP CONSTRAINT
        var dropConstraintMatch = Regex.Match(sql, @"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+DROP\s+CONSTRAINT\s+\[?([^\]]+)\]?", RegexOptions.IgnoreCase);
        if (dropConstraintMatch.Success)
        {
            return $"{dropConstraintMatch.Groups[1].Value}.{dropConstraintMatch.Groups[3].Value}";
        }
        
        return null;
    }
}