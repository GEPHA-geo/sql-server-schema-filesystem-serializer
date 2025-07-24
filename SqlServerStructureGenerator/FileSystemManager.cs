using System.Text;

namespace SqlServerStructureGenerator;

// Handles file system operations for creating directories and writing files
public class FileSystemManager
{
    // Helper method to ensure prefixes aren't duplicated
    public static string GetPrefixedFileName(string prefix, string fileName)
    {
        // If the file name already starts with the prefix, don't add it again
        if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return fileName;
        
        return $"{prefix}{fileName}";
    }
    public async Task WriteFileAsync(string filePath, string content)
    {
        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        // Write file with UTF-8 encoding without blocking on console output
        var writeTask = File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
        
        // Log asynchronously to avoid blocking
        _ = Task.Run(() => Console.WriteLine($"  Created: {filePath}"));
        
        await writeTask;
    }
    
    public void CreateDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            Console.WriteLine($"  Created directory: {path}");
        }
    }
    
    public void CleanDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            Console.WriteLine($"  Cleaned directory: {path}");
        }
        Directory.CreateDirectory(path);
    }
}