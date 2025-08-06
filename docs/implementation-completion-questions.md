# Standard Implementation Completion Questions

When Claude Code stops working on a feature implementation, ask these questions to ensure completeness:

## Copy and paste this when Claude Code stops:

```
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
```

## Alternative shorter version:

```
Please confirm implementation is complete:

1. Run `dotnet build` - any errors?
2. Run `dotnet test` - all tests pass?
3. Show me what files you created/modified
4. Demonstrate the feature working with a real example
5. Any TODOs or unfinished work?
6. What should I test manually?
```

## For specific feature implementations, add:

```
Additionally for this feature:
- Show me the [specific output/file/result] that was requested
- Run the specific scenario: [describe scenario]
- Verify [specific requirement] is working
```

*Collaboration by Claude*