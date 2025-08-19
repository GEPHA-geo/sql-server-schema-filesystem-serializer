using System.Data;
using Microsoft.Data.SqlClient;

namespace SqlServer.Schema.Migration.Runner.Core;

public class DatabaseConnection(string connectionString)
{
    public async Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(sql, connection);
        AddParameters(command, parameters);

        var result = await command.ExecuteScalarAsync();
        return result == DBNull.Value || result == null ? default : (T)result;
    }

    public async Task<int> ExecuteNonQueryAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(sql, connection);
        AddParameters(command, parameters);

        return await command.ExecuteNonQueryAsync();
    }

    public async Task<List<T>> QueryAsync<T>(string sql, object? parameters = null) where T : new()
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(sql, connection);
        AddParameters(command, parameters);

        await using var reader = await command.ExecuteReaderAsync();
        return MapResults<T>(reader);
    }

    public async Task ExecuteMigrationAsync(string migrationScript)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        // Split by GO statements
        var batches = SplitByGo(migrationScript);

        foreach (var batch in batches.Where(batch => !string.IsNullOrWhiteSpace(batch)))
        {
            await using var command = new SqlCommand(batch, connection);
            command.CommandTimeout = 300; // 5 minutes timeout for migrations
            await command.ExecuteNonQueryAsync();
        }
    }

    static List<string> SplitByGo(string script)
    {
        var batches = new List<string>();
        var lines = script.Split('\n');
        var currentBatch = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (currentBatch.Length <= 0) continue;
                batches.Add(currentBatch.ToString());
                currentBatch.Clear();
            }
            else
            {
                currentBatch.AppendLine(line);
            }
        }

        if (currentBatch.Length > 0)
        {
            batches.Add(currentBatch.ToString());
        }

        return batches;
    }

    static void AddParameters(SqlCommand command, object? parameters)
    {
        if (parameters == null) return;

        var properties = parameters.GetType().GetProperties();
        foreach (var property in properties)
        {
            var value = property.GetValue(parameters) ?? DBNull.Value;
            command.Parameters.AddWithValue($"@{property.Name}", value);
        }
    }

    static List<T> MapResults<T>(SqlDataReader reader) where T : new()
    {
        var results = new List<T>();
        var properties = typeof(T).GetProperties();

        while (reader.Read())
        {
            var item = new T();

            foreach (var property in properties)
            {
                if (reader.GetOrdinal(property.Name) >= 0)
                {
                    var value = reader[property.Name];
                    if (value != DBNull.Value)
                    {
                        property.SetValue(item, value);
                    }
                }
            }

            results.Add(item);
        }

        return results;
    }
}