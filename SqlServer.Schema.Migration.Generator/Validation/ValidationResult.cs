using System.Collections.Generic;

namespace SqlServer.Schema.Migration.Generator.Validation;

public class ValidationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? DetailedError { get; set; }
    public List<string> Warnings { get; set; } = new();
    public Dictionary<string, string> Details { get; set; } = new();
    public long ExecutionTimeMs { get; set; }
}