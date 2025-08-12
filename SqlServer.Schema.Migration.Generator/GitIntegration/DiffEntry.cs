namespace SqlServer.Schema.Migration.Generator.GitIntegration;

public class DiffEntry
{
    public string Path { get; init; } = string.Empty;
    public ChangeType ChangeType { get; init; }
    public string OldContent { get; init; } = string.Empty;
    public string NewContent { get; init; } = string.Empty;
}

public enum ChangeType
{
    Unknown,
    Added,
    Modified,
    Deleted
}