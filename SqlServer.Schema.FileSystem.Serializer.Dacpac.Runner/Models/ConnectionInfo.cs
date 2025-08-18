namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Models;

/// <summary>
/// Contains database connection information
/// </summary>
public record ConnectionInfo
{
    /// <summary>
    /// Server name or IP address
    /// </summary>
    public required string Server { get; init; }
    
    /// <summary>
    /// Database name
    /// </summary>
    public required string Database { get; init; }
    
    /// <summary>
    /// Full connection string
    /// </summary>
    public required string ConnectionString { get; init; }
    
    /// <summary>
    /// Sanitized server name for file paths
    /// </summary>
    public string SanitizedServer => Server
        .Replace('\\', '-')
        .Replace(':', '-')
        .Replace(',', '_')
        // .Replace('.', '_')
;
    
    /// <summary>
    /// Sanitized database name for file paths
    /// </summary>
    public string SanitizedDatabase => Database
        .Replace(' ', '_');
}