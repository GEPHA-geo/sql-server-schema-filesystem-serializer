using System.Text.RegularExpressions;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac;

public class DacpacScriptParser
{
    private readonly FileSystemManager _fileSystemManager;
    
    public DacpacScriptParser()
    {
        _fileSystemManager = new FileSystemManager();
    }
    
    public void ParseAndOrganizeScripts(string script, string outputPath, string databaseName)
    {
        // Create base directory
        var basePath = Path.Combine(outputPath, databaseName);
        _fileSystemManager.CreateDirectory(basePath);
        
        // Split script into individual statements
        var statements = SplitIntoStatements(script);
        
        // Group statements by object
        var objectGroups = GroupStatementsByObject(statements);
        
        // Process each object group
        foreach (var objectGroup in objectGroups)
        {
            ProcessObjectGroup(objectGroup, basePath);
        }
        
        // Create README for empty schemas if needed
        CreateEmptySchemaReadmes(basePath);
    }
    
    private List<SqlStatement> SplitIntoStatements(string script)
    {
        var statements = new List<SqlStatement>();
        var lines = script.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
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
        }
        
        return statements;
    }
    
    private SqlStatement? ParseStatement(string statementText)
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
        else
        {
            // Skip statements we don't handle
            return null;
        }
        
        return statement;
    }
    
    private void ExtractConstraintInfo(string statementText, SqlStatement statement)
    {
        var match = Regex.Match(statementText, @"ALTER\s+TABLE\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+ADD\s+CONSTRAINT\s+\[?([^\]]+)\]?", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (match.Success)
        {
            statement.Schema = match.Groups[1].Value;
            statement.ParentTable = match.Groups[2].Value;
            statement.Name = match.Groups[3].Value;
        }
    }
    
    private Dictionary<string, List<SqlStatement>> GroupStatementsByObject(List<SqlStatement> statements)
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
                groups[key] = new List<SqlStatement>();
            }
            
            groups[key].Add(statement);
        }
        
        return groups;
    }
    
    private bool IsTableChild(ObjectType type) =>
        type == ObjectType.PrimaryKey ||
        type == ObjectType.ForeignKey ||
        type == ObjectType.CheckConstraint ||
        type == ObjectType.DefaultConstraint ||
        type == ObjectType.Index ||
        type == ObjectType.Trigger;
    
    private void ProcessObjectGroup(KeyValuePair<string, List<SqlStatement>> objectGroup, string basePath)
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
    }
    
    private void ProcessTableGroup(List<SqlStatement> statements, string basePath)
    {
        var tableStatement = statements.FirstOrDefault(s => s.Type == ObjectType.Table);
        if (tableStatement == null) return;
        
        var schemaPath = Path.Combine(basePath, tableStatement.Schema);
        var tablesPath = Path.Combine(schemaPath, "Tables");
        var tablePath = Path.Combine(tablesPath, tableStatement.Name);
        
        _fileSystemManager.CreateDirectory(tablePath);
        
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
    
    private void ProcessView(SqlStatement statement, string basePath)
    {
        var schemaPath = Path.Combine(basePath, statement.Schema);
        var viewsPath = Path.Combine(schemaPath, "Views");
        _fileSystemManager.CreateDirectory(viewsPath);
        
        var filePath = Path.Combine(viewsPath, $"{statement.Name}.sql");
        _fileSystemManager.WriteFile(filePath, statement.Text);
    }
    
    private void ProcessStoredProcedure(SqlStatement statement, string basePath)
    {
        var schemaPath = Path.Combine(basePath, statement.Schema);
        var procsPath = Path.Combine(schemaPath, "StoredProcedures");
        _fileSystemManager.CreateDirectory(procsPath);
        
        var filePath = Path.Combine(procsPath, $"{statement.Name}.sql");
        _fileSystemManager.WriteFile(filePath, statement.Text);
    }
    
    private void ProcessFunction(SqlStatement statement, string basePath)
    {
        var schemaPath = Path.Combine(basePath, statement.Schema);
        var functionsPath = Path.Combine(schemaPath, "Functions");
        _fileSystemManager.CreateDirectory(functionsPath);
        
        var filePath = Path.Combine(functionsPath, $"{statement.Name}.sql");
        _fileSystemManager.WriteFile(filePath, statement.Text);
    }
    
    private string GetFilePrefix(ObjectType type) => type switch
    {
        ObjectType.PrimaryKey => "PK",
        ObjectType.ForeignKey => "FK",
        ObjectType.CheckConstraint => "CK",
        ObjectType.DefaultConstraint => "DF",
        ObjectType.Index => "IDX",
        ObjectType.Trigger => "TR",
        _ => ""
    };
    
    private void CreateEmptySchemaReadmes(string basePath)
    {
        // Get all schema directories
        var schemaDirs = Directory.GetDirectories(basePath);
        
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
    Function
}