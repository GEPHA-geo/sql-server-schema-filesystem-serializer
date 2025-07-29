using System.Diagnostics;

namespace SqlServer.Schema.Migration.Generator.GitIntegration;

public class GitDiffAnalyzer
{
    public bool IsGitRepository(string path)
    {
        var gitDir = Path.Combine(path, ".git");
        return Directory.Exists(gitDir);
    }
    
    public void InitializeRepository(string path)
    {
        RunGitCommand(path, "init");
        
        // Create .gitignore
        var gitignorePath = Path.Combine(path, ".gitignore");
        if (!File.Exists(gitignorePath))
        {
            File.WriteAllText(gitignorePath, @"# Temporary files
*.tmp
*.bak
generated_script.sql
");
        }
        
        RunGitCommand(path, "add .");
    }
    
    public List<DiffEntry> GetUncommittedChanges(string path, string databaseName)
    {
        var entries = new List<DiffEntry>();
        
        // Get status of files (including untracked files)
        var statusOutput = RunGitCommand(path, "status --porcelain -u");
        var lines = statusOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        // Normalize databaseName to use forward slashes for comparison
        var normalizedDatabaseName = databaseName.Replace('\\', '/');
        
        foreach (var line in lines)
        {
            if (line.Length < 3) continue;
            
            var status = line.Substring(0, 2).Trim();
            var filePath = line.Substring(3).Trim();
            
            // Only process SQL files from the database directory
            if (filePath.StartsWith(normalizedDatabaseName) && filePath.EndsWith(".sql"))
            {
                var changeType = MapGitStatus(status);
                if (changeType != ChangeType.Unknown)
                {
                    entries.Add(new DiffEntry
                    {
                        Path = filePath,
                        ChangeType = changeType,
                        OldContent = GetFileContentFromGit(path, filePath),
                        NewContent = GetFileContent(Path.Combine(path, filePath))
                    });
                }
            }
        }
        
        return entries;
    }

    public (bool success, string message) CheckoutBranch(string path, string branch)
    {
        try
        {
            Console.WriteLine("=== Starting Git Branch Setup for Migration Generation ===");
            Console.WriteLine($"Working directory: {path}");
            
            // First, log the current branch
            var currentBranch = RunGitCommand(path, "branch --show-current").Trim();
            Console.WriteLine($"Current branch: {currentBranch}");
            
            // Check git status before any operations
            Console.WriteLine("\nChecking git status before operations:");
            try
            {
                var status = RunGitCommand(path, "status --short");
                if (!string.IsNullOrWhiteSpace(status))
                {
                    Console.WriteLine("Uncommitted changes detected:");
                    Console.WriteLine(status);
                }
                else
                {
                    Console.WriteLine("Working directory is clean");
                }
            }
            catch (Exception statusEx)
            {
                Console.WriteLine($"Could not get git status: {statusEx.Message}");
            }
            
            // Check if we're already on main branch
            if (currentBranch == "main")
            {
                Console.WriteLine("\nAlready on main branch, performing hard reset to ensure clean state...");
                try
                {
                    RunGitCommand(path, "reset --hard HEAD");
                    Console.WriteLine("✓ Hard reset completed, using clean main branch state");
                    
                    // Log the commit we're on after reset
                    var commitAfterReset = RunGitCommand(path, "rev-parse HEAD").Trim();
                    var commitMessage = RunGitCommand(path, "log -1 --pretty=%s").Trim();
                    Console.WriteLine($"Now at commit: {commitAfterReset.Substring(0, Math.Min(8, commitAfterReset.Length))} - {commitMessage}");
                }
                catch (Exception resetEx)
                {
                    Console.WriteLine($"⚠ Warning: Could not perform hard reset: {resetEx.Message}");
                }
                return (true, "Using current main branch state after reset");
            }
            
            // Check available remotes
            Console.WriteLine("\nChecking for git remotes...");
            string remoteList = "";
            try
            {
                remoteList = RunGitCommand(path, "remote -v").Trim();
                if (!string.IsNullOrWhiteSpace(remoteList))
                {
                    Console.WriteLine("Available remotes:");
                    Console.WriteLine(remoteList);
                }
                else
                {
                    Console.WriteLine("No git remotes found");
                }
            }
            catch
            {
                Console.WriteLine("No git remotes configured");
            }
            
            // Try to fetch if remotes are available
            if (!string.IsNullOrEmpty(remoteList))
            {
                try
                {
                    Console.WriteLine("\nFetching latest changes from all remotes...");
                    var fetchOutput = RunGitCommand(path, "fetch --all --verbose");
                    Console.WriteLine("✓ Fetch completed successfully");
                    if (!string.IsNullOrWhiteSpace(fetchOutput))
                    {
                        Console.WriteLine("Fetch output:");
                        Console.WriteLine(fetchOutput);
                    }
                }
                catch (Exception fetchEx)
                {
                    Console.WriteLine($"⚠ Warning: Could not fetch from remote: {fetchEx.Message}");
                }
            }
            
            // Perform hard reset to clean any uncommitted changes before switching branches
            Console.WriteLine("\nPerforming hard reset to ensure clean working directory...");
            try
            {
                // First stash any changes just in case
                try
                {
                    var stashResult = RunGitCommand(path, "stash push -m \"Auto-stash before migration generation\"");
                    if (stashResult.Contains("Saved working directory"))
                    {
                        Console.WriteLine("✓ Stashed uncommitted changes");
                    }
                }
                catch
                {
                    // Ignore stash errors
                }
                
                RunGitCommand(path, "reset --hard HEAD");
                Console.WriteLine("✓ Hard reset completed");
                
                // Clean untracked files as well
                try
                {
                    RunGitCommand(path, "clean -fd");
                    Console.WriteLine("✓ Cleaned untracked files and directories");
                }
                catch (Exception cleanEx)
                {
                    Console.WriteLine($"⚠ Could not clean untracked files: {cleanEx.Message}");
                }
            }
            catch (Exception resetEx)
            {
                Console.WriteLine($"⚠ Warning: Could not perform hard reset: {resetEx.Message}");
            }
            
            // Create a new branch based on main (local or origin/main if available)
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var newBranchName = $"migration-{timestamp}";
            Console.WriteLine($"\nCreating new branch '{newBranchName}' for migration detection...");
            
            try
            {
                // First, check if origin/main exists
                Console.WriteLine("Checking if origin/main exists...");
                try
                {
                    var originMainHash = RunGitCommand(path, "rev-parse origin/main").Trim();
                    Console.WriteLine($"✓ Found origin/main at commit: {originMainHash.Substring(0, Math.Min(8, originMainHash.Length))}");
                    
                    // Try to create branch from origin/main
                    RunGitCommand(path, $"checkout -b {newBranchName} origin/main");
                    Console.WriteLine("✓ Successfully created branch from origin/main");
                }
                catch
                {
                    throw new Exception("origin/main not found");
                }
            }
            catch
            {
                // Fallback to local main branch
                Console.WriteLine("origin/main not found, checking for local main branch...");
                try
                {
                    var localMainHash = RunGitCommand(path, "rev-parse main").Trim();
                    Console.WriteLine($"✓ Found local main at commit: {localMainHash.Substring(0, Math.Min(8, localMainHash.Length))}");
                    
                    RunGitCommand(path, $"checkout -b {newBranchName} main");
                    Console.WriteLine("✓ Successfully created branch from local main");
                }
                catch (Exception mainEx)
                {
                    // If even local main doesn't exist, just create a new branch
                    Console.WriteLine($"⚠ Could not find main branch: {mainEx.Message}");
                    Console.WriteLine("Creating new branch from current HEAD...");
                    RunGitCommand(path, $"checkout -b {newBranchName}");
                    Console.WriteLine("✓ Created new branch from current HEAD");
                }
            }
            
            // Verify we're on the correct branch/commit
            Console.WriteLine("\nVerifying branch state:");
            var verifyBranch = RunGitCommand(path, "rev-parse --abbrev-ref HEAD").Trim();
            var commitHash = RunGitCommand(path, "rev-parse HEAD").Trim();
            var commitMessage = RunGitCommand(path, "log -1 --pretty=%s").Trim();
            Console.WriteLine($"✓ Now on branch: {verifyBranch}");
            Console.WriteLine($"  Commit: {commitHash.Substring(0, Math.Min(8, commitHash.Length))} - {commitMessage}");
            
            // Final status check
            Console.WriteLine("\nFinal git status:");
            try
            {
                var finalStatus = RunGitCommand(path, "status --short");
                if (string.IsNullOrWhiteSpace(finalStatus))
                {
                    Console.WriteLine("✓ Working directory is clean and ready for migration detection");
                }
                else
                {
                    Console.WriteLine("⚠ Warning: Unexpected changes after setup:");
                    Console.WriteLine(finalStatus);
                }
            }
            catch
            {
                // Ignore status check errors
            }
            
            Console.WriteLine("\n=== Git Branch Setup Completed ===");
            return (true, $"Successfully created branch {newBranchName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Error during git branch setup: {ex.Message}");
            return (false, ex.Message);
        }
    }
    
    public void CommitChanges(string path, string message)
    {
        RunGitCommand(path, "add .");
        RunGitCommand(path, $"commit -m \"{message}\"");
    }
    
    public void CommitSpecificFiles(string path, string filePattern, string message)
    {
        RunGitCommand(path, $"add {filePattern}");
        try
        {
            RunGitCommand(path, $"commit -m \"{message}\"");
        }
        catch (Exception ex)
        {
            // If there's nothing to commit, that's okay
            if (!ex.Message.Contains("nothing to commit"))
            {
                throw;
            }
        }
    }
    
    public void FetchRemote(string path, string remote = "origin")
    {
        Console.WriteLine($"Fetching latest changes from {remote}...");
        RunGitCommand(path, $"fetch {remote}");
    }
    
    public void CheckoutRemoteBranch(string path, string remote = "origin", string branch = "main")
    {
        Console.WriteLine($"Checking out {remote}/{branch}...");
        RunGitCommand(path, $"checkout {remote}/{branch}");
    }
    
    public void CreateAndCheckoutBranch(string path, string branchName)
    {
        Console.WriteLine($"Creating and checking out branch: {branchName}");
        RunGitCommand(path, $"checkout -b {branchName}");
    }
    
    public string GetCurrentBranch(string path)
    {
        var output = RunGitCommand(path, "branch --show-current").Trim();
        return string.IsNullOrEmpty(output) ? "HEAD" : output;
    }
    
    public bool HasRemote(string path, string remote = "origin")
    {
        try
        {
            RunGitCommand(path, $"remote get-url {remote}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    ChangeType MapGitStatus(string status)
    {
        return status switch
        {
            "A" => ChangeType.Added,
            "M" => ChangeType.Modified,
            "D" => ChangeType.Deleted,
            "AM" => ChangeType.Modified,
            "MM" => ChangeType.Modified,
            "??" => ChangeType.Added,  // Untracked files are considered Added
            _ => status.Contains("A") ? ChangeType.Added : 
                 status.Contains("M") ? ChangeType.Modified :
                 status.Contains("D") ? ChangeType.Deleted : ChangeType.Unknown
        };
    }

    string GetFileContentFromGit(string repoPath, string filePath)
    {
        try
        {
            return RunGitCommand(repoPath, $"show HEAD:\"{filePath}\"");
        }
        catch
        {
            // File doesn't exist in HEAD
            return string.Empty;
        }
    }

    string GetFileContent(string fullPath)
    {
        if (File.Exists(fullPath))
        {
            return File.ReadAllText(fullPath);
        }
        return string.Empty;
    }

    string RunGitCommand(string workingDirectory, string arguments)
    {
        // Ensure the working directory exists
        if (!Directory.Exists(workingDirectory))
        {
            throw new InvalidOperationException($"Working directory does not exist: {workingDirectory}");
        }
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment = { ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin" }
        };
        
        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start git process");
            }
        
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            
            process.WaitForExit();
            
            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException($"Git command failed: {error}");
            }
            
            return output;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"Failed to execute git command. Make sure git is installed and in PATH. Error: {ex.Message}", ex);
        }
    }
}