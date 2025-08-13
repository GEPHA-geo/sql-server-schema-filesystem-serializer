#r "nuget: Microsoft.SqlServer.DacFx, 162.5.67"
#r "nuget: Microsoft.Data.SqlClient, 5.2.0"

using Microsoft.SqlServer.Dac;
using System;
using System.IO;
using System.Text.RegularExpressions;

var connectionString = "Server=10.188.49.19;Database=abc_20250801_1804;User Id=sa_abc_dev;Password=Vr9t#nE8%P068@iZ;TrustServerCertificate=true";
var dacpacPath = Path.Combine(Path.GetTempPath(), $"test_authorization_{DateTime.Now:yyyyMMdd_HHmmss}.dacpac");

Console.WriteLine("=== DACPAC Authorization Test ===\n");

try
{
    // Step 1: Extract DACPAC
    Console.WriteLine("1. Extracting DACPAC from database...");
    var dacServices = new DacServices(connectionString);
    
    var extractOptions = new DacExtractOptions
    {
        ExtractAllTableData = false,
        IgnoreExtendedProperties = false,
        IgnorePermissions = false,
        IgnoreUserLoginMappings = true  // Changed to true to match your current settings
    };
    
    dacServices.Extract(dacpacPath, "abc_20250801_1804", "AuthorizationTest", new Version(1, 0), null, null, extractOptions);
    Console.WriteLine($"   ✓ DACPAC extracted to: {dacpacPath}");
    Console.WriteLine($"   File size: {new FileInfo(dacpacPath).Length:N0} bytes\n");
    
    // Step 2: Load DACPAC
    Console.WriteLine("2. Loading DACPAC...");
    var dacpac = DacPackage.Load(dacpacPath);
    Console.WriteLine($"   ✓ DACPAC loaded successfully\n");
    
    // Test different configurations
    Console.WriteLine("3. Testing different DacDeployOptions configurations:\n");
    
    var configurations = new[]
    {
        new { 
            Name = "A. Default options", 
            Options = new DacDeployOptions
            {
                CreateNewDatabase = true
            }
        },
        new { 
            Name = "B. With IgnoreAuthorizer = true", 
            Options = new DacDeployOptions
            {
                CreateNewDatabase = true,
                IgnoreAuthorizer = true
            }
        },
        new { 
            Name = "C. Exclude Users only", 
            Options = new DacDeployOptions
            {
                CreateNewDatabase = true,
                ExcludeObjectTypes = new[] { Microsoft.SqlServer.Dac.ObjectType.Users }
            }
        },
        new {
            Name = "D. IgnoreAuthorizer + Exclude Users",
            Options = new DacDeployOptions
            {
                CreateNewDatabase = true,
                IgnoreAuthorizer = true,
                ExcludeObjectTypes = new[] { Microsoft.SqlServer.Dac.ObjectType.Users }
            }
        },
        new {
            Name = "E. Script with CreateScriptOnly",
            Options = new DacDeployOptions
            {
                CreateNewDatabase = false,
                IgnoreAuthorizer = true,
                ExcludeObjectTypes = new[] { Microsoft.SqlServer.Dac.ObjectType.Users },
                ScriptDatabaseOptions = false
            }
        }
    };
    
    foreach (var config in configurations)
    {
        Console.WriteLine($"--- {config.Name} ---");
        
        var deployOptions = config.Options;
        
        try
        {
            // Create a new DacServices instance for each test
            var testDacServices = new DacServices(connectionString);
            
            // Handle messages
            var errors = new List<string>();
            var warnings = new List<string>();
            
            testDacServices.Message += (sender, e) =>
            {
                if (e.Message.MessageType == DacMessageType.Error)
                {
                    errors.Add($"Error {e.Message.Number}: {e.Message.Message}");
                }
                else if (e.Message.MessageType == DacMessageType.Warning)
                {
                    warnings.Add(e.Message.Message);
                }
            };
            
            var script = testDacServices.GenerateDeployScript(dacpac, "TestDatabase", deployOptions);
            
            // Analyze the script
            Console.WriteLine($"   Script length: {script.Length:N0} characters");
            
            // Count CREATE SCHEMA statements with and without AUTHORIZATION
            var schemaPattern = @"CREATE\s+SCHEMA\s+\[([^\]]+)\](\s+AUTHORIZATION\s+\[([^\]]+)\])?";
            var schemaMatches = Regex.Matches(script, schemaPattern, RegexOptions.IgnoreCase);
            
            var schemasWithAuth = 0;
            var schemasWithoutAuth = 0;
            var schemaAuthUsers = new HashSet<string>();
            
            foreach (Match match in schemaMatches)
            {
                if (match.Groups[3].Success)
                {
                    schemasWithAuth++;
                    schemaAuthUsers.Add(match.Groups[3].Value);
                }
                else
                {
                    schemasWithoutAuth++;
                }
            }
            
            Console.WriteLine($"   CREATE SCHEMA statements: {schemaMatches.Count}");
            Console.WriteLine($"     - With AUTHORIZATION: {schemasWithAuth}");
            if (schemaAuthUsers.Count > 0)
            {
                Console.WriteLine($"       Users referenced: {string.Join(", ", schemaAuthUsers)}");
            }
            Console.WriteLine($"     - Without AUTHORIZATION: {schemasWithoutAuth}");
            
            // Count other objects
            var tableCount = Regex.Matches(script, @"CREATE\s+TABLE", RegexOptions.IgnoreCase).Count;
            var viewCount = Regex.Matches(script, @"CREATE\s+VIEW", RegexOptions.IgnoreCase).Count;
            var userCount = Regex.Matches(script, @"CREATE\s+USER", RegexOptions.IgnoreCase).Count;
            
            Console.WriteLine($"   CREATE TABLE statements: {tableCount}");
            Console.WriteLine($"   CREATE VIEW statements: {viewCount}");
            Console.WriteLine($"   CREATE USER statements: {userCount}");
            
            if (errors.Count > 0)
            {
                Console.WriteLine($"   Errors: {errors.Count}");
                foreach (var error in errors.Take(3))
                {
                    Console.WriteLine($"     - {error}");
                }
                if (errors.Count > 3)
                {
                    Console.WriteLine($"     ... and {errors.Count - 3} more");
                }
            }
            
            // Save sample schema statements
            var schemaStatements = new List<string>();
            foreach (Match match in schemaMatches.Take(3))
            {
                schemaStatements.Add(match.Value);
            }
            
            if (schemaStatements.Count > 0)
            {
                Console.WriteLine($"\n   Sample CREATE SCHEMA statements:");
                foreach (var stmt in schemaStatements)
                {
                    Console.WriteLine($"     {stmt}");
                }
            }
            
            // Also check for AUTHORIZATION in the script more broadly
            var authPattern = @"AUTHORIZATION\s+\[([^\]]+)\]";
            var authMatches = Regex.Matches(script, authPattern, RegexOptions.IgnoreCase);
            var uniqueAuthUsers = new HashSet<string>();
            foreach (Match match in authMatches)
            {
                uniqueAuthUsers.Add(match.Groups[1].Value);
            }
            if (uniqueAuthUsers.Count > 0)
            {
                Console.WriteLine($"\n   Total AUTHORIZATION references: {authMatches.Count}");
                Console.WriteLine($"   Unique users in AUTHORIZATION: {string.Join(", ", uniqueAuthUsers)}");
            }
        }
        catch (DacServicesException ex)
        {
            Console.WriteLine($"   ❌ Script generation failed:");
            foreach (var msg in ex.Messages.Take(5))
            {
                Console.WriteLine($"     - {msg.MessageType} {msg.Number}: {msg.Message}");
            }
            if (ex.Messages.Count > 5)
            {
                Console.WriteLine($"     ... and {ex.Messages.Count - 5} more messages");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ Unexpected error: {ex.Message}");
        }
        
        Console.WriteLine();
    }
    
    // Clean up
    if (File.Exists(dacpacPath))
    {
        File.Delete(dacpacPath);
        Console.WriteLine("4. Cleaned up temporary DACPAC file");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Fatal error: {ex.Message}");
    Console.WriteLine($"   Stack trace: {ex.StackTrace}");
}

Console.WriteLine("\n=== Test Complete ===");