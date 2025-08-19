using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlServer.Schema.Migration.Runner.Core;

public class MigrationFile
{
    public string FilePath { get; }
    public string FileName { get; }
    public string MigrationId { get; }
    public string Content { get; }
    public string Checksum { get; }

    public MigrationFile(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        Content = File.ReadAllText(filePath);
        Checksum = CalculateChecksum(Content);
        MigrationId = ExtractMigrationId(Content) ?? Path.GetFileNameWithoutExtension(FileName);
    }

    static string? ExtractMigrationId(string content)
    {
        var match = Regex.Match(content, @"--\s*MigrationId:\s*(.+)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    static string CalculateChecksum(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}