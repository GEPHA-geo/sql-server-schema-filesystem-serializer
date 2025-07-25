using SqlServer.Schema.Migration.Generator.Parsing;
using SqlServer.Schema.Migration.Generator.GitIntegration;

namespace SqlServer.Schema.Migration.Generator.Generation;

public class DependencyResolver
{
    public List<SchemaChange> OrderChanges(List<SchemaChange> changes)
    {
        var ordered = new List<SchemaChange>();
        var remaining = new List<SchemaChange>(changes);
        
        // Group changes by operation type and object type
        var dropConstraints = remaining.Where(c => c.ChangeType == ChangeType.Deleted && c.ObjectType == "Constraint").ToList();
        var dropIndexes = remaining.Where(c => c.ChangeType == ChangeType.Deleted && c.ObjectType == "Index").ToList();
        var dropColumns = remaining.Where(c => c.ChangeType == ChangeType.Deleted && c.ObjectType == "Column").ToList();
        var dropTables = remaining.Where(c => c.ChangeType == ChangeType.Deleted && c.ObjectType == "Table").ToList();
        var dropOthers = remaining.Where(c => c.ChangeType == ChangeType.Deleted && 
            c.ObjectType != "Constraint" && c.ObjectType != "Index" && c.ObjectType != "Column" && c.ObjectType != "Table").ToList();
        
        var createTables = remaining.Where(c => c.ChangeType == ChangeType.Added && c.ObjectType == "Table").ToList();
        var createColumns = remaining.Where(c => c.ChangeType == ChangeType.Added && c.ObjectType == "Column").ToList();
        var createIndexes = remaining.Where(c => c.ChangeType == ChangeType.Added && c.ObjectType == "Index").ToList();
        var createConstraints = remaining.Where(c => c.ChangeType == ChangeType.Added && c.ObjectType == "Constraint").ToList();
        var createOthers = remaining.Where(c => c.ChangeType == ChangeType.Added && 
            c.ObjectType != "Table" && c.ObjectType != "Column" && c.ObjectType != "Index" && c.ObjectType != "Constraint").ToList();
        
        var modifications = remaining.Where(c => c.ChangeType == ChangeType.Modified).ToList();
        
        // Order of operations:
        // 1. Drop foreign key constraints first
        ordered.AddRange(dropConstraints.Where(c => c.ObjectName.StartsWith("FK_")));
        
        // 2. Drop other constraints
        ordered.AddRange(dropConstraints.Where(c => !c.ObjectName.StartsWith("FK_")));
        
        // 3. Drop indexes
        ordered.AddRange(dropIndexes);
        
        // 4. Drop columns
        ordered.AddRange(dropColumns);
        
        // 5. Drop views, procedures, functions
        ordered.AddRange(dropOthers);
        
        // 6. Drop tables
        ordered.AddRange(dropTables);
        
        // 7. Create tables
        ordered.AddRange(createTables);
        
        // 8. Add columns
        ordered.AddRange(createColumns);
        
        // 9. Modify existing objects
        ordered.AddRange(modifications);
        
        // 10. Create indexes
        ordered.AddRange(createIndexes);
        
        // 11. Create non-FK constraints
        ordered.AddRange(createConstraints.Where(c => !c.ObjectName.StartsWith("FK_")));
        
        // 12. Create foreign key constraints
        ordered.AddRange(createConstraints.Where(c => c.ObjectName.StartsWith("FK_")));
        
        // 13. Create views, procedures, functions
        ordered.AddRange(createOthers);
        
        return ordered;
    }
}