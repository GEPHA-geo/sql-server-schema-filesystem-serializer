using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;

namespace SqlServer.Schema.FileSystem.Serializer.Dacpac;

internal static class Program
{
    static void Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Usage: DacpacStructureGenerator <connectionString> <outputPath>");
            return;
        }

        var connectionString = args[0];
        var outputPath = args[1];

        try
        {
            // Extract database to DACPAC
            Console.WriteLine("Extracting database to DACPAC...");
            var dacpacPath = Path.Combine(Path.GetTempPath(), "temp_database.dacpac");
            var dacServices = new DacServices(connectionString);
            
            // Extract database name from connection string
            var builder = new SqlConnectionStringBuilder(connectionString);
            var databaseName = builder.InitialCatalog;
            
            dacServices.Extract(dacpacPath, databaseName, "DacpacStructureGenerator", new Version(1, 0));
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
                databaseName,
                deployOptions
            );
            
            // Save script for debugging
            File.WriteAllText("generated_script.sql", script);
            Console.WriteLine($"Script saved to generated_script.sql ({script.Length} characters)");
            
            // Clean only the database-specific directory
            var databaseOutputDir = Path.Combine(outputPath, databaseName);
            if (Directory.Exists(databaseOutputDir))
            {
                Console.WriteLine($"Removing existing database directory: {databaseOutputDir}");
                Directory.Delete(databaseOutputDir, recursive: true);
            }
            
            // Parse and organize the script into separate files
            Console.WriteLine("Parsing and organizing scripts...");
            var parser = new DacpacScriptParser();
            parser.ParseAndOrganizeScripts(script, outputPath, databaseName);
            
            // Clean up temp DACPAC file
            if (File.Exists(dacpacPath))
            {
                File.Delete(dacpacPath);
            }
            
            Console.WriteLine($"Database structure generated successfully at: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }
}