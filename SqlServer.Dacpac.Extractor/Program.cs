using System.CommandLine;
using Microsoft.SqlServer.Dac;

namespace SqlServer.Dacpac.Extractor;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("SQL Server DACPAC Extractor - Extracts database schema to DACPAC file");
        
        var connectionStringOption = new Option<string>(
            "--connection-string",
            "SQL Server connection string"
        ) { IsRequired = true };
        
        var outputPathOption = new Option<string>(
            "--output",
            () => "database.dacpac",
            "Output DACPAC file path"
        );
        
        var databaseNameOption = new Option<string?>(
            "--database-name",
            "Override database name in DACPAC (defaults to database from connection string)"
        );
        
        var extractOptionsOption = new Option<bool>(
            "--include-data",
            () => false,
            "Include data in DACPAC (default: schema only)"
        );
        
        rootCommand.AddOption(connectionStringOption);
        rootCommand.AddOption(outputPathOption);
        rootCommand.AddOption(databaseNameOption);
        rootCommand.AddOption(extractOptionsOption);
        
        rootCommand.SetHandler((string connectionString, string outputPath, string? databaseName, bool includeData) =>
        {
            try
            {
                ExtractDacpac(connectionString, outputPath, databaseName, includeData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                Environment.Exit(1);
            }
        }, connectionStringOption, outputPathOption, databaseNameOption, extractOptionsOption);
        
        // If no arguments provided, use the default connection string
        if (args.Length == 0)
        {
            Console.WriteLine("No arguments provided, using default connection string...");
            var defaultConnectionString = "Server=pharm-n1.pharm.local;Database=abc;User Id=sa_abc_ro;Password=v4dHkT1#tOH%Y4zA;TrustServerCertificate=true";
            var defaultOutput = "abc_reference.dacpac";
            
            try
            {
                ExtractDacpac(defaultConnectionString, defaultOutput, "abc", false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                return 1;
            }
            return 0;
        }
        
        return await rootCommand.InvokeAsync(args);
    }
    
    static void ExtractDacpac(string connectionString, string outputPath, string? databaseName, bool includeData)
    {
        Console.WriteLine("=== SQL Server DACPAC Extractor ===");
        Console.WriteLine($"Connection String: {MaskPassword(connectionString)}");
        Console.WriteLine($"Output Path: {outputPath}");
        
        // Parse database name from connection string if not provided
        if (string.IsNullOrEmpty(databaseName))
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            databaseName = builder.InitialCatalog;
            if (string.IsNullOrEmpty(databaseName))
            {
                throw new ArgumentException("Database name not found in connection string and not provided as parameter");
            }
        }
        
        Console.WriteLine($"Database Name: {databaseName}");
        Console.WriteLine($"Include Data: {includeData}");
        Console.WriteLine();
        
        // Create DacServices instance
        Console.WriteLine("Connecting to database...");
        var dacServices = new DacServices(connectionString);
        
        // Subscribe to events for progress reporting
        dacServices.Message += (sender, e) =>
        {
            Console.WriteLine($"[{e.Message.MessageType}] {e.Message.Message}");
        };
        
        dacServices.ProgressChanged += (sender, e) =>
        {
            Console.WriteLine($"Progress: {e.Status} - {e.Message}");
        };
        
        // Set up extraction options
        var extractOptions = new DacExtractOptions
        {
            ExtractAllTableData = includeData,
            ExtractApplicationScopedObjectsOnly = false,
            ExtractReferencedServerScopedElements = false,
            VerifyExtraction = false,
            IgnorePermissions = true,
            IgnoreUserLoginMappings = true,
            IgnoreExtendedProperties = false,
            ExtractUsageProperties = false,
            Storage = includeData ? DacSchemaModelStorageType.Memory : DacSchemaModelStorageType.File
        };
        
        // Extract the DACPAC
        Console.WriteLine("Starting extraction...");
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }
            
            // Extract to DACPAC file
            dacServices.Extract(outputPath, databaseName, "SQL Server DACPAC Extractor", new Version(1, 0, 0, 0));
            
            var elapsed = DateTime.UtcNow - startTime;
            var fileInfo = new FileInfo(outputPath);
            
            Console.WriteLine();
            Console.WriteLine("=== Extraction Complete ===");
            Console.WriteLine($"Output File: {outputPath}");
            Console.WriteLine($"File Size: {fileInfo.Length / 1024.0 / 1024.0:F2} MB");
            Console.WriteLine($"Time Taken: {elapsed.TotalSeconds:F2} seconds");
            Console.WriteLine();
            Console.WriteLine("✅ DACPAC extracted successfully!");
        }
        catch (DacServicesException ex)
        {
            Console.WriteLine();
            Console.WriteLine("❌ Extraction failed!");
            Console.WriteLine($"Error: {ex.Message}");
            
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
            }
            
            // Check for common issues
            if (ex.Message.Contains("Login failed", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine();
                Console.WriteLine("💡 Tip: Check your connection string credentials and server accessibility");
            }
            else if (ex.Message.Contains("network-related", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine();
                Console.WriteLine("💡 Tip: Ensure the SQL Server is accessible from this machine");
            }
            
            throw;
        }
    }
    
    static string MaskPassword(string connectionString)
    {
        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            if (!string.IsNullOrEmpty(builder.Password))
            {
                builder.Password = "****";
            }
            return builder.ToString();
        }
        catch
        {
            // If parsing fails, just mask anything that looks like a password
            return System.Text.RegularExpressions.Regex.Replace(
                connectionString,
                @"(Password|Pwd|Pass)\s*=\s*[^;]+",
                "$1=****",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }
}