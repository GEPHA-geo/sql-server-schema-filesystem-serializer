using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SqlServer.Schema.Migration.Generator;

/// <summary>
/// Splits migration SQL scripts into organized segments based on database objects
/// </summary>
public class MigrationScriptSplitter
{
    // Regex patterns for identifying SQL objects and operations
    private static readonly Regex TablePattern = new(@"(?:CREATE|ALTER|DROP)\s+TABLE\s+(?:\[?(\w+)\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase);
    private static readonly Regex TempTablePattern = new(@"tmp_ms_xx_(\w+)", RegexOptions.IgnoreCase);
    private static readonly Regex ForeignKeyPattern = new(@"(?:ADD|DROP)\s+CONSTRAINT\s+\[?(\w+)\]?(?:.*?FOREIGN\s+KEY.*?REFERENCES\s+(?:\[?(\w+)\]?\.)?\[?(\w+)\]?)?", RegexOptions.IgnoreCase);
    private static readonly Regex IndexPattern = new(@"CREATE\s+(?:UNIQUE\s+)?(?:CLUSTERED\s+)?(?:NONCLUSTERED\s+)?INDEX\s+\[?(\w+)\]?\s+ON\s+(?:\[?(\w+)\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase);
    private static readonly Regex DropIndexPattern = new(@"DROP\s+INDEX\s+\[?(\w+)\]?\s+ON\s+(?:\[?(\w+)\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase);
    private static readonly Regex PrimaryKeyPattern = new(@"(?:ADD|DROP)\s+CONSTRAINT\s+\[?(\w+)\]?\s+PRIMARY\s+KEY", RegexOptions.IgnoreCase);
    private static readonly Regex ViewPattern = new(@"(?:CREATE|ALTER|DROP)\s+VIEW\s+(?:\[?(\w+)\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase);
    private static readonly Regex ProcedurePattern = new(@"(?:CREATE|ALTER|DROP)\s+PROC(?:EDURE)?\s+(?:\[?(\w+)\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase);
    private static readonly Regex FunctionPattern = new(@"(?:CREATE|ALTER|DROP)\s+FUNCTION\s+(?:\[?(\w+)\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase);
    private static readonly Regex TriggerPattern = new(@"(?:CREATE|ALTER|DROP)\s+TRIGGER\s+(?:\[?(\w+)\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase);
    private static readonly Regex RenamePattern = new(@"sp_rename\s+'(?:\[?(\w+)\]?\.)?\[?(\w+)\]?'\s*,\s*'(\w+)'", RegexOptions.IgnoreCase);
    private static readonly Regex SchemaPattern = new(@"CREATE\s+SCHEMA\s+\[?(\w+)\]?", RegexOptions.IgnoreCase);
    
    /// <summary>
    /// Splits a migration script into organized segments by database object
    /// </summary>
    public async Task SplitMigrationScript(string migrationScriptPath, string outputDirectory)
    {
        if (!File.Exists(migrationScriptPath))
            throw new FileNotFoundException($"Migration script not found: {migrationScriptPath}");

        // Read the migration script
        var script = await File.ReadAllTextAsync(migrationScriptPath);
        
        // Parse and group operations by object
        var objectGroups = ParseAndGroupByObject(script);
        
        // Create output directory structure
        Directory.CreateDirectory(outputDirectory);
        var changesDir = Path.Combine(outputDirectory, "changes");
        Directory.CreateDirectory(changesDir);
        
        // Write each object group to its own file
        var manifestEntries = new List<ManifestEntry>();
        var sequence = 1;
        
        foreach (var group in objectGroups)
        {
            var filename = $"{sequence:D3}_{group.ObjectType}_{group.Schema}_{group.ObjectName}.sql";
            var filePath = Path.Combine(changesDir, filename);
            
            // Add header comment to the file
            var fileContent = GenerateFileHeader(group) + group.Script;
            await File.WriteAllTextAsync(filePath, fileContent);
            
            // Create manifest entry
            manifestEntries.Add(new ManifestEntry
            {
                Sequence = sequence,
                Filename = filename,
                ObjectType = group.ObjectType,
                Schema = group.Schema,
                ObjectName = group.ObjectName,
                Operations = group.Operations,
                LineCount = group.Script.Split('\n').Length,
                HasDataModification = ContainsDataModification(group.Script)
            });
            
            sequence++;
        }
        
        // Copy original migration script for reference
        var originalScriptPath = Path.Combine(outputDirectory, "migration.sql");
        File.Copy(migrationScriptPath, originalScriptPath, overwrite: true);
        
        // Generate and save manifest
        await GenerateManifest(outputDirectory, manifestEntries, migrationScriptPath);
    }
    
    private List<ObjectScriptGroup> ParseAndGroupByObject(string script)
    {
        var groups = new Dictionary<string, ObjectScriptGroup>();
        var statements = SplitIntoStatements(script);
        
        foreach (var statement in statements)
        {
            if (string.IsNullOrWhiteSpace(statement))
                continue;
                
            var objectInfo = IdentifyObject(statement);
            if (objectInfo == null)
                continue;
                
            var key = $"{objectInfo.Schema}.{objectInfo.Name}";
            
            if (!groups.ContainsKey(key))
            {
                groups[key] = new ObjectScriptGroup
                {
                    Schema = objectInfo.Schema,
                    ObjectName = objectInfo.Name,
                    ObjectType = objectInfo.Type,
                    Statements = new List<string>(),
                    Operations = new List<string>()
                };
            }
            
            // Add statement to the appropriate group
            groups[key].Statements.Add(statement);
            
            // Track the operation type
            var operation = ExtractOperation(statement);
            if (!string.IsNullOrEmpty(operation) && !groups[key].Operations.Contains(operation))
            {
                groups[key].Operations.Add(operation);
            }
        }
        
        // Combine statements for each group
        foreach (var group in groups.Values)
        {
            group.Script = string.Join("\nGO\n", group.Statements);
        }
        
        // Sort groups by dependency order
        return SortByDependencies(groups.Values.ToList());
    }
    
    private List<string> SplitIntoStatements(string script)
    {
        // Split on GO statements, but preserve the statement content
        var statements = new List<string>();
        var lines = script.Split('\n');
        var currentStatement = new StringBuilder();
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Check if this is a GO statement
            if (trimmedLine.Equals("GO", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("GO ", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("GO\t", StringComparison.OrdinalIgnoreCase))
            {
                if (currentStatement.Length > 0)
                {
                    statements.Add(currentStatement.ToString().Trim());
                    currentStatement.Clear();
                }
            }
            else
            {
                currentStatement.AppendLine(line);
            }
        }
        
        // Add any remaining statement
        if (currentStatement.Length > 0)
        {
            statements.Add(currentStatement.ToString().Trim());
        }
        
        return statements;
    }
    
    private ObjectInfo? IdentifyObject(string statement)
    {
        // Handle schema operations first
        var schemaMatch = SchemaPattern.Match(statement);
        if (schemaMatch.Success)
        {
            return new ObjectInfo
            {
                Name = schemaMatch.Groups[1].Value,
                Schema = "sys",
                Type = "schema"
            };
        }
        
        // Handle tmp_ms_xx pattern - map to actual table name
        var tempTableMatch = TempTablePattern.Match(statement);
        if (tempTableMatch.Success)
        {
            var actualTableName = tempTableMatch.Groups[1].Value;
            var schema = ExtractSchemaFromStatement(statement) ?? "dbo";
            
            return new ObjectInfo
            {
                Name = actualTableName,
                Schema = schema,
                Type = "table"
            };
        }
        
        // Handle sp_rename operations
        var renameMatch = RenamePattern.Match(statement);
        if (renameMatch.Success)
        {
            var oldName = renameMatch.Groups[2].Value;
            var newName = renameMatch.Groups[3].Value;
            var schema = renameMatch.Groups[1].Value;
            
            // If renaming from tmp_ms_xx, use the final table name
            if (oldName.StartsWith("tmp_ms_xx_", StringComparison.OrdinalIgnoreCase))
            {
                return new ObjectInfo
                {
                    Name = newName,
                    Schema = !string.IsNullOrEmpty(schema) ? schema : "dbo",
                    Type = "table"
                };
            }
        }
        
        // Handle foreign key operations - group with the parent table
        var fkMatch = ForeignKeyPattern.Match(statement);
        if (fkMatch.Success && fkMatch.Groups[3].Success)
        {
            // Get the referenced table (parent table)
            var referencedTable = fkMatch.Groups[3].Value;
            var schema = fkMatch.Groups[2].Success ? fkMatch.Groups[2].Value : "dbo";
            
            // For foreign keys, we need to determine if this should be grouped with source or target table
            // We'll group it with the table being modified (found in ALTER TABLE part)
            var alterTableMatch = Regex.Match(statement, @"ALTER\s+TABLE\s+(?:\[?(\w+)\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase);
            if (alterTableMatch.Success)
            {
                return new ObjectInfo
                {
                    Name = alterTableMatch.Groups[2].Value,
                    Schema = alterTableMatch.Groups[1].Success ? alterTableMatch.Groups[1].Value : "dbo",
                    Type = "table"
                };
            }
        }
        
        // Handle index operations
        var indexMatch = IndexPattern.Match(statement);
        if (indexMatch.Success)
        {
            var tableName = indexMatch.Groups[3].Value;
            var schema = indexMatch.Groups[2].Success ? indexMatch.Groups[2].Value : "dbo";
            
            return new ObjectInfo
            {
                Name = tableName,
                Schema = schema,
                Type = "table"
            };
        }
        
        var dropIndexMatch = DropIndexPattern.Match(statement);
        if (dropIndexMatch.Success)
        {
            var tableName = dropIndexMatch.Groups[3].Value;
            var schema = dropIndexMatch.Groups[2].Success ? dropIndexMatch.Groups[2].Value : "dbo";
            
            return new ObjectInfo
            {
                Name = tableName,
                Schema = schema,
                Type = "table"
            };
        }
        
        // Handle primary key operations
        if (PrimaryKeyPattern.IsMatch(statement))
        {
            var alterTableMatch = Regex.Match(statement, @"ALTER\s+TABLE\s+(?:\[?(\w+)\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase);
            if (alterTableMatch.Success)
            {
                return new ObjectInfo
                {
                    Name = alterTableMatch.Groups[2].Value,
                    Schema = alterTableMatch.Groups[1].Success ? alterTableMatch.Groups[1].Value : "dbo",
                    Type = "table"
                };
            }
        }
        
        // Handle table operations
        var tableMatch = TablePattern.Match(statement);
        if (tableMatch.Success)
        {
            return new ObjectInfo
            {
                Name = tableMatch.Groups[2].Value,
                Schema = tableMatch.Groups[1].Success ? tableMatch.Groups[1].Value : "dbo",
                Type = "table"
            };
        }
        
        // Handle view operations
        var viewMatch = ViewPattern.Match(statement);
        if (viewMatch.Success)
        {
            return new ObjectInfo
            {
                Name = viewMatch.Groups[2].Value,
                Schema = viewMatch.Groups[1].Success ? viewMatch.Groups[1].Value : "dbo",
                Type = "view"
            };
        }
        
        // Handle stored procedure operations
        var procMatch = ProcedurePattern.Match(statement);
        if (procMatch.Success)
        {
            return new ObjectInfo
            {
                Name = procMatch.Groups[2].Value,
                Schema = procMatch.Groups[1].Success ? procMatch.Groups[1].Value : "dbo",
                Type = "procedure"
            };
        }
        
        // Handle function operations
        var funcMatch = FunctionPattern.Match(statement);
        if (funcMatch.Success)
        {
            return new ObjectInfo
            {
                Name = funcMatch.Groups[2].Value,
                Schema = funcMatch.Groups[1].Success ? funcMatch.Groups[1].Value : "dbo",
                Type = "function"
            };
        }
        
        // Handle trigger operations
        var triggerMatch = TriggerPattern.Match(statement);
        if (triggerMatch.Success)
        {
            return new ObjectInfo
            {
                Name = triggerMatch.Groups[2].Value,
                Schema = triggerMatch.Groups[1].Success ? triggerMatch.Groups[1].Value : "dbo",
                Type = "trigger"
            };
        }
        
        // If we can't identify the object, return null
        return null;
    }
    
    private string? ExtractSchemaFromStatement(string statement)
    {
        // Try to extract schema from various patterns like [schema].[object] or schema.object
        var match = Regex.Match(statement, @"\[?(\w+)\]?\.\[?\w+\]?", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        return null;
    }
    
    private string ExtractOperation(string statement)
    {
        var trimmed = statement.TrimStart();
        
        if (trimmed.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase))
        {
            if (TempTablePattern.IsMatch(trimmed))
                return "CREATE TEMP TABLE";
            return "CREATE TABLE";
        }
        if (trimmed.StartsWith("ALTER TABLE", StringComparison.OrdinalIgnoreCase))
        {
            if (trimmed.Contains("ADD CONSTRAINT", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase))
                    return "ADD PRIMARY KEY";
                if (trimmed.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase))
                    return "ADD FOREIGN KEY";
                return "ADD CONSTRAINT";
            }
            if (trimmed.Contains("DROP CONSTRAINT", StringComparison.OrdinalIgnoreCase))
                return "DROP CONSTRAINT";
            return "ALTER TABLE";
        }
        if (trimmed.StartsWith("DROP TABLE", StringComparison.OrdinalIgnoreCase))
            return "DROP TABLE";
        if (trimmed.StartsWith("INSERT INTO", StringComparison.OrdinalIgnoreCase))
            return "INSERT DATA";
        if (trimmed.StartsWith("EXEC sp_rename", StringComparison.OrdinalIgnoreCase))
            return "RENAME OBJECT";
        if (trimmed.StartsWith("CREATE INDEX", StringComparison.OrdinalIgnoreCase))
            return "CREATE INDEX";
        if (trimmed.StartsWith("DROP INDEX", StringComparison.OrdinalIgnoreCase))
            return "DROP INDEX";
        if (trimmed.StartsWith("CREATE VIEW", StringComparison.OrdinalIgnoreCase))
            return "CREATE VIEW";
        if (trimmed.StartsWith("ALTER VIEW", StringComparison.OrdinalIgnoreCase))
            return "ALTER VIEW";
        if (trimmed.StartsWith("DROP VIEW", StringComparison.OrdinalIgnoreCase))
            return "DROP VIEW";
        if (trimmed.StartsWith("CREATE PROC", StringComparison.OrdinalIgnoreCase))
            return "CREATE PROCEDURE";
        if (trimmed.StartsWith("ALTER PROC", StringComparison.OrdinalIgnoreCase))
            return "ALTER PROCEDURE";
        if (trimmed.StartsWith("DROP PROC", StringComparison.OrdinalIgnoreCase))
            return "DROP PROCEDURE";
        if (trimmed.StartsWith("CREATE FUNCTION", StringComparison.OrdinalIgnoreCase))
            return "CREATE FUNCTION";
        if (trimmed.StartsWith("ALTER FUNCTION", StringComparison.OrdinalIgnoreCase))
            return "ALTER FUNCTION";
        if (trimmed.StartsWith("DROP FUNCTION", StringComparison.OrdinalIgnoreCase))
            return "DROP FUNCTION";
        if (trimmed.StartsWith("CREATE SCHEMA", StringComparison.OrdinalIgnoreCase))
            return "CREATE SCHEMA";
            
        return "OTHER";
    }
    
    private List<ObjectScriptGroup> SortByDependencies(List<ObjectScriptGroup> groups)
    {
        // Sort objects by type and dependencies
        // Order: schemas, tables, views, functions, procedures, triggers
        var sorted = new List<ObjectScriptGroup>();
        
        // Add schemas first
        sorted.AddRange(groups.Where(g => g.ObjectType == "schema").OrderBy(g => g.ObjectName));
        
        // Add tables (already in dependency order from the original script)
        sorted.AddRange(groups.Where(g => g.ObjectType == "table").OrderBy(g => g.ObjectName));
        
        // Add views
        sorted.AddRange(groups.Where(g => g.ObjectType == "view").OrderBy(g => g.ObjectName));
        
        // Add functions
        sorted.AddRange(groups.Where(g => g.ObjectType == "function").OrderBy(g => g.ObjectName));
        
        // Add procedures
        sorted.AddRange(groups.Where(g => g.ObjectType == "procedure").OrderBy(g => g.ObjectName));
        
        // Add triggers
        sorted.AddRange(groups.Where(g => g.ObjectType == "trigger").OrderBy(g => g.ObjectName));
        
        // Add any remaining object types
        sorted.AddRange(groups.Where(g => !sorted.Contains(g)).OrderBy(g => g.ObjectType).ThenBy(g => g.ObjectName));
        
        return sorted;
    }
    
    private string GenerateFileHeader(ObjectScriptGroup group)
    {
        var header = new StringBuilder();
        header.AppendLine($"-- Migration Segment: {group.ObjectType} {group.Schema}.{group.ObjectName}");
        header.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        header.AppendLine($"-- This file contains all operations related to {group.ObjectType} [{group.Schema}].[{group.ObjectName}]");
        if (group.Operations.Any())
        {
            header.AppendLine($"-- Operations: {string.Join(", ", group.Operations)}");
        }
        header.AppendLine();
        return header.ToString();
    }
    
    private bool ContainsDataModification(string script)
    {
        var dataModificationPatterns = new[]
        {
            @"\bINSERT\s+INTO\b",
            @"\bUPDATE\s+\w+\s+SET\b",
            @"\bDELETE\s+FROM\b",
            @"\bTRUNCATE\s+TABLE\b",
            @"\bMERGE\s+\w+\s+",
            @"\bSET\s+IDENTITY_INSERT\b"
        };
        
        return dataModificationPatterns.Any(pattern => 
            Regex.IsMatch(script, pattern, RegexOptions.IgnoreCase));
    }
    
    private async Task GenerateManifest(string outputDirectory, List<ManifestEntry> entries, string originalScriptPath)
    {
        var filename = Path.GetFileNameWithoutExtension(originalScriptPath);
        
        // Extract timestamp, actor, and description from filename
        // Format: _20250812_123456_actor_description.sql
        var parts = filename.Split('_').Where(p => !string.IsNullOrEmpty(p)).ToArray();
        var timestamp = parts.Length > 1 ? $"{parts[0]}_{parts[1]}" : "";
        var actor = parts.Length > 2 ? parts[2] : "system";
        var description = parts.Length > 3 ? string.Join("_", parts.Skip(3)) : "migration";
        
        var manifest = new MigrationManifest
        {
            Version = "1.0",
            Timestamp = timestamp,
            Actor = actor,
            Description = description,
            OriginalScript = "migration.sql",
            TotalSegments = entries.Count,
            ExecutionOrder = entries,
            Summary = new MigrationSummary
            {
                TablesModified = entries.Count(e => e.ObjectType == "table"),
                ViewsModified = entries.Count(e => e.ObjectType == "view"),
                ProceduresModified = entries.Count(e => e.ObjectType == "procedure"),
                FunctionsModified = entries.Count(e => e.ObjectType == "function"),
                TotalOperations = entries.Sum(e => e.Operations.Count)
            }
        };
        
        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(manifestPath, json);
    }
    
    /// <summary>
    /// Reconstructs the original migration script from segmented files
    /// </summary>
    public async Task<string> ReconstructMigration(string segmentsDirectory)
    {
        var manifestPath = Path.Combine(segmentsDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Manifest file not found: {manifestPath}");
            
        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        var manifest = JsonSerializer.Deserialize<MigrationManifest>(manifestJson, 
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        
        if (manifest == null)
            throw new InvalidOperationException("Failed to deserialize manifest");
            
        var scriptBuilder = new StringBuilder();
        scriptBuilder.AppendLine("-- Reconstructed migration script");
        scriptBuilder.AppendLine($"-- Reconstructed at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        scriptBuilder.AppendLine($"-- From {manifest.TotalSegments} segments");
        scriptBuilder.AppendLine();
        
        foreach (var segment in manifest.ExecutionOrder)
        {
            var segmentPath = Path.Combine(segmentsDirectory, "changes", segment.Filename);
            if (!File.Exists(segmentPath))
            {
                throw new FileNotFoundException($"Segment file not found: {segmentPath}");
            }
            
            var segmentContent = await File.ReadAllTextAsync(segmentPath);
            scriptBuilder.AppendLine(segmentContent);
            scriptBuilder.AppendLine(); // Add spacing between segments
        }
        
        return scriptBuilder.ToString();
    }
    
    // Internal classes for organizing data
    private class ObjectInfo
    {
        public string Name { get; set; } = "";
        public string Schema { get; set; } = "dbo";
        public string Type { get; set; } = "";
    }
    
    private class ObjectScriptGroup
    {
        public string Schema { get; set; } = "dbo";
        public string ObjectName { get; set; } = "";
        public string ObjectType { get; set; } = "";
        public List<string> Statements { get; set; } = new();
        public List<string> Operations { get; set; } = new();
        public string Script { get; set; } = "";
    }
    
    private class ManifestEntry
    {
        public int Sequence { get; set; }
        public string Filename { get; set; } = "";
        public string ObjectType { get; set; } = "";
        public string Schema { get; set; } = "";
        public string ObjectName { get; set; } = "";
        public List<string> Operations { get; set; } = new();
        public int LineCount { get; set; }
        public bool HasDataModification { get; set; }
    }
    
    private class MigrationManifest
    {
        public string Version { get; set; } = "1.0";
        public string Timestamp { get; set; } = "";
        public string Actor { get; set; } = "";
        public string Description { get; set; } = "";
        public string OriginalScript { get; set; } = "";
        public int TotalSegments { get; set; }
        public List<ManifestEntry> ExecutionOrder { get; set; } = new();
        public MigrationSummary Summary { get; set; } = new();
    }
    
    private class MigrationSummary
    {
        public int TablesModified { get; set; }
        public int ViewsModified { get; set; }
        public int ProceduresModified { get; set; }
        public int FunctionsModified { get; set; }
        public int TotalOperations { get; set; }
    }
}