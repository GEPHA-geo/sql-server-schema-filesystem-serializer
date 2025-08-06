#!/usr/bin/env dotnet-script

// Simple stop hook that prompts for implementation verification

Console.WriteLine(@"
⚠️  IMPLEMENTATION VERIFICATION REQUIRED

Before stopping, please verify:

1. ✓ Run `dotnet build` - does it succeed without errors?
2. ✓ Run `dotnet test` - do all tests pass?  
3. ✓ Check for TODO comments - are there any left?
4. ✓ Review modified files - is everything implemented?
5. ✓ Test the feature - does it work end-to-end?

If any items above are not complete, please continue working.

Respond with either:
- 'CONTINUE' to keep working on incomplete items
- 'COMPLETE: [summary]' if everything is done
");

// Exit with code 1 to indicate verification needed
// This should prevent Claude Code from stopping until verification is complete
Environment.Exit(1);