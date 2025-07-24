using Microsoft.Data.SqlClient;
using System.Data;

namespace SqlServerStructureGenerator;

// Efficiently counts objects in schemas without expensive SMO enumeration
public static class SchemaObjectCounter
{
    public static async Task<Dictionary<string, int>> GetObjectCountsAsync(string connectionString, string schemaName)
    {
        var counts = new Dictionary<string, int>
        {
            ["Tables"] = 0,
            ["Views"] = 0,
            ["Procedures"] = 0,
            ["Functions"] = 0
        };

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        // Count tables
        using (var cmd = new SqlCommand(@"
            SELECT COUNT(*) 
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @schema AND t.is_ms_shipped = 0", connection))
        {
            cmd.Parameters.AddWithValue("@schema", schemaName);
            counts["Tables"] = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        // Count views
        using (var cmd = new SqlCommand(@"
            SELECT COUNT(*) 
            FROM sys.views v
            INNER JOIN sys.schemas s ON v.schema_id = s.schema_id
            WHERE s.name = @schema AND v.is_ms_shipped = 0", connection))
        {
            cmd.Parameters.AddWithValue("@schema", schemaName);
            counts["Views"] = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        // Count stored procedures
        using (var cmd = new SqlCommand(@"
            SELECT COUNT(*) 
            FROM sys.procedures p
            INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
            WHERE s.name = @schema AND p.is_ms_shipped = 0", connection))
        {
            cmd.Parameters.AddWithValue("@schema", schemaName);
            counts["Procedures"] = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        // Count functions
        using (var cmd = new SqlCommand(@"
            SELECT COUNT(*) 
            FROM sys.objects o
            INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
            WHERE s.name = @schema 
            AND o.type IN ('FN', 'IF', 'TF', 'FS', 'FT')
            AND o.is_ms_shipped = 0", connection))
        {
            cmd.Parameters.AddWithValue("@schema", schemaName);
            counts["Functions"] = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        return counts;
    }
}