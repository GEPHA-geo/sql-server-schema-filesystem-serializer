#!/usr/bin/env dotnet-script

#r "nuget: System.Diagnostics.Process, 4.3.0"

using System;
using System.Diagnostics;
using System.IO;
using System.Text;

// Claude Code Stop Hook - Advanced Implementation Verification
// This script runs when Claude Code stops execution and automatically checks implementation status

var issues = new List<string>();
var warnings = new List<string>();

// 1. Check if solution builds
Console.WriteLine("🔍 Checking build status...");
var buildResult = RunCommand("dotnet", "build --no-restore");
if (buildResult.ExitCode != 0)
{
    issues.Add("❌ Build failed with errors");
}
else if (buildResult.Output.Contains("Warning"))
{
    warnings.Add("⚠️  Build succeeded but has warnings");
}
else
{
    Console.WriteLine("✅ Build succeeded without errors or warnings");
}

// 2. Check if tests pass
Console.WriteLine("\n🔍 Checking test status...");
var testResult = RunCommand("dotnet", "test --no-build");
if (testResult.ExitCode != 0)
{
    issues.Add("❌ Some tests are failing");
}
else
{
    Console.WriteLine("✅ All tests pass");
}

// 3. Check for TODO comments
Console.WriteLine("\n🔍 Checking for TODO comments...");
var todoCount = 0;
var csFiles = Directory.GetFiles(".", "*.cs", SearchOption.AllDirectories)
    .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/"));

foreach (var file in csFiles)
{
    var content = File.ReadAllText(file);
    var todos = System.Text.RegularExpressions.Regex.Matches(content, @"//\s*TODO", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    todoCount += todos.Count;
}

if (todoCount > 0)
{
    warnings.Add($"⚠️  Found {todoCount} TODO comments in code");
}

// Generate response based on findings
var response = new StringBuilder();

if (issues.Any() || warnings.Any())
{
    response.AppendLine("\n🚨 **Implementation Status Check**\n");
    
    if (issues.Any())
    {
        response.AppendLine("**Critical Issues Found:**");
        foreach (var issue in issues)
        {
            response.AppendLine(issue);
        }
        response.AppendLine();
    }
    
    if (warnings.Any())
    {
        response.AppendLine("**Warnings:**");
        foreach (var warning in warnings)
        {
            response.AppendLine(warning);
        }
        response.AppendLine();
    }
    
    response.AppendLine("**Please address the above issues and run the following verification:**\n");
}

// Always append the full checklist
response.Append(@"
Please verify the implementation is complete by answering these questions:

1. Build Status:
   - Does the entire solution build without errors? " + (issues.Any(i => i.Contains("Build")) ? "❌" : "✅") + @"
   - Are there any compiler warnings? " + (warnings.Any(w => w.Contains("Build")) ? "⚠️" : "✅") + @"
   - Run: `dotnet build` and show me the output

2. Test Status:
   - Do all existing tests still pass? " + (issues.Any(i => i.Contains("tests")) ? "❌" : "✅") + @"
   - Have you written tests for all new functionality?
   - What is the test coverage for the new code?
   - Run: `dotnet test` and show me the results

3. Implementation Completeness:
   - Have all requested features been implemented?
   - Are there any TODO comments left in the code? " + (todoCount > 0 ? $"⚠️ ({todoCount} found)" : "✅") + @"
   - Show me a summary of all files that were created or modified

4. Integration:
   - Does the new code integrate properly with existing code?
   - Have all necessary interfaces been implemented?
   - Are all dependencies properly injected?

5. Error Handling:
   - How does the code handle error scenarios?
   - What happens if inputs are invalid?
   - Are errors logged appropriately?

6. Documentation:
   - Are all public methods documented with XML comments?
   - Is there user documentation for new features?
   - Have you updated any relevant README files?

7. Manual Testing:
   - Can you demonstrate the feature working end-to-end?
   - Show me example input and output
   - What edge cases have you tested?

8. Code Quality:
   - Does the code follow the project's coding standards?
   - Are there any code smells or potential issues?
   - Is the code maintainable and readable?

9. Performance:
   - Are there any performance implications?
   - Does the code handle large datasets efficiently?
   - Are there any potential memory leaks?

10. Final Verification:
    - Is there anything from the original requirements that hasn't been addressed?
    - Are there any known limitations or issues?
    - What should be tested manually after deployment?

**IMPORTANT**: " + (issues.Any() ? "❌ Critical issues must be resolved before stopping!" : "If any of the above items are not complete, please continue working on them.") + @"

Only stop when:
- All tests pass
- The solution builds without errors
- All requested features are implemented
- Documentation is complete

If work remains, please continue implementing. If everything is complete, provide a summary of what was accomplished.
");

Console.WriteLine(response.ToString());

// Return non-zero exit code if there are critical issues
return issues.Any() ? 1 : 0;

// Helper function to run commands
(int ExitCode, string Output) RunCommand(string command, string arguments)
{
    try
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        
        return (process.ExitCode, output + error);
    }
    catch (Exception ex)
    {
        return (1, $"Error running command: {ex.Message}");
    }
}