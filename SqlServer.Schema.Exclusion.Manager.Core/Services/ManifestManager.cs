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
            var detectedChanges = await _changeDetector.DetectChangesAsync(targetServer, targetDatabase);
            
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
                    RotationMarker = '/'
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
            
            // Update exclusion comments in serialized files at target location
            await _commentUpdater.UpdateSerializedFilesAsync(_outputPath, targetServer, targetDatabase, manifest);
            
            // Find and update migration scripts in target location
            var migrationsPath = Path.Combine(_outputPath, "servers", targetServer, targetDatabase, "z_migrations");
            if (Directory.Exists(migrationsPath))
            {
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
                    Console.WriteLine($"Processing migration: {Path.GetFileName(latestMigration)}");
                    await _commentUpdater.UpdateMigrationScriptAsync(latestMigration, manifest);
                }
            }
            
            Console.WriteLine("Exclusion comments updated successfully");
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
            // Only toggle rotation marker if we're on origin/main
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
            // Check if the parent of current HEAD is on origin/main
            // This means current commit is "next to" origin/main
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse HEAD^",
                    WorkingDirectory = _outputPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            var parentCommit = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            
            if (string.IsNullOrEmpty(parentCommit))
                return false;
            
            // Check if parent commit is the same as origin/main
            process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse origin/main",
                    WorkingDirectory = _outputPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            var mainCommit = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            
            // Toggle only if parent commit is exactly origin/main
            return parentCommit == mainCommit;
        }
        catch
        {
            // If we can't determine, don't toggle
            return false;
        }
    }
    
    private string GetManifestPath(string sourceServer, string sourceDatabase, string targetServer, string targetDatabase)
    {
        // Manifest is stored in target location but named after source
        return Path.Combine(_outputPath, "servers", targetServer, targetDatabase, 
            $"change-manifest-{sourceServer}-{sourceDatabase}.manifest");
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