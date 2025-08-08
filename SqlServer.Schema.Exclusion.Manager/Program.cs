using SqlServer.Schema.Exclusion.Manager.Core.Services;
using System.CommandLine;

var rootCommand = new RootCommand("SQL Server Schema Exclusion Manager - Manages change manifests and exclusion comments");

var outputPathOption = new Option<string>(
    "--output-path",
    "The path where database schema was serialized"
) { IsRequired = true };

var sourceServerOption = new Option<string>(
    "--source-server",
    "The source server name (from DACPAC extraction)"
) { IsRequired = true };

var sourceDatabaseOption = new Option<string>(
    "--source-database", 
    "The source database name (from DACPAC extraction)"
) { IsRequired = true };

var targetServerOption = new Option<string>(
    "--target-server",
    "The target server name (where files are serialized)"
) { IsRequired = true };

var targetDatabaseOption = new Option<string>(
    "--target-database",
    "The target database name (where files are serialized)"
) { IsRequired = true };

var updateCommentsOption = new Option<bool>(
    "--update-exclusion-comments",
    "Only update exclusion comments based on existing manifest (used by GitHub workflow)"
);

rootCommand.AddOption(outputPathOption);
rootCommand.AddOption(sourceServerOption);
rootCommand.AddOption(sourceDatabaseOption);
rootCommand.AddOption(targetServerOption);
rootCommand.AddOption(targetDatabaseOption);
rootCommand.AddOption(updateCommentsOption);

rootCommand.SetHandler(async (string outputPath, string sourceServer, string sourceDatabase, string targetServer, string targetDatabase, bool updateComments) =>
{
    try
    {
        var manager = new ManifestManager(outputPath);
        
        if (updateComments)
        {
            // GitHub workflow mode - only update comments based on manifest
            Console.WriteLine($"Updating exclusion comments for source {sourceServer}/{sourceDatabase} in target {targetServer}/{targetDatabase}...");
            var success = await manager.UpdateExclusionCommentsAsync(sourceServer, sourceDatabase, targetServer, targetDatabase);
            Environment.Exit(success ? 0 : 1);
        }
        else
        {
            // Normal mode - create/update manifest and comments
            Console.WriteLine($"Managing manifest for source {sourceServer}/{sourceDatabase} in target {targetServer}/{targetDatabase}...");
            var success = await manager.CreateOrUpdateManifestAsync(sourceServer, sourceDatabase, targetServer, targetDatabase);
            Environment.Exit(success ? 0 : 1);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        Environment.Exit(1);
    }
}, outputPathOption, sourceServerOption, sourceDatabaseOption, targetServerOption, targetDatabaseOption, updateCommentsOption);

return await rootCommand.InvokeAsync(args);