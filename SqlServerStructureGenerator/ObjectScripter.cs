using Microsoft.SqlServer.Management.Smo;
using System.Text;

namespace SqlServerStructureGenerator;

// Handles scripting of database objects like views, stored procedures, and functions
public class ObjectScripter(ScriptingOptions scriptingOptions, FileSystemManager fileManager)
{
    readonly ScriptingOptions _scriptingOptions = scriptingOptions;

    public async Task ScriptViewsAsync(IEnumerable<View> views, string viewsPath)
    {
        var scriptTasks = new List<(string path, string script)>();
        
        // Script views sequentially to avoid SMO connection issues
        foreach (var view in views)
        {
            try
            {
                var script = ScriptView(view);
                scriptTasks.Add((Path.Combine(viewsPath, $"{view.Name}.sql"), script));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error scripting view {view.Name}: {ex.Message}");
                Console.WriteLine($"    Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"    Inner exception: {ex.InnerException.Message}");
                    if (!string.IsNullOrEmpty(ex.InnerException.StackTrace))
                        Console.WriteLine($"    Stack trace: {ex.InnerException.StackTrace.Split('\n')[0]}");
                }
            }
        }
        
        // Write all files in parallel
        var fileTasks = scriptTasks.Select(task => fileManager.WriteFileAsync(task.path, task.script));
        await Task.WhenAll(fileTasks);
    }

    public async Task ScriptStoredProceduresAsync(IEnumerable<StoredProcedure> procedures, string proceduresPath)
    {
        var scriptTasks = new List<(string path, string script)>();
        
        // Script procedures sequentially to avoid SMO connection issues
        foreach (var procedure in procedures)
        {
            try
            {
                var script = ScriptStoredProcedure(procedure);
                scriptTasks.Add((Path.Combine(proceduresPath, $"{procedure.Name}.sql"), script));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error scripting procedure {procedure.Name}: {ex.Message}");
                Console.WriteLine($"    Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"    Inner exception: {ex.InnerException.Message}");
                    if (!string.IsNullOrEmpty(ex.InnerException.StackTrace))
                        Console.WriteLine($"    Stack trace: {ex.InnerException.StackTrace.Split('\n')[0]}");
                }
            }
        }
        
        // Write all files in parallel
        var fileTasks = scriptTasks.Select(task => fileManager.WriteFileAsync(task.path, task.script));
        await Task.WhenAll(fileTasks);
    }

    public async Task ScriptFunctionsAsync(IEnumerable<UserDefinedFunction> functions, string functionsPath)
    {
        var scriptTasks = new List<(string path, string script)>();
        
        // Script functions sequentially to avoid SMO connection issues
        foreach (var function in functions)
        {
            try
            {
                var script = ScriptFunction(function);
                scriptTasks.Add((Path.Combine(functionsPath, $"{function.Name}.sql"), script));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error scripting function {function.Name}: {ex.Message}");
                Console.WriteLine($"    Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"    Inner exception: {ex.InnerException.Message}");
                    if (!string.IsNullOrEmpty(ex.InnerException.StackTrace))
                        Console.WriteLine($"    Stack trace: {ex.InnerException.StackTrace.Split('\n')[0]}");
                }
            }
        }
        
        // Write all files in parallel
        var fileTasks = scriptTasks.Select(task => fileManager.WriteFileAsync(task.path, task.script));
        await Task.WhenAll(fileTasks);
    }

    string ScriptView(View view)
    {
        var sb = new StringBuilder();
        
        // Add header
        sb.AppendLine($"-- View: {view.Schema}.{view.Name}");
        sb.AppendLine($"-- Created: {view.CreateDate}");
        sb.AppendLine($"-- Modified: {view.DateLastModified}");
        sb.AppendLine();
        
        // Drop if exists
        sb.AppendLine($"IF EXISTS (SELECT * FROM sys.views WHERE object_id = OBJECT_ID(N'[{view.Schema}].[{view.Name}]'))");
        sb.AppendLine($"    DROP VIEW [{view.Schema}].[{view.Name}]");
        sb.AppendLine("GO");
        sb.AppendLine();
        
        // Script the view
        var viewOptions = new ScriptingOptions
        {
            ScriptDrops = false,
            IncludeHeaders = false,
            SchemaQualify = true,
            SchemaQualifyForeignKeysReferences = true
        };
        
        var scripts = view.Script(viewOptions);
        sb.AppendLine(string.Join(Environment.NewLine, scripts.Cast<string>()));
        sb.AppendLine("GO");
        
        return sb.ToString();
    }

    string ScriptStoredProcedure(StoredProcedure procedure)
    {
        var sb = new StringBuilder();
        
        // Add header
        sb.AppendLine($"-- Stored Procedure: {procedure.Schema}.{procedure.Name}");
        sb.AppendLine($"-- Created: {procedure.CreateDate}");
        sb.AppendLine($"-- Modified: {procedure.DateLastModified}");
        sb.AppendLine();
        
        // Drop if exists
        sb.AppendLine($"IF EXISTS (SELECT * FROM sys.procedures WHERE object_id = OBJECT_ID(N'[{procedure.Schema}].[{procedure.Name}]'))");
        sb.AppendLine($"    DROP PROCEDURE [{procedure.Schema}].[{procedure.Name}]");
        sb.AppendLine("GO");
        sb.AppendLine();
        
        // Script the procedure
        var procOptions = new ScriptingOptions
        {
            ScriptDrops = false,
            IncludeHeaders = false,
            SchemaQualify = true,
            SchemaQualifyForeignKeysReferences = true
        };
        
        var scripts = procedure.Script(procOptions);
        sb.AppendLine(string.Join(Environment.NewLine, scripts.Cast<string>()));
        sb.AppendLine("GO");
        
        return sb.ToString();
    }

    string ScriptFunction(UserDefinedFunction function)
    {
        var sb = new StringBuilder();
        
        // Add header
        sb.AppendLine($"-- Function: {function.Schema}.{function.Name}");
        sb.AppendLine($"-- Type: {function.FunctionType}");
        sb.AppendLine($"-- Created: {function.CreateDate}");
        sb.AppendLine($"-- Modified: {function.DateLastModified}");
        sb.AppendLine();
        
        // Drop if exists
        sb.AppendLine($"IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[{function.Schema}].[{function.Name}]') AND type in (N'FN', N'IF', N'TF', N'FS', N'FT'))");
        sb.AppendLine($"    DROP FUNCTION [{function.Schema}].[{function.Name}]");
        sb.AppendLine("GO");
        sb.AppendLine();
        
        // Script the function
        var funcOptions = new ScriptingOptions
        {
            ScriptDrops = false,
            IncludeHeaders = false,
            SchemaQualify = true,
            SchemaQualifyForeignKeysReferences = true
        };
        
        var scripts = function.Script(funcOptions);
        sb.AppendLine(string.Join(Environment.NewLine, scripts.Cast<string>()));
        sb.AppendLine("GO");
        
        return sb.ToString();
    }
}