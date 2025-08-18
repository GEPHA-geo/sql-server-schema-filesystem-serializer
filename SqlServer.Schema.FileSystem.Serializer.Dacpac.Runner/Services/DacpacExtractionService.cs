using System.Diagnostics;
using CSharpFunctionalExtensions;
using Microsoft.Data.SqlClient;
using SqlServer.Schema.Exclusion.Manager.Core.Services;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Constants;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Models;
using static SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Constants.DacpacConstants.DacpacNames;
using static SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Constants.DacpacConstants.Files;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Services;

/// <summary>
/// Main orchestrator service for DACPAC extraction process
/// </summary>
public class DacpacExtractionService
{
    readonly GitWorktreeManager _gitManager;
    readonly DacpacBuilder _dacpacBuilder;
    readonly SchemaExtractor _schemaExtractor;
    readonly SchemaComparisonService _comparisonService;
    readonly MigrationGenerator _migrationGenerator;
    readonly ScmpManifestHandler _scmpHandler;

    public DacpacExtractionService()
    {
        _gitManager = new GitWorktreeManager();
        _dacpacBuilder = new DacpacBuilder();
        _schemaExtractor = new SchemaExtractor();
        _comparisonService = new SchemaComparisonService();
        _migrationGenerator = new MigrationGenerator();
        _scmpHandler = new ScmpManifestHandler();
    }

    /// <summary>
    /// Main entry point for DACPAC extraction
    /// </summary>
    public async Task<Result<ExtractionResult>> ExtractAsync(DacpacExtractionOptions options)
    {
        try
        {
            Console.WriteLine("=== SCMP-Based DACPAC Tool ===");
            Console.WriteLine($"Loading SCMP file: {options.ScmpFilePath}");

            // Initialize context
            var contextResult = await InitializeContext(options);
            if (contextResult.IsFailure)
                return Result.Failure<ExtractionResult>(contextResult.Error);

            var context = contextResult.Value;

            try
            {
                // Configure Git safe directory
                _gitManager.ConfigureGitSafeDirectory(options.OutputPath);

                // Prepare Git repository if needed
                if (_gitManager.IsGitRepository(options.OutputPath))
                {
                    var prepareResult = await _gitManager.PrepareRepository(options.OutputPath);
                    if (prepareResult.IsFailure)
                        return Result.Failure<ExtractionResult>(prepareResult.Error);
                }

                // Phase 1: Build Target Filesystem DACPAC from git worktree
                await BuildTargetFilesystemDacpac(context);
                // Note: We don't fail if target filesystem build fails - it's expected initially

                // Phase 2-3: Extract Original DACPACs from databases
                var targetExtractResult = await ExtractTargetOriginalDacpac(context);
                if (targetExtractResult.IsFailure)
                    return Result.Failure<ExtractionResult>(targetExtractResult.Error);

                var sourceExtractResult = await ExtractSourceOriginalDacpac(context);
                if (sourceExtractResult.IsFailure)
                    return Result.Failure<ExtractionResult>(sourceExtractResult.Error);

                // Phase 4: Extract Source to Filesystem and Build DACPAC
                var buildResult = await ExtractAndBuildSourceFilesystem(context);
                if (buildResult.IsFailure)
                    return Result.Failure<ExtractionResult>(buildResult.Error);

                // Phase 5: Schema Comparison using SCMP file
                var comparisonResult = await _comparisonService.CompareWithScmpFile(context);
                if (comparisonResult.IsFailure)
                    return Result.Failure<ExtractionResult>(comparisonResult.Error);

                // Generate and save migration
                var migrationResult = await _migrationGenerator.GenerateAndSaveMigration(
                    context, comparisonResult.Value);

                if (migrationResult.IsFailure)
                    return Result.Failure<ExtractionResult>(migrationResult.Error);

                // Commit changes if needed
                if (_gitManager.IsGitRepository(options.OutputPath))
                {
                    await _gitManager.CommitChangesIfNeeded(
                        options.OutputPath,
                        options.CommitMessage);
                }

                // Prepare successful result
                var result = new ExtractionResult
                {
                    MigrationPath = migrationResult.Value.MigrationPath,
                    DifferenceCount = comparisonResult.Value.TotalDifferences,
                    ExcludedDifferenceCount = comparisonResult.Value.ExcludedDifferences
                };

                Console.WriteLine("\n✓ SCMP-based extraction completed successfully");
                Console.WriteLine($"  - 4 DACPACs generated in: {context.TargetOutputPath}");
                Console.WriteLine($"  - Migration script generated in: {DacpacConstants.Directories.Migrations}/");
                Console.WriteLine($"  - Source schema extracted to: {DacpacConstants.Directories.Schemas}/");

                return Result.Success(result);
            }
            finally
            {
                // Cleanup
                await Cleanup(context);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);

            return Result.Failure<ExtractionResult>($"Extraction failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Initializes the extraction context from options
    /// </summary>
    async Task<Result<DacpacExtractionContext>> InitializeContext(DacpacExtractionOptions options)
    {
        // Load SCMP file
        var scmpModel = await _scmpHandler.LoadManifestAsync(options.ScmpFilePath);
        if (scmpModel == null)
        {
            return Result.Failure<DacpacExtractionContext>("Failed to load SCMP file");
        }

        // Extract connection information
        var (sourceServer, targetServer) = _scmpHandler.GetServerInfo(scmpModel);
        var (sourceDb, targetDb) = _scmpHandler.GetDatabaseInfo(scmpModel);

        Console.WriteLine($"Source: {sourceServer}/{sourceDb}");
        Console.WriteLine($"Target: {targetServer}/{targetDb}");

        // Get and update connection strings
        var sourceConnectionString = scmpModel.SourceModelProvider?.ConnectionBasedModelProvider?.ConnectionString;
        var targetConnectionString = scmpModel.TargetModelProvider?.ConnectionBasedModelProvider?.ConnectionString;

        if (string.IsNullOrEmpty(sourceConnectionString))
        {
            return Result.Failure<DacpacExtractionContext>("Source connection string not found in SCMP file");
        }

        if (string.IsNullOrEmpty(targetConnectionString))
        {
            return Result.Failure<DacpacExtractionContext>("Target connection string not found in SCMP file");
        }

        // Update connection strings with provided passwords
        sourceConnectionString = UpdateConnectionPassword(sourceConnectionString, options.SourcePassword);
        targetConnectionString = UpdateConnectionPassword(targetConnectionString, options.TargetPassword);

        // Create connection info objects
        var sourceConnection = new ConnectionInfo
        {
            Server = sourceServer ?? "unknown-source-server",
            Database = sourceDb ?? "unknown-source-db",
            ConnectionString = sourceConnectionString
        };

        var targetConnection = new ConnectionInfo
        {
            Server = targetServer ?? "unknown-target-server",
            Database = targetDb ?? "unknown-target-db",
            ConnectionString = targetConnectionString
        };

        // Calculate paths
        var tempDirectory =
            Path.Combine(Path.GetTempPath(), $"dacpac_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
        var targetOutputPath = Path.Combine(
            options.OutputPath,
            DacpacConstants.Directories.Servers,
            targetConnection.SanitizedServer,
            targetConnection.Database);

        // Create directories
        Directory.CreateDirectory(targetOutputPath);
        Directory.CreateDirectory(tempDirectory);

        Console.WriteLine($"Output path: {targetOutputPath}");

        // Create DACPAC paths
        var dacpacPaths = new DacpacPaths
        {
            TargetFilesystemDacpac = Path.Combine(targetOutputPath,
                $"{targetConnection.SanitizedServer}_{targetConnection.SanitizedDatabase}{FilesystemSuffix}{DacpacExtension}"),
            TargetOriginalDacpac = Path.Combine(targetOutputPath,
                $"{targetConnection.SanitizedServer}_{targetConnection.SanitizedDatabase}{OriginalSuffix}{DacpacExtension}"),
            SourceOriginalDacpac = Path.Combine(targetOutputPath,
                $"{sourceConnection.SanitizedServer}_{sourceConnection.SanitizedDatabase}{OriginalSuffix}{DacpacExtension}"),
            SourceFilesystemDacpac = Path.Combine(targetOutputPath,
                $"{sourceConnection.SanitizedServer}_{sourceConnection.SanitizedDatabase}{FilesystemSuffix}{DacpacExtension}")
        };

        // Create context with all values initialized
        var context = new DacpacExtractionContext
        {
            OutputPath = options.OutputPath,
            ScmpModel = scmpModel,
            KeepTempFiles = options.KeepTempFiles || Debugger.IsAttached,
            TempDirectory = tempDirectory,
            SourceConnection = sourceConnection,
            TargetConnection = targetConnection,
            TargetOutputPath = targetOutputPath,
            DacpacPaths = dacpacPaths
        };

        return Result.Success(context);
    }

    /// <summary>
    /// Phase 1: Build Target Filesystem DACPAC
    /// </summary>
    async Task<Result<DacpacExtractionContext>> BuildTargetFilesystemDacpac(DacpacExtractionContext context)
    {
        Console.WriteLine("\n=== Phase 1: Building Target Filesystem DACPAC ===");

        var worktreeResult = await _gitManager.CreateWorktree(
            context.OutputPath,
            context.TempDirectory);

        if (worktreeResult.IsFailure) return worktreeResult.ConvertFailure<DacpacExtractionContext>();
        var worktreePath = worktreeResult.Value;

        // Create new context with worktree path
        var newContext = new DacpacExtractionContext
        {
            OutputPath = context.OutputPath,
            TargetOutputPath = context.TargetOutputPath,
            WorktreePath = worktreePath,
            DacpacPaths = context.DacpacPaths,
            SourceConnection = context.SourceConnection,
            TargetConnection = context.TargetConnection,
            TempDirectory = context.TempDirectory,
            MigrationsPath = context.MigrationsPath,
            ScmpModel = context.ScmpModel,
            KeepTempFiles = context.KeepTempFiles
        };

        var result = await _dacpacBuilder.BuildFromWorktree(newContext);
        if (result.IsFailure)
        {
            // Log but continue - it's OK if no committed state exists initially
            Console.WriteLine($"⚠ Target filesystem: {result.Error}");
        }

        return Result.Success(newContext);
    }

    /// <summary>
    /// Phase 2: Extract Target Original DACPAC
    /// </summary>
    async Task<Result> ExtractTargetOriginalDacpac(DacpacExtractionContext context)
    {
        Console.WriteLine("\n=== Phase 2: Extracting Target Original DACPAC ===");

        var result = await _schemaExtractor.ExtractFromDatabase(
            context.TargetConnection.ConnectionString,
            context.DacpacPaths.TargetOriginalDacpac,
            TargetOriginal,
            context.TargetConnection.Database);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    /// <summary>
    /// Phase 3: Extract Source Original DACPAC
    /// </summary>
    async Task<Result> ExtractSourceOriginalDacpac(DacpacExtractionContext context)
    {
        Console.WriteLine("\n=== Phase 3: Extracting Source Original DACPAC ===");

        var result = await _schemaExtractor.ExtractFromDatabase(
            context.SourceConnection.ConnectionString,
            context.DacpacPaths.SourceOriginalDacpac,
            SourceOriginal,
            context.SourceConnection.Database);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    /// <summary>
    /// Phase 4: Extract Source to Filesystem and Build DACPAC
    /// </summary>
    async Task<Result> ExtractAndBuildSourceFilesystem(DacpacExtractionContext context)
    {
        Console.WriteLine("\n=== Phase 4: Extracting Source Schema and Building Filesystem DACPAC ===");

        // Extract source schema to filesystem (this will also handle staging for line ending normalization)
        var extractResult = await _schemaExtractor.ExtractToFileSystem(
            context,
            context.DacpacPaths.SourceOriginalDacpac);

        if (extractResult.IsFailure)
            return Result.Failure(extractResult.Error);

        // Calculate the target schema path where files were extracted
        var targetSchemaPath = Path.Combine(
            context.OutputPath,
            DacpacConstants.Directories.Servers,
            context.TargetConnection.SanitizedServer,
            context.TargetConnection.Database);

        // Build source filesystem DACPAC
        var buildResult = await _dacpacBuilder.BuildFromFileSystem(
            targetSchemaPath,
            context.DacpacPaths.SourceFilesystemDacpac,
            SourceFilesystem,
            SourceFilesystem);

        return buildResult.IsSuccess ? Result.Success() : Result.Failure(buildResult.Error);
    }

    /// <summary>
    /// Updates connection string with password if needed
    /// </summary>
    string UpdateConnectionPassword(string connectionString, string password)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!builder.IntegratedSecurity)
        {
            builder.Password = password;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Cleanup temporary resources
    /// </summary>
    async Task Cleanup(DacpacExtractionContext context)
    {
        // Clean up worktree
        if (!string.IsNullOrEmpty(context.WorktreePath) &&
            context.WorktreePath != context.OutputPath) await _gitManager.RemoveWorktree(context.OutputPath, context.WorktreePath);

        switch (context.KeepTempFiles)
        {
            // Clean up temp directory
            case false when Directory.Exists(context.TempDirectory):
                try
                {
                    Directory.Delete(context.TempDirectory, recursive: true);
                    Console.WriteLine("Cleaned up temporary files");
                }
                catch
                {
                    // Ignore cleanup errors
                }

                break;
            case true:
                Console.WriteLine($"Debug mode: Temp files preserved at {context.TempDirectory}");
                break;
        }
    }
}