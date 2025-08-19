using SqlServer.Schema.Common.Constants;

namespace SqlServer.Schema.Common.PathManagement;

/// <summary>
/// Manages all file and directory paths for DACPAC extraction and schema comparison
/// Provides centralized path generation with full paths for all folders and config files
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

    // ============================================
    // Directory paths (full paths)
    // ============================================
    
    /// <summary>
    /// Servers root directory: {output}/servers/
    /// </summary>
    public string ServersPath => Path.Combine(
        OutputPath,
        SharedConstants.Directories.Servers);
    
    /// <summary>
    /// Target server directory: {output}/servers/{target_server}/
    /// </summary>
    public string TargetServerPath => Path.Combine(
        ServersPath,
        TargetServer);
    
    /// <summary>
    /// Target database directory: {output}/servers/{target_server}/{target_db}/
    /// </summary>
    public string TargetDatabasePath => Path.Combine(
        TargetServerPath,
        TargetDatabase);

    /// <summary>
    /// Alias for TargetDatabasePath for backward compatibility
    /// </summary>
    public string TargetOutputPath => TargetDatabasePath;

    /// <summary>
    /// SCMP directory: {output}/servers/{target_server}/{target_db}/scmp/
    /// </summary>
    public string ScmpDirectoryPath => Path.Combine(
        TargetDatabasePath,
        SharedConstants.Directories.Scmp);

    /// <summary>
    /// Source subdirectory in SCMP: {output}/servers/{target_server}/{target_db}/scmp/{source_server}_{source_db}/
    /// </summary>
    public string SourceSubdirectoryPath => Path.Combine(
        ScmpDirectoryPath,
        $"{SourceServer}_{SourceDatabase}");

    /// <summary>
    /// Schemas directory: {output}/servers/{target_server}/{target_db}/schemas/
    /// </summary>
    public string SchemasPath => Path.Combine(
        TargetDatabasePath,
        SharedConstants.Directories.Schemas);

    /// <summary>
    /// Migrations directory: {output}/servers/{target_server}/{target_db}/z_migrations/
    /// </summary>
    public string MigrationsPath => Path.Combine(
        TargetDatabasePath,
        SharedConstants.Directories.Migrations);
    
    /// <summary>
    /// Change manifests directory: {output}/servers/{target_server}/{target_db}/_change-manifests/
    /// </summary>
    public string ChangeManifestsPath => Path.Combine(
        TargetDatabasePath,
        SharedConstants.Directories.ChangeManifests);

    // Schema subdirectories
    
    /// <summary>
    /// Tables directory: {output}/servers/{target_server}/{target_db}/schemas/dbo/Tables/
    /// </summary>
    public string GetTablesPath(string schema = "dbo") => Path.Combine(
        SchemasPath,
        schema,
        SharedConstants.Directories.Tables);

    /// <summary>
    /// Views directory: {output}/servers/{target_server}/{target_db}/schemas/dbo/Views/
    /// </summary>
    public string GetViewsPath(string schema = "dbo") => Path.Combine(
        SchemasPath,
        schema,
        SharedConstants.Directories.Views);

    /// <summary>
    /// Procedures directory: {output}/servers/{target_server}/{target_db}/schemas/dbo/Procedures/
    /// </summary>
    public string GetProceduresPath(string schema = "dbo") => Path.Combine(
        SchemasPath,
        schema,
        SharedConstants.Directories.Procedures);

    /// <summary>
    /// Functions directory: {output}/servers/{target_server}/{target_db}/schemas/dbo/Functions/
    /// </summary>
    public string GetFunctionsPath(string schema = "dbo") => Path.Combine(
        SchemasPath,
        schema,
        SharedConstants.Directories.Functions);

    // ============================================
    // DACPAC file paths (full paths)
    // ============================================
    
    /// <summary>
    /// Target filesystem DACPAC: {scmp}/{target_server}_{target_db}_filesystem.dacpac
    /// </summary>
    public string TargetFilesystemDacpacPath => Path.Combine(
        ScmpDirectoryPath,
        $"{TargetServer}_{TargetDatabase}{SharedConstants.Files.FilesystemSuffix}{SharedConstants.Files.DacpacExtension}");

    /// <summary>
    /// Target original DACPAC: {scmp}/{target_server}_{target_db}_original.dacpac
    /// </summary>
    public string TargetOriginalDacpacPath => Path.Combine(
        ScmpDirectoryPath,
        $"{TargetServer}_{TargetDatabase}{SharedConstants.Files.OriginalSuffix}{SharedConstants.Files.DacpacExtension}");

    /// <summary>
    /// Source filesystem DACPAC: {scmp}/{source_server}_{source_db}/{source_server}_{source_db}_filesystem.dacpac
    /// </summary>
    public string SourceFilesystemDacpacPath => Path.Combine(
        SourceSubdirectoryPath,
        $"{SourceServer}_{SourceDatabase}{SharedConstants.Files.FilesystemSuffix}{SharedConstants.Files.DacpacExtension}");

    /// <summary>
    /// Source original DACPAC: {scmp}/{source_server}_{source_db}/{source_server}_{source_db}_original.dacpac
    /// </summary>
    public string SourceOriginalDacpacPath => Path.Combine(
        SourceSubdirectoryPath,
        $"{SourceServer}_{SourceDatabase}{SharedConstants.Files.OriginalSuffix}{SharedConstants.Files.DacpacExtension}");

    // ============================================
    // SCMP file paths (full paths)
    // ============================================
    
    /// <summary>
    /// Original SCMP file: {scmp}/{source_server}_{source_db}/{source_server}_{source_db}_original.scmp
    /// </summary>
    public string OriginalScmpPath => Path.Combine(
        SourceSubdirectoryPath,
        $"{SourceServer}_{SourceDatabase}{SharedConstants.Files.OriginalSuffix}{SharedConstants.Files.ScmpExtension}");

    /// <summary>
    /// DACPACs SCMP file: {scmp}/{source_server}_{source_db}/{source_server}_{source_db}_dacpacs.scmp
    /// </summary>
    public string DacpacsScmpPath => Path.Combine(
        SourceSubdirectoryPath,
        $"{SourceServer}_{SourceDatabase}{SharedConstants.Files.DacpacsSuffix}{SharedConstants.Files.ScmpExtension}");

    // ============================================
    // Config and special file paths (full paths)
    // ============================================
    
    /// <summary>
    /// Extra SQL file: {output}/servers/{target_server}/{target_db}/{source_server}_{source_db}_extra.sql
    /// </summary>
    public string ExtraSqlPath => Path.Combine(
        TargetDatabasePath,
        $"{SourceServer}_{SourceDatabase}{SharedConstants.Files.ExtraSqlSuffix}{SharedConstants.Files.SqlExtension}");

    /// <summary>
    /// Exclusions JSON file: {scmp}/.dacpac-exclusions.json
    /// </summary>
    public string ExclusionsJsonPath => Path.Combine(
        ScmpDirectoryPath,
        SharedConstants.Files.ExclusionsFile);

    /// <summary>
    /// Temporary exclusions path (in parent, before moving): {output}/servers/{target_server}/{target_db}/.dacpac-exclusions.json
    /// </summary>
    public string TempExclusionsJsonPath => Path.Combine(
        TargetDatabasePath,
        SharedConstants.Files.ExclusionsFile);

    /// <summary>
    /// Manifest file in migrations: {output}/servers/{target_server}/{target_db}/z_migrations/manifest.json
    /// </summary>
    public string MigrationsManifestPath => Path.Combine(
        MigrationsPath,
        SharedConstants.Files.ManifestFile);

    /// <summary>
    /// Manifest file in change manifests: {output}/servers/{target_server}/{target_db}/_change-manifests/manifest.json
    /// </summary>
    public string ChangeManifestPath => Path.Combine(
        ChangeManifestsPath,
        SharedConstants.Files.ManifestFile);

    // ============================================
    // Helper methods
    // ============================================

    /// <summary>
    /// Creates all necessary directories for the extraction
    /// </summary>
    public void CreateDirectories()
    {
        Directory.CreateDirectory(TargetDatabasePath);
        Directory.CreateDirectory(ScmpDirectoryPath);
        Directory.CreateDirectory(SourceSubdirectoryPath);
        Directory.CreateDirectory(SchemasPath);
        Directory.CreateDirectory(MigrationsPath);
        Directory.CreateDirectory(ChangeManifestsPath);
    }

    /// <summary>
    /// Gets the full path for a schema file
    /// </summary>
    public string GetSchemaFilePath(string schema, string objectType, string objectName)
    {
        var directory = objectType.ToLower() switch
        {
            "table" => GetTablesPath(schema),
            "view" => GetViewsPath(schema),
            "procedure" => GetProceduresPath(schema),
            "function" => GetFunctionsPath(schema),
            _ => Path.Combine(SchemasPath, schema, objectType)
        };

        return Path.Combine(directory, $"{objectName}{SharedConstants.Files.SqlExtension}");
    }
    
    /// <summary>
    /// Static helper to get exclusion file path from a schema directory
    /// Checks SCMP directory first, then falls back to schema directory
    /// </summary>
    public static string GetExclusionFilePath(string schemaPath)
    {
        var scmpPath = Path.Combine(schemaPath, SharedConstants.Directories.Scmp);
        var scmpExclusionPath = Path.Combine(scmpPath, SharedConstants.Files.ExclusionsFile);
        var schemaExclusionPath = Path.Combine(schemaPath, SharedConstants.Files.ExclusionsFile);
        return File.Exists(scmpExclusionPath) ? scmpExclusionPath : schemaExclusionPath;
    }
    
    /// <summary>
    /// Static helper to get the SCMP directory path from a schema directory
    /// </summary>
    public static string GetScmpPath(string schemaPath) => 
        Path.Combine(schemaPath, SharedConstants.Directories.Scmp);
}

/// <summary>
/// Legacy DacpacPaths structure for backward compatibility
/// </summary>
public class DacpacPaths
{
    public required string TargetFilesystemDacpac { get; init; }
    public required string TargetOriginalDacpac { get; init; }
    public required string SourceFilesystemDacpac { get; init; }
    public required string SourceOriginalDacpac { get; init; }
}