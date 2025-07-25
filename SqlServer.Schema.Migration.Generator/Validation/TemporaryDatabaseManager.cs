using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlServer.Schema.Migration.Generator.Validation;

public class TemporaryDatabaseManager
{
    readonly string _masterConnectionString;
    
    public TemporaryDatabaseManager(string baseConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "master"
        };
        _masterConnectionString = builder.ToString();
    }
    
    public async Task<string> CreateTemporaryDatabaseAsync(string baseDatabaseName)
    {
        var tempDbName = $"{baseDatabaseName}_MigrationTest_{DateTime.UtcNow:yyyyMMddHHmmss}";
        
        using var connection = new SqlConnection(_masterConnectionString);
        await connection.OpenAsync();
        
        // Create the temporary database
        var createDbSql = $@"
            IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = '{tempDbName}')
            BEGIN
                CREATE DATABASE [{tempDbName}];
            END";
            
        using var command = new SqlCommand(createDbSql, connection);
        await command.ExecuteNonQueryAsync();
        
        Console.WriteLine($"Created temporary database: {tempDbName}");
        return tempDbName;
    }
    
    public async Task DropDatabaseAsync(string databaseName)
    {
        try
        {
            using var connection = new SqlConnection(_masterConnectionString);
            await connection.OpenAsync();
            
            // Kill all connections to the database
            var killConnectionsSql = $@"
                DECLARE @sql NVARCHAR(MAX) = '';
                SELECT @sql = @sql + 'KILL ' + CAST(session_id AS NVARCHAR(10)) + ';'
                FROM sys.dm_exec_sessions
                WHERE database_id = DB_ID('{databaseName}');
                EXEC sp_executesql @sql;";
                
            using (var killCmd = new SqlCommand(killConnectionsSql, connection))
            {
                try
                {
                    await killCmd.ExecuteNonQueryAsync();
                }
                catch
                {
                    // Ignore errors from killing connections
                }
            }
            
            // Set database to single user mode and drop it
            var dropDbSql = $@"
                IF EXISTS (SELECT name FROM sys.databases WHERE name = '{databaseName}')
                BEGIN
                    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{databaseName}];
                END";
                
            using var dropCommand = new SqlCommand(dropDbSql, connection);
            await dropCommand.ExecuteNonQueryAsync();
            
            Console.WriteLine($"Dropped temporary database: {databaseName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to drop temporary database {databaseName}: {ex.Message}");
        }
    }
    
    public string GetConnectionString(string baseConnectionString, string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = databaseName
        };
        return builder.ToString();
    }
}