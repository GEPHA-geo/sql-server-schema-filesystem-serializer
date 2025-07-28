using SqlServer.Schema.Migration.Generator;

var outputPath = "/mnt/c/Users/petre.chitashvili/repos/gepha/db_comparison";
var targetServer = "prod-server";
var targetDatabase = "abc_20250723_1442";
var migrationsPath = Path.Combine(outputPath, "servers", targetServer, targetDatabase, "migrations");

// Get actor from environment variable or use current user as fallback
var actor = Environment.GetEnvironmentVariable("GITHUB_ACTOR") ?? Environment.UserName;

var generator = new MigrationGenerator();
var changesDetected = generator.GenerateMigrations(outputPath, targetServer, targetDatabase, migrationsPath, actor);

Console.WriteLine(changesDetected ? "Migration generated!" : "No changes detected.");