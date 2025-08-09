@echo off
REM Local Docker publish script for SQL Server Schema Migrator
REM This script builds and publishes Docker image using Dockerfile
REM Includes both DACPAC Runner and Exclusion Manager tools

setlocal enabledelayedexpansion

REM Configuration
set REGISTRY=ghcr.io
set IMAGE_NAME=gepha-geo/sqlserver-schema-migrator

REM Read version from VERSION file
set /p VERSION=<VERSION
echo Using version: %VERSION%

REM Ensure you're logged in to GitHub Container Registry
echo.
echo === Step 1: Login to GitHub Container Registry ===
echo Please ensure you're logged in to ghcr.io
echo If not, run: docker login ghcr.io -u YOUR_GITHUB_USERNAME
echo.
@REM pause

REM Run tests before building Docker image
echo.
echo === Step 2: Running tests ===
dotnet test --configuration Release --verbosity normal
if %ERRORLEVEL% neq 0 (
    echo Warning: Tests failed, but continuing with build
    REM Uncomment the next line to stop on test failure
    REM exit /b 1
)

REM Build Docker image using Dockerfile
echo.
echo === Step 3: Building Docker image ===
echo Building multi-stage Docker image with DACPAC Runner and Exclusion Manager...
docker build -t %REGISTRY%/%IMAGE_NAME%:%VERSION% -t %REGISTRY%/%IMAGE_NAME%:latest .
if %ERRORLEVEL% neq 0 (
    echo Error: Failed to build Docker image
    exit /b 1
)

REM Test the image
echo.
echo === Step 4: Testing Docker image ===
echo Testing DACPAC Runner (default entrypoint)...
docker run --rm %REGISTRY%/%IMAGE_NAME%:latest
echo Note: The above command is expected to show usage error

echo.
echo Testing Exclusion Manager...
docker run --rm --entrypoint exclusion-manager %REGISTRY%/%IMAGE_NAME%:latest --help
echo Note: The above command should show Exclusion Manager help

REM Push images to registry
echo.
echo === Step 5: Pushing images to registry ===
echo Pushing version tag...
docker push %REGISTRY%/%IMAGE_NAME%:%VERSION%
if %ERRORLEVEL% neq 0 (
    echo Error: Failed to push version tag
    exit /b 1
)

echo Pushing latest tag...
docker push %REGISTRY%/%IMAGE_NAME%:latest
if %ERRORLEVEL% neq 0 (
    echo Error: Failed to push latest tag
    exit /b 1
)

REM Display image info
echo.
echo === Step 6: Image information ===
docker images %REGISTRY%/%IMAGE_NAME%
echo.
echo To inspect the image, run:
echo   docker inspect %REGISTRY%/%IMAGE_NAME%:latest
echo.
echo To test dotnet-script in the container, run:
echo   docker run --rm %REGISTRY%/%IMAGE_NAME%:latest dotnet-script --version
echo.
echo === Build and publish completed successfully! ===
echo Images created and pushed:
echo   - %REGISTRY%/%IMAGE_NAME%:%VERSION%
echo   - %REGISTRY%/%IMAGE_NAME%:latest
echo.
echo Available tools in the image:
echo   - DACPAC Runner (default entrypoint)
echo   - Exclusion Manager (via exclusion-manager command)
echo   - dotnet-script (for running C# scripts)
echo   - git (for repository operations)
echo   - docker (for container operations)
pause
endlocal