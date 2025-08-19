namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Models;

/// <summary>
/// Holds all DACPAC file paths used during the extraction process
/// </summary>
public record DacpacPaths
{
    /// <summary>
    /// Path to the target filesystem DACPAC built from git worktree
    /// </summary>
    public required string TargetFilesystemDacpac { get; init; }

    /// <summary>
    /// Path to the original target database DACPAC
    /// </summary>
    public required string TargetOriginalDacpac { get; init; }

    /// <summary>
    /// Path to the original source database DACPAC
    /// </summary>
    public required string SourceOriginalDacpac { get; init; }

    /// <summary>
    /// Path to the source filesystem DACPAC built from extracted schema
    /// </summary>
    public required string SourceFilesystemDacpac { get; init; }
}