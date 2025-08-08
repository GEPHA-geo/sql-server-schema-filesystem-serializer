using SqlServer.Schema.Exclusion.Manager.Models;
using System.Globalization;
using System.Text;

namespace SqlServer.Schema.Exclusion.Manager.Services;

public class ManifestFileHandler
{
    public async Task<ChangeManifest?> ReadManifestAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;
            
        var lines = await File.ReadAllLinesAsync(filePath);
        var manifest = new ChangeManifest
        {
            DatabaseName = string.Empty,
            ServerName = string.Empty
        };
        var currentSection = "";
        
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
                
            if (line.StartsWith("DATABASE:"))
            {
                var parts = line.Split(' ');
                manifest.DatabaseName = parts[1];
                manifest.RotationMarker = parts.Length > 2 ? parts[2][0] : '/';
            }
            else if (line.StartsWith("SERVER:"))
            {
                manifest.ServerName = line.Split(' ')[1];
            }
            else if (line.StartsWith("GENERATED:"))
            {
                var dateStr = line.Replace("GENERATED:", "").Trim().Split(' ')[0];
                manifest.Generated = DateTime.Parse(dateStr, null, DateTimeStyles.RoundtripKind);
            }
            else if (line.StartsWith("COMMIT:"))
            {
                manifest.CommitHash = line.Split(' ')[1];
            }
            else if (line == "=== INCLUDED CHANGES ===")
            {
                currentSection = "included";
            }
            else if (line == "=== EXCLUDED CHANGES ===")
            {
                currentSection = "excluded";
            }
            else if (currentSection != "" && line.Contains(" - "))
            {
                var parts = line.Split(" - ", 2);
                var identifierParts = parts[0].Split(' ');
                var identifier = identifierParts[0];
                
                var change = new ManifestChange
                {
                    Identifier = identifier,
                    Description = parts[1].TrimEnd(' ', '/', '\\')
                };
                
                if (currentSection == "included")
                    manifest.IncludedChanges.Add(change);
                else
                    manifest.ExcludedChanges.Add(change);
            }
        }
        
        return manifest;
    }
    
    public async Task WriteManifestAsync(string filePath, ChangeManifest manifest)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"DATABASE: {manifest.DatabaseName} {manifest.RotationMarker}");
        sb.AppendLine($"SERVER: {manifest.ServerName} {manifest.RotationMarker}");
        sb.AppendLine($"GENERATED: {manifest.Generated:yyyy-MM-ddTHH:mm:ssZ} {manifest.RotationMarker}");
        sb.AppendLine($"COMMIT: {manifest.CommitHash} {manifest.RotationMarker}");
        sb.AppendLine();
        
        sb.AppendLine("=== INCLUDED CHANGES ===");
        foreach (var change in manifest.IncludedChanges)
        {
            sb.AppendLine($"{change} {manifest.RotationMarker}");
        }
        
        sb.AppendLine();
        sb.AppendLine("=== EXCLUDED CHANGES ===");
        foreach (var change in manifest.ExcludedChanges)
        {
            sb.AppendLine($"{change} {manifest.RotationMarker}");
        }
        
        await File.WriteAllTextAsync(filePath, sb.ToString());
    }
    
    public char FlipRotationMarker(char currentMarker) => currentMarker == '/' ? '\\' : '/';
}