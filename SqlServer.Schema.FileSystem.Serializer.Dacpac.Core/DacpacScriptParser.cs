using System.Text.RegularExpressions;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac.Core;

public class DacpacScriptParser
{
    readonly FileSystemManager _fileSystemManager = new();

    public void ParseAndOrganizeScripts(string script, string outputPath, string targetServer, string targetDatabase, 
        string? sourceServer = null, string? sourceDatabase = null, HashSet<string>? excludedObjectNames = null)
    {
        // Create base directory with new hierarchical structure
        var basePath = Path.Combine(outputPath, "servers", targetServer, targetDatabase);
        FileSystemManager.CreateDirectory(basePath);
        
        // Count total GO statements in original script
        var totalGoStatements = CountGoStatements(script);
        
        // Split script into individual statements, tracking skipped ones
        var (statements, skippedStatements) = SplitIntoStatementsWithSkipped(script);
        
        // Verify parsing completeness - only show errors
        VerifyParsingCompleteness(script, statements, totalGoStatements);
        
        // Group statements by object
        var objectGroups = GroupStatementsByObject(statements);
        
        // Track processed statements
        var processedCount = 0;
        var excludedCount = 0;
        
        // Process each object group
        foreach (var objectGroup in objectGroups)
        {
            // Check if this object should be excluded
            if (excludedObjectNames != null && IsObjectExcluded(objectGroup.Key, excludedObjectNames))
            {
                excludedCount++;
                processedCount += objectGroup.Value.Count;
                continue; // Skip generating files for excluded objects
            }
            
            ProcessObjectGroup(objectGroup, basePath);
            processedCount += objectGroup.Value.Count;
        }
        
        // Save skipped statements to source_server_database_extra.sql if any exist
        if (skippedStatements.Any())
        {
            // Use source server/database if provided, otherwise fall back to target
            var extraFileServer = sourceServer ?? targetServer;
            var extraFileDatabase = sourceDatabase ?? targetDatabase;
            SaveSkippedStatements(skippedStatements, basePath, extraFileServer, extraFileDatabase);
        }
        
        // Create README for empty schemas if needed
        CreateEmptySchemaReadmes(basePath);
        
        // Report exclusions
        if (excludedCount > 0)
        {
            Console.WriteLine($"Excluded {excludedCount} objects based on SCMP configuration");
        }
        
        // Only show errors/warnings
        if (processedCount < statements.Count)
        {
            Console.WriteLine($"WARNING: {statements.Count - processedCount} parsed statements were not processed!");
        }
    }

    private static bool IsObjectExcluded(string objectName, HashSet<string> excludedObjectNames)
    {
        // Check if the object name matches any excluded object
        // Handle different formats: [schema].[object] vs schema.object
        var normalizedObjectName = objectName.Replace("[", "").Replace("]", "");
        
        return excludedObjectNames.Contains(objectName) || 
               excludedObjectNames.Contains(normalizedObjectName);
    }

    (List<SqlStatement>, List<string>) SplitIntoStatementsWithSkipped(string script)
    {
        var statements = new List<SqlStatement>();
        var skippedStatements = new List<string>();
        var lines = script.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var currentStatement = new List<string>();
        
        foreach (var line in lines)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (currentStatement.Any())
                {
                    var statementText = string.Join("\n", currentStatement);
                    var statement = ParseStatement(statementText);
                    if (statement != null)
                    {
                        statements.Add(statement);
                    }
                    else if (!string.IsNullOrWhiteSpace(statementText))
                    {
                        // Save skipped statements that aren't empty
                        skippedStatements.Add(statementText);
                    }
                    currentStatement.Clear();
                }
            }
            else
            {
                currentStatement.Add(line);
            }
        }
        
        // Handle last statement if no final GO
        if (currentStatement.Any())
        {
            var statementText = string.Join("\n", currentStatement);
            var statement = ParseStatement(statementText);
            if (statement != null)
            {
                statements.Add(statement);
            }
            else if (!string.IsNullOrWhiteSpace(statementText))
            {
                skippedStatements.Add(statementText);
            }
        }
        
        return (statements, skippedStatements);
    }
    
    // Keep the old method for backward compatibility, but have it call the new one
    List<SqlStatement> SplitIntoStatements(string script)
    {
        var (statements, _) = SplitIntoStatementsWithSkipped(script);
        return statements;
    }

    SqlStatement? ParseStatement(string statementText)
    {
        if (string.IsNullOrWhiteSpace(statementText))
            return null;
        
        var statement = new SqlStatement { Text = statementText };
        
        // Determine statement type and extract metadata
        if (Regex.IsMatch(statementText, @"CREATE\s+TABLE", RegexOptions.IgnoreCase))
        {
            statement.Type = ObjectType.Table;
            var match = Regex.Match(statementText, @"CREATE\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                statement.Schema = match.Groups[1].Value;
                statement.Name = match.Groups[2].Value;
            }
        }
        else if (Regex.IsMatch(statementText, @"ALTER\s+TABLE.*ADD\s+CONSTRAINT.*PRIMARY\s+KEY", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            statement.Type = ObjectType.PrimaryKey;
            ExtractConstraintInfo(statementText, statement);
        }
        else if (Regex.IsMatch(statementText, @"ALTER\s+TABLE.*ADD\s+CONSTRAINT.*FOREIGN\s+KEY", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            statement.Type = ObjectType.ForeignKey;
            ExtractConstraintInfo(statementText, statement);
        }
        else if (Regex.IsMatch(statementText, @"ALTER\s+TABLE.*ADD\s+CONSTRAINT.*CHECK", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            statement.Type = ObjectType.CheckConstraint;
            ExtractConstraintInfo(statementText, statement);
        }
        else if (Regex.IsMatch(statementText, @"ALTER\s+TABLE.*ADD\s+CONSTRAINT.*DEFAULT", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            statement.Type = ObjectType.DefaultConstraint;
            ExtractConstraintInfo(statementText, statement);
        }
        else if (Regex.IsMatch(statementText, @"CREATE\s+.*INDEX", RegexOptions.IgnoreCase))
        {
            statement.Type = ObjectType.Index;
            var match = Regex.Match(statementText, @"CREATE\s+.*INDEX\s+\[?([^\]]+)\]?\s+ON\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                statement.Name = match.Groups[1].Value;
                statement.Schema = match.Groups[2].Value;
                statement.ParentTable = match.Groups[3].Value;
            }
        }
        else if (Regex.IsMatch(statementText, @"CREATE\s+TRIGGER", RegexOptions.IgnoreCase))
        {
            statement.Type = ObjectType.Trigger;
            var match = Regex.Match(statementText, @"CREATE\s+TRIGGER\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+ON\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                statement.Schema = match.Groups[1].Value;
                statement.Name = match.Groups[2].Value;
                statement.ParentTable = match.Groups[4].Value;
            }
        }
        else if (Regex.IsMatch(statementText, @"CREATE\s+VIEW", RegexOptions.IgnoreCase))
        {
            statement.Type = ObjectType.View;
            var match = Regex.Match(statementText, @"CREATE\s+VIEW\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                statement.Schema = match.Groups[1].Value;
                statement.Name = match.Groups[2].Value;
            }
        }
        else if (Regex.IsMatch(statementText, @"CREATE\s+PROCEDURE", RegexOptions.IgnoreCase))
        {
            statement.Type = ObjectType.StoredProcedure;
            var match = Regex.Match(statementText, @"CREATE\s+PROCEDURE\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                statement.Schema = match.Groups[1].Value;
                statement.Name = match.Groups[2].Value;
            }
        }
        else if (Regex.IsMatch(statementText, @"CREATE\s+FUNCTION", RegexOptions.IgnoreCase))
        {
            statement.Type = ObjectType.Function;
            var match = Regex.Match(statementText, @"CREATE\s+FUNCTION\s+\[?(\w+)\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                statement.Schema = match.Groups[1].Value;
                statement.Name = match.Groups[2].Value;
            }
        }
        else if (Regex.IsMatch(statementText, @"EXEC(UTE)?\s+(sys\.)?sp_addextendedproperty", RegexOptions.IgnoreCase))
        {
            statement.Type = ObjectType.ExtendedProperty;
            // Extract table and schema from the sp_addextendedproperty call
            var match = Regex.Match(statementText, 
                @"@level0name\s*=\s*N?'([^']+)'.*?@level1name\s*=\s*N?'([^']+)'", 
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                statement.Schema = match.Groups[1].Value;
                statement.ParentTable = match.Groups[2].Value;
                
                // Extract property name
                var nameMatch = Regex.Match(statementText, @"@name\s*=\s*N?'([^']+)'", RegexOptions.IgnoreCase);
                if (nameMatch.Success)
                {
                    // Check if this is a column description
                    var columnMatch = Regex.Match(statementText, @"@level2name\s*=\s*N?'([^']+)'", RegexOptions.IgnoreCase);
                    if (columnMatch.Success)
                    {
                        statement.Name = $"Column_Description_{columnMatch.Groups[1].Value}";
                    }
                    else
                    {
                        statement.Name = nameMatch.Groups[1].Value;
                    }
                }
            }
        }
        else if (Regex.IsMatch(statementText, @"ALTER\s+DATABASE.*?ADD\s+FILEGROUP", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            statement.Type = ObjectType.Filegroup;
            var match = Regex.Match(statementText, @"ADD\s+FILEGROUP\s+\[?([^\]]+)\]?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                statement.Name = match.Groups[1].Value;
                statement.Schema = "sys"; // Filegroups are system-level objects
            }
        }
        else if (Regex.IsMatch(statementText, @"CREATE\s+SCHEMA", RegexOptions.IgnoreCase))
        {
            statement.Type = ObjectType.Schema;
            var match = Regex.Match(statementText, @"CREATE\s+SCHEMA\s+\[?([^\]\s]+)\]?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                statement.Name = match.Groups[1].Value;
                statement.Schema = "sys"; // Schemas are system-level objects
            }
        }
        else if (Regex.IsMatch(statementText, @"CREATE\s+USER", RegexOptions.IgnoreCase))
        {
            statement.Type = ObjectType.User;
            var match = Regex.Match(statementText, @"CREATE\s+USER\s+\[?([^\]\s]+)\]?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                statement.Name = match.Groups[1].Value;
                statement.Schema = "sys"; // Users are database-level objects
            }
        }
        else if (Regex.IsMatch(statementText, @"CREATE\s+LOGIN", RegexOptions.IgnoreCase))
        {
            statement.Type = ObjectType.Login;
            var match = Regex.Match(statementText, @"CREATE\s+LOGIN\s+\[?([^\]\s]+)\]?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                statement.Name = match.Groups[1].Value;
                statement.Schema = "sys"; // Logins are server-level objects
            }
        }
        else if (Regex.IsMatch(statementText, @"CREATE\s+ROLE", RegexOptions.IgnoreCase))
        {
            statement.Type = ObjectType.Role;
            var match = Regex.Match(statementText, @"CREATE\s+ROLE\s+\[?([^\]\s]+)\]?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                statement.Name = match.Groups[1].Value;
                statement.Schema = "sys"; // Roles are database-level objects
            }
        }
        else
        {
            // Skip statements we don't handle
            return null;
        }
        
        return statement;
    }

    void ExtractConstraintInfo(string statementText, SqlStatement statement)
    {
        var match = Regex.Match(statementText, @"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+ADD\s+CONSTRAINT\s+\[?([^\]]+)\]?", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (match.Success)
        {
            statement.Schema = match.Groups[1].Value;
            statement.ParentTable = match.Groups[2].Value;
            statement.Name = match.Groups[3].Value;
        }
    }

    Dictionary<string, List<SqlStatement>> GroupStatementsByObject(List<SqlStatement> statements)
    {
        var groups = new Dictionary<string, List<SqlStatement>>();
        
        foreach (var statement in statements)
        {
            string key;
            
            if (statement.Type == ObjectType.Table)
            {
                key = $"{statement.Schema}.{statement.Name}";
            }
            else if (IsTableChild(statement.Type))
            {
                key = $"{statement.Schema}.{statement.ParentTable}";
            }
            else
            {
                key = $"{statement.Schema}.{statement.Name}.{statement.Type}";
            }
            
            if (!groups.ContainsKey(key))
            {
                groups[key] = [];
            }
            
            groups[key].Add(statement);
        }
        
        return groups;
    }

    bool IsTableChild(ObjectType type) =>
        type == ObjectType.PrimaryKey ||
        type == ObjectType.ForeignKey ||
        type == ObjectType.CheckConstraint ||
        type == ObjectType.DefaultConstraint ||
        type == ObjectType.Index ||
        type == ObjectType.Trigger ||
        type == ObjectType.ExtendedProperty;

    void ProcessObjectGroup(KeyValuePair<string, List<SqlStatement>> objectGroup, string basePath)
    {
        var statements = objectGroup.Value;
        var firstStatement = statements.First();
        
        if (firstStatement.Type == ObjectType.Table)
        {
            ProcessTableGroup(statements, basePath);
        }
        else if (firstStatement.Type == ObjectType.View)
        {
            ProcessView(firstStatement, basePath);
        }
        else if (firstStatement.Type == ObjectType.StoredProcedure)
        {
            ProcessStoredProcedure(firstStatement, basePath);
        }
        else if (firstStatement.Type == ObjectType.Function)
        {
            ProcessFunction(firstStatement, basePath);
        }
        else if (firstStatement.Type == ObjectType.Filegroup)
        {
            ProcessFilegroup(firstStatement, basePath);
        }
        else if (firstStatement.Type == ObjectType.Schema)
        {
            ProcessSchema(firstStatement, basePath);
        }
        else if (firstStatement.Type == ObjectType.User)
        {
            ProcessUser(firstStatement, basePath);
        }
        else if (firstStatement.Type == ObjectType.Login)
        {
            ProcessLogin(firstStatement, basePath);
        }
        else if (firstStatement.Type == ObjectType.Role)
        {
            ProcessRole(firstStatement, basePath);
        }
    }

    void ProcessTableGroup(List<SqlStatement> statements, string basePath)
    {
        var tableStatement = statements.FirstOrDefault(s => s.Type == ObjectType.Table);
        if (tableStatement == null) return;
        
        var schemaPath = Path.Combine(basePath, "schemas", tableStatement.Schema);
        var tablesPath = Path.Combine(schemaPath, "Tables");
        var tablePath = Path.Combine(tablesPath, tableStatement.Name);
        
        FileSystemManager.CreateDirectory(tablePath);
        
        // Write table definition
        var tableFile = Path.Combine(tablePath, $"TBL_{tableStatement.Name}.sql");
        _fileSystemManager.WriteFile(tableFile, tableStatement.Text);
        
        // Write constraints and indexes
        foreach (var statement in statements.Where(s => s.Type != ObjectType.Table))
        {
            var prefix = GetFilePrefix(statement.Type);
            var fileName = statement.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) 
                ? $"{statement.Name}.sql" 
                : $"{prefix}_{statement.Name}.sql";
            var filePath = Path.Combine(tablePath, fileName);
            _fileSystemManager.WriteFile(filePath, statement.Text);
        }
    }

    void ProcessView(SqlStatement statement, string basePath)
    {
        var schemaPath = Path.Combine(basePath, "schemas", statement.Schema);
        var viewsPath = Path.Combine(schemaPath, "Views");
        FileSystemManager.CreateDirectory(viewsPath);
        
        var filePath = Path.Combine(viewsPath, $"{statement.Name}.sql");
        _fileSystemManager.WriteFile(filePath, statement.Text);
    }

    void ProcessStoredProcedure(SqlStatement statement, string basePath)
    {
        var schemaPath = Path.Combine(basePath, "schemas", statement.Schema);
        var procsPath = Path.Combine(schemaPath, "StoredProcedures");
        FileSystemManager.CreateDirectory(procsPath);
        
        var filePath = Path.Combine(procsPath, $"{statement.Name}.sql");
        _fileSystemManager.WriteFile(filePath, statement.Text);
    }

    void ProcessFunction(SqlStatement statement, string basePath)
    {
        var schemaPath = Path.Combine(basePath, "schemas", statement.Schema);
        var functionsPath = Path.Combine(schemaPath, "Functions");
        FileSystemManager.CreateDirectory(functionsPath);
        
        var filePath = Path.Combine(functionsPath, $"{statement.Name}.sql");
        _fileSystemManager.WriteFile(filePath, statement.Text);
    }
    
    void ProcessFilegroup(SqlStatement statement, string basePath)
    {
        // Filegroups are stored at the root level, not under schemas
        var filegroupsPath = Path.Combine(basePath, "filegroups");
        FileSystemManager.CreateDirectory(filegroupsPath);
        
        var filePath = Path.Combine(filegroupsPath, $"{statement.Name}.sql");
        _fileSystemManager.WriteFile(filePath, statement.Text);
    }
    
    void ProcessSchema(SqlStatement statement, string basePath)
    {
        // Create schemas directory at the root level
        var schemasPath = Path.Combine(basePath, "schemas");
        FileSystemManager.CreateDirectory(schemasPath);
        
        // Create the schema directory structure for organizing objects
        var schemaDir = Path.Combine(schemasPath, statement.Name);
        FileSystemManager.CreateDirectory(schemaDir);
        
        // Store the schema CREATE statement inside the schema directory
        var filePath = Path.Combine(schemaDir, $"{statement.Name}.sql");
        _fileSystemManager.WriteFile(filePath, statement.Text);
    }
    
    void ProcessUser(SqlStatement statement, string basePath)
    {
        // Users are stored at the root level in a users directory
        var usersPath = Path.Combine(basePath, "users");
        FileSystemManager.CreateDirectory(usersPath);
        
        var filePath = Path.Combine(usersPath, $"{statement.Name}.sql");
        _fileSystemManager.WriteFile(filePath, statement.Text);
    }
    
    void ProcessLogin(SqlStatement statement, string basePath)
    {
        // Logins are stored at the root level in a logins directory
        var loginsPath = Path.Combine(basePath, "logins");
        FileSystemManager.CreateDirectory(loginsPath);
        
        var filePath = Path.Combine(loginsPath, $"{statement.Name}.sql");
        _fileSystemManager.WriteFile(filePath, statement.Text);
    }
    
    void ProcessRole(SqlStatement statement, string basePath)
    {
        // Roles are stored at the root level in a roles directory
        var rolesPath = Path.Combine(basePath, "roles");
        FileSystemManager.CreateDirectory(rolesPath);
        
        var filePath = Path.Combine(rolesPath, $"{statement.Name}.sql");
        _fileSystemManager.WriteFile(filePath, statement.Text);
    }
    
    void SaveSkippedStatements(List<string> skippedStatements, string basePath, string targetServer, string targetDatabase)
    {
        // Save all skipped statements to server_database_extra.sql
        var filePath = Path.Combine(basePath, $"{targetServer}_{targetDatabase}_extra.sql");
        var content = string.Join("\nGO\n", skippedStatements);
        _fileSystemManager.WriteFile(filePath, content);
    }

    string GetFilePrefix(ObjectType type) => type switch
    {
        ObjectType.PrimaryKey => "PK",
        ObjectType.ForeignKey => "FK",
        ObjectType.CheckConstraint => "CK",
        ObjectType.DefaultConstraint => "DF",
        ObjectType.Index => "IDX",
        ObjectType.Trigger => "TR",
        ObjectType.ExtendedProperty => "EP",
        _ => ""
    };

    void CreateEmptySchemaReadmes(string basePath)
    {
        // Get all schema directories
        var schemasPath = Path.Combine(basePath, "schemas");
        if (!Directory.Exists(schemasPath)) return;
        
        var schemaDirs = Directory.GetDirectories(schemasPath);
        
        foreach (var schemaDir in schemaDirs)
        {
            var hasContent = Directory.GetDirectories(schemaDir).Any() || 
                           Directory.GetFiles(schemaDir, "*.sql").Any();
            
            if (!hasContent)
            {
                var readmePath = Path.Combine(schemaDir, "README.md");
                _fileSystemManager.WriteFile(readmePath, $"# {Path.GetFileName(schemaDir)} Schema\n\nThis schema is currently empty.");
            }
        }
    }


    int CountGoStatements(string script)
    {
        var lines = script.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        return lines.Count(line => line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase));
    }

    int CountGeneratedFiles(string basePath)
    {
        return Directory.GetFiles(basePath, "*.sql", SearchOption.AllDirectories).Length;
    }

    void VerifyParsingCompleteness(string script, List<SqlStatement> statements, int totalGoStatements)
    {
        // Calculate totals
        var totalStatements = statements.Count;
        var skippedStatements = totalGoStatements - totalStatements;
        
        // Final accounting - only show errors
        var totalAccountedFor = totalStatements + skippedStatements;
        if (totalAccountedFor != totalGoStatements)
        {
            Console.WriteLine($"ERROR: Statement accounting mismatch!");
            Console.WriteLine($"   Total GO statements: {totalGoStatements}");
            Console.WriteLine($"   Parsed + Skipped: {totalAccountedFor}");
            Console.WriteLine($"   Missing: {totalGoStatements - totalAccountedFor}");
            throw new InvalidOperationException("Not all statements are accounted for!");
        }
    }

    int CountPattern(string text, string pattern)
    {
        return Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline).Count;
    }

    void CheckCount(string type, int original, int parsed)
    {
        var status = original == parsed ? "OK" : "MISMATCH";
        var color = original == parsed ? ConsoleColor.Green : ConsoleColor.Yellow;
        
        Console.ForegroundColor = color;
        Console.WriteLine($"{type,-20} {original,-10} {parsed,-10} {status,-10}");
        Console.ResetColor();
    }
}

public class SqlStatement
{
    public string Text { get; set; } = "";
    public ObjectType Type { get; set; }
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public string ParentTable { get; set; } = "";
}

public enum ObjectType
{
    Table,
    PrimaryKey,
    ForeignKey,
    CheckConstraint,
    DefaultConstraint,
    Index,
    Trigger,
    View,
    StoredProcedure,
    Function,
    ExtendedProperty,
    Filegroup,
    Schema,
    User,
    Login,
    Role
}