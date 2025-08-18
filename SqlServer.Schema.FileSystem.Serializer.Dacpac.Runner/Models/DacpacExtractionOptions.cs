namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Models;

/// <summary>
/// Configuration options for DACPAC extraction
/// </summary>
public class DacpacExtractionOptions
{
    /// <summary>
    /// Path to the SCMP file
    /// </summary>
    public required string ScmpFilePath { get; init; }
    
    /// <summary>
    /// Password for source database connection
    /// </summary>
    public required string SourcePassword { get; init; }
    
    /// <summary>
    /// Password for target database connection
    /// </summary>
    public required string TargetPassword { get; init; }
    
    /// <summary>
    /// Output path for extraction
    /// </summary>
    public required string OutputPath { get; init; }
    
    /// <summary>
    /// Optional commit message
    /// </summary>
    public string? CommitMessage { get; init; }
    
    /// <summary>
    /// Whether to keep temporary files for debugging
    /// </summary>
    public bool KeepTempFiles { get; init; }
}