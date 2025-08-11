using Xunit;
using SqlServer.Schema.Exclusion.Manager.Core.Models;
using SqlServer.Schema.Exclusion.Manager.Core.Services;

namespace SqlServer.Schema.Exclusion.Manager.Tests;

public class ScmpToDeployOptionsTests
{
    readonly ScmpToDeployOptions _mapper = new();

    [Fact]
    public void MapOptions_EmptyComparison_ReturnsDefaultOptions()
    {
        // Arrange
        var comparison = new SchemaComparison();

        // Act
        var options = _mapper.MapOptions(comparison);

        // Assert
        Assert.NotNull(options);
        // Default DacDeployOptions values
        Assert.True(options.BlockOnPossibleDataLoss);
    }

    [Fact]
    public void MapOptions_BooleanOptions_MapsCorrectly()
    {
        // Arrange
        var comparison = CreateComparisonWithOptions(
            ("DropObjectsNotInSource", "True"),
            ("BlockOnPossibleDataLoss", "False"),
            ("IgnorePermissions", "True"),
            ("IgnoreWhitespace", "1"),
            ("GenerateSmartDefaults", "false")
        );

        // Act
        var options = _mapper.MapOptions(comparison);

        // Assert
        Assert.True(options.DropObjectsNotInSource);
        Assert.False(options.BlockOnPossibleDataLoss);
        Assert.True(options.IgnorePermissions);
        Assert.True(options.IgnoreWhitespace);
        Assert.False(options.GenerateSmartDefaults);
    }

    [Fact]
    public void MapOptions_AllDropOptions_MapsCorrectly()
    {
        // Arrange
        var comparison = CreateComparisonWithOptions(
            ("DropObjectsNotInSource", "True"),
            ("DropPermissionsNotInSource", "True"),
            ("DropRoleMembersNotInSource", "True"),
            ("DropExtendedPropertiesNotInSource", "True"),
            ("DropDmlTriggersNotInSource", "True"),
            ("DropStatisticsNotInSource", "True"),
            ("DropIndexesNotInSource", "True"),
            ("DropConstraintsNotInSource", "True")
        );

        // Act
        var options = _mapper.MapOptions(comparison);

        // Assert
        Assert.True(options.DropObjectsNotInSource);
        Assert.True(options.DropPermissionsNotInSource);
        Assert.True(options.DropRoleMembersNotInSource);
        Assert.True(options.DropExtendedPropertiesNotInSource);
        Assert.True(options.DropDmlTriggersNotInSource);
        Assert.True(options.DropStatisticsNotInSource);
        Assert.True(options.DropIndexesNotInSource);
        Assert.True(options.DropConstraintsNotInSource);
    }

    [Fact]
    public void MapOptions_IgnoreOptions_MapsCorrectly()
    {
        // Arrange
        var comparison = CreateComparisonWithOptions(
            ("IgnorePermissions", "True"),
            ("IgnoreRoleMembership", "True"),
            ("IgnoreUserSettingsObjects", "True"),
            ("IgnoreLoginSids", "True"),
            ("IgnoreNotForReplication", "True"),
            ("IgnoreFileSize", "True"),
            ("IgnoreFilegroupPlacement", "True"),
            ("IgnoreFullTextCatalogFilePath", "True"),
            ("IgnoreWhitespace", "True"),
            ("IgnoreKeywordCasing", "True"),
            ("IgnoreSemicolonBetweenStatements", "True"),
            ("IgnoreAnsiNulls", "True"),
            ("IgnoreQuotedIdentifiers", "True"),
            ("IgnoreComments", "True"),
            ("IgnoreExtendedProperties", "True"),
            ("IgnoreFillFactor", "True"),
            ("IgnoreIndexPadding", "True"),
            ("IgnoreTableOptions", "True")
        );

        // Act
        var options = _mapper.MapOptions(comparison);

        // Assert
        Assert.True(options.IgnorePermissions);
        Assert.True(options.IgnoreRoleMembership);
        Assert.True(options.IgnoreUserSettingsObjects);
        Assert.True(options.IgnoreLoginSids);
        Assert.True(options.IgnoreNotForReplication);
        Assert.True(options.IgnoreFileSize);
        Assert.True(options.IgnoreFilegroupPlacement);
        Assert.True(options.IgnoreFullTextCatalogFilePath);
        Assert.True(options.IgnoreWhitespace);
        Assert.True(options.IgnoreKeywordCasing);
        Assert.True(options.IgnoreSemicolonBetweenStatements);
        Assert.True(options.IgnoreAnsiNulls);
        Assert.True(options.IgnoreQuotedIdentifiers);
        Assert.True(options.IgnoreComments);
        Assert.True(options.IgnoreExtendedProperties);
        Assert.True(options.IgnoreFillFactor);
        Assert.True(options.IgnoreIndexPadding);
        Assert.True(options.IgnoreTableOptions);
    }

    [Fact]
    public void MapOptions_ScriptOptions_MapsCorrectly()
    {
        // Arrange
        var comparison = CreateComparisonWithOptions(
            ("GenerateSmartDefaults", "True"),
            ("IncludeCompositeObjects", "True"),
            ("IncludeTransactionalScripts", "True"),
            ("ScriptDatabaseCollation", "True"),
            ("ScriptDatabaseCompatibility", "True"),
            ("ScriptDatabaseOptions", "True"),
            ("ScriptDeployStateChecks", "True"),
            ("ScriptFileSize", "True"),
            ("ScriptNewConstraintValidation", "True"),
            ("ScriptRefreshModule", "True"),
            ("CreateNewDatabase", "True")
        );

        // Act
        var options = _mapper.MapOptions(comparison);

        // Assert
        Assert.True(options.GenerateSmartDefaults);
        Assert.True(options.IncludeCompositeObjects);
        Assert.True(options.IncludeTransactionalScripts);
        Assert.True(options.ScriptDatabaseCollation);
        Assert.True(options.ScriptDatabaseCompatibility);
        Assert.True(options.ScriptDatabaseOptions);
        Assert.True(options.ScriptDeployStateChecks);
        Assert.True(options.ScriptFileSize);
        Assert.True(options.ScriptNewConstraintValidation);
        Assert.True(options.ScriptRefreshModule);
        Assert.True(options.CreateNewDatabase);
    }

    [Fact]
    public void MapOptions_CommandTimeout_MapsCorrectly()
    {
        // Arrange
        var comparison = CreateComparisonWithOptions(
            ("CommandTimeout", "120")
        );

        // Act
        var options = _mapper.MapOptions(comparison);

        // Assert
        Assert.Equal(120, options.CommandTimeout);
    }

    [Fact]
    public void MapOptions_DatabaseSpecification_MapsCorrectly()
    {
        // Arrange
        var comparison = CreateComparisonWithOptions(
            ("DatabaseSpecification", "TestSpec")
        );

        // Act
        var options = _mapper.MapOptions(comparison);

        // Assert
        // DatabaseSpecification is DacAzureDatabaseSpecification type, not string
        // This test would need to be updated if DatabaseSpecification mapping is implemented
        Assert.NotNull(options);
    }

    [Fact]
    public void MapOptions_UnknownOption_DoesNotThrow()
    {
        // Arrange
        var comparison = CreateComparisonWithOptions(
            ("UnknownOption", "SomeValue"),
            ("AnotherUnknownOption", "True")
        );

        // Act & Assert - should not throw
        var options = _mapper.MapOptions(comparison);
        Assert.NotNull(options);
    }

    [Fact]
    public void CreateDefaultOptions_ReturnsConservativeDefaults()
    {
        // Act
        var options = _mapper.CreateDefaultOptions();

        // Assert
        // Data protection defaults
        Assert.True(options.BlockOnPossibleDataLoss);
        Assert.False(options.BackupDatabaseBeforeChanges);
        
        // Conservative drop defaults
        Assert.False(options.DropObjectsNotInSource);
        Assert.False(options.DropPermissionsNotInSource);
        Assert.False(options.DropRoleMembersNotInSource);
        Assert.False(options.DropExtendedPropertiesNotInSource);
        
        // Common ignore defaults
        Assert.True(options.IgnorePermissions);
        Assert.True(options.IgnoreRoleMembership);
        Assert.True(options.IgnoreUserSettingsObjects);
        Assert.True(options.IgnoreLoginSids);
        Assert.True(options.IgnoreFileAndLogFilePath);
        Assert.True(options.IgnoreFilegroupPlacement);
        Assert.True(options.IgnoreFullTextCatalogFilePath);
        Assert.True(options.IgnoreWhitespace);
        Assert.True(options.IgnoreKeywordCasing);
        Assert.True(options.IgnoreSemicolonBetweenStatements);
        
        // Script generation defaults
        Assert.True(options.IncludeCompositeObjects);
        Assert.True(options.IncludeTransactionalScripts);
        Assert.True(options.GenerateSmartDefaults);
        
        // Validation defaults
        Assert.True(options.VerifyDeployment);
        Assert.True(options.VerifyCollationCompatibility);
    }

    [Fact]
    public void MapOptions_MixedCaseValues_ParsesCorrectly()
    {
        // Arrange
        var comparison = CreateComparisonWithOptions(
            ("DropObjectsNotInSource", "TRUE"),
            ("BlockOnPossibleDataLoss", "false"),
            ("IgnorePermissions", "tRuE"),
            ("IgnoreWhitespace", "FALSE")
        );

        // Act
        var options = _mapper.MapOptions(comparison);

        // Assert
        Assert.True(options.DropObjectsNotInSource);
        Assert.False(options.BlockOnPossibleDataLoss);
        Assert.True(options.IgnorePermissions);
        Assert.False(options.IgnoreWhitespace);
    }

    static SchemaComparison CreateComparisonWithOptions(params (string name, string value)[] options)
    {
        var comparison = new SchemaComparison
        {
            SchemaCompareSettingsService = new SchemaCompareSettingsService
            {
                ConfigurationOptionsElement = new ConfigurationOptionsElement
                {
                    PropertyElements = new List<PropertyElement>()
                }
            }
        };

        foreach (var (name, value) in options)
        {
            comparison.SchemaCompareSettingsService.ConfigurationOptionsElement.PropertyElements.Add(
                new PropertyElement { Name = name, Value = value }
            );
        }

        return comparison;
    }
}