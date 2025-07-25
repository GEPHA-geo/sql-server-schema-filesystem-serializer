using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlServer.Schema.Migration.Generator.Validation;

public class SchemaScriptExecutor
{
    readonly Dictionary<string, int> _objectTypePriority = new()
    {
        ["Tables"] = 1,
        ["PrimaryKeys"] = 2,
        ["DefaultConstraints"] = 3,
        ["CheckConstraints"] = 4,
        ["ForeignKeys"] = 5,
        ["Indexes"] = 6,
        ["Triggers"] = 7,
        ["Views"] = 8,
        ["StoredProcedures"] = 9,
        ["Functions"] = 10
    };
    
    public async Task ExecuteSchemasAsync(string schemasPath, string connectionString)
    {
        if (!Directory.Exists(schemasPath))
        {
            throw new DirectoryNotFoundException($"Schemas directory not found: {schemasPath}");
        }
        
        Console.WriteLine($"Executing schema scripts from: {schemasPath}");
        
        // Group all SQL files by type
        var scriptGroups = new Dictionary<string, List<string>>();
        var allSqlFiles = Directory.GetFiles(schemasPath, "*.sql", SearchOption.AllDirectories);
        
        foreach (var file in allSqlFiles)
        {
            var objectType = DetermineObjectType(file);
            if (!scriptGroups.ContainsKey(objectType))
            {
                scriptGroups[objectType] = new List<string>();
            }
            scriptGroups[objectType].Add(file);
        }
        
        // Execute in priority order
        var orderedGroups = scriptGroups
            .OrderBy(g => _objectTypePriority.ContainsKey(g.Key) ? _objectTypePriority[g.Key] : 99)
            .ToList();
            
        foreach (var group in orderedGroups)
        {
            Console.WriteLine($"Executing {group.Value.Count} {group.Key} scripts...");
            
            foreach (var scriptFile in group.Value.OrderBy(f => f))
            {
                await ExecuteScriptFileAsync(scriptFile, connectionString);
            }
        }
        
        Console.WriteLine("Schema execution completed successfully");
    }
    
    public async Task ExecuteMigrationAsync(string migrationScript, string connectionString)
    {
        Console.WriteLine("Executing migration script...");
        
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        
        // Split by GO statements
        var batches = SplitByGo(migrationScript);
        
        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch))
                continue;
                
            using var command = new SqlCommand(batch, connection);
            command.CommandTimeout = 300; // 5 minutes
            
            try
            {
                await command.ExecuteNonQueryAsync();
            }
            catch (SqlException ex)
            {
                throw new Exception($"Migration execution failed: {ex.Message}\nBatch: {batch.Substring(0, Math.Min(200, batch.Length))}...", ex);
            }
        }
        
        Console.WriteLine("Migration executed successfully");
    }
    
    async Task ExecuteScriptFileAsync(string scriptFile, string connectionString)
    {
        try
        {
            var script = await File.ReadAllTextAsync(scriptFile);
            
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            
            // Split by GO statements
            var batches = SplitByGo(script);
            
            foreach (var batch in batches)
            {
                if (string.IsNullOrWhiteSpace(batch))
                    continue;
                    
                using var command = new SqlCommand(batch, connection);
                command.CommandTimeout = 300; // 5 minutes
                
                try
                {
                    await command.ExecuteNonQueryAsync();
                }
                catch (SqlException ex)
                {
                    // Log but continue for some errors
                    if (ShouldContinueOnError(ex, scriptFile))
                    {
                        Console.WriteLine($"Warning in {Path.GetFileName(scriptFile)}: {ex.Message}");
                        continue;
                    }
                    
                    throw new Exception($"Script execution failed for {scriptFile}: {ex.Message}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to execute script {scriptFile}: {ex.Message}", ex);
        }
    }
    
    string DetermineObjectType(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var directory = Path.GetFileName(Path.GetDirectoryName(filePath));
        
        if (directory == "Tables")
        {
            if (fileName.StartsWith("TBL_")) return "Tables";
            if (fileName.StartsWith("PK_")) return "PrimaryKeys";
            if (fileName.StartsWith("FK_")) return "ForeignKeys";
            if (fileName.StartsWith("DF_")) return "DefaultConstraints";
            if (fileName.StartsWith("CHK_") || fileName.StartsWith("CK_")) return "CheckConstraints";
            if (fileName.StartsWith("IDX_") || fileName.StartsWith("IX_")) return "Indexes";
            if (fileName.StartsWith("trg_") || fileName.StartsWith("TR_")) return "Triggers";
        }
        else if (directory == "Views") return "Views";
        else if (directory == "StoredProcedures") return "StoredProcedures";
        else if (directory == "Functions") return "Functions";
        
        return "Other";
    }
    
    List<string> SplitByGo(string script)
    {
        var batches = new List<string>();
        var currentBatch = new StringBuilder();
        
        using var reader = new StringReader(script);
        string line;
        
        while ((line = reader.ReadLine()) != null)
        {
            var trimmedLine = line.Trim();
            
            if (trimmedLine.Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (currentBatch.Length > 0)
                {
                    batches.Add(currentBatch.ToString());
                    currentBatch.Clear();
                }
            }
            else
            {
                currentBatch.AppendLine(line);
            }
        }
        
        if (currentBatch.Length > 0)
        {
            batches.Add(currentBatch.ToString());
        }
        
        return batches;
    }
    
    bool ShouldContinueOnError(SqlException ex, string scriptFile)
    {
        // Continue on certain expected errors during initial schema creation
        var fileName = Path.GetFileName(scriptFile);
        
        // Foreign key might fail if referenced table doesn't exist yet
        if (fileName.StartsWith("FK_") && ex.Message.Contains("references invalid table"))
            return true;
            
        // Index might already exist
        if ((fileName.StartsWith("IDX_") || fileName.StartsWith("IX_")) && ex.Message.Contains("already exists"))
            return true;
            
        return false;
    }
}