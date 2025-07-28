using SqlServerStructureGenerator;

// Parse command line arguments
if (args.Length < 3)
{
    Console.WriteLine("Usage: SqlServerStructureGenerator <sourceConnectionString> <targetConnectionString> <output-path>");
    Console.WriteLine();
    Console.WriteLine("Example:");
    Console.WriteLine(@"  SqlServerStructureGenerator ""Server=dev;Database=DevDB;..."" ""Server=prod;Database=ProdDB;..."" ""C:\Output""");
    return 1;
}

var sourceConnectionString = args[0];
var targetConnectionString = args[1];
var outputPath = args[2];

try
{
    // Create and run the generator
    var generator = new DatabaseScriptGenerator(sourceConnectionString, targetConnectionString, outputPath);
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
