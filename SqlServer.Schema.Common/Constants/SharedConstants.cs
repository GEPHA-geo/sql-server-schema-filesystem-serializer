namespace SqlServer.Schema.Common.Constants;

/// <summary>
/// Shared constants used across all SQL Server Compare projects
/// </summary>
public static class SharedConstants
{
    /// <summary>
    /// Directory names used in the file system structure
    /// </summary>
    public static class Directories
    {
        public const string Servers = "servers";
        public const string Schemas = "schemas";
        public const string Migrations = "z_migrations";
        public const string ReverseMigrations = "z_migrations_reverse";
        public const string ChangeManifests = "_change-manifests";
        public const string Scmp = "scmp";
        public const string Tables = "Tables";
        public const string Views = "Views";
        public const string Procedures = "Procedures";
        public const string Functions = "Functions";
        public const string Triggers = "Triggers";
        public const string Indexes = "Indexes";
        public const string Synonyms = "Synonyms";
        public const string Users = "Users";
        public const string Roles = "Roles";
        public const string Permissions = "Permissions";
    }

    /// <summary>
    /// File names and extensions
    /// </summary>
    public static class Files
    {
        public const string DacpacExtension = ".dacpac";
        public const string SqlExtension = ".sql";
        public const string ScmpExtension = ".scmp";
        public const string JsonExtension = ".json";
        public const string ExclusionsFile = ".dacpac-exclusions.json";
        public const string ManifestFile = "manifest.json";
        public const string ExtraSqlSuffix = "_extra";
        public const string OriginalSuffix = "_original";
        public const string FilesystemSuffix = "_filesystem";
        public const string DacpacsSuffix = "_dacpacs";
    }

    /// <summary>
    /// DACPAC names used in extraction
    /// </summary>
    public static class DacpacNames
    {
        public const string TargetFilesystem = "TargetFilesystem";
        public const string TargetOriginal = "TargetOriginal";
        public const string SourceFilesystem = "SourceFilesystem";
        public const string SourceOriginal = "SourceOriginal";
        public const string DatabasePrefix = "Database";
    }
    
    /// <summary>
    /// SQL Server versions
    /// </summary>
    public static class SqlVersions
    {
        public const string Default = "Sql150"; // SQL Server 2019
    }

    /// <summary>
    /// Git-related constants
    /// </summary>
    public static class Git
    {
        public const string MainBranch = "origin/main";
        public const string HeadRef = "HEAD";
        public static readonly string[] SafeDirectories = { "/app", "/workspace", "/github/workspace", "/output", "." };
    }
}