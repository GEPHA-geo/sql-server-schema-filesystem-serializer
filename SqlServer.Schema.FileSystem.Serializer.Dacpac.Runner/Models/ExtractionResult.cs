namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Models;

/// <summary>
/// Result of the DACPAC extraction process
/// </summary>
public record ExtractionResult
{
    /// <summary>
    /// Path to generated migration file
    /// </summary>
    public string? MigrationPath { get; init; }

    /// <summary>
    /// Number of differences found
    /// </summary>
    public int DifferenceCount { get; init; }

    /// <summary>
    /// Number of excluded differences
    /// </summary>
    public int ExcludedDifferenceCount { get; init; }
}