namespace SqlServer.Schema.Exclusion.Manager.Models;

public class ManifestChange
{
    public required string Identifier { get; set; }
    public required string Description { get; set; }
    public string ObjectType { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    
    // For change detection
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    
    public override string ToString() => $"{Identifier} - {Description}";
}