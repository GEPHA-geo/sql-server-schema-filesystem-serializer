using SqlServer.Schema.Migration.Generator;

var outputPath = "/mnt/c/Users/petre.chitashvili/repos/gepha/db_comparison";
var databaseName = "abc_20250723_1442";
var migrationsPath = Path.Combine(outputPath, databaseName, "migrations");

var generator = new MigrationGenerator();
var changesDetected = generator.GenerateMigrations(outputPath, databaseName, migrationsPath);

Console.WriteLine(changesDetected ? "Migration generated!" : "No changes detected.");