using SqlServer.Schema.Exclusion.Manager.Core.Services;
using SqlServer.Schema.Exclusion.Manager.Core.Models;
using System.CommandLine;

var rootCommand = new RootCommand("SQL Server Schema Exclusion Manager - SCMP Format Support");

// Primary command - process SCMP file
var scmpFileOption = new Option<string>(
    "--scmp",
    "Path to the SCMP XML file containing source and target database configurations"
) { IsRequired = true };

var outputPathOption = new Option<string>(
    "--output-path",
    "The path where database schema analysis results will be written"
);

var verboseOption = new Option<bool>(
    "--verbose",
    "Show detailed information about the SCMP file contents"
);

rootCommand.AddOption(scmpFileOption);
rootCommand.AddOption(outputPathOption);
rootCommand.AddOption(verboseOption);

rootCommand.SetHandler(async (string scmpPath, string? outputPath, bool verbose) =>
{
    try
    {
        var handler = new ScmpManifestHandler();
        Console.WriteLine($"Loading SCMP file: {scmpPath}");
        var comparison = await handler.LoadManifestAsync(scmpPath);
        
        if (comparison == null)
        {
            Console.WriteLine($"Error: Could not load SCMP file from {scmpPath}");
            Console.WriteLine("Please ensure the file exists and is a valid SCMP XML file.");
            Environment.Exit(1);
        }
        
        // Extract database information
        var (sourceDb, targetDb) = handler.GetDatabaseInfo(comparison);
        var (sourceServer, targetServer) = handler.GetServerInfo(comparison);
        
        Console.WriteLine($"✓ SCMP file loaded successfully");
        Console.WriteLine($"  Source: {sourceServer}/{sourceDb}");
        Console.WriteLine($"  Target: {targetServer}/{targetDb}");
        
        if (verbose)
        {
            Console.WriteLine("\nSCMP Details:");
            Console.WriteLine($"  Version: {comparison.Version}");
            
            // Show configuration options
            var options = comparison.SchemaCompareSettingsService?.ConfigurationOptionsElement?.PropertyElements;
            if (options != null && options.Any())
            {
                Console.WriteLine($"\n  Configuration Options ({options.Count}):");
                foreach (var option in options.Take(10)) // Show first 10 options
                {
                    Console.WriteLine($"    - {option.Name}: {option.Value}");
                }
                if (options.Count > 10)
                    Console.WriteLine($"    ... and {options.Count - 10} more options");
            }
            
            // Show excluded objects
            var excluded = handler.GetExcludedObjects(comparison);
            if (excluded.Any())
            {
                Console.WriteLine($"\n  Excluded Objects ({excluded.Count}):");
                foreach (var obj in excluded.Take(10)) // Show first 10 exclusions
                {
                    Console.WriteLine($"    - {obj}");
                }
                if (excluded.Count > 10)
                    Console.WriteLine($"    ... and {excluded.Count - 10} more exclusions");
            }
        }
        
        // Map to DacDeployOptions for use with SqlPackage
        var mapper = new ScmpToDeployOptions();
        var deployOptions = mapper.MapOptions(comparison);
        
        Console.WriteLine("\n✓ Deployment options mapped successfully");
        Console.WriteLine($"  Block on data loss: {deployOptions.BlockOnPossibleDataLoss}");
        Console.WriteLine($"  Drop objects not in source: {deployOptions.DropObjectsNotInSource}");
        Console.WriteLine($"  Ignore permissions: {deployOptions.IgnorePermissions}");
        
        // If output path is specified, could save analysis results there
        if (!string.IsNullOrEmpty(outputPath))
        {
            var analysisFile = Path.Combine(outputPath, "scmp_analysis.txt");
            var content = $"SCMP Analysis Report\n" +
                         $"====================\n" +
                         $"Source: {sourceServer}/{sourceDb}\n" +
                         $"Target: {targetServer}/{targetDb}\n" +
                         $"Version: {comparison.Version}\n" +
                         $"Excluded Objects: {handler.GetExcludedObjects(comparison).Count}\n" +
                         $"Configuration Options: {comparison.SchemaCompareSettingsService?.ConfigurationOptionsElement?.PropertyElements?.Count ?? 0}\n";
            
            await File.WriteAllTextAsync(analysisFile, content);
            Console.WriteLine($"\n✓ Analysis report written to: {analysisFile}");
        }
        
        Console.WriteLine("\n✓ SCMP processing completed successfully");
        Environment.Exit(0);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n✗ Error: {ex.Message}");
        if (ex.InnerException != null)
            Console.WriteLine($"  Details: {ex.InnerException.Message}");
        Environment.Exit(1);
    }
}, scmpFileOption, outputPathOption, verboseOption);

// Add subcommand for creating a new SCMP file
var createCommand = new Command("create", "Create a new SCMP file with basic configuration");
var sourceConnOption = new Option<string>("--source", "Source database connection string") { IsRequired = true };
var targetConnOption = new Option<string>("--target", "Target database connection string") { IsRequired = true };
var outputOption = new Option<string>("--output", "Output SCMP file path") { IsRequired = true };

createCommand.AddOption(sourceConnOption);
createCommand.AddOption(targetConnOption);
createCommand.AddOption(outputOption);

createCommand.SetHandler(async (string sourceConn, string targetConn, string outputPath) =>
{
    try
    {
        var handler = new ScmpManifestHandler();
        var mapper = new ScmpToDeployOptions();
        
        // Create comparison with default conservative options
        var comparison = new SchemaComparison
        {
            Version = "10",
            SourceModelProvider = new ModelProvider
            {
                ConnectionBasedModelProvider = new ConnectionBasedModelProvider
                {
                    ConnectionString = sourceConn
                }
            },
            TargetModelProvider = new ModelProvider
            {
                ConnectionBasedModelProvider = new ConnectionBasedModelProvider
                {
                    ConnectionString = targetConn
                }
            },
            SchemaCompareSettingsService = new SchemaCompareSettingsService
            {
                ConfigurationOptionsElement = new ConfigurationOptionsElement
                {
                    PropertyElements = new List<PropertyElement>
                    {
                        // Conservative defaults for safety
                        new() { Name = "BlockOnPossibleDataLoss", Value = "True" },
                        new() { Name = "DropObjectsNotInSource", Value = "False" },
                        new() { Name = "DropPermissionsNotInSource", Value = "False" },
                        new() { Name = "DropRoleMembersNotInSource", Value = "False" },
                        new() { Name = "DropExtendedPropertiesNotInSource", Value = "False" },
                        
                        // Common ignore settings
                        new() { Name = "IgnorePermissions", Value = "True" },
                        new() { Name = "IgnoreRoleMembership", Value = "True" },
                        new() { Name = "IgnoreUserSettingsObjects", Value = "True" },
                        new() { Name = "IgnoreLoginSids", Value = "True" },
                        new() { Name = "IgnoreFileAndLogFilePath", Value = "True" },
                        new() { Name = "IgnoreFilegroupPlacement", Value = "True" },
                        new() { Name = "IgnoreFullTextCatalogFilePath", Value = "True" },
                        new() { Name = "IgnoreWhitespace", Value = "True" },
                        new() { Name = "IgnoreKeywordCasing", Value = "True" },
                        new() { Name = "IgnoreSemicolonBetweenStatements", Value = "True" },
                        
                        // Script generation
                        new() { Name = "IncludeCompositeObjects", Value = "True" },
                        new() { Name = "IncludeTransactionalScripts", Value = "True" },
                        new() { Name = "GenerateSmartDefaults", Value = "True" },
                        new() { Name = "VerifyDeployment", Value = "True" }
                    }
                }
            }
        };
        
        await handler.SaveManifestAsync(comparison, outputPath);
        Console.WriteLine($"✓ SCMP file created successfully: {outputPath}");
        Console.WriteLine($"  You can now use this file with: --scmp {outputPath}");
        Console.WriteLine($"  Or open it in Visual Studio SSDT for further configuration");
        Environment.Exit(0);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Error creating SCMP file: {ex.Message}");
        Environment.Exit(1);
    }
}, sourceConnOption, targetConnOption, outputOption);

rootCommand.AddCommand(createCommand);

// Add info command to display SCMP file details
var infoCommand = new Command("info", "Display detailed information about an SCMP file");
infoCommand.AddOption(new Option<string>("--file", "Path to SCMP file") { IsRequired = true });

infoCommand.SetHandler(async (string filePath) =>
{
    try
    {
        var handler = new ScmpManifestHandler();
        var comparison = await handler.LoadManifestAsync(filePath);
        
        if (comparison == null)
        {
            Console.WriteLine($"Error: Could not load SCMP file from {filePath}");
            Environment.Exit(1);
        }
        
        var (sourceDb, targetDb) = handler.GetDatabaseInfo(comparison);
        var (sourceServer, targetServer) = handler.GetServerInfo(comparison);
        
        Console.WriteLine($"SCMP File Information");
        Console.WriteLine($"=====================");
        Console.WriteLine($"File: {filePath}");
        Console.WriteLine($"Version: {comparison.Version}");
        Console.WriteLine();
        Console.WriteLine($"Source Database:");
        Console.WriteLine($"  Server: {sourceServer ?? "N/A"}");
        Console.WriteLine($"  Database: {sourceDb ?? "N/A"}");
        Console.WriteLine();
        Console.WriteLine($"Target Database:");
        Console.WriteLine($"  Server: {targetServer ?? "N/A"}");
        Console.WriteLine($"  Database: {targetDb ?? "N/A"}");
        
        var options = comparison.SchemaCompareSettingsService?.ConfigurationOptionsElement?.PropertyElements;
        if (options != null && options.Any())
        {
            Console.WriteLine();
            Console.WriteLine($"Configuration Options ({options.Count} total):");
            foreach (var option in options)
            {
                Console.WriteLine($"  {option.Name}: {option.Value}");
            }
        }
        
        var excluded = handler.GetExcludedObjects(comparison);
        if (excluded.Any())
        {
            Console.WriteLine();
            Console.WriteLine($"Excluded Objects ({excluded.Count} total):");
            foreach (var obj in excluded)
            {
                Console.WriteLine($"  {obj}");
            }
        }
        
        Environment.Exit(0);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        Environment.Exit(1);
    }
}, new Option<string>("--file"));

rootCommand.AddCommand(infoCommand);

return await rootCommand.InvokeAsync(args);