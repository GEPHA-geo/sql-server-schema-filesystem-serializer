using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using SqlServer.Schema.FileSystem.Serializer.Dacpac.Core;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac;

internal static class Program
{
    static void Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.WriteLine("Usage: DacpacStructureGenerator <sourceConnectionString> <targetConnectionString> <outputPath>");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine(@"  DacpacStructureGenerator ""Server=dev;Database=DevDB;..."" ""Server=prod;Database=ProdDB;..."" ""/output""");
            return;
        }

        var sourceConnectionString = args[0];
        var targetConnectionString = args[1];
        var outputPath = args[2];
        
        // Extract target server and database from target connection string
        var targetBuilder = new SqlConnectionStringBuilder(targetConnectionString);
        var targetServer = targetBuilder.DataSource.Replace('\\', '-').Replace(':', '-'); // Sanitize for folder names
        var targetDatabase = targetBuilder.InitialCatalog;

        try
        {
            // Extract database to DACPAC
            Console.WriteLine("Extracting database to DACPAC...");
            var dacpacPath = Path.Combine(Path.GetTempPath(), "temp_database.dacpac");
            var dacServices = new DacServices(sourceConnectionString);
            
            // Extract source database name from source connection string
            var sourceBuilder = new SqlConnectionStringBuilder(sourceConnectionString);
            var sourceDatabaseName = sourceBuilder.InitialCatalog;
            
            // Configure extract options to include extended properties
            var extractOptions = new DacExtractOptions
            {
                ExtractAllTableData = false,
                IgnoreExtendedProperties = false,  // Include extended properties (column descriptions)
                IgnorePermissions = true,
                IgnoreUserLoginMappings = true
            };
            
            dacServices.Extract(dacpacPath, sourceDatabaseName, "DacpacStructureGenerator", new Version(1, 0), null, null, extractOptions);
            Console.WriteLine($"DACPAC extracted successfully to: {dacpacPath}");

            // Load the DACPAC
            var dacpac = DacPackage.Load(dacpacPath);
            
            // Configure deployment options to generate full schema scripts
            var deployOptions = new DacDeployOptions
            {
                CreateNewDatabase = true,
                IgnorePermissions = true,
                IgnoreUserSettingsObjects = true,
                IgnoreLoginSids = true,
                IgnoreRoleMembership = true,
                IgnoreExtendedProperties = false,  // Include column descriptions and other extended properties
                ExcludeObjectTypes =
                [
                    Microsoft.SqlServer.Dac.ObjectType.Users,
                    Microsoft.SqlServer.Dac.ObjectType.Logins,
                    Microsoft.SqlServer.Dac.ObjectType.RoleMembership,
                    Microsoft.SqlServer.Dac.ObjectType.Permissions
                ]
            };
            
            // Generate deployment script
            Console.WriteLine("Generating deployment script...");
            var script = dacServices.GenerateDeployScript(
                dacpac,
                sourceDatabaseName,
                deployOptions
            );
            
            // Save script for debugging
            File.WriteAllText("generated_script.sql", script);
            Console.WriteLine($"Script saved to generated_script.sql ({script.Length} characters)");
            
            // Clean only the database-specific directory (preserving z_migrations)
            var targetOutputPath = Path.Combine(outputPath, "servers", targetServer, targetDatabase);
            if (Directory.Exists(targetOutputPath))
            {
                Console.WriteLine($"Cleaning database directory: {targetOutputPath} (preserving z_migrations and z_migrations_reverse)");

                // Get all subdirectories except z_migrations and z_migrations_reverse
                var subdirs = Directory.GetDirectories(targetOutputPath)
                    .Where(d => !Path.GetFileName(d).Equals("z_migrations", StringComparison.OrdinalIgnoreCase) &&
                               !Path.GetFileName(d).Equals("z_migrations_reverse", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                // Delete each subdirectory
                foreach (var dir in subdirs)
                {
                    Directory.Delete(dir, recursive: true);
                }
                
                // Delete all files in the root (if any)
                foreach (var file in Directory.GetFiles(targetOutputPath))
                {
                    File.Delete(file);
                }
            }
            
            // Parse and organize the script into separate files
            Console.WriteLine("Parsing and organizing scripts...");
            var parser = new DacpacScriptParser();
            parser.ParseAndOrganizeScripts(script, outputPath, targetServer, targetDatabase);
            
            // Clean up temp DACPAC file
            if (File.Exists(dacpacPath))
            {
                File.Delete(dacpacPath);
            }
            
            Console.WriteLine($"Database structure generated successfully at: {targetOutputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }
}