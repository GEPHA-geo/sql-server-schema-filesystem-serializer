using System.Diagnostics;

namespace SqlServer.Schema.Exclusion.Manager.Tests;

// Base class for tests that require a git repository
public abstract class GitTestBase : IDisposable
{
    protected string GitRepoPath { get; }
    
    protected GitTestBase()
    {
        GitRepoPath = SetupTestGitRepo();
    }
    
    // Create a test git repository with initial commit structure
    protected static string SetupTestGitRepo(bool withChanges = false, bool parentIsOriginMain = false)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"git_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        
        // Initialize git repo
        RunGitCommand(tempDir, "init");
        RunGitCommand(tempDir, "config user.email test@test.com");
        RunGitCommand(tempDir, "config user.name TestUser");
        
        // Create initial commit on main
        File.WriteAllText(Path.Combine(tempDir, "test.txt"), "initial");
        RunGitCommand(tempDir, "add .");
        RunGitCommand(tempDir, "commit -m \"Initial commit\"");
        
        // Set up origin/main
        RunGitCommand(tempDir, "branch -M main");
        
        if (withChanges)
        {
            // Create some schema-like files for testing
            var schemasDir = Path.Combine(tempDir, "servers", "TestServer", "TestDB", "schemas");
            Directory.CreateDirectory(schemasDir);
            
            var tablesDir = Path.Combine(schemasDir, "dbo", "Tables");
            Directory.CreateDirectory(tablesDir);
            File.WriteAllText(Path.Combine(tablesDir, "Users.sql"), "CREATE TABLE Users");
            
            var viewsDir = Path.Combine(schemasDir, "dbo", "Views");
            Directory.CreateDirectory(viewsDir);
            File.WriteAllText(Path.Combine(viewsDir, "vw_UserList.sql"), "CREATE VIEW vw_UserList");
            
            RunGitCommand(tempDir, "add .");
            RunGitCommand(tempDir, "commit -m \"Add schema files\"");
        }
        
        if (parentIsOriginMain)
        {
            // Mark current commit as origin/main
            RunGitCommand(tempDir, "update-ref refs/remotes/origin/main HEAD");
            
            // Create a new commit on top of origin/main
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "updated");
            RunGitCommand(tempDir, "add .");
            RunGitCommand(tempDir, "commit -m \"New commit on top of main\"");
        }
        else if (withChanges)
        {
            // Mark the first commit as origin/main (not the parent of HEAD)
            RunGitCommand(tempDir, "update-ref refs/remotes/origin/main HEAD~1");
        }
        else
        {
            // Mark current as origin/main for simple cases
            RunGitCommand(tempDir, "update-ref refs/remotes/origin/main HEAD");
        }
        
        return tempDir;
    }
    
    protected static void RunGitCommand(string workingDir, string arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        
        process.Start();
        process.WaitForExit();
        
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new Exception($"Git command failed: git {arguments}\nError: {error}");
        }
    }
    
    // Add a file and commit it
    protected void AddFileAndCommit(string relativePath, string content, string commitMessage)
    {
        var fullPath = Path.Combine(GitRepoPath, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        
        File.WriteAllText(fullPath, content);
        RunGitCommand(GitRepoPath, $"add \"{relativePath}\"");
        RunGitCommand(GitRepoPath, $"commit -m \"{commitMessage}\"");
    }
    
    // Modify a file and stage it (but don't commit)
    protected void ModifyAndStageFile(string relativePath, string newContent)
    {
        var fullPath = Path.Combine(GitRepoPath, relativePath);
        File.WriteAllText(fullPath, newContent);
        RunGitCommand(GitRepoPath, $"add \"{relativePath}\"");
    }
    
    public void Dispose()
    {
        if (Directory.Exists(GitRepoPath))
        {
            try
            {
                // Remove read-only attributes from all files (needed for .git folder on Windows)
                foreach (var file in Directory.GetFiles(GitRepoPath, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                
                // Remove read-only attributes from all directories
                foreach (var dir in Directory.GetDirectories(GitRepoPath, "*", SearchOption.AllDirectories))
                {
                    new DirectoryInfo(dir).Attributes = FileAttributes.Normal;
                }
                
                Directory.Delete(GitRepoPath, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}