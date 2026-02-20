# DAoCLogWatcher Tests

Comprehensive unit test suite for the DAoCLogWatcher project.

## Structure

```
DAoCLogWatcher.Tests/
├── Core/
│   ├── Parsing/
│   │   └── RealmPointParserTests.cs    # Parser logic tests (17 tests)
│   └── LogWatcherTests.cs              # File watching tests (14 tests)
├── UI/
│   └── Models/
│       └── RealmPointSummaryTests.cs   # Calculation tests (17 tests)
└── Integration/
    └── EndToEndTests.cs                # Full flow tests (6 tests)
```

## Test Coverage (54 tests total)

### RealmPointParserTests (17 tests)
- ✅ Valid realm point line parsing (6 source types)
- ✅ Player kill two-line sequence handling
- ✅ Relic capture sequence detection
- ✅ Bonus line filtering (realm rank, guild buffs)
- ✅ Invalid line rejection
- ✅ Timestamp parsing accuracy
- ✅ Edge case point values (0, 999999)
- ✅ Multiple parser instance independence
- ✅ State machine reset after emission

### LogWatcherTests (14 tests)
- ✅ File existence validation
- ✅ Empty file handling
- ✅ Initial content reading from start
- ✅ Start position offset functionality
- ✅ Real-time file append detection
- ✅ Cancellation token support
- ✅ Chat log opened marker processing
- ✅ Last position tracking
- ✅ Incomplete line buffering
- ✅ Constructor argument validation
- ✅ Multiple disposal safety

### RealmPointSummaryTests (17 tests)
- ✅ Total entries calculation
- ✅ Percentage calculations for all 8 sources
- ✅ Percentages sum to 100% validation
- ✅ Zero total handling
- ✅ RPs per hour calculation
- ✅ Live vs old session detection
- ✅ Reset functionality
- ✅ **Critical bug detection test** for RP accumulation

### EndToEndTests (6 tests) - Integration
- ✅ Full flow: Log file → Parser → Summary
- ✅ Mixed RP sources with percentage validation
- ✅ Single source (100% verification)
- ✅ Bonus line exclusion
- ✅ All RP sources coverage
- ✅ Real log sample processing
- ✅ Empty log handling

## Running Tests

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter RealmPointParserTests

# Run with coverage (requires coverlet.msbuild)
dotnet test /p:CollectCoverage=true
```

## Technologies

- **xUnit** - Test framework
- **FluentAssertions** - Readable assertions
- **NSubstitute** - Mocking library (for future use)

## Best Practices Implemented

1. **Arrange-Act-Assert** pattern
2. **Theory tests** with InlineData for parameterized testing
3. **IDisposable** pattern for test cleanup
4. **Isolated tests** using temporary files
5. **Descriptive test names** following convention: `MethodName_Scenario_ExpectedBehavior`
6. **Async/await** proper handling for async operations

## Future Enhancements

- ViewModel tests for MainWindowViewModel
- Integration tests for UI components
- Performance benchmarks for parser
- Test coverage reporting
- Mock FileStream for faster LogWatcher tests
