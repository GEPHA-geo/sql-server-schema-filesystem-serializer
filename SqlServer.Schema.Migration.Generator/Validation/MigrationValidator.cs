using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlServer.Schema.Migration.Generator.Validation;

public class MigrationValidator
{
    readonly string _connectionString;
    readonly TemporaryDatabaseManager _dbManager;
    readonly SchemaScriptExecutor _scriptExecutor;
    readonly GitSchemaStateManager _gitManager;
    
    public MigrationValidator(string connectionString)
    {
        _connectionString = connectionString;
        _dbManager = new TemporaryDatabaseManager(connectionString);
        _scriptExecutor = new SchemaScriptExecutor();
        _gitManager = new GitSchemaStateManager();
    }
    
    public async Task<ValidationResult> ValidateMigrationAsync(
        string migrationScript,
        string outputPath,
        string databaseName)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new ValidationResult();
        
        string? tempDbName = null;
        string? previousSchemaPath = null;
        
        try
        {
            Console.WriteLine("\n=== Starting Migration Validation ===");
            
            // Get the previous commit hash
            Console.WriteLine("Getting previous commit hash...");
            var previousCommit = await _gitManager.GetPreviousCommitHashAsync(outputPath);
            result.Details["PreviousCommit"] = previousCommit;
            
            // Create temporary database
            Console.WriteLine("Creating temporary database...");
            tempDbName = await _dbManager.CreateTemporaryDatabaseAsync(databaseName);
            result.Details["TempDatabase"] = tempDbName;
            
            // Export previous schema from Git
            Console.WriteLine($"Exporting schema from commit {previousCommit}...");
            previousSchemaPath = await _gitManager.ExportPreviousSchemaAsync(
                outputPath, databaseName, previousCommit);
            result.Details["SchemaPath"] = previousSchemaPath;
            
            // Execute schema scripts to build previous state
            Console.WriteLine("Building previous schema state...");
            var tempDbConnectionString = _dbManager.GetConnectionString(_connectionString, tempDbName);
            await _scriptExecutor.ExecuteSchemasAsync(previousSchemaPath, tempDbConnectionString);
            
            // Apply the migration
            Console.WriteLine("Applying migration script...");
            await _scriptExecutor.ExecuteMigrationAsync(migrationScript, tempDbConnectionString);
            
            // Verify migration was recorded (optional)
            if (await VerifyMigrationRecorded(tempDbConnectionString))
            {
                Console.WriteLine("Migration history verified successfully");
            }
            else
            {
                result.Warnings.Add("Migration was not recorded in DatabaseMigrationHistory table");
            }
            
            result.Success = true;
            Console.WriteLine("\n=== Migration Validation Successful ===");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.DetailedError = ex.ToString();
            Console.WriteLine($"\n=== Migration Validation Failed ===\nError: {ex.Message}");
        }
        finally
        {
            // Cleanup
            if (tempDbName != null)
            {
                Console.WriteLine("Cleaning up temporary database...");
                await _dbManager.DropDatabaseAsync(tempDbName);
            }
            
            if (previousSchemaPath != null)
            {
                Console.WriteLine("Cleaning up temporary files...");
                _gitManager.CleanupTempDirectory(System.IO.Path.GetDirectoryName(previousSchemaPath));
            }
            
            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            Console.WriteLine($"Validation completed in {result.ExecutionTimeMs}ms\n");
        }
        
        return result;
    }
    
    async Task<bool> VerifyMigrationRecorded(string connectionString)
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            
            // Check if migration history table exists and has records
            var checkSql = @"
                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'DatabaseMigrationHistory')
                    SELECT COUNT(*) FROM [dbo].[DatabaseMigrationHistory]
                ELSE
                    SELECT 0";
                    
            using var command = new SqlCommand(checkSql, connection);
            var count = (int)await command.ExecuteScalarAsync();
            
            return count > 0;
        }
        catch
        {
            return false;
        }
    }
}