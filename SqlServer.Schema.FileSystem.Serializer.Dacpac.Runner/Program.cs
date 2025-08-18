using System.CommandLine;
using System.Diagnostics;
using CSharpFunctionalExtensions;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("SQL Server SCMP-based DACPAC Tool - Generates database comparisons and migrations using SCMP files");
        
        // SCMP file is now REQUIRED
        var scmpFileOption = new Option<string>(
            aliases: new[] { "--scmp" },
            description: "Path to SCMP file containing comparison settings and exclusions (REQUIRED)"
        ) { IsRequired = true };
        
        // Required passwords for source and target databases
        var sourcePasswordOption = new Option<string>(
            aliases: new[] { "--source-password" },
            description: "Password for source database connection (REQUIRED)"
        ) { IsRequired = true };
        
        var targetPasswordOption = new Option<string>(
            aliases: new[] { "--target-password" },
            description: "Password for target database connection (REQUIRED)"
        ) { IsRequired = true };
        
        // Output path is required
        var outputPathOption = new Option<string>(
            aliases: new[] { "--output-path" },
            description: "Output directory path for generated files (REQUIRED)"
        ) { IsRequired = true };
        
        // Optional parameters
        var commitMessageOption = new Option<string?>(
            aliases: new[] { "--commit-message" },
            description: "Custom commit message (optional)"
        ) { IsRequired = false };
        
        var skipExclusionManagerOption = new Option<bool>(
            aliases: new[] { "--skip-exclusion-manager" },
            description: "Skip exclusion manager step (optional)"
        ) { IsRequired = false };
        
        // Add options to root command
        rootCommand.AddOption(scmpFileOption);
        rootCommand.AddOption(sourcePasswordOption);
        rootCommand.AddOption(targetPasswordOption);
        rootCommand.AddOption(outputPathOption);
        rootCommand.AddOption(commitMessageOption);
        rootCommand.AddOption(skipExclusionManagerOption);
        
        // Set handler with all required parameters
        rootCommand.SetHandler(async (string scmpFile, string sourcePassword, string targetPassword, string outputPath, string? commitMessage, bool skipExclusionManager) =>
        {
            await RunDacpacExtraction(scmpFile, sourcePassword, targetPassword, outputPath, commitMessage);
        }, scmpFileOption, sourcePasswordOption, targetPasswordOption, outputPathOption, commitMessageOption, skipExclusionManagerOption);
        
        return await rootCommand.InvokeAsync(args);
    }
    
    static async Task RunDacpacExtraction(string scmpFilePath, string sourcePassword, string targetPassword, string outputPath, string? commitMessage = null)
    {
        // Use the new refactored service
        var options = new Models.DacpacExtractionOptions
        {
            ScmpFilePath = scmpFilePath,
            SourcePassword = sourcePassword,
            TargetPassword = targetPassword,
            OutputPath = outputPath,
            CommitMessage = commitMessage,
            KeepTempFiles = Debugger.IsAttached
        };
        
        var service = new Services.DacpacExtractionService();
        var result = await service.ExtractAsync(options);
        
        result.Match(
            onSuccess: extraction => 
            {
                if (extraction.MigrationPath != null)
                    Console.WriteLine($"✓ Migration generated: {extraction.MigrationPath}");
            },
            onFailure: error => 
            {
                Console.WriteLine($"❌ Extraction failed: {error}");
                Environment.Exit(1);
            });
    }
}
