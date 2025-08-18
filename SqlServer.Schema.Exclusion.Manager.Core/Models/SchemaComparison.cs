using System.Xml.Serialization;

namespace SqlServer.Schema.Exclusion.Manager.Core.Models;

/// <summary>
/// Root element for SCMP (Schema Comparison) XML files used by SQL Server Data Tools
/// </summary>
[XmlRoot("SchemaComparison")]
public class SchemaComparison
{
    /// <summary>
    /// Version of the schema comparison format
    /// </summary>
    [XmlElement("Version")]
    public string Version { get; set; } = "10";

    /// <summary>
    /// Source database model provider configuration
    /// </summary>
    [XmlElement("SourceModelProvider")]
    public ModelProvider? SourceModelProvider { get; set; }

    /// <summary>
    /// Target database model provider configuration
    /// </summary>
    [XmlElement("TargetModelProvider")]
    public ModelProvider? TargetModelProvider { get; set; }

    /// <summary>
    /// Schema comparison settings and options
    /// </summary>
    [XmlElement("SchemaCompareSettingsService")]
    public SchemaCompareSettingsService? SchemaCompareSettingsService { get; set; }

    /// <summary>
    /// List of excluded source elements from comparison
    /// </summary>
    [XmlElement("ExcludedSourceElements")]
    public ExcludedElements? ExcludedSourceElements { get; set; }

    /// <summary>
    /// List of excluded target elements from comparison
    /// </summary>
    [XmlElement("ExcludedTargetElements")]
    public ExcludedElements? ExcludedTargetElements { get; set; }
}

/// <summary>
/// Database model provider containing connection information
/// </summary>
public class ModelProvider
{
    /// <summary>
    /// Connection-based model provider for live database connections
    /// </summary>
    [XmlElement("ConnectionBasedModelProvider")]
    public ConnectionBasedModelProvider? ConnectionBasedModelProvider { get; set; }

    /// <summary>
    /// File-based model provider for DACPAC files
    /// </summary>
    [XmlElement("FileBasedModelProvider")]
    public FileBasedModelProvider? FileBasedModelProvider { get; set; }
}

/// <summary>
/// Provider for live database connections
/// </summary>
public class ConnectionBasedModelProvider
{
    /// <summary>
    /// Database connection string
    /// </summary>
    [XmlElement("ConnectionString")]
    public string ConnectionString { get; set; } = string.Empty;
}

/// <summary>
/// Provider for DACPAC file sources
/// </summary>
public class FileBasedModelProvider
{
    /// <summary>
    /// Name of the provider (usually empty)
    /// </summary>
    [XmlElement("Name", IsNullable = false)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Path to the DACPAC file
    /// </summary>
    [XmlElement("DatabaseFileName", IsNullable = false)]
    public string DatabaseFileName { get; set; } = string.Empty;
}

/// <summary>
/// Container for schema comparison settings
/// </summary>
public class SchemaCompareSettingsService
{
    /// <summary>
    /// Configuration options for the comparison
    /// </summary>
    [XmlElement("ConfigurationOptionsElement")]
    public ConfigurationOptionsElement? ConfigurationOptionsElement { get; set; }
}

/// <summary>
/// Configuration options container
/// </summary>
public class ConfigurationOptionsElement
{
    /// <summary>
    /// List of property settings
    /// </summary>
    [XmlElement("PropertyElementName")]
    public List<PropertyElement> PropertyElements { get; set; } = new();
}

/// <summary>
/// Individual configuration property
/// </summary>
public class PropertyElement
{
    /// <summary>
    /// Name of the configuration property
    /// </summary>
    [XmlElement("Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Value of the configuration property
    /// </summary>
    [XmlElement("Value")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Container for excluded database elements
/// </summary>
public class ExcludedElements
{
    /// <summary>
    /// List of excluded items
    /// </summary>
    [XmlElement("SelectedItem")]
    public List<SelectedItem> SelectedItems { get; set; } = new();
}

/// <summary>
/// Individual excluded database element
/// </summary>
public class SelectedItem
{
    [XmlAttribute("Type")]
    public string Type { get; set; } = string.Empty;
    
    // Changed to handle multiple Name elements that form the object path
    [XmlElement("Name")]
    public List<string> NameParts { get; set; } = new List<string>();
    
    // Computed property to get the full qualified name
    [XmlIgnore]
    public string Name => string.Join(".", NameParts);
}