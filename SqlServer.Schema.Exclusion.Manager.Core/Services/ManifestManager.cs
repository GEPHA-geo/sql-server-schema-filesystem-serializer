using LibGit2Sharp;
using SqlServer.Schema.Exclusion.Manager.Core.Models;

namespace SqlServer.Schema.Exclusion.Manager.Core.Services;

public class ManifestManager
{
    private readonly ManifestFileHandler _fileHandler;
    private readonly GitChangeDetector _changeDetector;
    private readonly ExclusionCommentUpdater _commentUpdater;
    private readonly string _outputPath;
    
    public ManifestManager(string outputPath)
    {
        _outputPath = outputPath;
        _fileHandler = new ManifestFileHandler();
        // GitChangeDetector needs the repository path, not the output path
        // The repository is at the output path location
        _changeDetector = new GitChangeDetector(outputPath);
        _commentUpdater = new ExclusionCommentUpdater();
    }
    
    public async Task<bool> CreateOrUpdateManifestAsync(string sourceServer, string sourceDatabase, string targetServer, string targetDatabase)
    {
        try
        {
            var manifestPath = GetManifestPath(sourceServer, sourceDatabase, targetServer, targetDatabase);
            var manifestDir = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(manifestDir))
                Directory.CreateDirectory(manifestDir);
            
            // Check for existing manifest
            var existingManifest = await _fileHandler.ReadManifestAsync(manifestPath);
            
            // Detect current changes in target location
            // For now, just get changes from migration file to avoid slow git operations
            var detectedChanges = new List<ManifestChange>();
            
            // Also parse the latest migration file for additional changes
            var migrationsPath = Path.Combine(_outputPath, "servers", targetServer, targetDatabase, "z_migrations");
            if (Directory.Exists(migrationsPath))
            {
                var migrationFiles = Directory.GetFiles(migrationsPath, "*.sql")
                    .OrderByDescending(f => f)
                    .ToList();
                    
                if (migrationFiles.Any())
                {
                    var latestMigration = migrationFiles.First();
                    Console.WriteLine($"Parsing migration file for changes: {Path.GetFileName(latestMigration)}");
                    var migrationChanges = await _changeDetector.ParseMigrationFileAsync(latestMigration, targetServer, targetDatabase);
                    
                    // Merge migration changes with detected changes
                    // Migration changes are more specific, so they should take precedence
                    var existingIdentifiers = detectedChanges.Select(c => c.Identifier).ToHashSet();
                    
                    foreach (var migrationChange in migrationChanges)
                    {
                        // If we don't already have this specific change, add it
                        if (!existingIdentifiers.Contains(migrationChange.Identifier))
                        {
                            detectedChanges.Add(migrationChange);
                        }
                        else
                        {
                            // Update the existing change with more specific info from migration
                            var existing = detectedChanges.FirstOrDefault(c => c.Identifier == migrationChange.Identifier);
                            if (existing != null && migrationChange.Description != existing.Description)
                            {
                                // Keep the more specific description from migration file
                                existing.Description = migrationChange.Description;
                            }
                        }
                    }
                    
                    Console.WriteLine($"  Found {migrationChanges.Count} additional changes from migration file");
                }
            }
            
            ChangeManifest manifest;
            
            if (existingManifest != null)
            {
                // Merge with existing
                manifest = MergeManifests(existingManifest, detectedChanges);
            }
            else
            {
                // Create new manifest with all changes included
                manifest = new ChangeManifest
                {
                    DatabaseName = sourceDatabase,
                    ServerName = sourceServer,
                    Generated = DateTime.UtcNow,
                    CommitHash = GetCurrentCommitHash(),
                    RotationMarker = '/' // Initial marker can be either '/' or '\\'
                };
                manifest.IncludedChanges.AddRange(detectedChanges);
            }
            
            // Write manifest
            await _fileHandler.WriteManifestAsync(manifestPath, manifest);
            
            // Update exclusion comments in serialized files at target location
            await _commentUpdater.UpdateSerializedFilesAsync(_outputPath, targetServer, targetDatabase, manifest);
            
            Console.WriteLine($"Manifest created/updated: {manifestPath}");
            Console.WriteLine($"Total changes: {manifest.IncludedChanges.Count + manifest.ExcludedChanges.Count}");
            Console.WriteLine($"Included: {manifest.IncludedChanges.Count}");
            Console.WriteLine($"Excluded: {manifest.ExcludedChanges.Count}");
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error managing manifest: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> UpdateExclusionCommentsAsync(string sourceServer, string sourceDatabase, string targetServer, string targetDatabase)
    {
        try
        {
            var manifestPath = GetManifestPath(sourceServer, sourceDatabase, targetServer, targetDatabase);
            var manifest = await _fileHandler.ReadManifestAsync(manifestPath);
            
            if (manifest == null)
            {
                Console.WriteLine($"No manifest found at: {manifestPath}");
                return false;
            }
            
            Console.WriteLine($"Using manifest: {Path.GetFileName(manifestPath)}");
            Console.WriteLine($"  Excluded changes: {manifest.ExcludedChanges.Count}");
            Console.WriteLine($"  Included changes: {manifest.IncludedChanges.Count}");
            
            // Update exclusion comments in serialized files at target location
            Console.WriteLine("\nUpdating serialized files...");
            await _commentUpdater.UpdateSerializedFilesAsync(_outputPath, targetServer, targetDatabase, manifest);
            
            // Find and update migration scripts in target location
            var migrationsPath = Path.Combine(_outputPath, "servers", targetServer, targetDatabase, "z_migrations");
            if (Directory.Exists(migrationsPath))
            {
                Console.WriteLine("\nUpdating migration scripts...");
                
                // Get the commit hash from manifest to find the cutoff point
                var commitHash = manifest.CommitHash;
                
                // Get migration files and only process recent ones
                var migrationFiles = Directory.GetFiles(migrationsPath, "*.sql")
                    .OrderByDescending(f => f)
                    .ToList();
                
                // Try to find migrations created after the manifest's commit
                // For now, we'll process only the most recent migration file
                // In a real implementation, we'd use git to find files created after the commit
                if (migrationFiles.Any())
                {
                    // Process only the most recent migration file
                    var latestMigration = migrationFiles.First();
                    await _commentUpdater.UpdateMigrationScriptAsync(latestMigration, manifest);
                }
                else
                {
                    Console.WriteLine("  No migration scripts found.");
                }
            }
            else
            {
                Console.WriteLine($"\nNo migrations directory found at: {migrationsPath}");
            }
            
            Console.WriteLine("\nExclusion comments update completed successfully.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating comments: {ex.Message}");
            return false;
        }
    }
    
    private ChangeManifest MergeManifests(ChangeManifest existing, List<ManifestChange> detectedChanges)
    {
        var merged = new ChangeManifest
        {
            DatabaseName = existing.DatabaseName,
            ServerName = existing.ServerName,
            Generated = DateTime.UtcNow,
            CommitHash = GetCurrentCommitHash(),
            // Always toggle rotation marker when we should (on top of origin/main)
            // This ensures the file always appears as changed in git diff
            RotationMarker = ShouldToggleRotationMarker() ? 
                (existing.RotationMarker == '/' ? '\\' : '/') : 
                existing.RotationMarker
        };
        merged.ExcludedChanges.AddRange(existing.ExcludedChanges);
        
        // Add new changes to included, preserving existing exclusions
        var existingIdentifiers = existing.IncludedChanges.Select(c => c.Identifier)
            .Concat(existing.ExcludedChanges.Select(c => c.Identifier))
            .ToHashSet();
            
        merged.IncludedChanges.Clear();
        
        // Keep existing included changes that still exist
        foreach (var change in existing.IncludedChanges)
        {
            if (detectedChanges.Any(d => d.Identifier == change.Identifier))
            {
                merged.IncludedChanges.Add(change);
            }
        }
        
        // Add new changes
        foreach (var change in detectedChanges)
        {
            if (!existingIdentifiers.Contains(change.Identifier))
            {
                merged.IncludedChanges.Add(change);
            }
        }
        
        return merged;
    }

    private bool ShouldToggleRotationMarker()
    {
        try
        {
            Console.WriteLine("Checking if rotation marker should be toggled...");
            
            // Toggle ONLY when we're the first commit directly on top of origin/main
            // This ensures the manifest file shows as changed in the PR
            // Subsequent commits should NOT toggle to avoid canceling out the rotation effect
            
            using var repo = new Repository(_outputPath);
            
            // Get the current HEAD commit
            var head = repo.Head.Tip;
            
            if (head == null)
            {
                Console.WriteLine("No HEAD commit found. Will NOT toggle rotation marker.");
                return false;
            }
            
            // Get origin/main branch
            var originMain = repo.Branches["origin/main"];
            
            if (originMain == null)
            {
                Console.WriteLine("Origin/main branch not found. Will NOT toggle rotation marker.");
                return false;
            }
            
            var originMainCommit = originMain.Tip;
            
            if (originMainCommit == null)
            {
                Console.WriteLine("Origin/main has no commits. Will NOT toggle rotation marker.");
                return false;
            }
            
            // Check if HEAD has a parent
            var parents = head.Parents.ToList();
            
            if (!parents.Any())
            {
                // No parent - check if HEAD is origin/main (we're about to create first commit)
                bool isAtOriginMain = head.Sha == originMainCommit.Sha;
                Console.WriteLine($"No parent commit, HEAD == origin/main: {isAtOriginMain}");
                return isAtOriginMain;
            }
            
            // Get the first parent
            var parentCommit = parents.First();
            
            Console.WriteLine($"Parent commit: {parentCommit.Sha.Substring(0, 8)}");
            Console.WriteLine($"Origin/main commit: {originMainCommit.Sha.Substring(0, 8)}");
            
            // Toggle ONLY if parent commit is origin/main (we're the first commit on top)
            bool shouldToggle = parentCommit.Sha == originMainCommit.Sha;
            
            if (shouldToggle)
            {
                Console.WriteLine("Parent commit IS origin/main - WILL toggle rotation marker (first commit on top)");
            }
            else
            {
                Console.WriteLine("Parent commit is NOT origin/main - will NOT toggle rotation marker");
            }
            
            return shouldToggle;
        }
        catch (Exception ex)
        {
            // If anything fails, don't toggle to be safe
            Console.WriteLine($"Exception while checking rotation marker: {ex.Message}");
            Console.WriteLine("Will NOT toggle rotation marker (exception occurred)");
            return false;
        }
    }
    
    private string GetManifestPath(string sourceServer, string sourceDatabase, string targetServer, string targetDatabase)
    {
        // Manifest is stored in _change-manifests folder within target location
        // Named with both server and database for clarity
        return Path.Combine(_outputPath, "servers", targetServer, targetDatabase, 
            "_change-manifests", $"{sourceServer}_{sourceDatabase}.manifest");
    }
    
    private string GetCurrentCommitHash()
    {
        try
        {
            using var repo = new Repository(_outputPath);
            return repo.Head.Tip.Sha.Substring(0, 8);
        }
        catch
        {
            return "unknown";
        }
    }
}