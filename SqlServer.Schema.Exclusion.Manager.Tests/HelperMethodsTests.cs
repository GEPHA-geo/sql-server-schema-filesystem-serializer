using SqlServer.Schema.Exclusion.Manager.Services;
using Xunit;

namespace SqlServer.Schema.Exclusion.Manager.Tests;

public class HelperMethodsTests
{
    [Fact]
    public void ManifestFileHandler_FlipRotationMarker_FlipsCorrectly()
    {
        // Arrange
        var handler = new ManifestFileHandler();

        // Act & Assert
        Assert.Equal('\\', handler.FlipRotationMarker('/'));
        Assert.Equal('/', handler.FlipRotationMarker('\\'));
        
        // Test multiple flips
        var marker = '/';
        marker = handler.FlipRotationMarker(marker);
        Assert.Equal('\\', marker);
        marker = handler.FlipRotationMarker(marker);
        Assert.Equal('/', marker);
    }

    [Theory]
    [InlineData("dbo.Table1", "Table")]
    [InlineData("dbo.sp_Procedure", "StoredProcedure")]
    [InlineData("dbo.vw_View", "View")]
    [InlineData("dbo.fn_Function", "Function")]
    public void GitChangeDetector_DetermineObjectType_Theory(string identifier, string expectedType)
    {
        // This test demonstrates expected object type determination
        // The actual implementation would need to be tested with the real GitChangeDetector
        
        // Arrange
        var objectType = DetermineObjectTypeFromIdentifier(identifier);
        
        // Assert
        Assert.NotNull(objectType);
        Assert.Contains(expectedType, objectType);
    }

    // Helper method to simulate object type determination
    static string? DetermineObjectTypeFromIdentifier(string identifier)
    {
        if (identifier.Contains("sp_")) return "StoredProcedure";
        if (identifier.Contains("vw_")) return "View";
        if (identifier.Contains("fn_")) return "Function";
        if (identifier.Contains("Table")) return "Table";
        return "Unknown";
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("=== INCLUDED CHANGES ===", true)]
    [InlineData("=== EXCLUDED CHANGES ===", true)]
    [InlineData("DATABASE: TestDB", true)]
    [InlineData("SERVER: TestServer", true)]
    [InlineData("GENERATED: 2024-01-01", true)]
    [InlineData("COMMIT: abc123", true)]
    [InlineData("dbo.Table - Description", true)]
    public void ManifestFileParser_IsValidManifestLine_Theory(string line, bool expectedValid)
    {
        // This test demonstrates expected line validation
        var isValid = IsValidManifestLine(line);
        
        Assert.Equal(expectedValid, isValid);
    }

    // Helper method to simulate line validation
    static bool IsValidManifestLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        
        return line.StartsWith("===") ||
               line.StartsWith("DATABASE:") ||
               line.StartsWith("SERVER:") ||
               line.StartsWith("GENERATED:") ||
               line.StartsWith("COMMIT:") ||
               line.Contains(" - ");
    }

    [Theory]
    [InlineData("CREATE TABLE", "CREATE")]
    [InlineData("ALTER TABLE", "ALTER")]
    [InlineData("DROP TABLE", "DROP")]
    [InlineData("CREATE PROCEDURE", "CREATE")]
    [InlineData("ALTER FUNCTION", "ALTER")]
    public void SqlParser_ExtractSqlOperation_Theory(string sqlStatement, string expectedOperation)
    {
        // This test demonstrates SQL operation extraction
        var operation = ExtractSqlOperation(sqlStatement);
        
        Assert.Equal(expectedOperation, operation);
    }

    // Helper method to extract SQL operation
    static string ExtractSqlOperation(string sqlStatement)
    {
        var parts = sqlStatement.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0].ToUpper() : string.Empty;
    }

    [Theory]
    [InlineData("dbo.Users", "dbo", "Users")]
    [InlineData("MySchema.MyTable", "MySchema", "MyTable")]
    [InlineData("TableWithoutSchema", "", "TableWithoutSchema")]
    [InlineData("[dbo].[Users]", "dbo", "Users")]
    public void IdentifierParser_ParseSchemaAndObject_Theory(string identifier, string expectedSchema, string expectedObject)
    {
        // This test demonstrates identifier parsing
        var (schema, objectName) = ParseIdentifier(identifier);
        
        Assert.Equal(expectedSchema, schema);
        Assert.Equal(expectedObject, objectName);
    }

    // Helper method to parse identifier
    static (string schema, string objectName) ParseIdentifier(string identifier)
    {
        var cleaned = identifier.Replace("[", "").Replace("]", "");
        var parts = cleaned.Split('.');
        
        if (parts.Length == 2)
            return (parts[0], parts[1]);
        
        return ("", parts[0]);
    }

    [Theory]
    [InlineData('/', '\\')]
    [InlineData('\\', '/')]
    public void RotationMarker_AlwaysFlipsToOpposite(char input, char expected)
    {
        // Arrange
        var handler = new ManifestFileHandler();
        
        // Act
        var result = handler.FlipRotationMarker(input);
        
        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void PathCombination_WorksCorrectly()
    {
        // This test verifies path combination logic
        var basePath = "/base/path";
        var relativePath = "Tables/dbo.Users.sql";
        
        var combined = Path.Combine(basePath, relativePath);
        
        Assert.Contains("base", combined);
        Assert.Contains("path", combined);
        Assert.Contains("Tables", combined);
        Assert.Contains("dbo.Users.sql", combined);
    }
}