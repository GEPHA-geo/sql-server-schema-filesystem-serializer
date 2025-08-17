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
            
            // First check if this is a fresh repository without remotes
            bool hasRemotes = false;
            try
            {
                // Add diagnostics for debugging git issues in containers
                Console.WriteLine($"Current working directory: {Directory.GetCurrentDirectory()}");
                Console.WriteLine($"Target path: {path}");
                Console.WriteLine($"Absolute target path: {Path.GetFullPath(path)}");
                Console.WriteLine($".git directory exists: {Directory.Exists(Path.Combine(path, ".git"))}");
                
                // List top-level directory contents
                try
                {
                    var targetDir = Path.GetFullPath(path);
                    Console.WriteLine($"\nDirectory contents of {targetDir}:");
                    
                    // List directories
                    var dirs = Directory.GetDirectories(targetDir).Select(Path.GetFileName).OrderBy(d => d);
                    Console.WriteLine($"Directories ({dirs.Count()}):");
                    foreach (var dir in dirs)
                    {
                        Console.WriteLine($"  [DIR] {dir}");
                    }
                    
                    // List files
                    var files = Directory.GetFiles(targetDir).Select(Path.GetFileName).OrderBy(f => f);
                    Console.WriteLine($"Files ({files.Count()}):");
                    foreach (var file in files.Take(20)) // Show first 20 files
                    {
                        Console.WriteLine($"  [FILE] {file}");
                    }
                    if (files.Count() > 20)
                    {
                        Console.WriteLine($"  ... and {files.Count() - 20} more files");
                    }
                }
                catch (Exception listEx)
                {
                    Console.WriteLine($"Could not list directory contents: {listEx.Message}");
                }
                
                // Check common GitHub Actions paths
                var githubWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
                if (!string.IsNullOrEmpty(githubWorkspace))
                {
                    Console.WriteLine($"GitHub Workspace: {githubWorkspace}");
                    Console.WriteLine($"GitHub Workspace .git exists: {Directory.Exists(Path.Combine(githubWorkspace, ".git"))}");
                }
                
                try
                {
                    var gitRoot = RunGitCommand(path, "rev-parse --show-toplevel").Trim();
                    Console.WriteLine($"Git root directory: {gitRoot}");
                    
                    // Check if we're in a subdirectory
                    var currentPath = Path.GetFullPath(path);
                    if (!currentPath.Equals(gitRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"WARNING: Working in subdirectory of git repo!");
                        Console.WriteLine($"  Current: {currentPath}");
                        Console.WriteLine($"  Git root: {gitRoot}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not determine git root: {ex.Message}");
                }
                
                // Check git config to understand remote issues
                try
                {
                    var configList = RunGitCommand(path, "config --list");
                    var remoteConfigs = configList.Split('\n').Where(line => line.StartsWith("remote.")).ToList();
                    if (remoteConfigs.Any())
                    {
                        Console.WriteLine("Git remote configuration found:");
                        foreach (var config in remoteConfigs.Take(5)) // Show first 5 remote configs
                        {
                            Console.WriteLine($"  {config}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("No remote configuration found in git config");
                    }
                }
                catch (Exception configEx)
                {
                    Console.WriteLine($"Could not read git config: {configEx.Message}");
                }
                
                var remotes = RunGitCommand(path, "remote").Trim();
                hasRemotes = !string.IsNullOrWhiteSpace(remotes);
                if (!hasRemotes)
                {
                    Console.WriteLine("WARNING: No git remotes found!");
                    Console.WriteLine("This might indicate:");
                    Console.WriteLine("  - Repository ownership issues (common in Docker)");
                    Console.WriteLine("  - Working in a subdirectory with 'git init' instead of the main repo");
                    Console.WriteLine("  - Missing git configuration");
                    
                    // Try to check git directory permissions
                    try
                    {
                        var gitDir = Path.Combine(path, ".git");
                        if (Directory.Exists(gitDir))
                        {
                            var gitConfigFile = Path.Combine(gitDir, "config");
                            Console.WriteLine($"Git config file exists: {File.Exists(gitConfigFile)}");
                            if (File.Exists(gitConfigFile))
                            {
                                // Try to read first few lines of git config
                                var configLines = File.ReadAllLines(gitConfigFile).Take(10);
                                Console.WriteLine("Git config file content (first 10 lines):");
                                foreach (var line in configLines)
                                {
                                    Console.WriteLine($"  {line}");
                                }
                            }
                        }
                    }
                    catch (Exception permEx)
                    {
                        Console.WriteLine($"Could not check git directory: {permEx.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"Found remotes: {remotes.Replace('\n', ' ')}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking for remotes: {ex.Message}");
                // Ignore - assume no remotes
            }
            
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
                Console.WriteLine("\nAlready on main branch, ensuring clean state...");
                
                // First perform hard reset to ensure clean state
                try
                {
                    Console.WriteLine("Performing hard reset to ensure clean state...");
                    RunGitCommand(path, "reset --hard HEAD");
                    Console.WriteLine("✓ Hard reset completed");
                    
                    // Create migration branch after hard reset
                    var migrationBranch = $"migration/{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                    Console.WriteLine($"\nCreating migration branch: {migrationBranch}");
                    RunGitCommand(path, $"checkout -b {migrationBranch}");
                    Console.WriteLine($"✓ Created and switched to branch: {migrationBranch}");
                }
                catch (Exception resetEx)
                {
                    Console.WriteLine($"❌ CRITICAL: Could not perform hard reset: {resetEx.Message}");
                    throw new InvalidOperationException($"Git hard reset failed: {resetEx.Message}", resetEx);
                }
                
                // Only attempt remote operations if remotes exist
                if (hasRemotes)
                {
                    Console.WriteLine("\nUpdating from remote...");
                    
                    // Check for available remotes before attempting to pull
                    string detectedRemote = null;
                    try
                    {
                        var remotes = RunGitCommand(path, "remote").Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        if (remotes.Length > 0)
                        {
                            // Prefer 'origin' if it exists, otherwise use the first available remote
                            detectedRemote = remotes.Contains("origin") ? "origin" : remotes[0];
                            Console.WriteLine($"Using remote: {detectedRemote}");
                        }
                    }
                    catch
                    {
                        Console.WriteLine("No git remotes configured");
                    }
                    
                    // Then try to pull --rebase from the detected remote/main
                    if (!string.IsNullOrEmpty(detectedRemote))
                    {
                        try
                        {
                            Console.WriteLine($"\nAttempting to pull --rebase from {detectedRemote}/main...");
                            var pullOutput = RunGitCommand(path, $"pull --rebase {detectedRemote} main");
                            Console.WriteLine($"✓ Successfully pulled latest changes from {detectedRemote}/main");
                            if (!string.IsNullOrWhiteSpace(pullOutput))
                            {
                                Console.WriteLine(pullOutput);
                            }
                            
                            // Log the commit we're on after reset
                            var commitAfterReset = RunGitCommand(path, "rev-parse HEAD").Trim();
                            var commitMsgAfterReset = RunGitCommand(path, "log -1 --pretty=%s").Trim();
                            Console.WriteLine($"Now at commit: {commitAfterReset.Substring(0, Math.Min(8, commitAfterReset.Length))} - {commitMsgAfterReset}");
                        }
                        catch (Exception pullEx)
                        {
                            Console.WriteLine($"⚠ Warning: Could not pull from {detectedRemote}/main: {pullEx.Message}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("❌ CRITICAL: No remotes configured in repository");
                    Console.WriteLine("This should not happen in a CI/CD environment where the repository was cloned.");
                    Console.WriteLine("Possible causes:");
                    Console.WriteLine("  - The repository was initialized with 'git init' instead of being cloned");
                    Console.WriteLine("  - The working directory is not the actual repository root");
                    throw new InvalidOperationException("No git remotes found. This indicates a misconfigured environment.");
                }
                
                return (true, "Using current main branch state");
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
                    Console.WriteLine("Fetching latest changes from origin main...");
                    var fetchOutput = RunGitCommand(path, "fetch origin main --verbose");
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
                Console.WriteLine($"❌ CRITICAL: Could not perform hard reset: {resetEx.Message}");
                throw new InvalidOperationException($"Git hard reset failed: {resetEx.Message}", resetEx);
            }
            
            // Create a new branch based on main (local or origin/main if available)
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var newBranchName = $"migration-{timestamp}";
            Console.WriteLine($"\nCreating new branch '{newBranchName}' for migration detection...");
            
            // Check for available remotes
            if (!hasRemotes)
            {
                Console.WriteLine("❌ CRITICAL: No remotes configured in repository");
                Console.WriteLine("Cannot create migration branch without remotes.");
                throw new InvalidOperationException("No git remotes found. Cannot proceed with migration branch creation.");
            }
            
            string remoteName = null;
            try
            {
                var remotes = RunGitCommand(path, "remote").Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (remotes.Length > 0)
                {
                    // Prefer 'origin' if it exists, otherwise use the first available remote
                    remoteName = remotes.Contains("origin") ? "origin" : remotes[0];
                    Console.WriteLine($"Using remote: {remoteName}");
                }
            }
            catch
            {
                Console.WriteLine("Error checking remotes");
            }
            
            try
            {
                // First, check if remote/main exists
                if (!string.IsNullOrEmpty(remoteName))
                {
                    Console.WriteLine($"Checking if {remoteName}/main exists...");
                    try
                    {
                        var remoteMainHash = RunGitCommand(path, $"rev-parse {remoteName}/main").Trim();
                        Console.WriteLine($"✓ Found {remoteName}/main at commit: {remoteMainHash.Substring(0, Math.Min(8, remoteMainHash.Length))}");
                        
                        // Try to create branch from remote/main
                        RunGitCommand(path, $"checkout -b {newBranchName} {remoteName}/main");
                        Console.WriteLine($"✓ Successfully created branch from {remoteName}/main");
                    }
                    catch
                    {
                        throw new Exception($"{remoteName}/main not found");
                    }
                }
                else
                {
                    throw new Exception("No remote configured");
                }
            }
            catch
            {
                // Fallback to local main branch
                Console.WriteLine($"{remoteName ?? "Remote"}/main not found, checking for local main branch...");
                try
                {
                    var localMainHash = RunGitCommand(path, "rev-parse main").Trim();
                    Console.WriteLine($"✓ Found local main at commit: {localMainHash.Substring(0, Math.Min(8, localMainHash.Length))}");
                    
                    // First checkout main
                    Console.WriteLine("Checking out main branch...");
                    RunGitCommand(path, "checkout main");
                    
                    // Hard reset to ensure clean state
                    try
                    {
                        Console.WriteLine("Performing hard reset to ensure clean state...");
                        RunGitCommand(path, "reset --hard HEAD");
                        Console.WriteLine("✓ Hard reset completed");
                    }
                    catch (Exception resetEx)
                    {
                        Console.WriteLine($"❌ CRITICAL: Could not perform hard reset: {resetEx.Message}");
                        throw new InvalidOperationException($"Git hard reset failed: {resetEx.Message}", resetEx);
                    }
                    
                    // Then pull latest changes if remote exists
                    if (!string.IsNullOrEmpty(remoteName))
                    {
                        try
                        {
                            Console.WriteLine($"\nAttempting to pull --rebase from {remoteName}/main...");
                            var pullOutput = RunGitCommand(path, $"pull --rebase {remoteName} main");
                            Console.WriteLine("✓ Successfully pulled latest changes");
                            if (!string.IsNullOrWhiteSpace(pullOutput))
                            {
                                Console.WriteLine(pullOutput);
                            }
                        }
                        catch (Exception pullEx)
                        {
                            Console.WriteLine($"⚠ Could not pull from {remoteName}: {pullEx.Message}");
                        }
                    }
                    
                    // Now create new branch from updated main
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
        Console.WriteLine($"\n📝 Committing changes with message: {message}");
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

    static string RunGitCommand(string workingDirectory, string arguments)
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