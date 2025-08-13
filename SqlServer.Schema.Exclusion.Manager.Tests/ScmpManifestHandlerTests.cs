using Xunit;
using SqlServer.Schema.Exclusion.Manager.Core.Models;
using SqlServer.Schema.Exclusion.Manager.Core.Services;

namespace SqlServer.Schema.Exclusion.Manager.Tests;

public class ScmpManifestHandlerTests : IDisposable
{
    readonly string _testDirectory;
    readonly ScmpManifestHandler _handler;

    public ScmpManifestHandlerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ScmpTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _handler = new ScmpManifestHandler();
    }

    public void Dispose() => Directory.Delete(_testDirectory, recursive: true);

    [Fact]
    public async Task SaveAndLoadManifest_RoundTrip_PreservesAllData()
    {
        // Arrange
        var comparison = CreateTestSchemaComparison();
        var filePath = Path.Combine(_testDirectory, "test.scmp.xml");

        // Act
        await _handler.SaveManifestAsync(comparison, filePath);
        var loaded = await _handler.LoadManifestAsync(filePath);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(comparison.Version, loaded.Version);
        Assert.Equal(
            comparison.SourceModelProvider?.ConnectionBasedModelProvider?.ConnectionString,
            loaded.SourceModelProvider?.ConnectionBasedModelProvider?.ConnectionString
        );
        Assert.Equal(
            comparison.TargetModelProvider?.ConnectionBasedModelProvider?.ConnectionString,
            loaded.TargetModelProvider?.ConnectionBasedModelProvider?.ConnectionString
        );
    }

    [Fact]
    public async Task LoadManifest_FileDoesNotExist_ReturnsNull()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "does_not_exist.scmp.xml");

        // Act
        var result = await _handler.LoadManifestAsync(nonExistentPath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetManifestFileName_SanitizesServerName()
    {
        // Arrange & Act
        var result1 = _handler.GetManifestFileName("server.domain.com,1433", "TestDB");
        var result2 = _handler.GetManifestFileName("server\\instance", "TestDB");
        var result3 = _handler.GetManifestFileName("192.168.1.1:1433", "TestDB");

        // Assert
        Assert.Equal("server_domain_com_1433_TestDB.scmp.xml", result1);
        Assert.Equal("server_instance_TestDB.scmp.xml", result2);
        Assert.Equal("192_168_1_1_1433_TestDB.scmp.xml", result3);
    }

    [Fact]
    public void GetDatabaseInfo_ExtractsFromConnectionString()
    {
        // Arrange
        var comparison = CreateTestSchemaComparison();

        // Act
        var (sourceDb, targetDb) = _handler.GetDatabaseInfo(comparison);

        // Assert
        Assert.Equal("SourceDB", sourceDb);
        Assert.Equal("TargetDB", targetDb);
    }

    [Fact]
    public void GetServerInfo_ExtractsFromConnectionString()
    {
        // Arrange
        var comparison = CreateTestSchemaComparison();

        // Act
        var (sourceServer, targetServer) = _handler.GetServerInfo(comparison);

        // Assert
        Assert.Equal("SourceServer", sourceServer);
        Assert.Equal("TargetServer", targetServer);
    }

    [Fact]
    public void GetExcludedObjects_ReturnsAllExclusions()
    {
        // Arrange
        var comparison = CreateTestSchemaComparison();
        comparison.ExcludedSourceElements = new ExcludedElements
        {
            SelectedItems = new List<SelectedItem>
            {
                new() { Name = "dbo.Table1", Type = "SqlTable" },
                new() { Name = "dbo.View1", Type = "SqlView" }
            }
        };
        comparison.ExcludedTargetElements = new ExcludedElements
        {
            SelectedItems = new List<SelectedItem>
            {
                new() { Name = "dbo.Proc1", Type = "SqlProcedure" }
            }
        };

        // Act
        var excluded = _handler.GetExcludedObjects(comparison);

        // Assert
        Assert.Equal(3, excluded.Count);
        Assert.Contains("dbo.Table1 (Source)", excluded);
        Assert.Contains("dbo.View1 (Source)", excluded);
        Assert.Contains("dbo.Proc1 (Target)", excluded);
    }

    [Fact]
    public void GetConfigurationOption_ReturnsCorrectValue()
    {
        // Arrange
        var comparison = CreateTestSchemaComparison();
        comparison.SchemaCompareSettingsService = new SchemaCompareSettingsService
        {
            ConfigurationOptionsElement = new ConfigurationOptionsElement
            {
                PropertyElements = new List<PropertyElement>
                {
                    new() { Name = "DropObjectsNotInSource", Value = "True" },
                    new() { Name = "BlockOnPossibleDataLoss", Value = "False" }
                }
            }
        };

        // Act
        var dropObjects = _handler.GetConfigurationOption(comparison, "DropObjectsNotInSource");
        var blockDataLoss = _handler.GetConfigurationOption(comparison, "BlockOnPossibleDataLoss");
        var nonExistent = _handler.GetConfigurationOption(comparison, "NonExistentOption");

        // Assert
        Assert.Equal("True", dropObjects);
        Assert.Equal("False", blockDataLoss);
        Assert.Null(nonExistent);
    }

    [Fact]
    public void SetConfigurationOption_UpdatesExistingOption()
    {
        // Arrange
        var comparison = CreateTestSchemaComparison();
        comparison.SchemaCompareSettingsService = new SchemaCompareSettingsService
        {
            ConfigurationOptionsElement = new ConfigurationOptionsElement
            {
                PropertyElements = new List<PropertyElement>
                {
                    new() { Name = "ExistingOption", Value = "OldValue" }
                }
            }
        };

        // Act
        _handler.SetConfigurationOption(comparison, "ExistingOption", "NewValue");

        // Assert
        var value = _handler.GetConfigurationOption(comparison, "ExistingOption");
        Assert.Equal("NewValue", value);
    }

    [Fact]
    public void SetConfigurationOption_AddsNewOption()
    {
        // Arrange
        var comparison = CreateTestSchemaComparison();

        // Act
        _handler.SetConfigurationOption(comparison, "NewOption", "NewValue");

        // Assert
        var value = _handler.GetConfigurationOption(comparison, "NewOption");
        Assert.Equal("NewValue", value);
    }

    [Fact]
    public async Task LoadManifest_ValidXml_ParsesCorrectly()
    {
        // Arrange
        var xmlContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SchemaComparison>
  <Version>10</Version>
  <SourceModelProvider>
    <ConnectionBasedModelProvider>
      <ConnectionString>Data Source=TestServer;Initial Catalog=TestDB;Integrated Security=True</ConnectionString>
    </ConnectionBasedModelProvider>
  </SourceModelProvider>
  <TargetModelProvider>
    <FileBasedModelProvider>
      <FilePath>C:\Test\Database.dacpac</FilePath>
    </FileBasedModelProvider>
  </TargetModelProvider>
  <SchemaCompareSettingsService>
    <ConfigurationOptionsElement>
      <PropertyElementName>
        <Name>IgnorePermissions</Name>
        <Value>True</Value>
      </PropertyElementName>
    </ConfigurationOptionsElement>
  </SchemaCompareSettingsService>
  <ExcludedSourceElements>
    <SelectedItem Type=""Microsoft.Data.Tools.Schema.Sql.SchemaModel.SqlTable"">
      <Name>dbo.ExcludedTable</Name>
    </SelectedItem>
  </ExcludedSourceElements>
</SchemaComparison>";
        
        var filePath = Path.Combine(_testDirectory, "valid.scmp.xml");
        await File.WriteAllTextAsync(filePath, xmlContent);

        // Act
        var loaded = await _handler.LoadManifestAsync(filePath);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("10", loaded.Version);
        Assert.NotNull(loaded.SourceModelProvider?.ConnectionBasedModelProvider);
        Assert.Contains("TestServer", loaded.SourceModelProvider.ConnectionBasedModelProvider.ConnectionString);
        Assert.NotNull(loaded.TargetModelProvider?.FileBasedModelProvider);
        Assert.Equal(@"C:\Test\Database.dacpac", loaded.TargetModelProvider.FileBasedModelProvider.FilePath);
        Assert.NotNull(loaded.ExcludedSourceElements);
        Assert.Single(loaded.ExcludedSourceElements.SelectedItems);
        Assert.Equal("dbo.ExcludedTable", loaded.ExcludedSourceElements.SelectedItems[0].Name);
    }

    [Fact]
    public void GetDatabaseInfo_HandlesFileBasedProvider()
    {
        // Arrange
        var comparison = new SchemaComparison
        {
            SourceModelProvider = new ModelProvider
            {
                FileBasedModelProvider = new FileBasedModelProvider
                {
                    FilePath = Path.Combine("C:", "Test", "MyDatabase.dacpac")
                }
            }
        };

        // Act
        var (sourceDb, targetDb) = _handler.GetDatabaseInfo(comparison);

        // Assert
        Assert.Equal("MyDatabase", sourceDb);
        Assert.Null(targetDb);
    }

    [Fact]
    public async Task SaveManifest_CreatesDirectoryIfNotExists()
    {
        // Arrange
        var comparison = CreateTestSchemaComparison();
        var subDir = Path.Combine(_testDirectory, "subdir", "nested");
        var filePath = Path.Combine(subDir, "test.scmp.xml");

        // Act
        await _handler.SaveManifestAsync(comparison, filePath);

        // Assert
        Assert.True(Directory.Exists(subDir));
        Assert.True(File.Exists(filePath));
    }

    static SchemaComparison CreateTestSchemaComparison() => new()
    {
        Version = "10",
        SourceModelProvider = new ModelProvider
        {
            ConnectionBasedModelProvider = new ConnectionBasedModelProvider
            {
                ConnectionString = "Data Source=SourceServer;Initial Catalog=SourceDB;Integrated Security=True"
            }
        },
        TargetModelProvider = new ModelProvider
        {
            ConnectionBasedModelProvider = new ConnectionBasedModelProvider
            {
                ConnectionString = "Server=TargetServer;Database=TargetDB;Trusted_Connection=Yes"
            }
        }
    };
}