using SqlServer.Schema.Exclusion.Manager.Core.Models;
using Xunit;

namespace SqlServer.Schema.Exclusion.Manager.Tests;

public class ModelsTests
{
    [Fact]
    public void ChangeManifest_HasCorrectDefaults()
    {
        // Act
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer"
        };

        // Assert
        Assert.Equal("TestDB", manifest.DatabaseName);
        Assert.Equal("TestServer", manifest.ServerName);
        Assert.Equal('/', manifest.RotationMarker);
        Assert.Empty(manifest.CommitHash);
        Assert.NotNull(manifest.IncludedChanges);
        Assert.NotNull(manifest.ExcludedChanges);
        Assert.Empty(manifest.IncludedChanges);
        Assert.Empty(manifest.ExcludedChanges);
    }

    [Fact]
    public void ChangeManifest_GetManifestFileName_GeneratesCorrectName()
    {
        // Arrange
        var manifest = new ChangeManifest
        {
            DatabaseName = "MyDatabase",
            ServerName = "MyServer"
        };

        // Act
        var fileName = manifest.GetManifestFileName();

        // Assert
        Assert.Equal("change-manifest-MyServer-MyDatabase.manifest", fileName);
    }

    [Fact]
    public void ChangeManifest_RequiredPropertiesCanBeSet()
    {
        // Act
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer",
            Generated = new DateTime(2024, 1, 15),
            CommitHash = "abc123",
            RotationMarker = '\\'
        };

        // Assert
        Assert.Equal("TestDB", manifest.DatabaseName);
        Assert.Equal("TestServer", manifest.ServerName);
        Assert.Equal(new DateTime(2024, 1, 15), manifest.Generated);
        Assert.Equal("abc123", manifest.CommitHash);
        Assert.Equal('\\', manifest.RotationMarker);
    }

    [Fact]
    public void ChangeManifest_CanAddChangesToCollections()
    {
        // Arrange
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer"
        };

        var includedChange = new ManifestChange
        {
            Identifier = "dbo.Table1",
            Description = "Table added"
        };

        var excludedChange = new ManifestChange
        {
            Identifier = "dbo.Table2",
            Description = "Table excluded"
        };

        // Act
        manifest.IncludedChanges.Add(includedChange);
        manifest.ExcludedChanges.Add(excludedChange);

        // Assert
        Assert.Single(manifest.IncludedChanges);
        Assert.Single(manifest.ExcludedChanges);
        Assert.Equal("dbo.Table1", manifest.IncludedChanges[0].Identifier);
        Assert.Equal("dbo.Table2", manifest.ExcludedChanges[0].Identifier);
    }

    [Fact]
    public void ManifestChange_HasCorrectDefaults()
    {
        // Act
        var change = new ManifestChange
        {
            Identifier = "dbo.TestTable",
            Description = "Test description"
        };

        // Assert
        Assert.Equal("dbo.TestTable", change.Identifier);
        Assert.Equal("Test description", change.Description);
        Assert.Empty(change.ObjectType);
        Assert.Empty(change.FilePath);
        Assert.Null(change.OldValue);
        Assert.Null(change.NewValue);
    }

    [Fact]
    public void ManifestChange_ToString_ReturnsCorrectFormat()
    {
        // Arrange
        var change = new ManifestChange
        {
            Identifier = "dbo.Users",
            Description = "Table modified"
        };

        // Act
        var result = change.ToString();

        // Assert
        Assert.Equal("dbo.Users - Table modified", result);
    }

    [Fact]
    public void ManifestChange_AllPropertiesCanBeSet()
    {
        // Act
        var change = new ManifestChange
        {
            Identifier = "dbo.TestProc",
            Description = "Stored procedure updated",
            ObjectType = "StoredProcedure",
            FilePath = "StoredProcedures/dbo.TestProc.sql",
            OldValue = "OLD_DEFINITION",
            NewValue = "NEW_DEFINITION"
        };

        // Assert
        Assert.Equal("dbo.TestProc", change.Identifier);
        Assert.Equal("Stored procedure updated", change.Description);
        Assert.Equal("StoredProcedure", change.ObjectType);
        Assert.Equal("StoredProcedures/dbo.TestProc.sql", change.FilePath);
        Assert.Equal("OLD_DEFINITION", change.OldValue);
        Assert.Equal("NEW_DEFINITION", change.NewValue);
    }

    [Fact]
    public void ChangeManifest_CollectionsAreNotNull()
    {
        // Arrange & Act
        var manifest = new ChangeManifest
        {
            DatabaseName = "TestDB",
            ServerName = "TestServer"
        };

        // Assert
        Assert.NotNull(manifest.IncludedChanges);
        Assert.NotNull(manifest.ExcludedChanges);
        Assert.IsType<List<ManifestChange>>(manifest.IncludedChanges);
        Assert.IsType<List<ManifestChange>>(manifest.ExcludedChanges);
    }

    [Fact]
    public void ChangeManifest_GetManifestFileName_HandlesSpecialCharacters()
    {
        // Arrange
        var manifest = new ChangeManifest
        {
            DatabaseName = "My-Database",
            ServerName = "Server.Name"
        };

        // Act
        var fileName = manifest.GetManifestFileName();

        // Assert
        Assert.Equal("change-manifest-Server.Name-My-Database.manifest", fileName);
    }
}