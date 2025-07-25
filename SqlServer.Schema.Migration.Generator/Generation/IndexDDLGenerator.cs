using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Generation;

public class IndexDDLGenerator
{
    public string GenerateIndexDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                return change.NewDefinition;
                
            case ChangeType.Deleted:
                return $"DROP INDEX IF EXISTS [{change.ObjectName}] ON [{change.Schema}].[{change.TableName}];";
                
            case ChangeType.Modified:
                // Always drop and recreate indexes
                var dropDDL = $"DROP INDEX IF EXISTS [{change.ObjectName}] ON [{change.Schema}].[{change.TableName}];";
                return $"{dropDDL}\nGO\n\n{change.NewDefinition}";
                
            default:
                return $"-- Unknown change type for index: {change.ObjectName}";
        }
    }
}