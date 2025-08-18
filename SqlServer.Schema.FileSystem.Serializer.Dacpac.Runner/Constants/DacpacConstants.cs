namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Constants;

/// <summary>
/// Constants used throughout the DACPAC extraction process
/// </summary>
public static class DacpacConstants
{
    /// <summary>
    /// Directory names
    /// </summary>
    public static class Directories
    {
        public const string Migrations = "z_migrations";
        public const string ReverseMigrations = "z_migrations_reverse";
        public const string ChangeManifests = "_change-manifests";
        public const string Servers = "servers";
        public const string Schemas = "schemas";
    }
    
    /// <summary>
    /// File names and extensions
    /// </summary>
    public static class Files
    {
        public const string ExclusionsFile = ".dacpac-exclusions.json";
        public const string DacpacExtension = ".dacpac";
        public const string SqlExtension = ".sql";
        public const string ScmpExtension = ".scmp";
    }
    
    /// <summary>
    /// DACPAC naming patterns
    /// </summary>
    public static class DacpacNames
    {
        public const string FilesystemSuffix = "_filesystem";
        public const string OriginalSuffix = "_original";
        public const string DatabasePrefix = "Database";
        public const string TargetFilesystem = "TargetFilesystem";
        public const string SourceFilesystem = "SourceFilesystem";
        public const string TargetOriginal = "TargetOriginal";
        public const string SourceOriginal = "SourceOriginal";
    }
    
    /// <summary>
    /// Git-related constants
    /// </summary>
    public static class Git
    {
        public const string MainBranch = "origin/main";
        public const string HeadRef = "HEAD";
        public static readonly string[] SafeDirectories = { "/workspace", "/output", "." };
    }
    
    /// <summary>
    /// SQL Server versions
    /// </summary>
    public static class SqlVersions
    {
        public const string Default = "Sql150"; // SQL Server 2019
    }
}