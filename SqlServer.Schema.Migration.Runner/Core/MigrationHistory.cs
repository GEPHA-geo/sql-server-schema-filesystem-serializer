namespace SqlServer.Schema.Migration.Runner.Core;

public class MigrationHistory
{
    public int Id { get; set; }
    public string MigrationId { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public DateTime AppliedDate { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ExecutionTime { get; set; }
    public string? ErrorMessage { get; set; }
}