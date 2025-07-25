#!/bin/bash

echo "Testing Docker container build locally..."

# Build the container locally
echo "Building container..."
dotnet publish SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner \
  --os linux \
  --arch x64 \
  -c Release \
  /t:PublishContainer \
  -p:ContainerRepository=sqlserver-schema-migrator-test \
  -p:ContainerImageTag=local-test

if [ $? -eq 0 ]; then
    echo "✅ Container build successful!"
    echo ""
    echo "Testing the container..."
    
    # Test running the container (should show usage)
    docker run --rm sqlserver-schema-migrator-test:local-test
    
    echo ""
    echo "Container images:"
    docker images | grep sqlserver-schema-migrator-test
else
    echo "❌ Container build failed!"
    exit 1
fi