using System.Diagnostics;
using CSharpFunctionalExtensions;
using Microsoft.Data.SqlClient;
using SqlServer.Schema.Exclusion.Manager.Core.Services;
using SqlServer.Schema.Common.Constants;
using SqlServer.Schema.Common.PathManagement;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Models;
using CommonDacpacPaths = SqlServer.Schema.Common.PathManagement.DacpacPaths;
using static SqlServer.Schema.Common.Constants.SharedConstants.DacpacNames;
using static SqlServer.Schema.Common.Constants.SharedConstants.Files;
using DacpacConstants = SqlServer.Schema.Common.Constants.SharedConstants;

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

                // Phases 1-4: Generate all DACPACs in parallel
                // All four DACPAC generation operations are independent and can run simultaneously
                Console.WriteLine("\n=== Generating all DACPACs in parallel ===");
                
                // Start all DACPAC generation tasks
                var buildTargetFilesystemTask = BuildTargetFilesystemDacpac(context);
                var extractTargetOriginalTask = ExtractTargetOriginalDacpac(context);
                var extractSourceOriginalTask = ExtractSourceOriginalDacpac(context);
                var extractAndBuildSourceTask = ExtractAndBuildSourceFilesystem(context);
                
                // Wait for all operations to complete
                await Task.WhenAll(
                    buildTargetFilesystemTask,
                    extractTargetOriginalTask, 
                    extractSourceOriginalTask,
                    extractAndBuildSourceTask);

                // var combinedResult = Result.Combine(await buildTargetFilesystemTask,
                //     await extractTargetOriginalTask,
                //     await extractSourceOriginalTask,
                //     await extractAndBuildSourceTask);
                // if (combinedResult.IsFailure) return combinedResult.ConvertFailure<ExtractionResult>();
                
                // Check results - Target filesystem can fail (expected initially)
                var targetFilesystemResult = await buildTargetFilesystemTask;
                if (targetFilesystemResult.IsSuccess)
                {
                    context = targetFilesystemResult.Value; // Update context with worktree info
                }
                else
                {
                    Console.WriteLine($"⚠ Target filesystem DACPAC: {targetFilesystemResult.Error}");
                }
                
                // Check required results
                var targetOriginalResult = await extractTargetOriginalTask;
                if (targetOriginalResult.IsFailure)
                    return Result.Failure<ExtractionResult>(targetOriginalResult.Error);
                    
                var sourceOriginalResult = await extractSourceOriginalTask;
                if (sourceOriginalResult.IsFailure)
                    return Result.Failure<ExtractionResult>(sourceOriginalResult.Error);
                    
                var sourceFilesystemResult = await extractAndBuildSourceTask;
                if (sourceFilesystemResult.IsFailure)
                    return Result.Failure<ExtractionResult>(sourceFilesystemResult.Error);

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

                // Move .dacpac-exclusions.json to SCMP directory if it exists in parent (final cleanup)
                if (context.FilePaths != null && File.Exists(context.FilePaths.TempExclusionsJsonPath))
                {
                    // Delete any existing file in SCMP directory first
                    if (File.Exists(context.FilePaths.ExclusionsJsonPath))
                    {
                        File.Delete(context.FilePaths.ExclusionsJsonPath);
                    }
                    File.Move(context.FilePaths.TempExclusionsJsonPath, context.FilePaths.ExclusionsJsonPath);
                    Console.WriteLine($"✓ Moved {DacpacConstants.Files.ExclusionsFile} to SCMP directory");
                }

                // Prepare successful result
                var result = new ExtractionResult
                {
                    MigrationPath = migrationResult.Value.MigrationPath,
                    DifferenceCount = comparisonResult.Value.TotalDifferences,
                    ExcludedDifferenceCount = comparisonResult.Value.ExcludedDifferences
                };

                Console.WriteLine("\n✓ SCMP-based extraction completed successfully");
                Console.WriteLine($"  - 4 DACPACs generated in: {context.ScmpOutputPath}");
                Console.WriteLine($"  - SCMP files saved in: {context.ScmpOutputPath}");
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

        // Create centralized path manager
        var filePaths = new DacpacFilePaths(
            options.OutputPath,
            targetConnection.SanitizedServer,
            targetConnection.Database,
            sourceConnection.SanitizedServer,
            sourceConnection.SanitizedDatabase);
        
        // Create all necessary directories
        filePaths.CreateDirectories();
        
        // Create temp directory
        var tempDirectory =
            Path.Combine(Path.GetTempPath(), $"dacpac_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        Console.WriteLine($"Output path: {filePaths.TargetOutputPath}");
        Console.WriteLine($"SCMP path: {filePaths.ScmpDirectoryPath}");
        Console.WriteLine($"Source files path: {filePaths.SourceSubdirectoryPath}");
        
        // Original SCMP file will be saved later in SchemaComparisonService
        // along with the dacpacs SCMP file to avoid race conditions

        // Create context with all values initialized
        var context = new DacpacExtractionContext
        {
            OutputPath = options.OutputPath,
            ScmpModel = scmpModel,
            KeepTempFiles = options.KeepTempFiles || Debugger.IsAttached,
            TempDirectory = tempDirectory,
            SourceConnection = sourceConnection,
            TargetConnection = targetConnection,
            TargetOutputPath = filePaths.TargetOutputPath,
            ScmpOutputPath = filePaths.ScmpDirectoryPath,
            DacpacPaths = new Models.DacpacPaths
            {
                TargetFilesystemDacpac = filePaths.TargetFilesystemDacpacPath,
                TargetOriginalDacpac = filePaths.TargetOriginalDacpacPath,
                SourceFilesystemDacpac = filePaths.SourceFilesystemDacpacPath,
                SourceOriginalDacpac = filePaths.SourceOriginalDacpacPath
            },
            FilePaths = filePaths
        };

        return Result.Success(context);
    }

    /// <summary>
    /// Phase 1: Build Target Filesystem DACPAC
    /// </summary>
    async Task<Result<DacpacExtractionContext>> BuildTargetFilesystemDacpac(DacpacExtractionContext context)
    {
        Console.WriteLine("  • Building Target Filesystem DACPAC from git worktree...");

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
            ScmpOutputPath = context.ScmpOutputPath,
            WorktreePath = worktreePath,
            DacpacPaths = context.DacpacPaths,
            FilePaths = context.FilePaths,  // IMPORTANT: Copy FilePaths to preserve original SCMP path
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
        Console.WriteLine("  • Extracting Target Original DACPAC from database...");

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
        Console.WriteLine("  • Extracting Source Original DACPAC from database...");

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
        Console.WriteLine("  • Extracting Source Schema and Building Filesystem DACPAC...");

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