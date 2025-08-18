using Xunit;
using SqlServer.Schema.Migration.Generator.GitIntegration;
using System.IO;
using System.Diagnostics;

namespace SqlServer.Schema.Migration.Generator.Tests;

public class GitDiffAnalyzerTests : IDisposable
{
    readonly GitDiffAnalyzer _analyzer = new();
    readonly string _testRepoPath;
    
    public GitDiffAnalyzerTests()
    {
        // Create a temporary directory for testing
        _testRepoPath = Path.Combine(Path.GetTempPath(), $"GitDiffAnalyzerTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRepoPath);
    }
    
    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testRepoPath))
        {
            try
            {
                // Remove readonly attributes if any
                var di = new DirectoryInfo(_testRepoPath);
                RemoveReadOnlyAttributes(di);
                Directory.Delete(_testRepoPath, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
    
    void RemoveReadOnlyAttributes(DirectoryInfo directory)
    {
        foreach (var file in directory.GetFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }
        foreach (var dir in directory.GetDirectories("*", SearchOption.AllDirectories))
        {
            dir.Attributes = FileAttributes.Normal;
        }
    }
    
    [Fact]
    public void IsGitRepository_WhenNotGitRepo_ShouldReturnFalse()
    {
        // Act
        var result = _analyzer.IsGitRepository(_testRepoPath);
        
        // Assert
        Assert.False(result);
    }
    
    [Fact]
    public void IsGitRepository_WhenGitRepo_ShouldReturnTrue()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_testRepoPath, ".git"));
        
        // Act
        var result = _analyzer.IsGitRepository(_testRepoPath);
        
        // Assert
        Assert.True(result);
    }
    
    [Fact]
    public void InitializeRepository_ShouldCreateGitRepoAndGitignore()
    {
        // Act
        _analyzer.InitializeRepository(_testRepoPath);
        
        // Assert
        Assert.True(Directory.Exists(Path.Combine(_testRepoPath, ".git")));
        Assert.True(File.Exists(Path.Combine(_testRepoPath, ".gitignore")));
        
        var gitignoreContent = File.ReadAllText(Path.Combine(_testRepoPath, ".gitignore"));
        Assert.Contains("*.tmp", gitignoreContent);
        Assert.Contains("generated_script.sql", gitignoreContent);
    }
    
    void ConfigureGitUser()
    {
        // Configure git user for the test repository
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = _testRepoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // Set user email
                Arguments = "config user.email \"test@example.com\""
            };

            using (var process = Process.Start(startInfo))
            {
                process?.WaitForExit();
            }
            
            // Set user name
            startInfo.Arguments = "config user.name \"Test User\"";
            using (var process = Process.Start(startInfo))
            {
                process?.WaitForExit();
            }
        }
        catch
        {
            // Ignore errors - tests will fail if git is not configured properly
        }
    }
    
    
    [Fact]
    public void GetUncommittedChanges_WithNewFile_ShouldDetectAddedFile()
    {
        // Arrange
        _analyzer.InitializeRepository(_testRepoPath);
        
        // Create database directory and SQL file
        var dbDir = Path.Combine(_testRepoPath, "TestDB", "schemas", "dbo", "Tables", "Customer");
        Directory.CreateDirectory(dbDir);
        var sqlFile = Path.Combine(dbDir, "TBL_Customer.sql");
        File.WriteAllText(sqlFile, "CREATE TABLE [dbo].[Customer] ([Id] INT)");
        
        // Act
        var changes = _analyzer.GetUncommittedChanges(_testRepoPath, "TestDB");
        
        // Assert
        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal(ChangeType.Added, change.ChangeType);
        Assert.Contains("TBL_Customer.sql", change.Path);
        Assert.Contains("CREATE TABLE", change.NewContent);
        Assert.Empty(change.OldContent);
    }
    
    [Fact]
    public void GetUncommittedChanges_WithModifiedFile_ShouldDetectModification()
    {
        // Arrange
        _analyzer.InitializeRepository(_testRepoPath);
        ConfigureGitUser();
        
        // Create and commit initial file
        var dbDir = Path.Combine(_testRepoPath, "TestDB", "schemas", "dbo", "Tables", "Customer");
        Directory.CreateDirectory(dbDir);
        var sqlFile = Path.Combine(dbDir, "TBL_Customer.sql");
        File.WriteAllText(sqlFile, "CREATE TABLE [dbo].[Customer] ([Id] INT)");
        _analyzer.CommitChanges(_testRepoPath, "Initial commit");
        
        // Modify file
        File.WriteAllText(sqlFile, "CREATE TABLE [dbo].[Customer] ([Id] INT, [Name] NVARCHAR(100))");
        
        // Act
        var changes = _analyzer.GetUncommittedChanges(_testRepoPath, "TestDB");
        
        // Assert
        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal(ChangeType.Modified, change.ChangeType);
        Assert.Contains("TBL_Customer.sql", change.Path);
        Assert.Contains("[Id] INT)", change.OldContent);
        Assert.Contains("[Name] NVARCHAR(100)", change.NewContent);
    }
    
    [Fact]
    public void GetUncommittedChanges_WithDeletedFile_ShouldDetectDeletion()
    {
        // Arrange
        _analyzer.InitializeRepository(_testRepoPath);
        ConfigureGitUser();
        
        // Create and commit initial file
        var dbDir = Path.Combine(_testRepoPath, "TestDB", "schemas", "dbo", "Tables", "Customer");
        Directory.CreateDirectory(dbDir);
        var sqlFile = Path.Combine(dbDir, "TBL_Customer.sql");
        File.WriteAllText(sqlFile, "CREATE TABLE [dbo].[Customer] ([Id] INT)");
        _analyzer.CommitChanges(_testRepoPath, "Initial commit");
        
        // Delete file
        File.Delete(sqlFile);
        
        // Act
        var changes = _analyzer.GetUncommittedChanges(_testRepoPath, "TestDB");
        
        // Assert
        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal(ChangeType.Deleted, change.ChangeType);
        Assert.Contains("TBL_Customer.sql", change.Path);
        Assert.Contains("CREATE TABLE", change.OldContent);
        Assert.Empty(change.NewContent);
    }
    
    [Fact]
    public void GetUncommittedChanges_WithNonSqlFiles_ShouldIgnoreThem()
    {
        // Arrange
        _analyzer.InitializeRepository(_testRepoPath);
        
        // Create database directory with mixed files
        var dbDir = Path.Combine(_testRepoPath, "TestDB");
        Directory.CreateDirectory(dbDir);
        File.WriteAllText(Path.Combine(dbDir, "readme.txt"), "This is a readme");
        File.WriteAllText(Path.Combine(dbDir, "script.sql"), "SELECT 1");
        File.WriteAllText(Path.Combine(_testRepoPath, "outside.sql"), "SELECT 2");
        
        // Act
        var changes = _analyzer.GetUncommittedChanges(_testRepoPath, "TestDB");
        
        // Assert
        Assert.Single(changes); // Only the SQL file inside TestDB directory
        Assert.Equal("TestDB/script.sql", changes[0].Path);
    }
    
    [Fact]
    public void CommitChanges_ShouldCommitAllChanges()
    {
        // Arrange
        _analyzer.InitializeRepository(_testRepoPath);
        ConfigureGitUser();
        
        // Create file
        var dbDir = Path.Combine(_testRepoPath, "TestDB");
        Directory.CreateDirectory(dbDir);
        File.WriteAllText(Path.Combine(dbDir, "test.sql"), "SELECT 1");
        
        // Act
        _analyzer.CommitChanges(_testRepoPath, "Test commit");
        
        // Assert
        var changes = _analyzer.GetUncommittedChanges(_testRepoPath, "TestDB");
        Assert.Empty(changes); // All changes should be committed
    }
    
    
    [Fact]
    public void GetUncommittedChanges_WithMultipleChanges_ShouldDetectAll()
    {
        // Arrange
        _analyzer.InitializeRepository(_testRepoPath);
        ConfigureGitUser();
        
        // Create and commit initial files
        var dbDir = Path.Combine(_testRepoPath, "TestDB", "schemas", "dbo", "Tables");
        Directory.CreateDirectory(dbDir);
        
        var customerDir = Path.Combine(dbDir, "Customer");
        Directory.CreateDirectory(customerDir);
        File.WriteAllText(Path.Combine(customerDir, "TBL_Customer.sql"), "CREATE TABLE [dbo].[Customer] ([Id] INT)");
        
        var orderDir = Path.Combine(dbDir, "Order");
        Directory.CreateDirectory(orderDir);
        File.WriteAllText(Path.Combine(orderDir, "TBL_Order.sql"), "CREATE TABLE [dbo].[Order] ([Id] INT)");
        
        _analyzer.CommitChanges(_testRepoPath, "Initial commit");
        
        // Make various changes
        // 1. Modify Customer table
        File.WriteAllText(Path.Combine(customerDir, "TBL_Customer.sql"), 
            "CREATE TABLE [dbo].[Customer] ([Id] INT, [Name] NVARCHAR(100))");
        
        // 2. Delete Order table
        File.Delete(Path.Combine(orderDir, "TBL_Order.sql"));
        
        // 3. Add new Product table
        var productDir = Path.Combine(dbDir, "Product");
        Directory.CreateDirectory(productDir);
        File.WriteAllText(Path.Combine(productDir, "TBL_Product.sql"), 
            "CREATE TABLE [dbo].[Product] ([Id] INT)");
        
        // Act
        var changes = _analyzer.GetUncommittedChanges(_testRepoPath, "TestDB");
        
        // Assert
        Assert.Equal(3, changes.Count);
        
        // Verify each change type
        Assert.Contains(changes, c => c.ChangeType == ChangeType.Modified && c.Path.Contains("Customer"));
        Assert.Contains(changes, c => c.ChangeType == ChangeType.Deleted && c.Path.Contains("Order"));
        Assert.Contains(changes, c => c.ChangeType == ChangeType.Added && c.Path.Contains("Product"));
    }
}