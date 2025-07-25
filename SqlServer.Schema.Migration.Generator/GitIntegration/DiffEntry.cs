namespace SqlServer.Schema.Migration.Generator.GitIntegration;

public class DiffEntry
{
    public string Path { get; set; } = string.Empty;
    public ChangeType ChangeType { get; set; }
    public string OldContent { get; set; } = string.Empty;
    public string NewContent { get; set; } = string.Empty;
}

public enum ChangeType
{
    Unknown,
    Added,
    Modified,
    Deleted
}