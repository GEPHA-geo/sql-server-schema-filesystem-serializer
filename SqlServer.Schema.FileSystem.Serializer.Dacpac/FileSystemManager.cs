using System.Text;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac;

public class FileSystemManager
{
    public void CreateDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }
    
    public void WriteFile(string path, string content)
    {
        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                CreateDirectory(directory);
            }
            
            // Write file with UTF-8 encoding
            File.WriteAllText(path, content, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing file {path}: {ex.Message}");
        }
    }
}