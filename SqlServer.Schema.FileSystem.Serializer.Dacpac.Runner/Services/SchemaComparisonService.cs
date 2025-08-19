using CSharpFunctionalExtensions;
using Microsoft.SqlServer.Dac.Compare;
using SqlServer.Schema.Exclusion.Manager.Core.Models;
using SqlServer.Schema.Exclusion.Manager.Core.Services;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Models;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner.Services;

/// <summary>
/// Handles schema comparison using SCMP files and Microsoft's official API
/// </summary>
public class SchemaComparisonService
{
    readonly ScmpManifestHandler _scmpHandler = new();
    
    /// <summary>
    /// Performs schema comparison using the SCMP file with updated DACPAC endpoints
    /// </summary>
    public async Task<Result<SchemaComparisonResult>> CompareWithScmpFile(
        DacpacExtractionContext context)
    {
        Console.WriteLine("\n=== Phase 5: Running Schema Comparison ===");
        
        // Create a temporary SCMP file with filesystem DACPAC endpoints
        var tempScmpPath = await CreateTempScmpWithDacpacEndpoints(
            context,
            context.DacpacPaths.SourceFilesystemDacpac,
            context.DacpacPaths.TargetFilesystemDacpac);
        
        try
        {
            // Use the SchemaComparison constructor that accepts an SCMP file path
            // This ensures all settings from the SCMP file are properly applied
            Console.WriteLine("Loading schema comparison from SCMP file...");
            var comparison = new Microsoft.SqlServer.Dac.Compare.SchemaComparison(tempScmpPath);
            
            Console.WriteLine("Comparing schemas...");
            var comparisonResult = comparison.Compare();
            
            // Log comparison statistics
            var totalDifferences = comparisonResult.Differences.Count();
            var includedDifferences = comparisonResult.Differences.Count(d => d.Included);
            var excludedDifferences = comparisonResult.Differences.Count(d => !d.Included);
            
            Console.WriteLine($"Found {totalDifferences} total differences");
            Console.WriteLine($"Included differences: {includedDifferences}");
            Console.WriteLine($"Excluded differences: {excludedDifferences}");
            
            // List excluded objects if any
            if (excludedDifferences > 0)
            {
                Console.WriteLine("\nExcluded objects:");
                foreach (var diff in comparisonResult.Differences.Where(d => !d.Included))
                {
                    Console.WriteLine($"  - {diff.Name} ({diff.UpdateAction})");
                }
            }
            
            return Result.Success(new SchemaComparisonResult
            {
                ComparisonResult = comparisonResult,
                TotalDifferences = totalDifferences,
                IncludedDifferences = includedDifferences,
                ExcludedDifferences = excludedDifferences
            });
        }
        catch (Exception ex)
        {
            return Result.Failure<SchemaComparisonResult>($"Schema comparison failed: {ex.Message}");
        }
        finally
        {
            // Clean up temporary SCMP file
            if (File.Exists(tempScmpPath))
            {
                try
                {
                    File.Delete(tempScmpPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
    
    /// <summary>
    /// Creates a temporary SCMP file with updated DACPAC endpoints and saves a permanent copy
    /// </summary>
    async Task<string> CreateTempScmpWithDacpacEndpoints(
        DacpacExtractionContext context,
        string sourceDacpacPath,
        string targetDacpacPath)
    {
        var originalScmp = context.ScmpModel;
        // Clone the original SCMP model
        var updatedScmp = new Exclusion.Manager.Core.Models.SchemaComparison
        {
            Version = originalScmp.Version,
            SchemaCompareSettingsService = originalScmp.SchemaCompareSettingsService,
            ExcludedSourceElements = originalScmp.ExcludedSourceElements,
            ExcludedTargetElements = originalScmp.ExcludedTargetElements,
            // Update source endpoint to use filesystem DACPAC
            SourceModelProvider = new ModelProvider
            {
                FileBasedModelProvider = new FileBasedModelProvider
                {
                    Name = string.Empty,
                    DatabaseFileName = sourceDacpacPath
                }
            },
            // Update target endpoint to use filesystem DACPAC
            TargetModelProvider = new ModelProvider
            {
                FileBasedModelProvider = new FileBasedModelProvider
                {
                    Name = string.Empty,
                    DatabaseFileName = targetDacpacPath
                }
            }
        };

        // Save to temporary location
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"temp_comparison_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.scmp");
        
        await _scmpHandler.SaveManifestAsync(updatedScmp, tempPath);
        
        // Save the original SCMP file first (with connection strings)
        if (context.FilePaths != null)
        {
            await _scmpHandler.SaveManifestAsync(originalScmp, context.FilePaths.OriginalScmpPath);
            Console.WriteLine($"Saved original SCMP file: {Path.GetFileName(context.FilePaths.OriginalScmpPath)}");
        }
        
        // Also save a permanent copy with relative paths in the source subdirectory
        var permanentScmpPath = context.FilePaths?.DacpacsScmpPath ?? 
            Path.Combine(context.ScmpOutputPath, 
                $"{context.SourceConnection.SanitizedServer}_{context.SourceConnection.SanitizedDatabase}",
                $"{context.SourceConnection.SanitizedServer}_{context.SourceConnection.SanitizedDatabase}_dacpacs{Constants.DacpacConstants.Files.ScmpExtension}");
        
        // Create a version with relative paths for the permanent file
        var permanentScmp = new Exclusion.Manager.Core.Models.SchemaComparison
        {
            Version = originalScmp.Version,
            SchemaCompareSettingsService = originalScmp.SchemaCompareSettingsService,
            ExcludedSourceElements = originalScmp.ExcludedSourceElements,
            ExcludedTargetElements = originalScmp.ExcludedTargetElements,
            // Use relative paths (just filenames since SCMP is in same directory as DACPACs)
            SourceModelProvider = new ModelProvider
            {
                FileBasedModelProvider = new FileBasedModelProvider
                {
                    Name = string.Empty,
                    DatabaseFileName = Path.GetFileName(sourceDacpacPath)
                }
            },
            TargetModelProvider = new ModelProvider
            {
                FileBasedModelProvider = new FileBasedModelProvider
                {
                    Name = string.Empty,
                    DatabaseFileName = Path.GetFileName(targetDacpacPath)
                }
            }
        };
        
        await _scmpHandler.SaveManifestAsync(permanentScmp, permanentScmpPath);
        Console.WriteLine($"Saved DACPAC SCMP file: {Path.GetFileName(permanentScmpPath)}");
        
        Console.WriteLine($"Created temporary SCMP file with filesystem DACPAC endpoints");
        Console.WriteLine($"  Temp SCMP path: {tempPath}");
        
        // Debug: Log the content for troubleshooting
        if (File.Exists(tempPath))
        {
            var content = await File.ReadAllTextAsync(tempPath);
            Console.WriteLine($"  SCMP file size: {content.Length} bytes");
            // Optionally log first few lines for debugging
            var lines = content.Split('\n').Take(10);
            Console.WriteLine("  First 10 lines of SCMP:");
            foreach (var line in lines)
            {
                Console.WriteLine($"    {line.TrimEnd()}");
            }
        }
        
        return tempPath;
    }
}

/// <summary>
/// Result of schema comparison
/// </summary>
public class SchemaComparisonResult
{
    /// <summary>
    /// The actual comparison result from Microsoft's API
    /// </summary>
    public Microsoft.SqlServer.Dac.Compare.SchemaComparisonResult? ComparisonResult { get; set; }
    
    /// <summary>
    /// Total number of differences found
    /// </summary>
    public int TotalDifferences { get; set; }
    
    /// <summary>
    /// Number of included differences
    /// </summary>
    public int IncludedDifferences { get; set; }
    
    /// <summary>
    /// Number of excluded differences
    /// </summary>
    public int ExcludedDifferences { get; set; }
}