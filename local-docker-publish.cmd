@echo off
REM Local Docker publish script for SQL Server Schema Migrator
REM This script mimics the GitHub Actions workflow for local testing and publishing

setlocal enabledelayedexpansion

REM Configuration
set REGISTRY=ghcr.io
set IMAGE_NAME=gepha-geo/sqlserver-schema-migrator
set PROJECT_PATH=SqlServer.Schema.FileSystem.Serializer.Dacpac.Runner

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

REM Restore dependencies
echo.
echo === Step 2: Restoring dependencies ===
dotnet restore
if %ERRORLEVEL% neq 0 (
    echo Error: Failed to restore dependencies
    exit /b 1
)

REM Build solution
echo.
echo === Step 3: Building solution ===
dotnet build --no-restore --configuration Release
if %ERRORLEVEL% neq 0 (
    echo Error: Failed to build solution
    exit /b 1
)

REM Run tests (optional - comment out if you want to skip)
echo.
echo === Step 4: Running tests ===
dotnet test --no-build --configuration Release --verbosity normal
if %ERRORLEVEL% neq 0 (
    echo Warning: Tests failed, but continuing with publish
    REM Uncomment the next line to stop on test failure
    REM exit /b 1
)

REM Build and publish container for Linux x64
echo.
echo === Step 5: Building and publishing Docker image (linux-x64) ===
dotnet publish %PROJECT_PATH% ^
    --os linux ^
    --arch x64 ^
    -c Release ^
    /t:PublishContainer ^
    -p:ContainerRegistry=%REGISTRY% ^
    -p:ContainerRepository=%IMAGE_NAME% ^
    -p:ContainerImageTags=\"%VERSION%;latest\"

if %ERRORLEVEL% neq 0 (
    echo Error: Failed to publish Docker image
    exit /b 1
)


REM Test the image
echo.
echo === Step 7: Testing Docker image ===
docker run --rm %REGISTRY%/%IMAGE_NAME%:latest
echo Note: The above command is expected to show usage error

REM Display image info
echo.
echo === Step 8: Image information ===
docker images %REGISTRY%/%IMAGE_NAME%
echo.
echo To inspect the image, run:
echo   docker inspect %REGISTRY%/%IMAGE_NAME%:latest
echo.
echo To push manually (if not automatically pushed), run:
echo   docker push %REGISTRY%/%IMAGE_NAME%:%VERSION%
echo   docker push %REGISTRY%/%IMAGE_NAME%:latest
echo.
echo === Build and publish completed successfully! ===
echo Images created:
echo   - %REGISTRY%/%IMAGE_NAME%:%VERSION%
echo   - %REGISTRY%/%IMAGE_NAME%:latest
pause
endlocal