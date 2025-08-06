# Change Exclusion System - Implementation Verification Checklist

Use these questions to verify that the change exclusion system has been fully implemented and tested.

## Core Implementation

### 1. ChangeManifestManager Implementation
- [ ] Has the `ChangeManifestManager` class been created in the appropriate project?
- [ ] Does it implement all the methods specified in the design document?
  - `LoadManifest(string path)`
  - `SaveManifest(ChangeManifest manifest, string path)`
  - `ApplyExclusions(List<SchemaChange> changes, ChangeManifest manifest)`
  - `GenerateManifest(List<SchemaChange> changes, string server, string database)`
  - `MergeWithExisting(ChangeManifest existing, List<SchemaChange> newChanges)`
  - `GenerateChangeIdentifier(SchemaChange change)`
  - `GenerateChangeDescription(SchemaChange change)`
- [ ] Does the manifest file use the `.manifest` extension?
- [ ] Is the filename format `change-manifest-{server}-{database}.manifest`?

### 2. Migration Generator Integration
- [ ] Has `MigrationGenerator.GenerateMigrationsAsync()` been updated to use the manifest?
- [ ] Does it load existing manifests and merge with new changes?
- [ ] Does it pass both included and excluded changes to the script builder?
- [ ] Does it handle the case when no manifest exists (first run)?

### 3. Migration Script Builder Updates
- [ ] Has `MigrationScriptBuilder.BuildMigration()` been updated to accept excluded changes?
- [ ] Do excluded changes appear as commented SQL in the migration script?
- [ ] Does each excluded change reference the manifest file?
- [ ] Is the migration header updated to show total/included/excluded counts?

### 4. Serializer Integration
- [ ] Has `DacpacScriptParser` been updated to read the manifest during serialization?
- [ ] Does it add "MIGRATION EXCLUDED" comments to serialized files?
- [ ] Are the comments added for both permanently and temporarily excluded changes?
- [ ] Do the comments reference the correct manifest file?

### 5. CLI Support
- [ ] Has the `--regenerate` flag been added to the migration generator CLI?
- [ ] Does regeneration correctly delete old migrations and create new ones?
- [ ] Can users specify which commit to compare against?

## Testing

### 6. Unit Tests
- [ ] Are all `ChangeManifestManager` methods covered by unit tests?
- [ ] Do tests verify correct manifest file naming?
- [ ] Do tests verify correct parsing of manifest content?
- [ ] Do tests verify exclusion filtering works correctly?
- [ ] Do tests verify merging preserves existing exclusions?

### 7. Integration Tests
- [ ] Is there a test for generating migrations with excluded changes?
- [ ] Is there a test for the temporary exclusion scenario (deleted from included)?
- [ ] Is there a test for moving changes from excluded back to included?
- [ ] Is there a test for multiple databases using different manifests?

### 8. End-to-End Tests
- [ ] Is there a test for the complete workflow from database change to migration with exclusions?
- [ ] Is there a test for regenerating migrations after manifest changes?
- [ ] Are edge cases tested (renamed objects, deleted objects, concurrent edits)?

### 9. Existing Tests
- [ ] Do all existing unit tests still pass?
- [ ] Do all existing integration tests still pass?
- [ ] Have any existing tests been updated to account for the manifest system?

## Build and Deployment

### 10. Project Builds
- [ ] Does the solution build without errors?
- [ ] Are there any new compiler warnings?
- [ ] Do all projects in the solution build successfully?
  - `SqlServer.Schema.FileSystem.Serializer.Dacpac`
  - `SqlServer.Schema.FileSystem.Serializer.Dacpac.Core`
  - `SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner`
  - `SqlServer.Schema.Migration.Generator`
  - `SqlServer.Schema.Migration.Generator.Tests`
  - `SqlServer.Schema.Migration.Runner`

### 11. Dependencies
- [ ] Have any new NuGet packages been added?
- [ ] Are all package versions compatible?
- [ ] Is the `Directory.Build.props` updated if needed?

## GitHub Workflow

### 12. Workflow Updates
- [ ] Has the GitHub workflow been updated to detect manifest changes?
- [ ] Does it trigger migration regeneration when manifests are modified?
- [ ] Does it handle the regeneration process correctly in CI/CD?

## Functional Verification

### 13. Manifest File Operations
- [ ] Can a manifest be created for a new database?
- [ ] Can changes be moved from INCLUDED to EXCLUDED sections?
- [ ] Can changes be deleted from INCLUDED (temporary exclusion)?
- [ ] Does the manifest preserve formatting after multiple edits?

### 14. Migration Generation
- [ ] Are excluded changes correctly filtered from migrations?
- [ ] Do temporarily excluded changes reappear in future runs?
- [ ] Are permanently excluded changes consistently excluded?
- [ ] Is the migration SQL syntactically correct with comments?

### 15. File System Comments
- [ ] Do serialized table files show exclusion comments?
- [ ] Do serialized index/constraint files show exclusion comments?
- [ ] Are the comments clear about what's excluded and why?

## Documentation

### 16. Code Documentation
- [ ] Are all new classes and methods properly documented with XML comments?
- [ ] Is the manifest file format documented in code?
- [ ] Are complex algorithms explained with inline comments?

### 17. User Documentation
- [ ] Is there a README or guide for using the exclusion system?
- [ ] Are example manifest files provided?
- [ ] Is the CLI usage documented with the new flags?

## Performance

### 18. Performance Considerations
- [ ] Does the system handle large numbers of changes efficiently?
- [ ] Is manifest parsing optimized for large files?
- [ ] Are there any noticeable performance regressions?

## Error Handling

### 19. Error Scenarios
- [ ] What happens if the manifest file is corrupted?
- [ ] What happens if excluded changes no longer exist?
- [ ] Are error messages helpful and actionable?
- [ ] Is there graceful fallback when manifest is missing?

## Final Verification

### 20. Complete Feature Test
Run through this scenario to verify everything works:
1. Make database changes (add columns, modify types, add indexes)
2. Run serialization - verify files are created with current state
3. Run migration generator - verify manifest is created with all changes in INCLUDED
4. Move some changes to EXCLUDED section
5. Delete some changes from INCLUDED (temporary exclusion)
6. Save and commit manifest
7. Run migration generator with `--regenerate`
8. Verify migration script has:
   - Active SQL for included changes
   - Commented SQL for excluded changes with manifest reference
   - Correct header with counts
9. Verify serialized files have exclusion comments
10. Make new changes and run again - verify temporarily excluded changes reappear

## Questions to Ask Claude Code

When implementation appears complete, ask:

1. "Show me all the test results - did all new and existing tests pass?"
2. "Run the complete build and show me any errors or warnings"
3. "Generate a migration for a test database with some excluded changes and show me the output"
4. "Show me an example of a serialized file with exclusion comments"
5. "Demonstrate the temporary exclusion feature - delete a change from INCLUDED and show it reappears"
6. "Run the end-to-end test scenario from the verification checklist"
7. "Show me the manifest file that was generated"
8. "What happens if I corrupt the manifest file and try to generate migrations?"
9. "Show me how the GitHub workflow handles manifest changes"
10. "Is there anything in the original requirements that hasn't been implemented?"

## Success Criteria

The feature is complete when:
- [ ] All items in this checklist are verified ✓
- [ ] All tests pass (new and existing)
- [ ] The solution builds without errors
- [ ] A real migration can be generated with exclusions
- [ ] The GitHub workflow correctly handles manifest changes
- [ ] Documentation is complete and accurate

*Collaboration by Claude*