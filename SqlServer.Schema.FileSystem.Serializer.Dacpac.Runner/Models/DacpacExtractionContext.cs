using CommonDacpacFilePaths = SqlServer.Schema.Common.PathManagement.DacpacFilePaths;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Models;

/// <summary>
/// Contains shared state and paths for the DACPAC extraction process
/// </summary>
public class DacpacExtractionContext : IDisposable
{
    /// <summary>
    /// Root output path for the extraction
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// Target output path (server/database specific)
    /// </summary>
    public required string TargetOutputPath { get; init; }

    /// <summary>
    /// Path to the SCMP subdirectory where DACPACs and SCMP files are stored
    /// </summary>
    public required string ScmpOutputPath { get; init; }

    /// <summary>
    /// Path to the git worktree (if created)
    /// </summary>
    public string? WorktreePath { get; init; }

    /// <summary>
    /// All DACPAC file paths
    /// </summary>
    public required DacpacPaths DacpacPaths { get; init; }

    /// <summary>
    /// Centralized file path manager
    /// </summary>
    public CommonDacpacFilePaths? FilePaths { get; init; }

    /// <summary>
    /// Source database connection information
    /// </summary>
    public required ConnectionInfo SourceConnection { get; init; }

    /// <summary>
    /// Target database connection information
    /// </summary>
    public required ConnectionInfo TargetConnection { get; init; }

    /// <summary>
    /// Temporary directory for build operations
    /// </summary>
    public required string TempDirectory { get; init; }

    /// <summary>
    /// Path to migrations directory
    /// </summary>
    public string MigrationsPath { get; init; } = string.Empty;

    /// <summary>
    /// Original SCMP model loaded from file
    /// </summary>
    public required Exclusion.Manager.Core.Models.SchemaComparison ScmpModel { get; init; }

    /// <summary>
    /// Whether to keep temp files for debugging
    /// </summary>
    public required bool KeepTempFiles { get; init; }

    public void Dispose()
    {
        // Cleanup will be handled by the service
    }
}