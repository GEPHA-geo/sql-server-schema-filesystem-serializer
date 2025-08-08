using SqlServer.Schema.Exclusion.Manager.Models;
using SqlServer.Schema.Exclusion.Manager.Services;
using Xunit;

namespace SqlServer.Schema.Exclusion.Manager.Tests;

public class ManifestFileHandlerTests : IDisposable
{
    readonly string _testDirectory;
    readonly ManifestFileHandler _handler;

    public ManifestFileHandlerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ManifestTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _handler = new ManifestFileHandler();
    }

    public void Dispose() => Directory.Delete(_testDirectory, true);

    [Fact]
    public async Task WriteManifestAsync_CreatesFileWithCorrectFormat()
    {
        // Arrange
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer",
            Generated = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            CommitHash = "abc123",
            RotationMarker = '/'
        };
        
        manifest.IncludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.Users",
            Description = "Table added"
        });
        
        manifest.ExcludedChanges.Add(new ManifestChange
        {
            Identifier = "dbo.Logs",
            Description = "Table removed"
        });

        var filePath = Path.Combine(_testDirectory, "test.manifest");

        // Act
        await _handler.WriteManifestAsync(filePath, manifest);

        // Assert
        Assert.True(File.Exists(filePath));
        var content = await File.ReadAllTextAsync(filePath);
        
        Assert.Contains("DATABASE: TestDB /", content);
        Assert.Contains("SERVER: TestServer /", content);
        Assert.Contains("GENERATED: 2024-01-15T10:30:00Z /", content);
        Assert.Contains("COMMIT: abc123 /", content);
        Assert.Contains("=== INCLUDED CHANGES ===", content);
        Assert.Contains("dbo.Users - Table added /", content);
        Assert.Contains("=== EXCLUDED CHANGES ===", content);
        Assert.Contains("dbo.Logs - Table removed /", content);
    }

    [Fact]
    public async Task ReadManifestAsync_ParsesFileCorrectly()
    {
        // Arrange
        var content = @"DATABASE: TestDB /
SERVER: TestServer /
GENERATED: 2024-01-15T10:30:00Z /
COMMIT: abc123 /

=== INCLUDED CHANGES ===
dbo.Users - Table added /
dbo.Products - Index created /

=== EXCLUDED CHANGES ===
dbo.Logs - Table removed /
";
        var filePath = Path.Combine(_testDirectory, "test.manifest");
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var manifest = await _handler.ReadManifestAsync(filePath);

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal("TestDB", manifest.DatabaseName);
        Assert.Equal("TestServer", manifest.ServerName);
        Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc), manifest.Generated);
        Assert.Equal("abc123", manifest.CommitHash);
        Assert.Equal('/', manifest.RotationMarker);
        
        Assert.Equal(2, manifest.IncludedChanges.Count);
        Assert.Equal("dbo.Users", manifest.IncludedChanges[0].Identifier);
        Assert.Equal("Table added", manifest.IncludedChanges[0].Description);
        
        Assert.Single(manifest.ExcludedChanges);
        Assert.Equal("dbo.Logs", manifest.ExcludedChanges[0].Identifier);
        Assert.Equal("Table removed", manifest.ExcludedChanges[0].Description);
    }

    [Fact]
    public async Task ReadManifestAsync_HandlesBackslashRotationMarker()
    {
        // Arrange
        var content = @"DATABASE: TestDB \
SERVER: TestServer \
GENERATED: 2024-01-15T10:30:00Z \
COMMIT: xyz789 \

=== INCLUDED CHANGES ===
dbo.Orders - View modified \

=== EXCLUDED CHANGES ===
";
        var filePath = Path.Combine(_testDirectory, "test.manifest");
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var manifest = await _handler.ReadManifestAsync(filePath);

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal('\\', manifest.RotationMarker);
        Assert.Single(manifest.IncludedChanges);
        Assert.Empty(manifest.ExcludedChanges);
    }

    [Fact]
    public async Task ReadManifestAsync_ReturnsNullForNonExistentFile()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "nonexistent.manifest");

        // Act
        var manifest = await _handler.ReadManifestAsync(filePath);

        // Assert
        Assert.Null(manifest);
    }

    [Fact]
    public void FlipRotationMarker_CorrectlyFlipsMarkers()
    {
        // Act & Assert
        Assert.Equal('\\', _handler.FlipRotationMarker('/'));
        Assert.Equal('/', _handler.FlipRotationMarker('\\'));
    }

    [Fact]
    public async Task WriteAndReadManifest_RoundTrip()
    {
        // Arrange
        var original = new ChangeManifest
        {
            DatabaseName = "RoundTripDB",
            ServerName = "RoundTripServer",
            Generated = DateTime.UtcNow,
            CommitHash = "roundtrip123",
            RotationMarker = '\\'
        };
        
        original.IncludedChanges.Add(new ManifestChange
        {
            Identifier = "schema.Table1",
            Description = "Column added"
        });
        
        original.ExcludedChanges.Add(new ManifestChange
        {
            Identifier = "schema.Table2",
            Description = "Constraint modified"
        });

        var filePath = Path.Combine(_testDirectory, "roundtrip.manifest");

        // Act
        await _handler.WriteManifestAsync(filePath, original);
        var read = await _handler.ReadManifestAsync(filePath);

        // Assert
        Assert.NotNull(read);
        Assert.Equal(original.DatabaseName, read.DatabaseName);
        Assert.Equal(original.ServerName, read.ServerName);
        Assert.Equal(original.CommitHash, read.CommitHash);
        Assert.Equal(original.RotationMarker, read.RotationMarker);
        Assert.Equal(original.IncludedChanges.Count, read.IncludedChanges.Count);
        Assert.Equal(original.ExcludedChanges.Count, read.ExcludedChanges.Count);
    }
}