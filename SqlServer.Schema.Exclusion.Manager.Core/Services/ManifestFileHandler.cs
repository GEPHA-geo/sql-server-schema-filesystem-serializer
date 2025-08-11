using SqlServer.Schema.Exclusion.Manager.Core.Models;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SqlServer.Schema.Exclusion.Manager.Core.Services;

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
                
                // Infer ObjectType from identifier structure
                var idParts = identifier.Split('.');
                if (idParts.Length == 3)
                {
                    // Column change - set ObjectType to Table
                    change.ObjectType = "Table";
                }
                else if (idParts.Length == 2)
                {
                    // Could be table, view, procedure, etc.
                    // Check for common prefixes
                    var objectName = idParts[1];
                    if (objectName.StartsWith("TBL_") || objectName.Contains("Table"))
                        change.ObjectType = "Table";
                    else if (objectName.StartsWith("VW_") || objectName.Contains("View"))
                        change.ObjectType = "View";
                    else if (objectName.StartsWith("SP_") || objectName.StartsWith("sp_"))
                        change.ObjectType = "StoredProcedure";
                    else if (objectName.StartsWith("IDX_") || objectName.Contains("Index"))
                        change.ObjectType = "Index";
                    else
                        change.ObjectType = "Unknown";
                }
                
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
        // Sort included changes alphabetically by their string representation
        foreach (var change in manifest.IncludedChanges.OrderBy(c => c.ToString()))
        {
            sb.AppendLine($"{change} {manifest.RotationMarker}");
        }
        
        sb.AppendLine();
        sb.AppendLine("=== EXCLUDED CHANGES ===");
        // Sort excluded changes alphabetically by their string representation for consistency
        foreach (var change in manifest.ExcludedChanges.OrderBy(c => c.ToString()))
        {
            sb.AppendLine($"{change} {manifest.RotationMarker}");
        }
        
        await File.WriteAllTextAsync(filePath, sb.ToString());
    }
    
    public char FlipRotationMarker(char currentMarker) => currentMarker == '/' ? '\\' : '/';
}