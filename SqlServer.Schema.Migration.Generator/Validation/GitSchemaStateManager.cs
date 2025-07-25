using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SqlServer.Schema.Migration.Generator.Validation;

public class GitSchemaStateManager
{
    public async Task<string> ExportPreviousSchemaAsync(string repoPath, string databaseName, string previousCommit)
    {
        // Create a temporary directory for the previous schema
        var tempPath = Path.Combine(Path.GetTempPath(), $"MigrationValidation_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);
        
        var schemaPath = Path.Combine(tempPath, "schemas");
        Directory.CreateDirectory(schemaPath);
        
        Console.WriteLine($"Exporting schema from commit {previousCommit} to {tempPath}");
        
        try
        {
            // Use git archive to export the schemas directory at the previous commit
            var gitProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"archive {previousCommit} {databaseName}/schemas | tar -x -C \"{tempPath}\"",
                    WorkingDirectory = repoPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            gitProcess.Start();
            
            var output = await gitProcess.StandardOutput.ReadToEndAsync();
            var error = await gitProcess.StandardError.ReadToEndAsync();
            
            await gitProcess.WaitForExitAsync();
            
            if (gitProcess.ExitCode != 0)
            {
                // Try alternative approach using git show
                await ExportUsingGitShow(repoPath, databaseName, previousCommit, tempPath);
            }
            
            // Move the schemas to the expected location
            var exportedSchemasPath = Path.Combine(tempPath, databaseName, "schemas");
            if (Directory.Exists(exportedSchemasPath))
            {
                // Move contents to the schemas directory
                foreach (var dir in Directory.GetDirectories(exportedSchemasPath))
                {
                    var destDir = Path.Combine(schemaPath, Path.GetFileName(dir));
                    Directory.Move(dir, destDir);
                }
                
                // Clean up the database directory
                Directory.Delete(Path.Combine(tempPath, databaseName), true);
            }
            
            return schemaPath;
        }
        catch (Exception ex)
        {
            // Clean up on failure
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
            
            throw new Exception($"Failed to export previous schema: {ex.Message}", ex);
        }
    }
    
    async Task ExportUsingGitShow(string repoPath, string databaseName, string previousCommit, string tempPath)
    {
        Console.WriteLine("Using alternative git export method...");
        
        // Get list of files at previous commit
        var listProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"ls-tree -r --name-only {previousCommit} -- {databaseName}/schemas",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        
        listProcess.Start();
        var fileList = await listProcess.StandardOutput.ReadToEndAsync();
        await listProcess.WaitForExitAsync();
        
        if (listProcess.ExitCode != 0)
        {
            throw new Exception($"Failed to list files at commit {previousCommit}");
        }
        
        // Export each file
        var files = fileList.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file))
                continue;
                
            var relativePath = file.Replace(databaseName + "/schemas/", "");
            var targetPath = Path.Combine(tempPath, "schemas", relativePath);
            var targetDir = Path.GetDirectoryName(targetPath);
            
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
            
            // Get file content at previous commit
            var showProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"show {previousCommit}:{file}",
                    WorkingDirectory = repoPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            showProcess.Start();
            var content = await showProcess.StandardOutput.ReadToEndAsync();
            await showProcess.WaitForExitAsync();
            
            if (showProcess.ExitCode == 0)
            {
                await File.WriteAllTextAsync(targetPath, content);
            }
        }
    }
    
    public async Task<string> GetPreviousCommitHashAsync(string repoPath)
    {
        var gitProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD~1",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        
        gitProcess.Start();
        var output = await gitProcess.StandardOutput.ReadToEndAsync();
        await gitProcess.WaitForExitAsync();
        
        if (gitProcess.ExitCode != 0)
        {
            throw new Exception("Failed to get previous commit hash");
        }
        
        return output.Trim();
    }
    
    public void CleanupTempDirectory(string tempPath)
    {
        try
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to cleanup temp directory {tempPath}: {ex.Message}");
        }
    }
}