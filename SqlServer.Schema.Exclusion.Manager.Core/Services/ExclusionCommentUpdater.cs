using SqlServer.Schema.Exclusion.Manager.Core.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlServer.Schema.Exclusion.Manager.Core.Services;

public class ExclusionCommentUpdater
{
    // No longer needed - we remove all comments instead of specific patterns
    // Kept for potential future use if we need selective comment removal
    
    public async Task UpdateSerializedFilesAsync(string outputPath, string serverName, string databaseName, ChangeManifest manifest)
    {
        var dbPath = Path.Combine(outputPath, "servers", serverName, databaseName);
        if (!Directory.Exists(dbPath))
        {
            Console.WriteLine($"Database path not found: {dbPath}");
            return;
        }
            
        // Only update files that are related to changes in the manifest
        // This includes both included and excluded changes
        var allChanges = manifest.IncludedChanges.Concat(manifest.ExcludedChanges).ToList();
        
        Console.WriteLine($"Processing {allChanges.Count} changes from manifest...");
        var filesModified = 0;
        
        foreach (var change in allChanges)
        {
            // Find the file related to this change
            var possiblePaths = GetPossibleFilePaths(dbPath, change.Identifier);
            
            foreach (var filePath in possiblePaths)
            {
                if (File.Exists(filePath))
                {
                    var wasModified = await UpdateFileCommentsAsync(filePath, manifest);
                    if (wasModified)
                    {
                        filesModified++;
                        Console.WriteLine($"  Modified: {GetRelativePathFromDb(filePath, dbPath)}");
                    }
                }
            }
        }
        
        if (filesModified == 0)
        {
            Console.WriteLine("  No serialized files were modified.");
        }
        else
        {
            Console.WriteLine($"  Total serialized files modified: {filesModified}");
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
        {
            Console.WriteLine($"  Migration file not found: {migrationFilePath}");
            return;
        }
            
        var lines = await File.ReadAllLinesAsync(migrationFilePath);
        var output = new List<string>();
        var i = 0;
        
        var changesExcluded = new List<string>();
        var changesIncluded = new List<string>();
        var fileName = Path.GetFileName(migrationFilePath);
        
        while (i < lines.Length)
        {
            var line = lines[i];
            
            // Check if this is an active SQL statement that should be excluded
            var identifier = ExtractIdentifierFromSql(line);
            if (identifier != null && manifest.ExcludedChanges.Any(c => c.Identifier == identifier))
            {
                // Comment out this change
                var change = manifest.ExcludedChanges.First(c => c.Identifier == identifier);
                changesExcluded.Add($"{identifier} - {change.Description}");
                
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
                    changesIncluded.Add(excludedIdentifier);
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
        
        // Only write if there were changes
        var newContent = string.Join(Environment.NewLine, output);
        var originalContent = string.Join(Environment.NewLine, lines);
        
        if (newContent != originalContent)
        {
            await File.WriteAllLinesAsync(migrationFilePath, output);
            
            Console.WriteLine($"  Modified migration: {fileName}");
            
            if (changesExcluded.Any())
            {
                Console.WriteLine($"    Excluded changes:");
                foreach (var change in changesExcluded)
                {
                    Console.WriteLine($"      - {change}");
                }
            }
            
            if (changesIncluded.Any())
            {
                Console.WriteLine($"    Re-included changes:");
                foreach (var change in changesIncluded)
                {
                    Console.WriteLine($"      - {change}");
                }
            }
        }
        else
        {
            Console.WriteLine($"  No changes needed for migration: {fileName}");
        }
    }
    
    private async Task<bool> UpdateFileCommentsAsync(string filePath, ChangeManifest manifest)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var originalContent = content;
        
        // Remove ALL comment lines and inline comments related to exclusions
        // First remove standalone comment lines
        content = Regex.Replace(content, @"^\s*--.*\r?\n", "", RegexOptions.Multiline);
        
        // Remove inline EXCLUDED comments
        content = Regex.Replace(content, @"\s*--\s*EXCLUDED:.*$", "", RegexOptions.Multiline);
        
        // Clean up any consecutive blank lines left after removing comments
        content = Regex.Replace(content, @"(\r?\n){3,}", "\n\n", RegexOptions.Multiline);
        
        // Remove leading blank lines
        content = Regex.Replace(content, @"^\s*\n+", "", RegexOptions.Singleline);
        
        // Add new exclusion comments based on manifest
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var relativePath = GetRelativePath(filePath);
        
        var exclusionsAdded = new List<string>();
        foreach (var excludedChange in manifest.ExcludedChanges)
        {
            if (IsChangeRelatedToFile(excludedChange, relativePath, fileName))
            {
                content = AddExclusionComment(content, excludedChange, manifest.GetManifestFileName());
                exclusionsAdded.Add(excludedChange.Identifier);
            }
        }
        
        if (content != originalContent)
        {
            await File.WriteAllTextAsync(filePath, content);
            
            // Log specific changes made
            if (exclusionsAdded.Any())
            {
                Console.WriteLine($"    Added exclusions: {string.Join(", ", exclusionsAdded)}");
            }
            
            return true;
        }
        
        return false;
    }
    
    private bool IsChangeRelatedToFile(ManifestChange change, string filePath, string fileName)
    {
        // Match based on identifier structure
        var parts = change.Identifier.Split('.');
        
        if (parts.Length == 3)
        {
            // Column change: schema.table.column
            // Match against table name (parts[1])
            var tableName = parts[1];
            
            // Handle both TBL_ prefix and direct table name
            return fileName.Equals($"TBL_{tableName}", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals(tableName, StringComparison.OrdinalIgnoreCase) ||
                   filePath.Contains($"/{tableName}/", StringComparison.OrdinalIgnoreCase);
        }
        else if (parts.Length == 2)
        {
            // Object change: schema.object
            var objectName = parts[1];
            
            // Handle various prefixes (TBL_, IDX_, FK_, etc.)
            return fileName.Equals(objectName, StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals($"TBL_{objectName}", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals($"IDX_{objectName}", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals($"FK_{objectName}", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals($"DF_{objectName}", StringComparison.OrdinalIgnoreCase) ||
                   filePath.Contains($"/{objectName}/", StringComparison.OrdinalIgnoreCase);
        }
        
        return false;
    }
    
    private string AddExclusionComment(string content, ManifestChange change, string manifestFileName)
    {
        // Check if this is a column-specific exclusion (3 parts: schema.table.column)
        var parts = change.Identifier.Split('.');
        if (parts.Length == 3 && change.ObjectType == "Table")
        {
            // Column-specific exclusion - add inline comment
            var columnName = parts[2];
            
            // Remove TBL_ prefix if present to get the actual column name
            if (columnName.StartsWith("TBL_", StringComparison.OrdinalIgnoreCase))
                columnName = columnName.Substring(4);
            
            // Pattern to match the column definition line
            // Matches: [columnName] followed by everything up to the end of line
            // Captures: (column definition)(whitespace)(optional comma)(end of line whitespace)
            var pattern = $@"(\[\s*{Regex.Escape(columnName)}\s*\].*?)(\s*)(,?)(\s*$)";
            
            // Add inline comment to the column definition, preserving comma and whitespace
            var replacement = $"$1  -- EXCLUDED: {change.Description}$2$3$4";
            
            var modifiedContent = Regex.Replace(content, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            
            // If no match found, fall back to header comment
            if (modifiedContent == content)
            {
                // Pattern didn't match, add header comment as fallback
                var lines = content.Split('\n').ToList();
                lines.Insert(0, $"-- MIGRATION EXCLUDED: Column '{columnName}' - {change.Description}");
                lines.Insert(1, $"-- This change is NOT included in current migration");
                lines.Insert(2, $"-- See: {manifestFileName}");
                lines.Insert(3, "");
                return string.Join('\n', lines);
            }
            
            return modifiedContent;
        }
        else
        {
            // Non-column exclusion - add header comment
            var lines = content.Split('\n').ToList();
            lines.Insert(0, $"-- MIGRATION EXCLUDED: {change.Description}");
            lines.Insert(1, $"-- This change is NOT included in current migration");
            lines.Insert(2, $"-- See: {manifestFileName}");
            lines.Insert(3, "");
            
            return string.Join('\n', lines);
        }
    }
    
    private string GetRelativePath(string fullPath)
    {
        var serversIndex = fullPath.IndexOf("servers");
        return serversIndex >= 0 ? fullPath.Substring(serversIndex) : fullPath;
    }

    // Helper method to get relative path from database directory
    string GetRelativePathFromDb(string fullPath, string dbPath) =>
        Path.GetRelativePath(dbPath, fullPath).Replace(Path.DirectorySeparatorChar, '/');
    
    private string? ExtractIdentifierFromComment(string comment)
    {
        var match = Regex.Match(comment, @"--\s*EXCLUDED:\s*([^\s]+)");
        return match.Success ? match.Groups[1].Value : null;
    }
    
    private string? ExtractIdentifierFromSql(string sql)
    {
        // Try to extract column identifier from ALTER TABLE ADD/DROP column statements
        var alterTableAddMatch = Regex.Match(sql, @"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+ADD\s+\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (alterTableAddMatch.Success)
        {
            return $"{alterTableAddMatch.Groups[1].Value}.{alterTableAddMatch.Groups[2].Value}.{alterTableAddMatch.Groups[3].Value}";
        }
        
        // Try ALTER TABLE DROP COLUMN
        var alterTableDropMatch = Regex.Match(sql, @"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+DROP\s+COLUMN\s+\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (alterTableDropMatch.Success)
        {
            return $"{alterTableDropMatch.Groups[1].Value}.{alterTableDropMatch.Groups[2].Value}.{alterTableDropMatch.Groups[3].Value}";
        }
        
        // Try ALTER TABLE ALTER COLUMN
        var alterTableAlterMatch = Regex.Match(sql, @"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+ALTER\s+COLUMN\s+\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (alterTableAlterMatch.Success)
        {
            return $"{alterTableAlterMatch.Groups[1].Value}.{alterTableAlterMatch.Groups[2].Value}.{alterTableAlterMatch.Groups[3].Value}";
        }
        
        // Try generic ALTER TABLE (without column - for other types of alterations)
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