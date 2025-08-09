using System.IO;

namespace SqlServer.Schema.Exclusion.Manager.Core.Models;

public class ChangeManifest
{
    public required string DatabaseName { get; set; }
    public required string ServerName { get; set; }
    public DateTime Generated { get; set; }
    public string CommitHash { get; set; } = string.Empty;
    public char RotationMarker { get; set; } = '/';
    
    public List<ManifestChange> IncludedChanges { get; } = new();
    public List<ManifestChange> ExcludedChanges { get; } = new();
    
    public string GetManifestFileName() => 
        Path.Combine("_change-manifests", $"{ServerName}_{DatabaseName}.manifest");
}