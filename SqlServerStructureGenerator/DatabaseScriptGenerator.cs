using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.Data.SqlClient;
using System.Text;

namespace SqlServerStructureGenerator;

// Main orchestrator for database structure generation
public class DatabaseScriptGenerator
{
    readonly string _sourceConnectionString;
    readonly string _targetConnectionString;
    readonly string _outputPath;
    readonly string _targetServer;
    readonly string _targetDatabase;

    public DatabaseScriptGenerator(string sourceConnectionString, string targetConnectionString, string outputPath)
    {
        _sourceConnectionString = sourceConnectionString;
        _targetConnectionString = targetConnectionString;
        _outputPath = outputPath;
        
        // Extract target server and database from target connection string
        var targetBuilder = new SqlConnectionStringBuilder(targetConnectionString);
        _targetServer = targetBuilder.DataSource.Replace('\\', '-').Replace(':', '-'); // Sanitize for folder names
        _targetDatabase = targetBuilder.InitialCatalog;
    }
    
    readonly ScriptingOptions _scriptingOptions = new()
    {
        ScriptDrops = false,
        ScriptData = false,
        ScriptSchema = true,
        IncludeHeaders = false, // Exclude headers to avoid Script Date changes
        IncludeIfNotExists = true,
        WithDependencies = false, // We'll handle dependencies manually
        Indexes = false, // Script indexes separately
        DriAll = false, // Script constraints separately
        Triggers = false, // Script triggers separately
        ScriptBatchTerminator = true,
        NoCommandTerminator = false,
        AllowSystemObjects = false,
        // Performance optimization - don't gather extended properties
        ExtendedProperties = false,
        // Don't script permissions to avoid additional queries
        Permissions = false
    };

    // Configure scripting options for complete DDL generation
    // Exclude headers to avoid Script Date changes
    // We'll handle dependencies manually
    // Script indexes separately
    // Script constraints separately
    // Script triggers separately

    public async Task GenerateStructureAsync()
    {
        var sourceBuilder = new SqlConnectionStringBuilder(_sourceConnectionString);
        var sourceDatabaseName = sourceBuilder.InitialCatalog;
        
        if (string.IsNullOrEmpty(sourceDatabaseName))
            throw new ArgumentException("Database name not found in source connection string");
            
        Console.WriteLine($"Starting structure generation from: {sourceDatabaseName} to: {_targetServer}/{_targetDatabase}");
        var startTime = DateTime.Now;
        
        // Create base output directory with new hierarchical structure
        var databasePath = Path.Combine(_outputPath, "servers", _targetServer, _targetDatabase);
        Directory.CreateDirectory(databasePath);
        
        // Process all schemas
        var schemaProcessor = new SchemaProcessor(_sourceConnectionString, databasePath, _scriptingOptions);
        await schemaProcessor.ProcessAllSchemasAsync();
        
        var endTime = DateTime.Now;
        var duration = endTime - startTime;
        Console.WriteLine($"Structure generation completed in {duration.TotalSeconds:F1} seconds");
        Console.WriteLine($"Output: {databasePath}");
    }
}