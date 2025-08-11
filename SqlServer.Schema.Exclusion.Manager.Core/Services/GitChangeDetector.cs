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
        
        // Parse CREATE TABLE operations
        var createTableMatches = Regex.Matches(content, @"CREATE\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in createTableMatches)
        {
            var schema = match.Groups[1].Value;
            var table = match.Groups[2].Value;
            
            changes.Add(new ManifestChange
            {
                Identifier = $"{schema}.{table}",
                Description = "added",
                ObjectType = "Table"
            });
        }
        
        // Parse DROP TABLE operations
        var dropTableMatches = Regex.Matches(content, @"DROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in dropTableMatches)
        {
            var schema = match.Groups[1].Value;
            var table = match.Groups[2].Value;
            
            changes.Add(new ManifestChange
            {
                Identifier = $"{schema}.{table}",
                Description = "removed",
                ObjectType = "Table"
            });
        }
        
        // Parse table renames from EXEC sp_rename (without 'COLUMN' parameter)
        var tableRenameMatches = Regex.Matches(content, @"EXEC\s+sp_rename\s+'?\[?(\w+)\]?\.\[?(\w+)\]?'?,\s+'?(\w+)'?\s*(?:,\s+'OBJECT')?\s*(?:;|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        foreach (Match match in tableRenameMatches)
        {
            var schema = match.Groups[1].Value;
            var oldTable = match.Groups[2].Value;
            var newTable = match.Groups[3].Value;
            
            changes.Add(new ManifestChange
            {
                Identifier = $"{schema}.{oldTable}",
                Description = $"renamed to {newTable}",
                ObjectType = "Table"
            });
        }
        
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
            
            // Check if this constraint was dropped earlier in the same migration (it's a modification, already handled above)
            // Only add as "added" if it wasn't dropped (i.e., it's a new constraint)
            if (!content.Contains($"DROP CONSTRAINT [{constraintName}]") && !content.Contains($"DROP CONSTRAINT {constraintName}"))
            {
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{table}.{constraintName}",
                    Description = "added",
                    ObjectType = "Constraint"
                });
            }
            // If it was dropped, it's already been handled as a modification in the DROP section
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
            
            // Check if this constraint is being re-added (modification)
            if (content.Contains($"ADD CONSTRAINT [{constraintName}]") || content.Contains($"ADD CONSTRAINT {constraintName}"))
            {
                // It's being modified (dropped and re-added)
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{table}.{constraintName}",
                    Description = "modified",
                    ObjectType = "Constraint"
                });
            }
            else
            {
                // It's only being dropped
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
            
            // Only add as "added" if it wasn't dropped (i.e., it's a new index)
            if (!Regex.IsMatch(content, $@"DROP\s+INDEX\s+\[?{Regex.Escape(indexName)}\]?\s+ON\s+\[?{Regex.Escape(schema)}\]?\.\[?{Regex.Escape(table)}\]?", RegexOptions.IgnoreCase))
            {
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{table}.{indexName}",
                    Description = "added",
                    ObjectType = "Index"
                });
            }
            // If it was dropped, it's already been handled as a modification in the DROP section
        }
        
        // Parse index drops
        var indexDropMatches = Regex.Matches(content, @"DROP\s+INDEX\s+\[?(\w+)\]?\s+ON\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in indexDropMatches)
        {
            var indexName = match.Groups[1].Value;
            var schema = match.Groups[2].Value;
            var table = match.Groups[3].Value;
            
            // Check if this index is being recreated (modification)
            if (Regex.IsMatch(content, $@"CREATE\s+(?:UNIQUE\s+|NONCLUSTERED\s+|CLUSTERED\s+)?INDEX\s+\[?{Regex.Escape(indexName)}\]?\s+ON\s+\[?{Regex.Escape(schema)}\]?\.\[?{Regex.Escape(table)}\]?", RegexOptions.IgnoreCase))
            {
                // It's being modified (dropped and recreated)
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{table}.{indexName}",
                    Description = "modified",
                    ObjectType = "Index"
                });
            }
            else
            {
                // It's only being dropped
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{table}.{indexName}",
                    Description = "removed",
                    ObjectType = "Index"
                });
            }
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
        
        // Parse extended property drops
        var extPropDropMatches = Regex.Matches(content, @"EXECUTE\s+sp_dropextendedproperty\s+@name\s*=\s*N'(\w+)'.*?@level0name\s*=\s*N'(\w+)'.*?@level1name\s*=\s*N'(\w+)'.*?@level2name\s*=\s*N'(\w+)'", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match match in extPropDropMatches)
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
                Description = "removed",
                ObjectType = "ExtendedProperty"
            });
        }
        
        // Parse VIEW operations
        // Match CREATE VIEW (but not CREATE OR ALTER VIEW)
        var createViewMatches = Regex.Matches(content, @"CREATE\s+(?!OR\s+ALTER\s+)VIEW\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in createViewMatches)
        {
            var schema = match.Groups[1].Value;
            var viewName = match.Groups[2].Value;
            
            // Only add as "added" if it wasn't dropped (i.e., it's a new view)
            if (!Regex.IsMatch(content, $@"DROP\s+VIEW\s+(?:IF\s+EXISTS\s+)?\[?{Regex.Escape(schema)}\]?\.\[?{Regex.Escape(viewName)}\]?", RegexOptions.IgnoreCase))
            {
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{viewName}",
                    Description = "added",
                    ObjectType = "View"
                });
            }
            // If it was dropped, it's already been handled as a modification in the DROP section
        }
        
        // Match CREATE OR ALTER VIEW separately
        var createOrAlterViewMatches = Regex.Matches(content, @"CREATE\s+OR\s+ALTER\s+VIEW\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in createOrAlterViewMatches)
        {
            var schema = match.Groups[1].Value;
            var viewName = match.Groups[2].Value;
            
            changes.Add(new ManifestChange
            {
                Identifier = $"{schema}.{viewName}",
                Description = "modified",
                ObjectType = "View"
            });
        }
        
        // Match ALTER VIEW separately (but not CREATE OR ALTER which is already handled above)
        var alterViewMatches = Regex.Matches(content, @"ALTER\s+VIEW\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in alterViewMatches)
        {
            var schema = match.Groups[1].Value;
            var viewName = match.Groups[2].Value;
            
            changes.Add(new ManifestChange
            {
                Identifier = $"{schema}.{viewName}",
                Description = "modified",
                ObjectType = "View"
            });
        }
        
        var dropViewMatches = Regex.Matches(content, @"DROP\s+VIEW\s+(?:IF\s+EXISTS\s+)?\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in dropViewMatches)
        {
            var schema = match.Groups[1].Value;
            var viewName = match.Groups[2].Value;
            
            // Check if this view is being recreated (modification)
            if (Regex.IsMatch(content, $@"CREATE\s+(?:OR\s+ALTER\s+)?VIEW\s+\[?{Regex.Escape(schema)}\]?\.\[?{Regex.Escape(viewName)}\]?", RegexOptions.IgnoreCase))
            {
                // It's being modified (dropped and recreated)
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{viewName}",
                    Description = "modified",
                    ObjectType = "View"
                });
            }
            else
            {
                // It's only being dropped
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{viewName}",
                    Description = "removed",
                    ObjectType = "View"
                });
            }
        }
        
        // Parse STORED PROCEDURE operations
        // Match CREATE PROC (but not CREATE OR ALTER PROC)
        var createProcMatches = Regex.Matches(content, @"CREATE\s+(?!OR\s+ALTER\s+)PROC(?:EDURE)?\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in createProcMatches)
        {
            var schema = match.Groups[1].Value;
            var procName = match.Groups[2].Value;
            
            // Only add as "added" if it wasn't dropped (i.e., it's a new procedure)
            if (!Regex.IsMatch(content, $@"DROP\s+PROC(?:EDURE)?\s+(?:IF\s+EXISTS\s+)?\[?{Regex.Escape(schema)}\]?\.\[?{Regex.Escape(procName)}\]?", RegexOptions.IgnoreCase))
            {
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{procName}",
                    Description = "added",
                    ObjectType = "StoredProcedure"
                });
            }
            // If it was dropped, it's already been handled as a modification in the DROP section
        }
        
        // Match CREATE OR ALTER PROC separately
        var createOrAlterProcMatches = Regex.Matches(content, @"CREATE\s+OR\s+ALTER\s+PROC(?:EDURE)?\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in createOrAlterProcMatches)
        {
            var schema = match.Groups[1].Value;
            var procName = match.Groups[2].Value;
            
            changes.Add(new ManifestChange
            {
                Identifier = $"{schema}.{procName}",
                Description = "modified",
                ObjectType = "StoredProcedure"
            });
        }
        
        // Match ALTER PROC separately (but not CREATE OR ALTER which is already handled above)
        var alterProcMatches = Regex.Matches(content, @"ALTER\s+PROC(?:EDURE)?\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in alterProcMatches)
        {
            var schema = match.Groups[1].Value;
            var procName = match.Groups[2].Value;
            
            changes.Add(new ManifestChange
            {
                Identifier = $"{schema}.{procName}",
                Description = "modified",
                ObjectType = "StoredProcedure"
            });
        }
        
        var dropProcMatches = Regex.Matches(content, @"DROP\s+PROC(?:EDURE)?\s+(?:IF\s+EXISTS\s+)?\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in dropProcMatches)
        {
            var schema = match.Groups[1].Value;
            var procName = match.Groups[2].Value;
            
            // Check if this procedure is being recreated (modification)
            if (Regex.IsMatch(content, $@"CREATE\s+(?:OR\s+ALTER\s+)?PROC(?:EDURE)?\s+\[?{Regex.Escape(schema)}\]?\.\[?{Regex.Escape(procName)}\]?", RegexOptions.IgnoreCase))
            {
                // It's being modified (dropped and recreated)
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{procName}",
                    Description = "modified",
                    ObjectType = "StoredProcedure"
                });
            }
            else
            {
                // It's only being dropped
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{procName}",
                    Description = "removed",
                    ObjectType = "StoredProcedure"
                });
            }
        }
        
        // Parse FUNCTION operations
        // Match CREATE FUNCTION (but not CREATE OR ALTER FUNCTION)
        // Functions can have parameters right after the name, so match until (
        var createFuncMatches = Regex.Matches(content, @"CREATE\s+(?!OR\s+ALTER\s+)FUNCTION\s+\[?(\w+)\]?\.\[?(\w+)\]?\s*\(", RegexOptions.IgnoreCase);
        foreach (Match match in createFuncMatches)
        {
            var schema = match.Groups[1].Value;
            var funcName = match.Groups[2].Value;
            
            // Only add as "added" if it wasn't dropped (i.e., it's a new function)
            if (!Regex.IsMatch(content, $@"DROP\s+FUNCTION\s+(?:IF\s+EXISTS\s+)?\[?{Regex.Escape(schema)}\]?\.\[?{Regex.Escape(funcName)}\]?", RegexOptions.IgnoreCase))
            {
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{funcName}",
                    Description = "added",
                    ObjectType = "Function"
                });
            }
            // If it was dropped, it's already been handled as a modification in the DROP section
        }
        
        // Match CREATE OR ALTER FUNCTION separately
        var createOrAlterFuncMatches = Regex.Matches(content, @"CREATE\s+OR\s+ALTER\s+FUNCTION\s+\[?(\w+)\]?\.\[?(\w+)\]?\s*\(", RegexOptions.IgnoreCase);
        foreach (Match match in createOrAlterFuncMatches)
        {
            var schema = match.Groups[1].Value;
            var funcName = match.Groups[2].Value;
            
            changes.Add(new ManifestChange
            {
                Identifier = $"{schema}.{funcName}",
                Description = "modified",
                ObjectType = "Function"
            });
        }
        
        // Match ALTER FUNCTION separately (but not CREATE OR ALTER which is already handled above)
        var alterFuncMatches = Regex.Matches(content, @"ALTER\s+FUNCTION\s+\[?(\w+)\]?\.\[?(\w+)\]?\s*\(", RegexOptions.IgnoreCase);
        foreach (Match match in alterFuncMatches)
        {
            var schema = match.Groups[1].Value;
            var funcName = match.Groups[2].Value;
            
            changes.Add(new ManifestChange
            {
                Identifier = $"{schema}.{funcName}",
                Description = "modified",
                ObjectType = "Function"
            });
        }
        
        var dropFuncMatches = Regex.Matches(content, @"DROP\s+FUNCTION\s+(?:IF\s+EXISTS\s+)?\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        foreach (Match match in dropFuncMatches)
        {
            var schema = match.Groups[1].Value;
            var funcName = match.Groups[2].Value;
            
            // Check if this function is being recreated (modification)
            if (Regex.IsMatch(content, $@"CREATE\s+(?:OR\s+ALTER\s+)?FUNCTION\s+\[?{Regex.Escape(schema)}\]?\.\[?{Regex.Escape(funcName)}\]?\s*\(", RegexOptions.IgnoreCase))
            {
                // It's being modified (dropped and recreated)
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{funcName}",
                    Description = "modified",
                    ObjectType = "Function"
                });
            }
            else
            {
                // It's only being dropped
                changes.Add(new ManifestChange
                {
                    Identifier = $"{schema}.{funcName}",
                    Description = "removed",
                    ObjectType = "Function"
                });
            }
        }
        
        // Remove duplicates - keep the first occurrence of each identifier
        // This handles cases where CREATE OR ALTER might match both CREATE and ALTER patterns
        var uniqueChanges = new List<ManifestChange>();
        var seenIdentifiers = new HashSet<string>();
        
        foreach (var change in changes)
        {
            if (seenIdentifiers.Add(change.Identifier))
            {
                uniqueChanges.Add(change);
            }
        }
        
        return uniqueChanges;
    }
    
    private string NormalizeColumnDefinition(string definition)
    {
        // Remove extra spaces and normalize the definition for comparison
        return Regex.Replace(definition.Trim().ToUpper(), @"\s+", " ");
    }
}