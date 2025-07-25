using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Parsing;

public class SchemaChange
{
    public string ObjectType { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public ChangeType ChangeType { get; set; }
    public string OldDefinition { get; set; } = string.Empty;
    public string NewDefinition { get; set; } = string.Empty;
    public string? TableName { get; set; } // For columns and constraints
    public string? ColumnName { get; set; } // For column changes
    public Dictionary<string, string> Properties { get; set; } = new();
}