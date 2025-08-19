using System.Text;
using System.Xml.Serialization;
using SqlServer.Schema.Exclusion.Manager.Core.Models;

namespace SqlServer.Schema.Exclusion.Manager.Core.Services;

/// <summary>
/// Handles reading and writing SCMP (Schema Comparison) XML files
/// </summary>
public class ScmpManifestHandler
{
    readonly XmlSerializer _serializer = new(typeof(SchemaComparison));

    /// <summary>
    /// Loads an SCMP file from the specified path
    /// </summary>
    /// <param name="filePath">Path to the SCMP XML file</param>
    /// <returns>Deserialized SchemaComparison object, or null if file doesn't exist</returns>
    public async Task<SchemaComparison?> LoadManifestAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        using var stringReader = new StringReader(content);
        return (SchemaComparison?)_serializer.Deserialize(stringReader);
    }

    /// <summary>
    /// Saves a SchemaComparison object to an SCMP XML file
    /// </summary>
    /// <param name="comparison">The SchemaComparison to save</param>
    /// <param name="filePath">Path where the SCMP file should be saved</param>
    public async Task SaveManifestAsync(SchemaComparison comparison, string filePath)
    {
        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        // Use UTF-8 encoding with BOM for compatibility with Microsoft.SqlServer.Dac.Compare
        var encoding = new UTF8Encoding(true); // true = include BOM

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(stream, encoding);

        // Write XML declaration explicitly
        await writer.WriteLineAsync("<?xml version=\"1.0\" encoding=\"utf-8\"?>");

        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add(string.Empty, string.Empty);

        using var stringWriter = new StringWriter();
        _serializer.Serialize(stringWriter, comparison, namespaces);

        // Get the XML content and skip the declaration line if present
        var xmlContent = stringWriter.ToString();
        var lines = xmlContent.Split('\n');
        var startIndex = lines[0].StartsWith("<?xml") ? 1 : 0;

        for (var i = startIndex; i < lines.Length; i++)
        {
            await writer.WriteLineAsync(lines[i].TrimEnd('\r'));
        }
    }

    /// <summary>
    /// Gets the standard SCMP file name for a given server and database
    /// </summary>
    /// <param name="server">Server name (may include port)</param>
    /// <param name="database">Database name</param>
    /// <returns>Standard SCMP file name</returns>
    public string GetManifestFileName(string server, string database)
    {
        // Sanitize server name - replace port separator and other special characters
        var sanitizedServer = server
            .Replace(",", "_")  // SQL Server port separator
            .Replace(":", "_")  // Alternative port separator
            .Replace("\\", "_") // Instance name separator
            .Replace(".", "_"); // Domain separator

        return $"{sanitizedServer}_{database}.scmp.xml";
    }

    /// <summary>
    /// Extracts database information from the SchemaComparison
    /// </summary>
    /// <param name="comparison">The SchemaComparison object</param>
    /// <returns>Tuple of (sourceDatabase, targetDatabase) names</returns>
    public (string? sourceDatabase, string? targetDatabase) GetDatabaseInfo(SchemaComparison comparison)
    {
        var sourceDb = ExtractDatabaseName(comparison.SourceModelProvider);
        var targetDb = ExtractDatabaseName(comparison.TargetModelProvider);
        return (sourceDb, targetDb);
    }

    /// <summary>
    /// Extracts server information from the SchemaComparison
    /// </summary>
    /// <param name="comparison">The SchemaComparison object</param>
    /// <returns>Tuple of (sourceServer, targetServer) names</returns>
    public (string? sourceServer, string? targetServer) GetServerInfo(SchemaComparison comparison)
    {
        var sourceServer = ExtractServerName(comparison.SourceModelProvider);
        var targetServer = ExtractServerName(comparison.TargetModelProvider);
        return (sourceServer, targetServer);
    }

    /// <summary>
    /// Gets all excluded objects from the comparison
    /// </summary>
    /// <param name="comparison">The SchemaComparison object</param>
    /// <returns>List of excluded object names</returns>
    public List<string> GetExcludedObjects(SchemaComparison comparison)
    {
        var excludedObjects = new List<string>();

        if (comparison.ExcludedSourceElements?.SelectedItems != null)
        {
            excludedObjects.AddRange(comparison.ExcludedSourceElements.SelectedItems
                .Select(item => $"{item.Name} (Source)"));
        }

        if (comparison.ExcludedTargetElements?.SelectedItems != null)
        {
            excludedObjects.AddRange(comparison.ExcludedTargetElements.SelectedItems
                .Select(item => $"{item.Name} (Target)"));
        }

        return excludedObjects;
    }

    /// <summary>
    /// Gets a specific configuration option value
    /// </summary>
    /// <param name="comparison">The SchemaComparison object</param>
    /// <param name="optionName">Name of the option to retrieve</param>
    /// <returns>Option value or null if not found</returns>
    public string? GetConfigurationOption(SchemaComparison comparison, string optionName)
    {
        var options = comparison.SchemaCompareSettingsService?.ConfigurationOptionsElement?.PropertyElements;
        if (options == null)
            return null;

        var option = options.FirstOrDefault(p => p.Name == optionName);
        return option?.Value;
    }

    /// <summary>
    /// Sets a configuration option value
    /// </summary>
    /// <param name="comparison">The SchemaComparison object</param>
    /// <param name="optionName">Name of the option to set</param>
    /// <param name="value">Value to set</param>
    public void SetConfigurationOption(SchemaComparison comparison, string optionName, string value)
    {
        // Ensure the structure exists
        comparison.SchemaCompareSettingsService ??= new SchemaCompareSettingsService();
        comparison.SchemaCompareSettingsService.ConfigurationOptionsElement ??= new ConfigurationOptionsElement();

        var options = comparison.SchemaCompareSettingsService.ConfigurationOptionsElement.PropertyElements;

        // Find existing option or add new one
        var existingOption = options.FirstOrDefault(p => p.Name == optionName);
        if (existingOption != null)
            existingOption.Value = value;
        else
            options.Add(new PropertyElement { Name = optionName, Value = value });
    }

    string? ExtractDatabaseName(ModelProvider? provider)
    {
        if (provider?.ConnectionBasedModelProvider != null)
        {
            var connectionString = provider.ConnectionBasedModelProvider.ConnectionString;
            // Parse Initial Catalog or Database from connection string
            var patterns = new[] { "Initial Catalog=", "Database=" };
            foreach (var pattern in patterns)
            {
                var index = connectionString.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    var start = index + pattern.Length;
                    var end = connectionString.IndexOf(';', start);
                    if (end < 0) end = connectionString.Length;
                    return connectionString.Substring(start, end - start).Trim();
                }
            }
        }
        else if (provider?.FileBasedModelProvider != null)
        {
            // Extract database name from DACPAC file path
            var filePath = provider.FileBasedModelProvider.DatabaseFileName;
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            return fileName;
        }

        return null;
    }

    string? ExtractServerName(ModelProvider? provider)
    {
        if (provider?.ConnectionBasedModelProvider != null)
        {
            var connectionString = provider.ConnectionBasedModelProvider.ConnectionString;
            // Parse Data Source or Server from connection string
            var patterns = new[] { "Data Source=", "Server=" };
            foreach (var pattern in patterns)
            {
                var index = connectionString.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    var start = index + pattern.Length;
                    var end = connectionString.IndexOf(';', start);
                    if (end < 0) end = connectionString.Length;
                    return connectionString.Substring(start, end - start).Trim();
                }
            }
        }

        return null;
    }
}