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
        
        // Get diff between origin/main and HEAD
        var originMain = repo.Branches["origin/main"];
        if (originMain == null)
            throw new InvalidOperationException("Could not find origin/main branch");
            
        var diff = repo.Diff.Compare<TreeChanges>(originMain.Tip.Tree, repo.Head.Tip.Tree);
        
        // Filter for the specific database path
        var dbPath = Path.Combine("servers", serverName, databaseName).Replace('\\', '/');
        
        foreach (var change in diff)
        {
            if (!change.Path.StartsWith(dbPath))
                continue;
                
            // Skip migration files
            if (change.Path.Contains("/z_migrations/") || change.Path.Contains("/z_migrations_reverse/"))
                continue;
                
            var manifestChange = await AnalyzeFileChangeAsync(change, repo);
            if (manifestChange != null)
                changes.Add(manifestChange);
        }
        
        return changes;
    }
    
    private async Task<ManifestChange?> AnalyzeFileChangeAsync(TreeEntryChanges change, Repository repo)
    {
        // Extract object info from file path
        var pathParts = change.Path.Split('/');
        if (pathParts.Length < 4)
            return null;
            
        var fileName = Path.GetFileNameWithoutExtension(pathParts[^1]);
        var objectType = DetermineObjectType(pathParts);
        
        if (objectType == null)
            return null;
            
        ManifestChange? manifestChange = null;
        
        // Handle different change types
        switch (change.Status)
        {
            case ChangeKind.Added:
                manifestChange = new ManifestChange
                {
                    Identifier = BuildIdentifier(pathParts, fileName),
                    Description = $"{objectType} added",
                    FilePath = change.Path,
                    ObjectType = objectType
                };
                break;
                
            case ChangeKind.Deleted:
                manifestChange = new ManifestChange
                {
                    Identifier = BuildIdentifier(pathParts, fileName),
                    Description = $"{objectType} removed",
                    FilePath = change.Path,
                    ObjectType = objectType
                };
                break;
                
            case ChangeKind.Modified:
                var changeDetails = await AnalyzeModificationAsync(change, repo);
                manifestChange = new ManifestChange
                {
                    Identifier = BuildIdentifier(pathParts, fileName),
                    Description = changeDetails ?? $"{objectType} definition changed",
                    FilePath = change.Path,
                    ObjectType = objectType
                };
                break;
                
            case ChangeKind.Renamed:
                var oldFileName = Path.GetFileNameWithoutExtension(change.OldPath.Split('/')[^1]);
                manifestChange = new ManifestChange
                {
                    Identifier = BuildIdentifier(pathParts, oldFileName),
                    Description = $"{objectType} renamed to {fileName}",
                    FilePath = change.Path,
                    ObjectType = objectType
                };
                break;
        }
        
        return manifestChange;
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
    
    private Task<string?> AnalyzeModificationAsync(TreeEntryChanges change, Repository repo)
    {
        try
        {
            // Get file content from both commits
            var oldBlob = repo.Lookup<Blob>(change.OldOid);
            var newBlob = repo.Lookup<Blob>(change.Oid);
            
            if (oldBlob == null || newBlob == null)
                return Task.FromResult<string?>(null);
                
            var oldContent = oldBlob.GetContentText();
            var newContent = newBlob.GetContentText();
            
            // Try to detect specific changes
            if (change.Path.Contains("/Tables/"))
            {
                return Task.FromResult<string?>(AnalyzeTableChanges(oldContent, newContent));
            }
            
            return Task.FromResult<string?>(null);
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }
    
    private string? AnalyzeTableChanges(string oldContent, string newContent)
    {
        // Simple regex to detect column type changes
        var columnRegex = new Regex(@"\[(\w+)\]\s+(\w+(?:\([^)]+\))?)", RegexOptions.IgnoreCase);
        
        var oldColumns = columnRegex.Matches(oldContent).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        var newColumns = columnRegex.Matches(newContent).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        
        foreach (var col in oldColumns)
        {
            if (newColumns.ContainsKey(col.Key) && newColumns[col.Key] != col.Value)
            {
                return $"Column type changed from {col.Value} to {newColumns[col.Key]}";
            }
        }
        
        // Check for new columns
        var addedColumns = newColumns.Keys.Except(oldColumns.Keys).ToList();
        if (addedColumns.Any())
        {
            return $"Column added";
        }
        
        return "Table definition changed";
    }
}