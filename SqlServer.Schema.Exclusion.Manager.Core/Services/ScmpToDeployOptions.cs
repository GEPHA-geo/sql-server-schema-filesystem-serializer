using Microsoft.SqlServer.Dac;
using SqlServer.Schema.Exclusion.Manager.Core.Models;

namespace SqlServer.Schema.Exclusion.Manager.Core.Services;

/// <summary>
/// Maps SCMP configuration settings to DacDeployOptions for use with SqlPackage/DacFx
/// </summary>
public class ScmpToDeployOptions
{
    /// <summary>
    /// Maps SchemaComparison settings to DacDeployOptions
    /// </summary>
    /// <param name="comparison">The SchemaComparison containing settings</param>
    /// <returns>Configured DacDeployOptions</returns>
    public DacDeployOptions MapOptions(SchemaComparison comparison)
    {
        var options = new DacDeployOptions();

        if (comparison.SchemaCompareSettingsService?.ConfigurationOptionsElement?.PropertyElements == null)
            return options;

        var properties = comparison.SchemaCompareSettingsService.ConfigurationOptionsElement.PropertyElements;

        foreach (var property in properties)
        {
            SetDeployOption(options, property.Name, property.Value);
        }

        return options;
    }

    /// <summary>
    /// Sets individual deploy option based on property name and value
    /// </summary>
    void SetDeployOption(DacDeployOptions options, string name, string value)
    {
        // Parse boolean values
        var boolValue = ParseBool(value);

        // Map SCMP option names to DacDeployOptions properties
        // This mapping covers the most common options used in schema comparisons
        switch (name)
        {
            // Object handling options
            case "DropObjectsNotInSource":
                options.DropObjectsNotInSource = boolValue;
                break;
            case "DropPermissionsNotInSource":
                options.DropPermissionsNotInSource = boolValue;
                break;
            case "DropRoleMembersNotInSource":
                options.DropRoleMembersNotInSource = boolValue;
                break;
            case "DropExtendedPropertiesNotInSource":
                options.DropExtendedPropertiesNotInSource = boolValue;
                break;
            case "DropDmlTriggersNotInSource":
                options.DropDmlTriggersNotInSource = boolValue;
                break;
            case "DropStatisticsNotInSource":
                options.DropStatisticsNotInSource = boolValue;
                break;
            case "DropIndexesNotInSource":
                options.DropIndexesNotInSource = boolValue;
                break;
            case "DropConstraintsNotInSource":
                options.DropConstraintsNotInSource = boolValue;
                break;

            // Data handling options
            case "BlockOnPossibleDataLoss":
                options.BlockOnPossibleDataLoss = boolValue;
                break;
            case "AllowTableRecreation":
                options.AllowDropBlockingAssemblies = boolValue;
                break;
            case "BackupDatabaseBeforeChanges":
                options.BackupDatabaseBeforeChanges = boolValue;
                break;
            case "VerifyDeployment":
                options.VerifyDeployment = boolValue;
                break;

            // Ignore options
            case "IgnorePermissions":
                options.IgnorePermissions = boolValue;
                break;
            case "IgnoreRoleMembership":
                options.IgnoreRoleMembership = boolValue;
                break;
            case "IgnoreUserSettingsObjects":
                options.IgnoreUserSettingsObjects = boolValue;
                break;
            case "IgnoreLoginSids":
                options.IgnoreLoginSids = boolValue;
                break;
            case "IgnoreNotForReplication":
                options.IgnoreNotForReplication = boolValue;
                break;
            case "IgnoreFileSize":
                options.IgnoreFileSize = boolValue;
                break;
            case "IgnoreFilegroupPlacement":
                options.IgnoreFilegroupPlacement = boolValue;
                break;
            case "IgnoreFullTextCatalogFilePath":
                options.IgnoreFullTextCatalogFilePath = boolValue;
                break;
            case "IgnoreWhitespace":
                options.IgnoreWhitespace = boolValue;
                break;
            case "IgnoreKeywordCasing":
                options.IgnoreKeywordCasing = boolValue;
                break;
            case "IgnoreSemicolonBetweenStatements":
                options.IgnoreSemicolonBetweenStatements = boolValue;
                break;
            case "IgnoreRouteLifetime":
                options.IgnoreRouteLifetime = boolValue;
                break;
            case "IgnoreAnsiNulls":
                options.IgnoreAnsiNulls = boolValue;
                break;
            case "IgnoreAuthorizer":
                options.IgnoreAuthorizer = boolValue;
                break;
            case "IgnoreColumnCollation":
                options.IgnoreColumnCollation = boolValue;
                break;
            case "IgnoreComments":
                options.IgnoreComments = boolValue;
                break;
            case "IgnoreCryptographicProviderFilePath":
                options.IgnoreCryptographicProviderFilePath = boolValue;
                break;
            case "IgnoreQuotedIdentifiers":
                options.IgnoreQuotedIdentifiers = boolValue;
                break;
            case "IgnoreDdlTriggerOrder":
                options.IgnoreDdlTriggerOrder = boolValue;
                break;
            case "IgnoreDdlTriggerState":
                options.IgnoreDdlTriggerState = boolValue;
                break;
            case "IgnoreDefaultSchema":
                options.IgnoreDefaultSchema = boolValue;
                break;
            case "IgnoreDmlTriggerOrder":
                options.IgnoreDmlTriggerOrder = boolValue;
                break;
            case "IgnoreDmlTriggerState":
                options.IgnoreDmlTriggerState = boolValue;
                break;
            case "IgnoreExtendedProperties":
                options.IgnoreExtendedProperties = boolValue;
                break;
            case "IgnoreFileAndLogFilePath":
                options.IgnoreFileAndLogFilePath = boolValue;
                break;
            case "IgnoreFillFactor":
                options.IgnoreFillFactor = boolValue;
                break;
            case "IgnoreIdentitySeed":
                options.IgnoreIdentitySeed = boolValue;
                break;
            case "IgnoreIncrement":
                options.IgnoreIncrement = boolValue;
                break;
            case "IgnoreIndexOptions":
                options.IgnoreIndexOptions = boolValue;
                break;
            case "IgnoreIndexPadding":
                options.IgnoreIndexPadding = boolValue;
                break;
            case "IgnoreLockHintsOnIndexes":
                options.IgnoreLockHintsOnIndexes = boolValue;
                break;
            case "IgnorePartitionSchemes":
                options.IgnorePartitionSchemes = boolValue;
                break;
            case "IgnoreWithNocheckOnCheckConstraints":
                options.IgnoreWithNocheckOnCheckConstraints = boolValue;
                break;
            case "IgnoreWithNocheckOnForeignKeys":
                options.IgnoreWithNocheckOnForeignKeys = boolValue;
                break;
            case "IgnoreTableOptions":
                options.IgnoreTableOptions = boolValue;
                break;

            // Schema handling
            case "DoNotDropObjectTypes":
                // This would need special handling as it's a collection
                // For now, we'll skip it
                break;

            // Script generation options
            case "GenerateSmartDefaults":
                options.GenerateSmartDefaults = boolValue;
                break;
            case "IncludeCompositeObjects":
                options.IncludeCompositeObjects = boolValue;
                break;
            case "IncludeTransactionalScripts":
                options.IncludeTransactionalScripts = boolValue;
                break;
            // PopulateFilesOnFilegroups - not available in current DacFx version
            // case "PopulateFilesOnFilegroups":
            //     options.PopulateFilesOnFilegroups = boolValue;
            //     break;
            case "RegisterDataTierApplication":
                options.RegisterDataTierApplication = boolValue;
                break;
            case "ScriptDatabaseCollation":
                options.ScriptDatabaseCollation = boolValue;
                break;
            case "ScriptDatabaseCompatibility":
                options.ScriptDatabaseCompatibility = boolValue;
                break;
            case "ScriptDatabaseOptions":
                options.ScriptDatabaseOptions = boolValue;
                break;
            case "ScriptDeployStateChecks":
                options.ScriptDeployStateChecks = boolValue;
                break;
            case "ScriptFileSize":
                options.ScriptFileSize = boolValue;
                break;
            case "ScriptNewConstraintValidation":
                options.ScriptNewConstraintValidation = boolValue;
                break;
            case "ScriptRefreshModule":
                options.ScriptRefreshModule = boolValue;
                break;
            case "CreateNewDatabase":
                options.CreateNewDatabase = boolValue;
                break;
            case "AllowIncompatiblePlatform":
                options.AllowIncompatiblePlatform = boolValue;
                break;

            // Validation options
            case "UnmodifiableObjectWarnings":
                options.UnmodifiableObjectWarnings = boolValue;
                break;
            case "VerifyCollationCompatibility":
                options.VerifyCollationCompatibility = boolValue;
                break;
            case "DisableAndReenableDdlTriggers":
                options.DisableAndReenableDdlTriggers = boolValue;
                break;
            case "DeployDatabaseInSingleUserMode":
                options.DeployDatabaseInSingleUserMode = boolValue;
                break;

            // Command timeout (integer value)
            case "CommandTimeout":
                if (int.TryParse(value, out var timeout))
                    options.CommandTimeout = timeout;
                break;

            // Database specification - requires DacAzureDatabaseSpecification object
            // case "DatabaseSpecification":
            //     // This requires a complex object, not a string
            //     // Would need special handling to create DacAzureDatabaseSpecification
            //     break;

            default:
                // Log unknown option for debugging
                // Could add logging here if needed
                break;
        }
    }

    /// <summary>
    /// Parses a string value to boolean
    /// </summary>
    bool ParseBool(string value) =>
        value.Equals("True", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("1", StringComparison.Ordinal);

    /// <summary>
    /// Creates default DacDeployOptions with sensible defaults for migration generation
    /// </summary>
    public DacDeployOptions CreateDefaultOptions()
    {
        return new DacDeployOptions
        {
            // Data protection
            BlockOnPossibleDataLoss = true,
            BackupDatabaseBeforeChanges = false,

            // Drop operations - conservative by default
            DropObjectsNotInSource = false,
            DropPermissionsNotInSource = false,
            DropRoleMembersNotInSource = false,
            DropExtendedPropertiesNotInSource = false,

            // Commonly ignored items for migrations
            IgnorePermissions = true,
            IgnoreRoleMembership = true,
            IgnoreUserSettingsObjects = true,
            IgnoreLoginSids = true,
            IgnoreFileAndLogFilePath = true,
            IgnoreFilegroupPlacement = true,
            IgnoreFullTextCatalogFilePath = true,
            IgnoreWhitespace = true,
            IgnoreKeywordCasing = true,
            IgnoreSemicolonBetweenStatements = true,

            // Script generation
            IncludeCompositeObjects = true,
            IncludeTransactionalScripts = true,
            GenerateSmartDefaults = true,

            // Validation
            VerifyDeployment = true,
            VerifyCollationCompatibility = true
        };
    }
}