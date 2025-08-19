# SCMP Refactoring Implementation Guide

## Executive Summary

The DACPAC Runner tool needs to be refactored to exclusively use SCMP (Schema Compare) files and Microsoft's official SchemaComparison API. The tool will generate four DACPACs for comprehensive database comparison and migration generation.

## Goals

1. **Single Purpose Tool**: Make the tool SCMP-only, removing all non-SCMP functionality
2. **Use Official APIs**: Replace custom XML manipulation with Microsoft's SchemaComparison API
3. **Four-DACPAC Strategy**: Generate and store all database states for complete visibility
4. **Line Ending Normalization**: Ensure consistent formatting to reduce false positives
5. **Git Integration**: Use git worktree for target filesystem state

## Required Changes

### 1. Make SCMP Mandatory

The tool should ONLY work with SCMP files. Remove all code paths that don't use SCMP.

**Required Parameters:**
- `--scmp` - Path to SCMP file (REQUIRED)
- `--source-password` - Password for source database (REQUIRED)
- `--target-password` - Password for target database (REQUIRED)
- `--output-path` - Output directory for generated files (REQUIRED)

**Optional Parameters:**
- `--commit-message` - Custom commit message
- `--skip-exclusion-manager` - Skip exclusion manager step

**Remove These Parameters:**
- Source connection string (get from SCMP)
- Target server/database (get from SCMP)
- Any non-SCMP workflow options

### 2. Four-DACPAC Generation Strategy

Generate and store four DACPACs in the target directory to capture all states:

```
servers/target-server/target-db/
    ├── source_server_original.dacpac      # Direct extract from source DB
    ├── source_server_filesystem.dacpac    # Rebuilt from extracted files
    ├── target_server_original.dacpac      # Direct extract from target DB
    ├── target_server_filesystem.dacpac    # Built from git committed state
    └── schemas/                           # Contains source extraction after completion
```

### 3. Workflow Overview

#### Phase 1: Target Filesystem DACPAC
1. Create git worktree of committed state (origin/main)
2. Build DACPAC from worktree's target directory
3. Save as `target_server_filesystem.dacpac`
4. Clean up worktree

#### Phase 2: Target Original DACPAC
1. Extract connection string from SCMP file
2. Update password using provided parameter
3. Extract DACPAC directly from target database
4. Save as `target_server_original.dacpac`

#### Phase 3: Source Original DACPAC
1. Extract connection string from SCMP file
2. Update password using provided parameter
3. Extract DACPAC directly from source database
4. Save as `source_server_original.dacpac`

#### Phase 4: Source Extraction and Filesystem DACPAC
1. Generate deployment script from source original DACPAC
2. Parse and extract to filesystem (overwrites current schemas/ folder)
3. Apply line ending normalization to all SQL files
4. Build DACPAC from extracted filesystem
5. Save as `source_server_filesystem.dacpac`

#### Phase 5: Schema Comparison
1. Load original SCMP file using Microsoft's SchemaComparison API
2. Replace Source endpoint with `source_server_filesystem.dacpac`
3. Replace Target endpoint with `target_server_filesystem.dacpac`
4. Run comparison using official API
5. Generate migration script
6. Save to `z_migrations/` directory

### 4. Technical Requirements

#### Remove Custom Code
- Delete all regex-based SCMP XML modifications
- Delete temporary SCMP file creation
- Delete non-SCMP workflow branches
- Fix duplicate variable declarations

#### Use Official APIs
- Use `SchemaComparison` class from Microsoft.SqlServer.Dac.Compare
- Use `SchemaCompareDacpacEndpoint` for DACPAC endpoints
- Set comparison.Source and comparison.Target properties directly
- Use comparison.Compare() method for execution

#### Line Ending Normalization
- Apply to all SQL files before building DACPACs
- Use LF (Unix) format consistently
- Normalize both source extraction and target filesystem

### 5. Error Handling

The tool should fail immediately if:
- SCMP file is not provided
- Either password is missing
- SCMP file cannot be loaded
- Connection strings are not found in SCMP
- Database connections fail
- DACPAC extraction fails

### 6. Benefits of This Approach

**Simplicity**: Single-purpose tool with clear workflow
**Completeness**: All four database states preserved
**Reproducibility**: Can regenerate exact migrations from stored DACPACs
**Debugging**: Can compare any combination of the four DACPACs
**Standards**: Uses only Microsoft's official APIs
**Version Control**: All artifacts stored in git

### 7. Testing Validation

After implementation, verify:
1. All four DACPACs are created successfully
2. Source extraction overwrites schemas/ folder
3. Migration script is generated correctly
4. No custom SCMP modifications occur
5. Tool fails appropriately when required parameters are missing
6. Triggers and all object types are properly extracted

### 8. Migration Path

For existing users:
- Document that non-SCMP workflow is removed
- Provide SCMP file examples
- Explain the four-DACPAC strategy
- Show how to use the required passwords

### 9. Future Considerations

- Consider adding `--keep-source-schemas` flag to preserve source extraction
- Add progress indicators for long-running operations
- Implement parallel DACPAC extraction where possible
- Add validation to compare original vs filesystem DACPACs for drift detection

## Implementation Priority

1. **Critical**: Fix compilation errors (duplicate variables)
2. **Critical**: Add target-password parameter
3. **High**: Remove custom SCMP modifications
4. **High**: Implement four-DACPAC generation
5. **Medium**: Remove non-SCMP code paths
6. **Low**: Add progress indicators and optimizations

## Success Criteria

The refactoring is complete when:
- Tool only accepts SCMP files
- All four DACPACs are generated and stored
- No custom XML manipulation exists
- Microsoft's official API is used throughout
- Line endings are normalized consistently
- All required parameters are validated