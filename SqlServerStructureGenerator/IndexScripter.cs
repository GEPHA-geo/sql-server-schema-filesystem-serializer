using Microsoft.SqlServer.Management.Smo;
using System.Text;
using SmoIndex = Microsoft.SqlServer.Management.Smo.Index;

namespace SqlServerStructureGenerator;

// Handles scripting of non-primary key indexes
public class IndexScripter(ScriptingOptions scriptingOptions, FileSystemManager fileManager)
{
    readonly ScriptingOptions _scriptingOptions = scriptingOptions;

    public async Task ScriptIndexesAsync(Table table, string tablePath)
    {
        // Script all non-primary key indexes
        var indexes = table.Indexes.Cast<SmoIndex>()
            .Where(i => i.IndexKeyType != IndexKeyType.DriPrimaryKey)
            .OrderBy(i => i.Name)
            .ToList();

        var indexTasks = indexes.Select(index =>
        {
            var indexScript = ScriptIndex(index, table);
            return fileManager.WriteFileAsync(
                Path.Combine(tablePath, $"{FileSystemManager.GetPrefixedFileName("IDX_", index.Name)}.sql"), 
                indexScript);
        });

        await Task.WhenAll(indexTasks);
    }

    string ScriptIndex(SmoIndex index, Table table)
    {
        var sb = new StringBuilder();
        
        // Add header with index details
        sb.AppendLine($"-- Index: {index.Name}");
        sb.AppendLine($"-- Table: {table.Schema}.{table.Name}");
        sb.AppendLine($"-- Type: {GetIndexTypeDescription(index)}");
        
        if (index.IndexedColumns.Count > 0)
        {
            var columns = string.Join(", ", index.IndexedColumns.Cast<IndexedColumn>()
                .Select(c => $"{c.Name} {(c.Descending ? "DESC" : "ASC")}"));
            sb.AppendLine($"-- Columns: {columns}");
        }
        
        // Note: SMO doesn't expose included columns directly through the Index object
        
        sb.AppendLine();
        
        // Drop if exists
        sb.AppendLine($"IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[{table.Schema}].[{table.Name}]') AND name = N'{index.Name}')");
        sb.AppendLine($"    DROP INDEX [{index.Name}] ON [{table.Schema}].[{table.Name}]");
        sb.AppendLine("GO");
        sb.AppendLine();
        
        // Script the index
        var indexOptions = new ScriptingOptions
        {
            IncludeHeaders = false,
            ScriptDrops = false,
            Indexes = true
        };
        
        var scripts = index.Script(indexOptions);
        sb.AppendLine(string.Join(Environment.NewLine, scripts.Cast<string>()));
        sb.AppendLine("GO");
        
        return sb.ToString();
    }

    string GetIndexTypeDescription(SmoIndex index)
    {
        var types = new List<string>();
        
        if (index.IndexType == IndexType.ClusteredIndex)
            types.Add("Clustered");
        else if (index.IndexType == IndexType.NonClusteredIndex)
            types.Add("Non-Clustered");
            
        if (index.IsUnique)
            types.Add("Unique");
            
        if (index.IndexKeyType == IndexKeyType.DriUniqueKey)
            types.Add("Unique Constraint");
            
        try
        {
            if (index.IsFullTextKey)
                types.Add("Full-Text");
        }
        catch { /* Some indexes don't support IsFullTextKey */ }
            
        try
        {
            if (index.IsSpatialIndex)
                types.Add("Spatial");
        }
        catch { /* Some indexes don't support IsSpatialIndex */ }
            
        try
        {
            if (index.IsMemoryOptimized)
                types.Add("Memory-Optimized");
        }
        catch { /* Some indexes don't support IsMemoryOptimized */ }
            
        return string.Join(", ", types);
    }
}