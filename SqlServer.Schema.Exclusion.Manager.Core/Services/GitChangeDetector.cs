using LibGit2Sharp;
using SqlServer.Schema.Exclusion.Manager.Core.Models;
using System.Text.RegularExpressions;

namespace SqlServer.Schema.Exclusion.Manager.Core.Services;

public class GitChangeDetector
{
    private readonly string _repositoryPath;
    
    public GitChangeDetector(string repositoryPath)
    {
        _repositoryPath = repositoryPath;
    }
    
    public async Task<List<ManifestChange>> DetectChangesAsync(string serverName, string databaseName)
    {
        var changes = new List<ManifestChange>();
        
        using var repo = new Repository(_repositoryPath);
        
        // Get diff between origin/main and working directory (including uncommitted changes)
        var originMain = repo.Branches["origin/main"];
        if (originMain == null)
            throw new InvalidOperationException("Could not find origin/main branch");
        
        // First, get diff between origin/main and HEAD (committed changes)
        var committedDiff = repo.Diff.Compare<TreeChanges>(originMain.Tip.Tree, repo.Head.Tip.Tree);
        
        // Then, get uncommitted changes in the working directory
        var workingDiff = repo.Diff.Compare<TreeChanges>(repo.Head.Tip.Tree, DiffTargets.WorkingDirectory);
        
        // Combine both diffs to get all changes from origin/main to working directory
        var allChanges = new List<TreeEntryChanges>();
        allChanges.AddRange(committedDiff);
        allChanges.AddRange(workingDiff);
        
        // Filter for the specific database path
        var dbPath = Path.Combine("servers", serverName, databaseName).Replace('\\', '/');
        
        // Use a HashSet to avoid duplicates (a file might appear in both diffs)
        var processedPaths = new HashSet<string>();
        
        foreach (var change in allChanges)
        {
            if (!change.Path.StartsWith(dbPath))
                continue;
                
            // Skip migration files
            if (change.Path.Contains("/z_migrations/") || change.Path.Contains("/z_migrations_reverse/"))
                continue;
            
            // Skip if we've already processed this path
            if (!processedPaths.Add(change.Path))
                continue;
                
            // Now returns a list of changes
            var manifestChanges = await AnalyzeFileChangeAsync(change, repo);
            changes.AddRange(manifestChanges);
        }
        
        return changes;
    }
    
    private async Task<List<ManifestChange>> AnalyzeFileChangeAsync(TreeEntryChanges change, Repository repo)
    {
        var results = new List<ManifestChange>();
        
        // Extract object info from file path
        var pathParts = change.Path.Split('/');
        if (pathParts.Length < 4)
            return results;
            
        var fileName = Path.GetFileNameWithoutExtension(pathParts[^1]);
        var objectType = DetermineObjectType(pathParts);
        
        if (objectType == null)
            return results;
        
        var baseIdentifier = BuildIdentifier(pathParts, fileName);
        
        // Handle different change types
        switch (change.Status)
        {
            case ChangeKind.Added:
                results.Add(new ManifestChange
                {
                    Identifier = baseIdentifier,
                    Description = "added",
                    FilePath = change.Path,
                    ObjectType = objectType
                });
                break;
                
            case ChangeKind.Deleted:
                results.Add(new ManifestChange
                {
                    Identifier = baseIdentifier,
                    Description = "removed",
                    FilePath = change.Path,
                    ObjectType = objectType
                });
                break;
                
            case ChangeKind.Modified:
                var modifications = await AnalyzeModificationAsync(change, repo, baseIdentifier);
                foreach (var (identifier, description) in modifications)
                {
                    results.Add(new ManifestChange
                    {
                        Identifier = identifier,
                        Description = description,
                        FilePath = change.Path,
                        ObjectType = objectType
                    });
                }
                break;
                
            case ChangeKind.Renamed:
                var oldFileName = Path.GetFileNameWithoutExtension(change.OldPath.Split('/')[^1]);
                results.Add(new ManifestChange
                {
                    Identifier = BuildIdentifier(pathParts, oldFileName),
                    Description = $"renamed to {fileName}",
                    FilePath = change.Path,
                    ObjectType = objectType
                });
                break;
        }
        
        return results;
    }
    
    private string? DetermineObjectType(string[] pathParts)
    {
        if (pathParts.Length < 4)
            return null;
        
        var fileName = pathParts[^1];
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        
        // Check file prefixes first for more specific identification
        if (fileNameWithoutExt.StartsWith("IDX_", StringComparison.OrdinalIgnoreCase))
            return "Index";
        if (fileNameWithoutExt.StartsWith("TBL_", StringComparison.OrdinalIgnoreCase))
            return "Table";
        if (fileNameWithoutExt.StartsWith("VW_", StringComparison.OrdinalIgnoreCase))
            return "View";
        if (fileNameWithoutExt.StartsWith("SP_", StringComparison.OrdinalIgnoreCase))
            return "Stored Procedure";
        if (fileNameWithoutExt.StartsWith("FN_", StringComparison.OrdinalIgnoreCase))
            return "Function";
        if (fileNameWithoutExt.StartsWith("FK_", StringComparison.OrdinalIgnoreCase) || 
            fileNameWithoutExt.StartsWith("PK_", StringComparison.OrdinalIgnoreCase) ||
            fileNameWithoutExt.StartsWith("DF_", StringComparison.OrdinalIgnoreCase) ||
            fileNameWithoutExt.StartsWith("CK_", StringComparison.OrdinalIgnoreCase) ||
            fileNameWithoutExt.StartsWith("UQ_", StringComparison.OrdinalIgnoreCase))
            return "Constraint";
            
        // Fall back to path-based detection
        foreach (var part in pathParts)
        {
            switch (part.ToLower())
            {
                case "tables":
                    return "Table";
                case "views":
                    return "View";
                case "indexes":
                    return "Index";
                case "storedprocedures":
                    return "Stored Procedure";
                case "functions":
                    return "Function";
                case "constraints":
                    return "Constraint";
            }
        }
        
        return null;
    }
    
    private string BuildIdentifier(string[] pathParts, string objectName)
    {
        // Extract schema from path
        var schema = "dbo"; // default
        
        // Look for schemas folder
        var schemasIndex = Array.IndexOf(pathParts, "schemas");
        if (schemasIndex >= 0 && schemasIndex + 1 < pathParts.Length)
        {
            schema = pathParts[schemasIndex + 1];
        }
        
        return $"{schema}.{objectName}";
    }
    
    private Task<List<(string identifier, string description)>> AnalyzeModificationAsync(TreeEntryChanges change, Repository repo, string baseIdentifier)
    {
        try
        {
            // Get file content from both commits
            var oldBlob = repo.Lookup<Blob>(change.OldOid);
            var newBlob = repo.Lookup<Blob>(change.Oid);
            
            if (oldBlob == null || newBlob == null)
                return Task.FromResult(new List<(string, string)>());
                
            var oldContent = oldBlob.GetContentText();
            var newContent = newBlob.GetContentText();
            
            // Try to detect specific changes
            if (change.Path.Contains("/Tables/"))
            {
                var tableChanges = AnalyzeTableChanges(oldContent, newContent);
                var results = new List<(string identifier, string description)>();
                
                foreach (var (columnName, changeType) in tableChanges)
                {
                    // Create column-specific identifier
                    var columnIdentifier = $"{baseIdentifier}.{columnName}";
                    results.Add((columnIdentifier, changeType));
                }
                
                // If no specific column changes detected, return a generic table change
                if (!results.Any())
                {
                    results.Add((baseIdentifier, "modified"));
                }
                
                return Task.FromResult(results);
            }
            
            return Task.FromResult(new List<(string, string)> { (baseIdentifier, "modified") });
        }
        catch
        {
            return Task.FromResult(new List<(string, string)>());
        }
    }
    
    private List<(string columnName, string changeType)> AnalyzeTableChanges(string oldContent, string newContent)
    {
        var changes = new List<(string columnName, string changeType)>();
        
        // Enhanced regex to capture column names and their full definitions including constraints
        var columnRegex = new Regex(@"\[(\w+)\]\s+(\w+(?:\([^)]+\))?(?:\s+(?:IDENTITY(?:\([^)]+\))?\s*)?(?:NOT\s+)?NULL)?)", RegexOptions.IgnoreCase);
        
        var oldColumns = columnRegex.Matches(oldContent).ToDictionary(m => m.Groups[1].Value.ToLower(), m => m.Groups[2].Value);
        var newColumns = columnRegex.Matches(newContent).ToDictionary(m => m.Groups[1].Value.ToLower(), m => m.Groups[2].Value);
        
        // Check for removed columns first
        var removedColumns = oldColumns.Keys.Except(newColumns.Keys).ToList();
        foreach (var col in removedColumns)
        {
            changes.Add((col, "removed"));
        }
        
        // Check for added columns
        var addedColumns = newColumns.Keys.Except(oldColumns.Keys).ToList();
        foreach (var col in addedColumns)
        {
            changes.Add((col, "added"));
        }
        
        // Check for modified columns (type changes)
        foreach (var col in oldColumns)
        {
            if (newColumns.ContainsKey(col.Key))
            {
                // Normalize the column definitions for comparison
                var oldDef = NormalizeColumnDefinition(col.Value);
                var newDef = NormalizeColumnDefinition(newColumns[col.Key]);
                
                if (oldDef != newDef)
                {
                    changes.Add((col.Key, "modified"));
                }
            }
        }
        
        return changes;
    }

    public async Task<List<ManifestChange>> ParseMigrationFileAsync(string migrationFilePath, string targetServer, string targetDatabase)
    {
        var changes = new List<ManifestChange>();
        
        if (!File.Exists(migrationFilePath))
            return changes;
            
        var content = await File.ReadAllTextAsync(migrationFilePath);
        
        // Parse column renames from EXEC sp_rename
        var renameMatches = Regex.Matches(content, @"EXEC\s+sp_rename\s+'?\[?(\w+)\]?\.\[?(\w+)\]?\.\[?(\w+)\]?'?,\s+'?(\w+)'?,\s+'COLUMN'", RegexOptions.IgnoreCase);
        foreach (Match match in renameMatches)
        {
            var schema = match.Groups[1].Value;
            var table = match.Groups[2].Value;
            var oldColumn = match.Groups[3].Value;
            var newColumn = match.Groups[4].Value;
            
            // Add both the removal of old and addition of new
            changes.Add(new ManifestChange
            {
                Identifier = $"{schema}.{table}.{oldColumn}",
                Description = $"renamed to {newColumn}",
                ObjectType = "Table"
            });
        }
        
        // Parse column additions from ALTER TABLE ADD (but not ADD CONSTRAINT)
        var addMatches = Regex.Matches(content, @"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+ADD\s+(?!CONSTRAINT)\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in addMatches)
        {
            var schema = match.Groups[1].Value;
            var table = match.Groups[2].Value;
            var column = match.Groups[3].Value;
            
            changes.Add(new ManifestChange
            {
                Identifier = $"{schema}.{table}.{column}",
                Description = "added",
                ObjectType = "Table"
            });
        }
        
        // Parse constraint additions from ALTER TABLE ADD CONSTRAINT
        var constraintAddMatches = Regex.Matches(content, @"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+ADD\s+CONSTRAINT\s+\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in constraintAddMatches)
        {
            var schema = match.Groups[1].Value;
            var table = match.Groups[2].Value;
            var constraintName = match.Groups[3].Value;
            
            // Check if this constraint was dropped earlier in the same migration (it's a recreation, not a new addition)
            if (!content.Contains($"DROP CONSTRAINT [{constraintName}]") && !content.Contains($"DROP CONSTRAINT {constraintName}"))
            {
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{table}.{constraintName}",
                    Description = "added",
                    ObjectType = "Constraint"
                });
            }
        }
        
        // Parse column drops from ALTER TABLE DROP COLUMN
        var dropMatches = Regex.Matches(content, @"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+DROP\s+COLUMN\s+\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in dropMatches)
        {
            var schema = match.Groups[1].Value;
            var table = match.Groups[2].Value;
            var column = match.Groups[3].Value;
            
            changes.Add(new ManifestChange
            {
                Identifier = $"{schema}.{table}.{column}",
                Description = "removed",
                ObjectType = "Table"
            });
        }
        
        // Parse column modifications from ALTER TABLE ALTER COLUMN
        var alterMatches = Regex.Matches(content, @"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+ALTER\s+COLUMN\s+\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in alterMatches)
        {
            var schema = match.Groups[1].Value;
            var table = match.Groups[2].Value;
            var column = match.Groups[3].Value;
            
            changes.Add(new ManifestChange
            {
                Identifier = $"{schema}.{table}.{column}",
                Description = "modified",
                ObjectType = "Table"
            });
        }
        
        // Parse constraint drops (like DF_test_migrations_gsdf)
        var constraintDropMatches = Regex.Matches(content, @"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+DROP\s+CONSTRAINT\s+\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in constraintDropMatches)
        {
            var schema = match.Groups[1].Value;
            var table = match.Groups[2].Value;
            var constraintName = match.Groups[3].Value;
            
            // Only add if it's being dropped without being re-added
            if (!content.Contains($"ADD CONSTRAINT [{constraintName}]") && !content.Contains($"ADD CONSTRAINT {constraintName}"))
            {
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{table}.{constraintName}",
                    Description = "removed",
                    ObjectType = "Constraint"
                });
            }
        }
        
        // Parse index creations
        var indexMatches = Regex.Matches(content, @"CREATE\s+(?:UNIQUE\s+|NONCLUSTERED\s+|CLUSTERED\s+)?INDEX\s+\[?(\w+)\]?\s+ON\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in indexMatches)
        {
            var indexName = match.Groups[1].Value;
            var schema = match.Groups[2].Value;
            var table = match.Groups[3].Value;
            
            changes.Add(new ManifestChange
            {
                Identifier = $"{schema}.{table}.{indexName}",
                Description = "added",
                ObjectType = "Index"
            });
        }
        
        // Parse extended properties (like EP_Column_Description_zura)
        var extPropMatches = Regex.Matches(content, @"EXECUTE\s+sp_addextendedproperty\s+@name\s*=\s*N'(\w+)'.*?@level0name\s*=\s*N'(\w+)'.*?@level1name\s*=\s*N'(\w+)'.*?@level2name\s*=\s*N'(\w+)'", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match match in extPropMatches)
        {
            var propName = match.Groups[1].Value;
            var schema = match.Groups[2].Value;
            var tableName = match.Groups[3].Value;
            var columnName = match.Groups[4].Value;
            
            // Use a descriptive identifier that includes table name
            var propIdentifier = propName == "MS_Description" ? $"EP_Column_Description_{columnName}" : $"EP_{propName}_{columnName}";
            
            changes.Add(new ManifestChange
            {
                Identifier = $"{schema}.{tableName}.{propIdentifier}",
                Description = "added",
                ObjectType = "ExtendedProperty"
            });
        }
        
        return changes;
    }
    
    private string NormalizeColumnDefinition(string definition)
    {
        // Remove extra spaces and normalize the definition for comparison
        return Regex.Replace(definition.Trim().ToUpper(), @"\s+", " ");
    }
}