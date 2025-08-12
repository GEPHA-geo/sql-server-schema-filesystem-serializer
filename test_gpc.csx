#r "nuget: Microsoft.Data.SqlClient, 5.2.0"

using Microsoft.Data.SqlClient;

var conn = new SqlConnection(@"Server=pharm-n1.pharm.local;Database=GPC;User Id=sa_abc_ro;Password=v4dHkT1#tOH%Y4zA;TrustServerCertificate=true");
try {
    conn.Open();
    var cmd = new SqlCommand("SELECT COUNT(*) FROM sys.tables", conn);
    var count = cmd.ExecuteScalar();
    Console.WriteLine($"Connected to GPC database. Found {count} tables.");
    conn.Close();
} catch (Exception ex) {
    Console.WriteLine($"Error: {ex.Message}");
}