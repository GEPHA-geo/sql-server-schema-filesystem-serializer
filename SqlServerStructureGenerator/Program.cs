using SqlServerStructureGenerator;

// Parse command line arguments
if (args.Length < 2)
{
    Console.WriteLine("Usage: SqlServerStructureGenerator <connection-string> <output-path>");
    Console.WriteLine();
    Console.WriteLine("Example:");
    Console.WriteLine(@"  SqlServerStructureGenerator ""Server=localhost;Database=MyDB;Integrated Security=true"" ""C:\Output""");
    return 1;
}

var connectionString = args[0];
var outputPath = args[1];

try
{
    // Create and run the generator
    var generator = new DatabaseScriptGenerator(connectionString, outputPath);
    await generator.GenerateStructureAsync();
    
    Console.WriteLine("Database structure generation completed successfully!");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}
