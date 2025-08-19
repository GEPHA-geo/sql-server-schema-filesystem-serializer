using System.Diagnostics;
using CSharpFunctionalExtensions;
using SqlServer.Schema.Common.Constants;
using DacpacConstants = SqlServer.Schema.Common.Constants.SharedConstants;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Services;

/// <summary>
/// Manages Git worktree operations for the DACPAC extraction process
/// </summary>
public class GitWorktreeManager
{
    readonly GitDiffAnalyzer _gitAnalyzer = new();

    /// <summary>
    /// Configures Git safe directories for Docker environments
    /// </summary>
    public void ConfigureGitSafeDirectory(string path)
    {
        try
        {
            var directories = new[] { path }.Concat(DacpacConstants.Git.SafeDirectories);

            foreach (var dir in directories)
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = $"config --global --add safe.directory {dir}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit();
            }
        }
        catch
        {
            // Ignore Git configuration errors - it's not critical for DACPAC extraction
            // This is just to help with migration generation later
        }
    }

    /// <summary>
    /// Prepares the Git repository by checking out the main branch
    /// </summary>
    public async Task<Result> PrepareRepository(string outputPath)
    {
        if (!_gitAnalyzer.IsGitRepository(outputPath))
            return Result.Success(); // Not a git repo, nothing to do

        Console.WriteLine("=== Preparing Git repository ===");
        var (success, message) = _gitAnalyzer.CheckoutBranch(outputPath, DacpacConstants.Git.MainBranch);

        if (!success)
        {
            Console.WriteLine($"❌ Git operation failed: {message}");
            return Result.Failure($"Git branch setup failed: {message}");
        }

        Console.WriteLine(message);
        return Result.Success();
    }

    /// <summary>
    /// Creates a Git worktree for the committed state
    /// </summary>
    public async Task<Result<string>> CreateWorktree(string repoPath, string tempPath)
    {
        var worktreePath = Path.Combine(tempPath, $"worktree_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"worktree add \"{worktreePath}\" {DacpacConstants.Git.MainBranch}",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            Console.WriteLine($"⚠ Could not create worktree: {error}");
            Console.WriteLine("Using current filesystem state instead");
            return Result.Success(repoPath);
        }

        Console.WriteLine("Created git worktree for committed state");
        return Result.Success(worktreePath);
    }

    /// <summary>
    /// Removes a Git worktree
    /// </summary>
    public async Task RemoveWorktree(string repoPath, string worktreePath)
    {
        if (worktreePath == repoPath)
            return; // Don't remove if it's the main repo

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"worktree remove \"{worktreePath}\" --force",
                    WorkingDirectory = repoPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            // Also try to delete the directory if git worktree remove failed
            if (Directory.Exists(worktreePath))
            {
                Directory.Delete(worktreePath, recursive: true);
            }

            Console.WriteLine("  Cleaned up git worktree");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠ Could not clean up worktree: {ex.Message}");
        }
    }

    /// <summary>
    /// Commits changes if there are any uncommitted changes
    /// </summary>
    public async Task<Result> CommitChangesIfNeeded(string outputPath, string? commitMessage)
    {
        if (_gitAnalyzer.GetUncommittedChanges(outputPath, "").Count == 0)
            return Result.Success();

        var message = !string.IsNullOrWhiteSpace(commitMessage)
            ? commitMessage
            : "Schema update with migrations";

        Console.WriteLine($"\n📝 Committing changes: {message}");
        _gitAnalyzer.CommitChanges(outputPath, message);
        return Result.Success();
    }

    /// <summary>
    /// Checks if the repository is a Git repository
    /// </summary>
    public bool IsGitRepository(string path) => _gitAnalyzer.IsGitRepository(path);

    /// <summary>
    /// Executes a git command and returns the result
    /// </summary>
    public async Task<Result<string>> ExecuteGitCommand(string workingDirectory, string arguments)
    {
        try
        {
            Console.WriteLine($"    [Git] Executing: git {arguments}");
            Console.WriteLine($"    [Git] Working directory: {workingDirectory}");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // Read output and error streams asynchronously to prevent deadlock
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            // Wait for process with timeout
            var completed = await Task.Run(() => process.WaitForExit(30000)); // 30 second timeout

            if (!completed)
            {
                try
                {
                    process.Kill();
                }
                catch { }
                return Result.Failure<string>("Git command timed out after 30 seconds");
            }

            var output = await outputTask;
            var error = await errorTask;

            Console.WriteLine($"    [Git] Exit code: {process.ExitCode}");

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"    [Git] Error output: {error}");
                return Result.Failure<string>($"Git command failed: {error}");
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine($"    [Git] Output: {output.Trim()}");
            }

            return Result.Success(output);
        }
        catch (Exception ex)
        {
            return Result.Failure<string>($"Failed to execute git command: {ex.Message}");
        }
    }

    /// <summary>
    /// Stages files to normalize line endings through Git
    /// </summary>
    public async Task<Result> StageFilesForLineEndingNormalization(string repoPath, string relativePath)
    {
        try
        {
            Console.WriteLine($"  Checking if path exists: {Path.Combine(repoPath, relativePath)}");
            var fullPath = Path.Combine(repoPath, relativePath);

            if (!Directory.Exists(fullPath))
            {
                Console.WriteLine($"  Warning: Directory does not exist: {fullPath}");
                return Result.Failure($"Directory does not exist: {fullPath}");
            }

            // Check if there are any SQL files to stage
            var sqlFiles = Directory.GetFiles(fullPath, "*.sql", SearchOption.AllDirectories);
            if (sqlFiles.Length == 0)
            {
                Console.WriteLine("  No SQL files found to normalize");
                return Result.Success();
            }

            Console.WriteLine($"  Found {sqlFiles.Length} SQL files to process");

            // Instead of staging all at once, let's use git add with --renormalize flag
            // This is specifically designed for line ending normalization
            Console.WriteLine("  Normalizing line endings using git add --renormalize...");
            var normalizeResult = await ExecuteGitCommand(repoPath, $"add --renormalize \"{relativePath}\"");

            if (normalizeResult.IsFailure)
            {
                // If --renormalize is not available (older git), fall back to regular add/reset
                Console.WriteLine("  Falling back to standard add/reset approach...");

                // Add files
                var stageResult = await ExecuteGitCommand(repoPath, $"add \"{relativePath}\"");
                if (stageResult.IsFailure)
                {
                    return Result.Failure($"Failed to stage files: {stageResult.Error}");
                }

                // Reset to unstage (this applies normalization)
                var unstageResult = await ExecuteGitCommand(repoPath, $"reset \"{relativePath}\"");
                if (unstageResult.IsFailure)
                {
                    Console.WriteLine($"  Warning: Could not unstage files: {unstageResult.Error}");
                }
            }
            else
            {
                // After renormalize, unstage the files
                var unstageResult = await ExecuteGitCommand(repoPath, $"reset \"{relativePath}\"");
                if (unstageResult.IsFailure)
                {
                    Console.WriteLine($"  Warning: Could not unstage files: {unstageResult.Error}");
                }
            }

            Console.WriteLine("  Line ending normalization complete");
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed in staging process: {ex.Message}");
        }
    }
}