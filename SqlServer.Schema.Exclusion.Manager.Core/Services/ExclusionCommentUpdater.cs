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
        
        // Group changes by their related files to avoid processing the same file multiple times
        var processedFiles = new HashSet<string>();
        var filesModified = 0;
        
        foreach (var change in allChanges)
        {
            // Find the file related to this change
            var possiblePaths = GetPossibleFilePaths(dbPath, change.Identifier);
            
            foreach (var filePath in possiblePaths)
            {
                if (File.Exists(filePath) && !processedFiles.Contains(filePath))
                {
                    processedFiles.Add(filePath);
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
        
        // Parse identifier (e.g., "dbo.TableName" or "dbo.IDX_IndexName" or "dbo.TableName.ColumnName")
        var parts = identifier.Split('.');
        if (parts.Length < 2)
            return paths;
            
        var schema = parts[0];
        
        // For 3-part identifiers (columns), we need to find the table file
        if (parts.Length == 3)
        {
            // This is a column identifier - find the table file
            var tableName = parts[1];
            
            // Try with TBL_ prefix first
            var tablePath = Path.Combine(dbPath, "schemas", schema, "Tables", tableName, $"TBL_{tableName}.sql");
            paths.Add(tablePath);
            
            // Also try without prefix
            tablePath = Path.Combine(dbPath, "schemas", schema, "Tables", tableName, $"{tableName}.sql");
            paths.Add(tablePath);
            
            return paths;
        }
        
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
            // For multi-line statements, collect the full statement before extracting identifier
            var fullStatement = line;
            var statementEndIndex = i; // Track where the statement ends
            
            // Handle CREATE INDEX statements that can span multiple lines
            if (line.Contains("CREATE ", StringComparison.OrdinalIgnoreCase) && 
                (line.Contains(" INDEX ", StringComparison.OrdinalIgnoreCase) || 
                 line.Contains(" INDEX[", StringComparison.OrdinalIgnoreCase)))
            {
                // CREATE INDEX can span multiple lines until semicolon or GO
                var j = i + 1;
                while (j < lines.Length && !lines[j - 1].Trim().EndsWith(";") && !lines[j].Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
                {
                    fullStatement += " " + lines[j];
                    statementEndIndex = j;
                    j++;
                }
            }
            // Handle ALTER TABLE statements that can span multiple lines
            else if (line.StartsWith("ALTER TABLE", StringComparison.OrdinalIgnoreCase))
            {
                // ALTER TABLE can span multiple lines until semicolon or GO
                var j = i + 1;
                while (j < lines.Length && !lines[j - 1].Trim().EndsWith(";") && !lines[j].Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
                {
                    fullStatement += " " + lines[j];
                    statementEndIndex = j;
                    j++;
                }
            }
            else if (line.Contains("sp_addextendedproperty", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("sp_dropextendedproperty", StringComparison.OrdinalIgnoreCase))
            {
                // Extended property statements can span multiple lines
                var j = i + 1;
                while (j < lines.Length && !lines[j - 1].Trim().EndsWith(";") && !lines[j].Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
                {
                    fullStatement += " " + lines[j];
                    statementEndIndex = j;
                    j++;
                }
            }
            else if (Regex.IsMatch(line, @"CREATE\s+(PROCEDURE|PROC|FUNCTION|VIEW)", RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(line, @"ALTER\s+(PROCEDURE|PROC|FUNCTION|VIEW)", RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(line, @"CREATE\s+OR\s+ALTER\s+(PROCEDURE|PROC|FUNCTION|VIEW)", RegexOptions.IgnoreCase))
            {
                // CREATE/ALTER PROCEDURE/FUNCTION/VIEW can span multiple lines
                // Collect lines until we find END; or GO
                var j = i + 1;
                while (j < lines.Length)
                {
                    fullStatement += " " + lines[j];
                    statementEndIndex = j;
                    var trimmedLine = lines[j].Trim();
                    
                    if (trimmedLine.Equals("GO", StringComparison.OrdinalIgnoreCase))
                    {
                        // Found GO, include it and stop
                        break;
                    }
                    else if (trimmedLine.EndsWith("END;", StringComparison.OrdinalIgnoreCase) && 
                             (line.Contains("PROCEDURE", StringComparison.OrdinalIgnoreCase) || 
                              line.Contains("PROC", StringComparison.OrdinalIgnoreCase) ||
                              line.Contains("FUNCTION", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Found END; - check if next line is GO
                        if (j + 1 < lines.Length && lines[j + 1].Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
                        {
                            fullStatement += " " + lines[j + 1];
                            statementEndIndex = j + 1;
                        }
                        break;
                    }
                    j++;
                }
            }
            
            var identifier = ExtractIdentifierFromSql(fullStatement);
            if (identifier != null && manifest.ExcludedChanges.Any(c => c.Identifier == identifier))
            {
                // Comment out this change
                var change = manifest.ExcludedChanges.First(c => c.Identifier == identifier);
                changesExcluded.Add($"{identifier} - {change.Description}");
                
                output.Add($"-- EXCLUDED: {identifier} - {change.Description}");
                output.Add($"-- Source: {manifest.GetManifestFileName()}");
                output.Add("/*");
                
                // Add all lines from i to statementEndIndex
                for (var k = i; k <= statementEndIndex && k < lines.Length; k++)
                {
                    output.Add(lines[k]);
                }
                
                output.Add("*/");
                
                // Move i to after the statement we just processed
                i = statementEndIndex;
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
        
        // Remove inline EXCLUDED comments but preserve trailing commas
        content = Regex.Replace(content, @"\s*--\s*EXCLUDED:[^,\r\n]*(,?)$", "$1", RegexOptions.Multiline);
        
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
        
        // For table files, only add inline comments for actual columns
        // Non-column objects (constraints, indexes, etc.) should not add any comments to table files
        if (parts.Length == 3)
        {
            var columnName = parts[2];
            
            // Check if this is actually a column (not a constraint, index, or extended property)
            // Constraints typically start with DF_, FK_, PK_, CK_, UQ_
            // Indexes typically start with IX_, IDX_, or end with _Index
            // Extended properties start with EP_
            bool isActualColumn = !columnName.StartsWith("DF_", StringComparison.OrdinalIgnoreCase) &&
                                   !columnName.StartsWith("FK_", StringComparison.OrdinalIgnoreCase) &&
                                   !columnName.StartsWith("PK_", StringComparison.OrdinalIgnoreCase) &&
                                   !columnName.StartsWith("CK_", StringComparison.OrdinalIgnoreCase) &&
                                   !columnName.StartsWith("UQ_", StringComparison.OrdinalIgnoreCase) &&
                                   !columnName.StartsWith("IX_", StringComparison.OrdinalIgnoreCase) &&
                                   !columnName.StartsWith("IDX_", StringComparison.OrdinalIgnoreCase) &&
                                   !columnName.StartsWith("EP_", StringComparison.OrdinalIgnoreCase) &&
                                   !columnName.EndsWith("_Index", StringComparison.OrdinalIgnoreCase) &&
                                   !columnName.Equals("iTesting2", StringComparison.OrdinalIgnoreCase); // Specific index name
            
            if (isActualColumn)
            {
                // Column-specific exclusion - add inline comment
                // Pattern to match the column definition line
                // Use non-greedy match up to optional comma to ensure comma is captured separately
                var pattern = $@"^(\s*\[\s*{Regex.Escape(columnName)}\s*\][^\n\r,]*(?:\([^)]*\)[^\n\r,]*)*)(,?)(\s*)$";
                
                // Check if inline comment already exists for this column
                if (Regex.IsMatch(content, $@"\[\s*{Regex.Escape(columnName)}\s*\].*--\s*EXCLUDED:", RegexOptions.IgnoreCase))
                {
                    // Comment already exists, don't add duplicate
                    return content;
                }
                
                // Add inline comment to the column definition
                // $1 = column definition without comma
                // $2 = optional comma  
                // $3 = trailing whitespace
                var replacement = $"$1$2  -- EXCLUDED: {change.Description}$3";
                

                
                var modifiedContent = Regex.Replace(content, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                

                
                // If column is not in the current table (e.g., it was removed), don't add any comment
                // Just return the content unchanged
                if (modifiedContent == content)
                {
                    // Column not found in table - it might have been removed
                    // Don't add any header comment for removed columns
                    return content;
                }
                
                return modifiedContent;
            }
        }
        
        // For non-column exclusions (constraints, indexes, etc.) in table files, 
        // don't add any comments - these are tracked elsewhere
        return content;
    }
    
    private string GetExclusionDescription(ManifestChange change)
    {
        var parts = change.Identifier.Split('.');
        if (parts.Length == 3)
        {
            var objectName = parts[2];
            
            // Determine the type of object for better description
            if (objectName.StartsWith("DF_", StringComparison.OrdinalIgnoreCase))
                return $"Constraint '{objectName}' - {change.Description}";
            else if (objectName.StartsWith("FK_", StringComparison.OrdinalIgnoreCase))
                return $"Foreign Key '{objectName}' - {change.Description}";
            else if (objectName.StartsWith("PK_", StringComparison.OrdinalIgnoreCase))
                return $"Primary Key '{objectName}' - {change.Description}";
            else if (objectName.StartsWith("IX_", StringComparison.OrdinalIgnoreCase) || 
                     objectName.StartsWith("IDX_", StringComparison.OrdinalIgnoreCase) ||
                     objectName.Equals("iTesting2", StringComparison.OrdinalIgnoreCase))
                return $"Index '{objectName}' - {change.Description}";
            else if (objectName.StartsWith("EP_", StringComparison.OrdinalIgnoreCase))
                return $"Extended Property '{objectName}' - {change.Description}";
            else
                return $"Column '{objectName}' - {change.Description}";
        }
        
        return change.Description;
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
        // Try sp_rename for column renames
        var renameMatch = Regex.Match(sql, @"sp_rename\s+'?\[?(\w+)\]?\.\[?(\w+)\]?\.\[?(\w+)\]?'?\s*,\s*'([^']+)'", RegexOptions.IgnoreCase);
        if (renameMatch.Success)
        {
            // Return the original column name that's being renamed
            return $"{renameMatch.Groups[1].Value}.{renameMatch.Groups[2].Value}.{renameMatch.Groups[3].Value}";
        }
        
        // Try to extract column identifier from ALTER TABLE ADD column statements
        // Need to exclude CONSTRAINT keyword
        var alterTableAddMatch = Regex.Match(sql, @"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+ADD\s+(?!CONSTRAINT)\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (alterTableAddMatch.Success)
        {
            return $"{alterTableAddMatch.Groups[1].Value}.{alterTableAddMatch.Groups[2].Value}.{alterTableAddMatch.Groups[3].Value}";
        }
        
        // Try ALTER TABLE ADD CONSTRAINT
        var addConstraintMatch = Regex.Match(sql, @"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+ADD\s+CONSTRAINT\s+\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (addConstraintMatch.Success)
        {
            var schema = addConstraintMatch.Groups[1].Value;
            var table = addConstraintMatch.Groups[2].Value;
            var constraintName = addConstraintMatch.Groups[3].Value;
            return $"{schema}.{table}.{constraintName}";
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
        
        // Try DROP CONSTRAINT
        var dropConstraintMatch = Regex.Match(sql, @"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+DROP\s+CONSTRAINT\s+\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (dropConstraintMatch.Success)
        {
            var schema = dropConstraintMatch.Groups[1].Value;
            var table = dropConstraintMatch.Groups[2].Value;
            var constraintName = dropConstraintMatch.Groups[3].Value;
            return $"{schema}.{table}.{constraintName}";
        }
        
        // Match CREATE INDEX with table name extraction
        var createIndexMatch = Regex.Match(sql, @"CREATE\s+(?:UNIQUE\s+|NONCLUSTERED\s+|CLUSTERED\s+)?INDEX\s+\[?([^\]]+)\]?\s+ON\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (createIndexMatch.Success)
        {
            var indexName = createIndexMatch.Groups[1].Value.Trim();
            var schema = createIndexMatch.Groups[2].Value;
            var table = createIndexMatch.Groups[3].Value;
            return $"{schema}.{table}.{indexName}";
        }
        
        // Match DROP INDEX with table name extraction
        var dropIndexMatch = Regex.Match(sql, @"DROP\s+INDEX\s+\[?(\w+)\]?\s+ON\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (dropIndexMatch.Success)
        {
            var indexName = dropIndexMatch.Groups[1].Value.Trim();
            var schema = dropIndexMatch.Groups[2].Value;
            var table = dropIndexMatch.Groups[3].Value;
            return $"{schema}.{table}.{indexName}";
        }
        
        // Match sp_addextendedproperty for column descriptions
        var extPropMatch = Regex.Match(sql, @"(?:EXECUTE\s+|EXEC\s+)?sp_addextendedproperty.*?@level0name\s*=\s*N?'(\w+)'.*?@level1name\s*=\s*N?'(\w+)'.*?@level2name\s*=\s*N?'(\w+)'", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (extPropMatch.Success)
        {
            var schema = extPropMatch.Groups[1].Value;
            var table = extPropMatch.Groups[2].Value;
            var column = extPropMatch.Groups[3].Value;
            // Use EP_Column_Description format to match what GitChangeDetector creates
            return $"{schema}.{table}.EP_Column_Description_{column}";
        }
        
        // Match sp_dropextendedproperty for column descriptions
        var dropExtPropMatch = Regex.Match(sql, @"(?:EXECUTE\s+|EXEC\s+)?sp_dropextendedproperty\s+@name\s*=\s*N?'([^']+)'.*?@level0name\s*=\s*N?'(\w+)'.*?@level1name\s*=\s*N?'(\w+)'.*?@level2name\s*=\s*N?'(\w+)'", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (dropExtPropMatch.Success)
        {
            var propName = dropExtPropMatch.Groups[1].Value;
            var schema = dropExtPropMatch.Groups[2].Value;
            var table = dropExtPropMatch.Groups[3].Value;
            var column = dropExtPropMatch.Groups[4].Value;
            
            // Format the property name similar to GitChangeDetector
            var formattedPropName = propName == "MS_Description" ? "Column_Description" : propName.Replace(" ", "_");
            return $"{schema}.{table}.EP_{formattedPropName}_{column}";
        }
        
        // Match DROP TABLE
        var dropTableMatch = Regex.Match(sql, @"DROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (dropTableMatch.Success)
        {
            return $"{dropTableMatch.Groups[1].Value}.{dropTableMatch.Groups[2].Value}";
        }
        
        // Check CREATE/ALTER/DROP PROCEDURE before CREATE TABLE to avoid false matches
        var procMatch = Regex.Match(sql, @"(CREATE|ALTER|DROP)\s+PROC(?:EDURE)?\s+(?:IF\s+EXISTS\s+)?\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (procMatch.Success)
        {
            return $"{procMatch.Groups[2].Value}.{procMatch.Groups[3].Value}";
        }
        
        // Match CREATE OR ALTER PROCEDURE
        var createOrAlterProcMatch = Regex.Match(sql, @"CREATE\s+OR\s+ALTER\s+PROC(?:EDURE)?\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (createOrAlterProcMatch.Success)
        {
            return $"{createOrAlterProcMatch.Groups[1].Value}.{createOrAlterProcMatch.Groups[2].Value}";
        }
        
        // Check CREATE/ALTER/DROP FUNCTION before CREATE TABLE
        var funcMatch = Regex.Match(sql, @"(CREATE|ALTER|DROP)\s+FUNCTION\s+(?:IF\s+EXISTS\s+)?\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (funcMatch.Success)
        {
            return $"{funcMatch.Groups[2].Value}.{funcMatch.Groups[3].Value}";
        }
        
        // Match CREATE OR ALTER FUNCTION
        var createOrAlterFuncMatch = Regex.Match(sql, @"CREATE\s+OR\s+ALTER\s+FUNCTION\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (createOrAlterFuncMatch.Success)
        {
            return $"{createOrAlterFuncMatch.Groups[1].Value}.{createOrAlterFuncMatch.Groups[2].Value}";
        }
        
        // Check CREATE/ALTER/DROP VIEW before CREATE TABLE
        var viewMatch = Regex.Match(sql, @"(CREATE|ALTER|DROP)\s+VIEW\s+(?:IF\s+EXISTS\s+)?\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (viewMatch.Success)
        {
            return $"{viewMatch.Groups[2].Value}.{viewMatch.Groups[3].Value}";
        }
        
        // Match CREATE OR ALTER VIEW
        var createOrAlterViewMatch = Regex.Match(sql, @"CREATE\s+OR\s+ALTER\s+VIEW\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (createOrAlterViewMatch.Success)
        {
            return $"{createOrAlterViewMatch.Groups[1].Value}.{createOrAlterViewMatch.Groups[2].Value}";
        }
        
        // Match CREATE TABLE (check last to avoid false matches with CREATE PROCEDURE/FUNCTION/VIEW)
        var createTableMatch = Regex.Match(sql, @"CREATE\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (createTableMatch.Success)
        {
            return $"{createTableMatch.Groups[1].Value}.{createTableMatch.Groups[2].Value}";
        }
        
        // Match sp_rename for tables (without 'OBJECT' parameter or with it)
        var renameTableMatch = Regex.Match(sql, @"(?:EXECUTE\s+|EXEC\s+)?sp_rename\s+'?\[?(\w+)\]?\.\[?(\w+)\]?'?\s*,\s*'([^']+)'(?:\s*,\s*'OBJECT')?", RegexOptions.IgnoreCase);
        if (renameTableMatch.Success && !sql.Contains("'COLUMN'", StringComparison.OrdinalIgnoreCase))
        {
            // Return the original table name that's being renamed
            return $"{renameTableMatch.Groups[1].Value}.{renameTableMatch.Groups[2].Value}";
        }
        
        return null;
    }
}