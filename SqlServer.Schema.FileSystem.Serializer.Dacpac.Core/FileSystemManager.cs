using System.Text;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Core;

public class FileSystemManager
{
    public static void CreateDirectory(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
    }
    
    public void WriteFile(string path, string content)
    {
        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) CreateDirectory(directory);
            
            // Normalize line endings to CRLF for SQL files to ensure consistency
            // This helps reduce false positives when comparing DACPACs
            if (path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                // First convert all line endings to LF, then to CRLF
                // This ensures consistent conversion regardless of source format
                content = content.Replace("\r\n", "\n");  // CRLF -> LF
                content = content.Replace("\r", "\n");    // CR -> LF (for old Mac files)
                content = content.Replace("\n", "\r\n");  // LF -> CRLF
            }
            
            // Write file with UTF-8 encoding
            File.WriteAllText(path, content, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing file {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Normalizes line endings for all SQL files in a directory to CRLF
    /// </summary>
    public void NormalizeDirectoryLineEndings(string directory)
    {
        return;
        
        if (!Directory.Exists(directory))
            return;
            
        Console.WriteLine($"  Normalizing line endings in {directory}...");
        var normalizedCount = 0;
        
        foreach (var file in Directory.GetFiles(directory, "*.sql", SearchOption.AllDirectories))
        {
            try
            {
                var content = File.ReadAllText(file);
                
                // Normalize line endings to CRLF
                content = content.Replace("\r\n", "\n");  // CRLF -> LF
                content = content.Replace("\r", "\n");    // CR -> LF
                //content = content.Replace("\n", "\r\n");  // LF -> CRLF
                
                File.WriteAllText(file, content, Encoding.UTF8);
                normalizedCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    WARNING: Failed to normalize {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        
        Console.WriteLine($"  Normalized line endings in {normalizedCount} SQL files");
    }
}