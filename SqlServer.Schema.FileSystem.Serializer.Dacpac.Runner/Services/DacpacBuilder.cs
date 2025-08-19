using System.Diagnostics;
using CSharpFunctionalExtensions;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Core;
using SqlServer.Schema.Common.Constants;
using DacpacConstants = SqlServer.Schema.Common.Constants.SharedConstants;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Models;
using SqlServer.Schema.Migration.Generator;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Services;

/// <summary>
/// Handles DACPAC building operations
/// </summary>
public class DacpacBuilder
{
    readonly FileSystemManager _fileSystemManager = new();
    readonly DacpacMigrationGenerator _migrationGenerator = new();

    /// <summary>
    /// Builds a DACPAC from filesystem state in a worktree
    /// </summary>
    public async Task<Result<string>> BuildFromWorktree(DacpacExtractionContext context)
    {
        Console.WriteLine("Building DACPAC from committed filesystem state...");

        var worktreeTargetPath = Path.Combine(
            context.WorktreePath!,
            DacpacConstants.Directories.Servers,
            context.TargetConnection.SanitizedServer,
            context.TargetConnection.Database);

        if (!Directory.Exists(worktreeTargetPath) ||
            !Directory.GetFiles(worktreeTargetPath, "*.sql", SearchOption.AllDirectories).Any())
        {
            // Schema didn't exist in committed state - this is expected for initial setup
            Console.WriteLine("Schema didn't exist in committed state");
            return Result.Failure<string>("No schema found in committed state - expected for initial setup");
        }

        // Normalize line endings before building
        // _fileSystemManager.NormalizeDirectoryLineEndings(worktreeTargetPath);

        var tempBuildPath = Path.Combine(
            context.TempDirectory,
            $"target-filesystem-build_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");

        var builtDacpac = await _migrationGenerator.BuildDacpacFromFileSystem(
            worktreeTargetPath,
            tempBuildPath,
            DacpacConstants.DacpacNames.TargetFilesystem,
            null);

        if (string.IsNullOrEmpty(builtDacpac) || !File.Exists(builtDacpac))
            return Result.Failure<string>("Failed to build DACPAC from worktree state");

        File.Copy(builtDacpac, context.DacpacPaths.TargetFilesystemDacpac, overwrite: true);
        Console.WriteLine($"✓ Target filesystem DACPAC created: {Path.GetFileName(context.DacpacPaths.TargetFilesystemDacpac)}");

        // Clean up temp directory unless debugging
        CleanupTempDirectory(tempBuildPath, context.KeepTempFiles);

        return Result.Success(context.DacpacPaths.TargetFilesystemDacpac);
    }

    /// <summary>
    /// Builds a DACPAC from the current filesystem state
    /// </summary>
    public async Task<Result<string>> BuildFromFileSystem(
        string schemaPath,
        string outputPath,
        string dacpacName,
        string projectName)
    {
        Console.WriteLine($"Building {dacpacName} DACPAC from filesystem...");

        // Normalize line endings before building
        // _fileSystemManager.NormalizeDirectoryLineEndings(schemaPath);

        var tempBuildPath = Path.Combine(
            Path.GetTempPath(),
            $"{projectName}-build_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");

        var builtDacpac = await _migrationGenerator.BuildDacpacFromFileSystem(
            schemaPath,
            tempBuildPath,
            projectName,
            null);

        if (!string.IsNullOrEmpty(builtDacpac) && File.Exists(builtDacpac))
        {
            File.Copy(builtDacpac, outputPath, overwrite: true);
            Console.WriteLine($"✓ {dacpacName} created: {Path.GetFileName(outputPath)}");

            // Clean up temp directory unless debugging
            CleanupTempDirectory(tempBuildPath, Debugger.IsAttached);

            return Result.Success(outputPath);
        }

        return Result.Failure<string>($"Failed to build DACPAC from filesystem: {schemaPath}");
    }

    /// <summary>
    /// Cleans up temporary build directory
    /// </summary>
    void CleanupTempDirectory(string tempPath, bool keepFiles)
    {
        if (!Directory.Exists(tempPath))
            return;

        if (keepFiles)
        {
            Console.WriteLine($"  Debug mode: Temp directory preserved at {tempPath}");
            return;
        }

        try
        {
            Directory.Delete(tempPath, recursive: true);
            Console.WriteLine("  Cleaned up temp build directory");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠ Could not clean up temp directory: {ex.Message}");
        }
    }
}