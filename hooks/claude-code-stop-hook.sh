#!/bin/bash

# Claude Code Stop Hook - Implementation Verification
# This script runs when Claude Code stops execution and asks it to verify implementation completeness

cat << 'EOF'
Please verify the implementation is complete by answering these questions:

1. Build Status:
   - Does the entire solution build without errors?
   - Are there any compiler warnings?
   - Run: `dotnet build` and show me the output

2. Test Status:
   - Do all existing tests still pass?
   - Have you written tests for all new functionality?
   - What is the test coverage for the new code?
   - Run: `dotnet test` and show me the results

3. Implementation Completeness:
   - Have all requested features been implemented?
   - Are there any TODO comments left in the code?
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

**IMPORTANT**: If any of the above items are not complete, please continue working on them. Only stop when:
- All tests pass
- The solution builds without errors
- All requested features are implemented
- Documentation is complete

If work remains, please continue implementing. If everything is complete, provide a summary of what was accomplished.
EOF