using System;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace SqlServerStructureGenerator;

public static class ConnectionFactory
{
    public static (Server server, Database database) CreateConnection(string connectionString)
    {
        // Add timeout to connection string if not present
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (builder.ConnectTimeout < 60)
        {
            builder.ConnectTimeout = 60;
        }
        // Set command timeout to 30 seconds
        builder.CommandTimeout = 30;
        
        // Enable MARS to allow multiple active result sets
        builder.MultipleActiveResultSets = true;
        
        var sqlConnection = new SqlConnection(builder.ConnectionString);
        var serverConnection = new ServerConnection(sqlConnection)
        {
            // Configure timeout settings
            StatementTimeout = 30 // 30 seconds for SQL statement execution
        };

        var server = new Server(serverConnection)
        {
            ConnectionContext =
            {
                // Set server-level timeout properties
                StatementTimeout = 30, // 30 seconds
                LockTimeout = 10000 // 10 seconds for lock timeout
            }
        };

        server.SetDefaultInitFields(typeof(Table), true); // Load all fields to avoid multiple round trips
        server.SetDefaultInitFields(typeof(View), true);
        server.SetDefaultInitFields(typeof(StoredProcedure), true);
        server.SetDefaultInitFields(typeof(UserDefinedFunction), true);
        
        var databaseName = builder.InitialCatalog;
        
        if (string.IsNullOrEmpty(databaseName))
            throw new ArgumentException("Database name not found in connection string");
            
        var database = server.Databases[databaseName];
        if (database == null)
            throw new InvalidOperationException($"Database '{databaseName}' not found");
            
        return (server, database);
    }
}