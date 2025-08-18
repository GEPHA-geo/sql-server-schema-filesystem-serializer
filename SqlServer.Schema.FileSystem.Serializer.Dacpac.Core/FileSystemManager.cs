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
            
            // Normalize line endings to LF for SQL files to match .gitattributes configuration
            // This prevents false positives when comparing DACPACs
            if (path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                // Convert all line endings to LF only
                // This ensures consistent conversion regardless of source format
                content = content.Replace("\r\n", "\n");  // CRLF -> LF
                content = content.Replace("\r", "\n");    // CR -> LF (for old Mac files)
                // Do NOT convert back to CRLF - keep as LF to match .gitattributes
            }
            
            // Write file with UTF-8 encoding (without BOM to avoid comparison issues)
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing file {path}: {ex.Message}");
        }
    }
}