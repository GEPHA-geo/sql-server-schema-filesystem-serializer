#!/usr/bin/env dotnet-script
#r "nuget: Microsoft.SqlServer.DacFx, *"
#r "/home/petre/repos/builds/SQL_Server_Compare/SqlServer.Schema.Migration.Generator/bin/Debug/net9.0/SqlServer.Schema.Migration.Generator.dll"
#r "/home/petre/repos/builds/SQL_Server_Compare/SqlServer.Schema.Exclusion.Manager.Core/bin/Debug/net9.0/SqlServer.Schema.Exclusion.Manager.Core.dll"

using SqlServer.Schema.Migration.Generator;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;

var splitter = new MigrationScriptSplitter();
var inputFile = "/mnt/c/Users/petre.chitashvili/repos/gepha/db_comparison/servers/pharm-n1.pharm.local/abc/z_migrations/_20250818_211009_petre.chitashvili_migration.sql";
var outputDir = "/tmp/test_split_" + Guid.NewGuid().ToString("N");

Console.WriteLine($"Splitting migration file: {inputFile}");
Console.WriteLine($"Output directory: {outputDir}");

try
{
    await splitter.SplitMigrationScript(inputFile, outputDir);
    
    Console.WriteLine("\nSplit completed successfully!");
    Console.WriteLine("\nGenerated files:");
    
    var files = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
    foreach (var file in files.OrderBy(f => f))
    {
        var relativePath = Path.GetRelativePath(outputDir, file);
        var fileInfo = new FileInfo(file);
        Console.WriteLine($"  {relativePath} ({fileInfo.Length} bytes)");
    }
    
    Console.WriteLine($"\nTotal files: {files.Length}");
    Console.WriteLine($"\nYou can view the files at: {outputDir}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}