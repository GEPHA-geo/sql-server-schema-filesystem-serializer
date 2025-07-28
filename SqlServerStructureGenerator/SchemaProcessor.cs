using Microsoft.SqlServer.Management.Smo;
using System.Text;
using System.Collections.Immutable;

namespace SqlServerStructureGenerator;

// Handles schema-level organization and processing
public class SchemaProcessor(string connectionString, string basePath, ScriptingOptions scriptingOptions)
{
    readonly FileSystemManager _fileManager = new();
    readonly string _connectionString = connectionString;

    public async Task ProcessAllSchemasAsync()
    {
        // Create connection to get schemas
        var (server, database) = ConnectionFactory.CreateConnection(_connectionString);
        
        // Get unique schemas from all objects
        var schemas = GetAllSchemas(database);
        
        // Dispose of initial connection
        server.ConnectionContext.Disconnect();
        
        // Process schemas sequentially to avoid SMO thread-safety issues
        // But process objects within each schema in parallel
        foreach (var schemaName in schemas)
        {
            Console.WriteLine($"Processing schema: {schemaName}");
            try
            {
                await ProcessSchemaAsync(schemaName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing schema {schemaName}: {ex.Message}");
            }
        }
    }

    async Task ProcessSchemaAsync(string schemaName)
    {
        var schemaPath = Path.Combine(basePath, "schemas", schemaName);
        var startTime = DateTime.Now;
        Console.WriteLine($"Processing schema: {schemaName} [{startTime:HH:mm:ss.fff}]");
        
        // Create tasks for parallel processing of different object types
        var tasks = new List<Task>();
        var objectCount = 0;
        
        // First, get quick counts to avoid expensive enumeration for empty schemas
        var countStart = DateTime.Now;
        var objectCounts = await SchemaObjectCounter.GetObjectCountsAsync(_connectionString, schemaName);
        Console.WriteLine($"  Object count query: {(DateTime.Now - countStart).TotalMilliseconds:F0}ms");
        Console.WriteLine($"  Schema contains: {objectCounts["Tables"]} tables, {objectCounts["Views"]} views, " +
                          $"{objectCounts["Procedures"]} procedures, {objectCounts["Functions"]} functions");
        
        // Create initial connection to enumerate objects
        var connectionStart = DateTime.Now;
        var (server, database) = ConnectionFactory.CreateConnection(_connectionString);
        Console.WriteLine($"  Connection established in {(DateTime.Now - connectionStart).TotalMilliseconds:F0}ms");
        
        try
        {
            // Process tables - enumerate to ImmutableList first
            var tablesStart = DateTime.Now;
            var allTablesInSchema = database.Tables.Cast<Table>()
                .Where(t => t.Schema == schemaName)
                .ToList();
            Console.WriteLine($"  Table enumeration: {(DateTime.Now - tablesStart).TotalMilliseconds:F0}ms");
            
            if (allTablesInSchema.Any())
            {
                Console.WriteLine($"  Total tables in schema '{schemaName}': {allTablesInSchema.Count} (including system objects)");
                foreach (var t in allTablesInSchema.Where(t => t.IsSystemObject))
                {
                    Console.WriteLine($"    - [SYSTEM] {t.Name}");
                }
            }
            
            var tables = allTablesInSchema
                .Where(t => !t.IsSystemObject)
                .Select(t => new { t.Name, Schema = t.Schema })
                .OrderBy(t => t.Name)
                .ToImmutableList();
                
            if (tables.Any())
            {
                Console.WriteLine($"  Found {tables.Count} tables in schema '{schemaName}'");
                foreach (var table in tables)
                {
                    Console.WriteLine($"    - {table.Name}");
                }
                objectCount += tables.Count;
                tasks.Add(Task.Run(async () =>
                {
                    // Create separate connection for table processing
                    var (tableServer, tableDatabase) = ConnectionFactory.CreateConnection(_connectionString);
                    try
                    {
                        var tablesToScript = tables.Select(t => tableDatabase.Tables[t.Name, t.Schema])
                            .Where(t => t != null)
                            .Cast<Table>()
                            .ToImmutableList();
                        var tableScripter = new TableScripter(_connectionString, scriptingOptions, _fileManager);
                        await tableScripter.ScriptTablesAsync(tablesToScript, Path.Combine(schemaPath, "Tables"));
                    }
                    finally
                    {
                        tableServer.ConnectionContext.Disconnect();
                    }
                }));
            }
            else
            {
                Console.WriteLine($"  No tables found in schema '{schemaName}'");
            }
            
            // Process views - only enumerate if count > 0
            var viewsStart = DateTime.Now;
            ImmutableList<dynamic> views;
            
            if (objectCounts["Views"] > 0)
            {
                views = database.Views.Cast<View>()
                    .Where(v => !v.IsSystemObject && v.Schema == schemaName)
                    .Select(v => new { v.Name, Schema = v.Schema })
                    .OrderBy(v => v.Name)
                    .Cast<dynamic>()
                    .ToImmutableList();
                Console.WriteLine($"  View enumeration: {(DateTime.Now - viewsStart).TotalMilliseconds:F0}ms");
            }
            else
            {
                views = ImmutableList<dynamic>.Empty;
                Console.WriteLine($"  View enumeration skipped (no views in schema)");
            }
            
            if (views.Any())
            {
                Console.WriteLine($"  Found {views.Count} views in schema '{schemaName}'");
                foreach (var view in views)
                {
                    Console.WriteLine($"    - {view.Name}");
                }
                objectCount += views.Count;
                tasks.Add(Task.Run(async () =>
                {
                    // Create separate connection for view processing
                    var (viewServer, viewDatabase) = ConnectionFactory.CreateConnection(_connectionString);
                    try
                    {
                        var viewsToScript = views.Select(v => viewDatabase.Views[v.Name, v.Schema])
                            .Where(v => v != null)
                            .Cast<View>()
                            .ToImmutableList();
                        var objectScripter = new ObjectScripter(scriptingOptions, _fileManager);
                        await objectScripter.ScriptViewsAsync(viewsToScript, Path.Combine(schemaPath, "Views"));
                    }
                    finally
                    {
                        viewServer.ConnectionContext.Disconnect();
                    }
                }));
            }
            else
            {
                Console.WriteLine($"  No views found in schema '{schemaName}'");
            }
            
            // Process stored procedures - only enumerate if count > 0
            var procsStart = DateTime.Now;
            ImmutableList<dynamic> procedures;
            
            if (objectCounts["Procedures"] > 0)
            {
                procedures = database.StoredProcedures.Cast<StoredProcedure>()
                    .Where(p => !p.IsSystemObject && p.Schema == schemaName)
                    .Select(p => new { p.Name, Schema = p.Schema })
                    .OrderBy(p => p.Name)
                    .Cast<dynamic>()
                    .ToImmutableList();
                Console.WriteLine($"  Procedure enumeration: {(DateTime.Now - procsStart).TotalMilliseconds:F0}ms");
            }
            else
            {
                procedures = ImmutableList<dynamic>.Empty;
                Console.WriteLine($"  Procedure enumeration skipped (no procedures in schema)");
            }
            
            if (procedures.Any())
            {
                Console.WriteLine($"  Found {procedures.Count} stored procedures in schema '{schemaName}'");
                foreach (var proc in procedures)
                {
                    Console.WriteLine($"    - {proc.Name}");
                }
                objectCount += procedures.Count;
                tasks.Add(Task.Run(async () =>
                {
                    // Create separate connection for procedure processing
                    var (procServer, procDatabase) = ConnectionFactory.CreateConnection(_connectionString);
                    try
                    {
                        var procsToScript = procedures.Select(p => procDatabase.StoredProcedures[p.Name, p.Schema])
                            .Where(p => p != null)
                            .Cast<StoredProcedure>()
                            .ToImmutableList();
                        var objectScripter = new ObjectScripter(scriptingOptions, _fileManager);
                        await objectScripter.ScriptStoredProceduresAsync(procsToScript, Path.Combine(schemaPath, "StoredProcedures"));
                    }
                    finally
                    {
                        procServer.ConnectionContext.Disconnect();
                    }
                }));
            }
            else
            {
                Console.WriteLine($"  No stored procedures found in schema '{schemaName}'");
            }
            
            // Process functions - only enumerate if count > 0
            var funcsStart = DateTime.Now;
            ImmutableList<dynamic> functions;
            
            if (objectCounts["Functions"] > 0)
            {
                functions = database.UserDefinedFunctions.Cast<UserDefinedFunction>()
                    .Where(f => !f.IsSystemObject && f.Schema == schemaName)
                    .Select(f => new { f.Name, Schema = f.Schema })
                    .OrderBy(f => f.Name)
                    .Cast<dynamic>()
                    .ToImmutableList();
                Console.WriteLine($"  Function enumeration: {(DateTime.Now - funcsStart).TotalMilliseconds:F0}ms");
            }
            else
            {
                functions = ImmutableList<dynamic>.Empty;
                Console.WriteLine($"  Function enumeration skipped (no functions in schema)");
            }
            
            if (functions.Any())
            {
                Console.WriteLine($"  Found {functions.Count} functions in schema '{schemaName}'");
                foreach (var func in functions)
                {
                    Console.WriteLine($"    - {func.Name}");
                }
                objectCount += functions.Count;
                tasks.Add(Task.Run(async () =>
                {
                    // Create separate connection for function processing
                    var (funcServer, funcDatabase) = ConnectionFactory.CreateConnection(_connectionString);
                    try
                    {
                        var funcsToScript = functions.Select(f => funcDatabase.UserDefinedFunctions[f.Name, f.Schema])
                            .Where(f => f != null)
                            .Cast<UserDefinedFunction>()
                            .ToImmutableList();
                        var objectScripter = new ObjectScripter(scriptingOptions, _fileManager);
                        await objectScripter.ScriptFunctionsAsync(funcsToScript, Path.Combine(schemaPath, "Functions"));
                    }
                    finally
                    {
                        funcServer.ConnectionContext.Disconnect();
                    }
                }));
            }
            else
            {
                Console.WriteLine($"  No functions found in schema '{schemaName}'");
            }
            
            // Create schema directory even if empty (especially important for dbo)
            if (objectCount == 0)
            {
                Console.WriteLine($"  Schema '{schemaName}' has no objects, but creating directory structure anyway");
                Directory.CreateDirectory(schemaPath);
                
                // Create a README file to indicate the schema exists but is empty
                var readmeContent = $"# Schema: {schemaName}\n\nThis schema exists in the database but currently contains no user objects.\n";
                await File.WriteAllTextAsync(Path.Combine(schemaPath, "README.md"), readmeContent);
            }
            
            // Wait for all object types to complete
            await Task.WhenAll(tasks);
            
            var totalTime = (DateTime.Now - startTime).TotalSeconds;
            Console.WriteLine($"  Completed processing schema '{schemaName}' with {objectCount} total objects in {totalTime:F1}s");
        }
        finally
        {
            server.ConnectionContext.Disconnect();
        }
    }

    ImmutableHashSet<string> GetAllSchemas(Database database)
    {
        var schemas = new HashSet<string>();
        
        // First, try to get all schemas from the database's Schemas collection
        // This ensures we get all schemas, including empty ones
        Console.WriteLine("Retrieving schemas from database...");
        foreach (Schema schema in database.Schemas)
        {
            // Skip system schemas (but always include dbo)
            if (schema.Name == "dbo" || (!schema.IsSystemObject && schema.Name != "sys" && 
                schema.Name != "INFORMATION_SCHEMA" && !schema.Name.StartsWith("db_")))
            {
                schemas.Add(schema.Name);
                Console.WriteLine($"  Found schema: {schema.Name} (IsSystemObject: {schema.IsSystemObject})");
            }
        }
        
        // If for some reason the Schemas collection doesn't work, fall back to object-based discovery
        if (!schemas.Any())
        {
            Console.WriteLine("No schemas found in database.Schemas, falling back to object-based discovery...");
            
            // Get schemas from tables
            foreach (Table table in database.Tables)
            {
                if (!table.IsSystemObject)
                {
                    schemas.Add(table.Schema);
                    Console.WriteLine($"  Found schema '{table.Schema}' from table '{table.Name}'");
                }
                else if (table.Schema == "dbo")
                {
                    Console.WriteLine($"  Skipped system table in dbo schema: '{table.Name}'");
                }
            }
            
            // Get schemas from views
            foreach (View view in database.Views)
            {
                if (!view.IsSystemObject)
                {
                    schemas.Add(view.Schema);
                    Console.WriteLine($"  Found schema '{view.Schema}' from view '{view.Name}'");
                }
            }
            
            // Get schemas from stored procedures
            foreach (StoredProcedure proc in database.StoredProcedures)
            {
                if (!proc.IsSystemObject)
                {
                    schemas.Add(proc.Schema);
                    Console.WriteLine($"  Found schema '{proc.Schema}' from procedure '{proc.Name}'");
                }
            }
            
            // Get schemas from functions
            foreach (UserDefinedFunction func in database.UserDefinedFunctions)
            {
                if (!func.IsSystemObject)
                {
                    schemas.Add(func.Schema);
                    Console.WriteLine($"  Found schema '{func.Schema}' from function '{func.Name}'");
                }
            }
        }
        
        // Always ensure dbo is included
        if (!schemas.Contains("dbo"))
        {
            Console.WriteLine("Adding 'dbo' schema explicitly");
            schemas.Add("dbo");
        }
        
        Console.WriteLine($"Total schemas to process: {schemas.Count}");
        foreach (var schema in schemas.OrderBy(s => s))
        {
            Console.WriteLine($"  - {schema}");
        }
        
        return schemas.ToImmutableHashSet();
    }
}