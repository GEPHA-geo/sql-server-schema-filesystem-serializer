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
    
    public void CommitChanges(string path, string message)
    {
        RunGitCommand(path, "add .");
        RunGitCommand(path, $"commit -m \"{message}\"");
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