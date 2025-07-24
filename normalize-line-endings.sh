#!/bin/bash
# Script to normalize line endings in the output directory

OUTPUT_DIR="$1"

if [ -z "$OUTPUT_DIR" ]; then
    echo "Usage: ./normalize-line-endings.sh <output-directory>"
    echo "Example: ./normalize-line-endings.sh /mnt/c/Users/petre.chitashvili/repos/gepha/db_comparison"
    exit 1
fi

cd "$OUTPUT_DIR" || exit 1

echo "Normalizing line endings in: $OUTPUT_DIR"

# Remove all files from git index
git rm --cached -r . 2>/dev/null || true

# Re-add all files - this will apply the .gitattributes rules
git add .

echo "Line endings normalized. Check with 'git status' to see the changes."