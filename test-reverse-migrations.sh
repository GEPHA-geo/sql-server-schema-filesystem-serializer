#!/bin/bash

# Test script to demonstrate reverse migration generation

echo "=== Testing Reverse Migration Generation ==="
echo

# Create a test directory structure
TEST_DIR="test-reverse-migrations"
SERVER_DIR="$TEST_DIR/servers/test-server/test-db"
MIGRATIONS_DIR="$SERVER_DIR/z_migrations"
REVERSE_DIR="$SERVER_DIR/z_migrations_reverse"

# Clean up from previous runs
rm -rf $TEST_DIR

# Create directory structure
mkdir -p $SERVER_DIR/{Tables,Indexes}

# Initialize git repository
cd $TEST_DIR
git init
git config user.email "test@example.com"
git config user.name "Test User"

# Create initial schema
cat > servers/test-server/test-db/Tables/Users.sql << 'EOF'
CREATE TABLE [dbo].[Users] (
    [Id] INT IDENTITY(1,1) PRIMARY KEY,
    [Username] NVARCHAR(50) NOT NULL,
    [Email] NVARCHAR(100) NOT NULL
);
EOF

# Commit initial schema
git add .
git commit -m "Initial schema"

# Make a change - add a column
cat > servers/test-server/test-db/Tables/Users.sql << 'EOF'
CREATE TABLE [dbo].[Users] (
    [Id] INT IDENTITY(1,1) PRIMARY KEY,
    [Username] NVARCHAR(50) NOT NULL,
    [Email] NVARCHAR(100) NOT NULL,
    [LastLogin] DATETIME2 NULL
);
EOF

# Create an index
cat > servers/test-server/test-db/Indexes/IX_Users_Email.sql << 'EOF'
CREATE UNIQUE INDEX [IX_Users_Email] ON [dbo].[Users] ([Email]);
EOF

echo "Schema changes created. Files would be processed by migration generator..."
echo
echo "Expected output structure:"
echo "  $MIGRATIONS_DIR/"
echo "    └── _[timestamp]_testuser_1tables_1indexes.sql"
echo "  $REVERSE_DIR/"
echo "    └── _[timestamp]_testuser_1tables_1indexes.sql"
echo
echo "The reverse migration would contain:"
echo "  - DROP INDEX [IX_Users_Email] ON [dbo].[Users];"
echo "  - ALTER TABLE [dbo].[Users] DROP COLUMN [LastLogin];"
echo
echo "This allows manual rollback of the migration if needed."

# Clean up
cd ..
rm -rf $TEST_DIR