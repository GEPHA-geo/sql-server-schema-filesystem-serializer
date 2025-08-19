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

    /// <summary>
    /// Reads a file and removes BOM if present, normalizing line endings
    /// </summary>
    public string ReadFileNormalized(string path)
    {
        if (!File.Exists(path))
            return string.Empty;

        // Read file as bytes to detect and remove BOM
        var bytes = File.ReadAllBytes(path);
        var content = string.Empty;

        // Check for UTF-8 BOM (EF BB BF)
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            // Skip BOM and read rest as UTF-8
            content = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        // Check for UTF-16 LE BOM (FF FE)
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            // Skip BOM and read as UTF-16 LE
            content = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        // Check for UTF-16 BE BOM (FE FF)
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            // Skip BOM and read as UTF-16 BE
            content = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        else
        {
            // No BOM, read as UTF-8
            content = Encoding.UTF8.GetString(bytes);
        }

        // Normalize line endings to LF for SQL files
        if (path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            content = content.Replace("\r\n", "\n");  // CRLF -> LF
            content = content.Replace("\r", "\n");    // CR -> LF
        }

        return content;
    }

    /// <summary>
    /// Normalizes an existing file by removing BOM and fixing line endings
    /// </summary>
    public void NormalizeFile(string path)
    {
        if (!File.Exists(path))
            return;

        var content = ReadFileNormalized(path);
        WriteFile(path, content);
    }
}