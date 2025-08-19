using SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Constants;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Models;

/// <summary>
/// Manages all file paths for the DACPAC extraction process in a centralized way
/// </summary>
public class DacpacFilePaths
{
    /// <summary>
    /// Root output path for the extraction
    /// </summary>
    public string OutputPath { get; }
    
    /// <summary>
    /// Target server name (sanitized)
    /// </summary>
    public string TargetServer { get; }
    
    /// <summary>
    /// Target database name
    /// </summary>
    public string TargetDatabase { get; }
    
    /// <summary>
    /// Source server name (sanitized)
    /// </summary>
    public string SourceServer { get; }
    
    /// <summary>
    /// Source database name (sanitized)
    /// </summary>
    public string SourceDatabase { get; }

    public DacpacFilePaths(
        string outputPath,
        string targetServer,
        string targetDatabase,
        string sourceServer,
        string sourceDatabase)
    {
        OutputPath = outputPath;
        TargetServer = targetServer;
        TargetDatabase = targetDatabase;
        SourceServer = sourceServer;
        SourceDatabase = sourceDatabase;
    }

    // Directory paths
    
    /// <summary>
    /// Target output path: servers/{target_server}/{target_db}/
    /// </summary>
    public string TargetOutputPath => Path.Combine(
        OutputPath,
        DacpacConstants.Directories.Servers,
        TargetServer,
        TargetDatabase);

    /// <summary>
    /// SCMP directory path: servers/{target_server}/{target_db}/scmp/
    /// </summary>
    public string ScmpDirectoryPath => Path.Combine(
        TargetOutputPath,
        DacpacConstants.Directories.Scmp);

    /// <summary>
    /// Source subdirectory path: servers/{target_server}/{target_db}/scmp/{source_server}_{source_db}/
    /// </summary>
    public string SourceSubdirectoryPath => Path.Combine(
        ScmpDirectoryPath,
        $"{SourceServer}_{SourceDatabase}");

    /// <summary>
    /// Schemas directory path: servers/{target_server}/{target_db}/schemas/
    /// </summary>
    public string SchemasPath => Path.Combine(
        TargetOutputPath,
        DacpacConstants.Directories.Schemas);

    /// <summary>
    /// Migrations directory path: servers/{target_server}/{target_db}/z_migrations/
    /// </summary>
    public string MigrationsPath => Path.Combine(
        TargetOutputPath,
        DacpacConstants.Directories.Migrations);

    // DACPAC file paths
    
    /// <summary>
    /// Target filesystem DACPAC: scmp/{target_server}_{target_db}_filesystem.dacpac
    /// </summary>
    public string TargetFilesystemDacpacPath => Path.Combine(
        ScmpDirectoryPath,
        $"{TargetServer}_{TargetDatabase}{DacpacConstants.DacpacNames.FilesystemSuffix}{DacpacConstants.Files.DacpacExtension}");

    /// <summary>
    /// Target original DACPAC: scmp/{target_server}_{target_db}_original.dacpac
    /// </summary>
    public string TargetOriginalDacpacPath => Path.Combine(
        ScmpDirectoryPath,
        $"{TargetServer}_{TargetDatabase}{DacpacConstants.DacpacNames.OriginalSuffix}{DacpacConstants.Files.DacpacExtension}");

    /// <summary>
    /// Source filesystem DACPAC: scmp/{source_server}_{source_db}/{source_server}_{source_db}_filesystem.dacpac
    /// </summary>
    public string SourceFilesystemDacpacPath => Path.Combine(
        SourceSubdirectoryPath,
        $"{SourceServer}_{SourceDatabase}{DacpacConstants.DacpacNames.FilesystemSuffix}{DacpacConstants.Files.DacpacExtension}");

    /// <summary>
    /// Source original DACPAC: scmp/{source_server}_{source_db}/{source_server}_{source_db}_original.dacpac
    /// </summary>
    public string SourceOriginalDacpacPath => Path.Combine(
        SourceSubdirectoryPath,
        $"{SourceServer}_{SourceDatabase}{DacpacConstants.DacpacNames.OriginalSuffix}{DacpacConstants.Files.DacpacExtension}");

    // SCMP file paths
    
    /// <summary>
    /// Original SCMP file: scmp/{source_server}_{source_db}/{source_server}_{source_db}_original.scmp
    /// </summary>
    public string OriginalScmpPath => Path.Combine(
        SourceSubdirectoryPath,
        $"{SourceServer}_{SourceDatabase}_original{DacpacConstants.Files.ScmpExtension}");

    /// <summary>
    /// DACPACs SCMP file: scmp/{source_server}_{source_db}/{source_server}_{source_db}_dacpacs.scmp
    /// </summary>
    public string DacpacsScmpPath => Path.Combine(
        SourceSubdirectoryPath,
        $"{SourceServer}_{SourceDatabase}_dacpacs{DacpacConstants.Files.ScmpExtension}");

    // Other file paths
    
    /// <summary>
    /// Extra SQL file: servers/{target_server}/{target_db}/{source_server}_{source_db}_extra.sql
    /// </summary>
    public string ExtraSqlPath => Path.Combine(
        TargetOutputPath,
        $"{SourceServer}_{SourceDatabase}_extra{DacpacConstants.Files.SqlExtension}");

    /// <summary>
    /// Exclusions JSON file: scmp/.dacpac-exclusions.json
    /// </summary>
    public string ExclusionsJsonPath => Path.Combine(
        ScmpDirectoryPath,
        DacpacConstants.Files.ExclusionsFile);

    /// <summary>
    /// Temporary exclusions path (in parent, before moving): servers/{target_server}/{target_db}/.dacpac-exclusions.json
    /// </summary>
    public string TempExclusionsJsonPath => Path.Combine(
        TargetOutputPath,
        DacpacConstants.Files.ExclusionsFile);

    /// <summary>
    /// Creates all necessary directories for the extraction
    /// </summary>
    public void CreateDirectories()
    {
        Directory.CreateDirectory(TargetOutputPath);
        Directory.CreateDirectory(ScmpDirectoryPath);
        Directory.CreateDirectory(SourceSubdirectoryPath);
        Directory.CreateDirectory(SchemasPath);
        Directory.CreateDirectory(MigrationsPath);
    }

    /// <summary>
    /// Gets a DacpacPaths object compatible with existing code
    /// </summary>
    public DacpacPaths GetLegacyDacpacPaths() => new()
    {
        TargetFilesystemDacpac = TargetFilesystemDacpacPath,
        TargetOriginalDacpac = TargetOriginalDacpacPath,
        SourceFilesystemDacpac = SourceFilesystemDacpacPath,
        SourceOriginalDacpac = SourceOriginalDacpacPath
    };
}