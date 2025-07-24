using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.Data.SqlClient;
using System.Text;

namespace SqlServerStructureGenerator;

// Main orchestrator for database structure generation
public class DatabaseScriptGenerator(string connectionString, string outputPath)
{
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
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        
        if (string.IsNullOrEmpty(databaseName))
            throw new ArgumentException("Database name not found in connection string");
            
        Console.WriteLine($"Starting structure generation for database: {databaseName}");
        var startTime = DateTime.Now;
        
        // Create base output directory
        var databasePath = Path.Combine(outputPath, databaseName);
        Directory.CreateDirectory(databasePath);
        
        // Process all schemas
        var schemaProcessor = new SchemaProcessor(connectionString, databasePath, _scriptingOptions);
        await schemaProcessor.ProcessAllSchemasAsync();
        
        var endTime = DateTime.Now;
        var duration = endTime - startTime;
        Console.WriteLine($"Structure generation completed in {duration.TotalSeconds:F1} seconds");
        Console.WriteLine($"Output: {databasePath}");
    }
}