using Microsoft.SqlServer.Management.Smo;
using System.Text;
using System.Threading;
using SmoIndex = Microsoft.SqlServer.Management.Smo.Index;

namespace SqlServerStructureGenerator;

// Handles scripting of tables and their related objects
public class TableScripter(string connectionString, ScriptingOptions scriptingOptions, FileSystemManager fileManager)
{
    public async Task ScriptTablesAsync(IEnumerable<Table> tables, string tablesPath)
    {
        // Process tables in parallel with controlled concurrency
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2); // Reduce concurrent table processing

        var tableTasks = tables.Select(async table =>
        {
            await semaphore.WaitAsync();
            try
            {
                await ScriptTableAsync(table, tablesPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error scripting table {table.Name}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"    Inner exception: {ex.InnerException.Message}");
                }

                // Log the error - connection is already fresh so no point in retrying
                Console.WriteLine($"    Failed to script table {table.Name} after error: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tableTasks);
    }

    async Task ScriptTableAsync(Table table, string tablesPath)
    {
        // Create a fresh connection for each table to avoid DataReader conflicts
        var (server, database) = ConnectionFactory.CreateConnection(connectionString);
        try
        {
            // Get the table with the fresh connection
            var freshTable = database.Tables[table.Name, table.Schema];
            if (freshTable == null)
            {
                throw new Exception($"Table {table.Schema}.{table.Name} not found in fresh connection");
            }
            
            var tablePath = Path.Combine(tablesPath, freshTable.Name);
            var fileTasks = new List<Task>();

            // Script table definition (columns only)
            var tableScript = ScriptTableDefinition(freshTable);
            fileTasks.Add(fileManager.WriteFileAsync(Path.Combine(tablePath, $"TBL_{freshTable.Name}.sql"), tableScript));

            // Script primary key
            if (freshTable.Indexes.Cast<SmoIndex>().Any(i => i.IndexKeyType == IndexKeyType.DriPrimaryKey))
            {
                var pkIndex = freshTable.Indexes.Cast<SmoIndex>().First(i => i.IndexKeyType == IndexKeyType.DriPrimaryKey);
                var pkScript = ScriptIndex(pkIndex, freshTable);
                fileTasks.Add(fileManager.WriteFileAsync(Path.Combine(tablePath, $"{FileSystemManager.GetPrefixedFileName("PK_", pkIndex.Name)}.sql"), pkScript));
            }

            // Script foreign keys
            foreach (ForeignKey fk in freshTable.ForeignKeys)
            {
                var fkScript = ScriptForeignKey(fk, freshTable);
                fileTasks.Add(fileManager.WriteFileAsync(Path.Combine(tablePath, $"{FileSystemManager.GetPrefixedFileName("FK_", fk.Name)}.sql"), fkScript));
            }

            // Script check constraints
            foreach (Check check in freshTable.Checks)
            {
                var checkScript = ScriptCheckConstraint(check, freshTable);
                fileTasks.Add(fileManager.WriteFileAsync(Path.Combine(tablePath, $"{FileSystemManager.GetPrefixedFileName("CK_", check.Name)}.sql"), checkScript));
            }

            // Script default constraints
            foreach (Column column in freshTable.Columns)
            {
                if (column.DefaultConstraint != null)
                {
                    var defaultScript = ScriptDefaultConstraint(column.DefaultConstraint, freshTable);
                    fileTasks.Add(fileManager.WriteFileAsync(Path.Combine(tablePath, $"{FileSystemManager.GetPrefixedFileName("DF_", column.DefaultConstraint.Name)}.sql"), defaultScript));
                }
            }

            // Script triggers
            foreach (Trigger trigger in freshTable.Triggers)
            {
                var triggerScript = ScriptTrigger(trigger);
                fileTasks.Add(fileManager.WriteFileAsync(Path.Combine(tablePath, $"{FileSystemManager.GetPrefixedFileName("TR_", trigger.Name)}.sql"), triggerScript));
            }

            // Wait for all basic table object files to complete
            await Task.WhenAll(fileTasks);

            // Script other indexes (handled separately as it has its own async method)
            var indexScripter = new IndexScripter(scriptingOptions, fileManager);
            await indexScripter.ScriptIndexesAsync(freshTable, tablePath);
        }
        finally
        {
            server.ConnectionContext.Disconnect();
        }
    }

    string ScriptTableDefinition(Table table)
    {
        // Configure options for table-only scripting
        var tableOptions = new ScriptingOptions(scriptingOptions)
        {
            DriPrimaryKey = false,
            DriForeignKeys = false,
            DriChecks = false,
            DriDefaults = false, // Exclude column defaults - script separately
            Indexes = false,
            Triggers = false,
            // Additional options to handle problematic tables
            EnforceScriptingOptions = true,
            ConvertUserDefinedDataTypesToBaseType = true,
            TargetServerVersion = SqlServerVersion.Version160, // SQL Server 2022
            AnsiPadding = false
        };

        const int maxRetries = 5;
        Exception? lastException = null;

        // Try up to 3 times with the standard options
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    Console.WriteLine($"    Retry attempt {attempt} for table {table.Name}");
                    // Small delay between retries
                    Thread.Sleep(1000 * attempt);
                }

                var scripts = table.Script(tableOptions);
                return string.Join(Environment.NewLine + "GO" + Environment.NewLine, scripts.Cast<string>());
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < maxRetries)
                {
                    Console.WriteLine($"    Attempt {attempt} failed for table {table.Name}: {ex.Message}");
                }
            }
        }

        // If all retries failed, try with minimal options
        Console.WriteLine($"    All retry attempts failed for table {table.Name}, trying minimal options");
        var minimalOptions = new ScriptingOptions
        {
            IncludeHeaders = false,
            ScriptDrops = false,
            ScriptData = false,
            ScriptSchema = true,
            IncludeIfNotExists = true,
            ConvertUserDefinedDataTypesToBaseType = true,
            EnforceScriptingOptions = true
        };

        try
        {
            var scripts = table.Script(minimalOptions);
            return string.Join(Environment.NewLine + "GO" + Environment.NewLine, scripts.Cast<string>());
        }
        catch (Exception exc)
        {
            // If all else fails, log detailed error and return a comment
            Console.WriteLine($"    Failed to script table {table.Name} even with minimal options");
            Console.WriteLine($"    Table properties: IsMemoryOptimized={table.IsMemoryOptimized}, IsSystemVersioned={table.IsSystemVersioned}");
            return $"-- ERROR: Could not script table {table.Name}\n-- Original error: {lastException?.Message}\n-- This may be due to special table features or permissions";
        }
    }

    string ScriptIndex(SmoIndex index, Table table)
    {
        var sb = new StringBuilder();

        // Add header
        sb.AppendLine($"-- Primary Key: {index.Name}");
        sb.AppendLine($"-- Table: {table.Schema}.{table.Name}");
        sb.AppendLine();

        // Drop if exists
        sb.AppendLine($"IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[{table.Schema}].[{table.Name}]') AND name = N'{index.Name}')");
        sb.AppendLine($"    ALTER TABLE [{table.Schema}].[{table.Name}] DROP CONSTRAINT [{index.Name}]");
        sb.AppendLine("GO");
        sb.AppendLine();

        // Script the index
        var indexOptions = new ScriptingOptions
        {
            IncludeHeaders = false,
            ScriptDrops = false
        };

        var scripts = index.Script(indexOptions);
        sb.AppendLine(string.Join(Environment.NewLine, scripts.Cast<string>()));
        sb.AppendLine("GO");

        return sb.ToString();
    }

    string ScriptForeignKey(ForeignKey fk, Table table)
    {
        var sb = new StringBuilder();

        // Add header
        sb.AppendLine($"-- Foreign Key: {fk.Name}");
        sb.AppendLine($"-- Table: {table.Schema}.{table.Name}");
        sb.AppendLine($"-- References: {fk.ReferencedTableSchema}.{fk.ReferencedTable}");
        sb.AppendLine();

        // Drop if exists
        sb.AppendLine($"IF EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[{table.Schema}].[{fk.Name}]') AND parent_object_id = OBJECT_ID(N'[{table.Schema}].[{table.Name}]'))");
        sb.AppendLine($"    ALTER TABLE [{table.Schema}].[{table.Name}] DROP CONSTRAINT [{fk.Name}]");
        sb.AppendLine("GO");
        sb.AppendLine();

        // Script the foreign key
        var fkOptions = new ScriptingOptions
        {
            IncludeHeaders = false,
            ScriptDrops = false
        };

        var scripts = fk.Script(fkOptions);
        sb.AppendLine(string.Join(Environment.NewLine, scripts.Cast<string>()));
        sb.AppendLine("GO");

        return sb.ToString();
    }

    string ScriptCheckConstraint(Check check, Table table)
    {
        var sb = new StringBuilder();

        // Add header
        sb.AppendLine($"-- Check Constraint: {check.Name}");
        sb.AppendLine($"-- Table: {table.Schema}.{table.Name}");
        sb.AppendLine();

        // Drop if exists
        sb.AppendLine(
            $"IF EXISTS (SELECT * FROM sys.check_constraints WHERE object_id = OBJECT_ID(N'[{table.Schema}].[{check.Name}]') AND parent_object_id = OBJECT_ID(N'[{table.Schema}].[{table.Name}]'))");
        sb.AppendLine($"    ALTER TABLE [{table.Schema}].[{table.Name}] DROP CONSTRAINT [{check.Name}]");
        sb.AppendLine("GO");
        sb.AppendLine();

        // Script the check constraint
        var checkOptions = new ScriptingOptions
        {
            IncludeHeaders = false,
            ScriptDrops = false
        };

        var scripts = check.Script(checkOptions);
        sb.AppendLine(string.Join(Environment.NewLine, scripts.Cast<string>()));
        sb.AppendLine("GO");

        return sb.ToString();
    }

    string ScriptTrigger(Trigger trigger)
    {
        var sb = new StringBuilder();

        // Add header
        sb.AppendLine($"-- Trigger: {trigger.Name}");
        sb.AppendLine();

        // Drop if exists
        var parentTable = trigger.Parent as Table;
        var schemaName = parentTable?.Schema ?? "dbo";
        sb.AppendLine($"IF EXISTS (SELECT * FROM sys.triggers WHERE object_id = OBJECT_ID(N'[{schemaName}].[{trigger.Name}]'))");
        sb.AppendLine($"    DROP TRIGGER [{schemaName}].[{trigger.Name}]");
        sb.AppendLine("GO");
        sb.AppendLine();

        // Script the trigger
        var triggerOptions = new ScriptingOptions
        {
            IncludeHeaders = false,
            ScriptDrops = false
        };

        var scripts = trigger.Script(triggerOptions);
        sb.AppendLine(string.Join(Environment.NewLine, scripts.Cast<string>()));
        sb.AppendLine("GO");

        return sb.ToString();
    }

    string ScriptDefaultConstraint(DefaultConstraint defaultConstraint, Table table)
    {
        var sb = new StringBuilder();

        // Add header
        sb.AppendLine($"-- Default Constraint: {defaultConstraint.Name}");
        sb.AppendLine($"-- Table: {table.Schema}.{table.Name}");
        sb.AppendLine($"-- Column: {defaultConstraint.Parent.Name}");
        sb.AppendLine();

        // Drop if exists
        sb.AppendLine($"IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[{table.Schema}].[{defaultConstraint.Name}]') AND type = 'D')");
        sb.AppendLine("BEGIN");
        sb.AppendLine($"    ALTER TABLE [{table.Schema}].[{table.Name}] DROP CONSTRAINT [{defaultConstraint.Name}]");
        sb.AppendLine("END");
        sb.AppendLine("GO");
        sb.AppendLine();

        // Script the default constraint
        var defaultOptions = new ScriptingOptions
        {
            IncludeHeaders = false,
            ScriptDrops = false
        };

        var scripts = defaultConstraint.Script(defaultOptions);
        sb.AppendLine(string.Join(Environment.NewLine, scripts.Cast<string>()));
        sb.AppendLine("GO");

        return sb.ToString();
    }


}