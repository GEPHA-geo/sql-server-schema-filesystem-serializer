using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Generation;

public class DDLGenerator
{
    readonly TableDDLGenerator _tableGenerator = new();
    readonly IndexDDLGenerator _indexGenerator = new();

    public string GenerateDDL(SchemaChange change)
    {
        return change.ObjectType switch
        {
            "Table" => _tableGenerator.GenerateTableDDL(change),
            "Column" => _tableGenerator.GenerateColumnDDL(change),
            "Index" => _indexGenerator.GenerateIndexDDL(change),
            "Constraint" => GenerateConstraintDDL(change),
            "View" => GenerateViewDDL(change),
            "StoredProcedure" => GenerateStoredProcedureDDL(change),
            "Function" => GenerateFunctionDDL(change),
            _ => $"-- Unsupported object type: {change.ObjectType}"
        };
    }

    string GenerateConstraintDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                return change.NewDefinition;
                
            case ChangeType.Deleted:
                // Need to determine constraint type from the definition or name
                if (change.ObjectName.StartsWith("FK_"))
                    return $"ALTER TABLE [{change.Schema}].[{change.TableName}] DROP CONSTRAINT [{change.ObjectName}];";
                else if (change.ObjectName.StartsWith("PK_"))
                    return $"ALTER TABLE [{change.Schema}].[{change.TableName}] DROP CONSTRAINT [{change.ObjectName}];";
                else if (change.ObjectName.StartsWith("DF_"))
                    return $"ALTER TABLE [{change.Schema}].[{change.TableName}] DROP CONSTRAINT [{change.ObjectName}];";
                else
                    return $"-- Drop constraint: {change.ObjectName}";
                    
            case ChangeType.Modified:
                // Drop and recreate
                var dropDDL = GenerateConstraintDDL(new SchemaChange 
                { 
                    ObjectType = change.ObjectType,
                    Schema = change.Schema,
                    TableName = change.TableName,
                    ObjectName = change.ObjectName,
                    ChangeType = ChangeType.Deleted,
                    OldDefinition = change.OldDefinition
                });
                
                return $"{dropDDL}\nGO\n\n{change.NewDefinition}";
                
            default:
                return $"-- Unknown change type for constraint: {change.ObjectName}";
        }
    }

    string GenerateViewDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                return change.NewDefinition;
                
            case ChangeType.Deleted:
                return $"DROP VIEW [{change.Schema}].[{change.ObjectName}];";
                
            case ChangeType.Modified:
                return $"DROP VIEW [{change.Schema}].[{change.ObjectName}];\nGO\n\n{change.NewDefinition}";
                
            default:
                return $"-- Unknown change type for view: {change.ObjectName}";
        }
    }

    string GenerateStoredProcedureDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                return change.NewDefinition;
                
            case ChangeType.Deleted:
                return $"DROP PROCEDURE [{change.Schema}].[{change.ObjectName}];";
                
            case ChangeType.Modified:
                return $"DROP PROCEDURE [{change.Schema}].[{change.ObjectName}];\nGO\n\n{change.NewDefinition}";
                
            default:
                return $"-- Unknown change type for stored procedure: {change.ObjectName}";
        }
    }

    string GenerateFunctionDDL(SchemaChange change)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Added:
                return change.NewDefinition;
                
            case ChangeType.Deleted:
                return $"DROP FUNCTION [{change.Schema}].[{change.ObjectName}];";
                
            case ChangeType.Modified:
                return $"DROP FUNCTION [{change.Schema}].[{change.ObjectName}];\nGO\n\n{change.NewDefinition}";
                
            default:
                return $"-- Unknown change type for function: {change.ObjectName}";
        }
    }
}