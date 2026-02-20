# How Integration Tests Work

## What is an Integration Test?

**Unit Test**: Tests a single component in isolation
- Example: `RealmPointParser.TryParse()` with a single line

**Integration Test**: Tests multiple components working together
- Example: Log file → LogWatcher → Parser → Summary → Verify percentages

---

## Architecture of Our Integration Tests

### Flow Diagram
```
┌─────────────┐
│  Test Log   │ Temporary file with realistic log content
│    File     │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ LogWatcher  │ Reads file, detects changes
└──────┬──────┘
       │
       ▼
┌─────────────┐
│   Parser    │ Parses RP lines, handles state machine
│ (Internal)  │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│  LogLine    │ Contains RealmPointEntry if parsed successfully
│   Object    │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│TestSummary  │ Mimics UI behavior: accumulates RPs, calculates %
└──────┬──────┘
       │
       ▼
┌─────────────┐
│  Assertions │ Verify percentages sum to 100%, RPs match expected
└─────────────┘
```

---

## Key Components

### 1. Test Log File Creation
```csharp
var logContent = @"*** Chat Log Opened: Mon Jan 15 10:00:00 2024
[10:00:01] You get 1000 realm points!
[10:00:02] Some other log line  // Triggers parser state machine
[10:00:05] You get 500 realm points for Campaign Quest!
";
await File.WriteAllTextAsync(testLogFilePath, logContent);
```

**Why temporary files?**
- Isolated: Each test gets its own file
- Clean: Automatically deleted after test
- Fast: Small synthetic logs vs. real 800KB files

### 2. TestSummary Helper Class
```csharp
private class TestSummary
{
    public int TotalRealmPoints { get; private set; }
    public int PlayerKillsRP { get; private set; }
    // ... other categories

    public void ProcessEntry(RealmPointEntry entry)
    {
        TotalRealmPoints += entry.Points;

        switch (entry.Source)
        {
            case RealmPointSource.PlayerKill:
                PlayerKillsRP += entry.Points;
                break;
            // ... handle all sources
        }
    }

    // Percentage properties (same logic as UI)
    public double PlayerKillsPercentage =>
        TotalRealmPoints > 0 ? (PlayerKillsRP * 100.0 / TotalRealmPoints) : 0;
}
```

**Why create TestSummary?**
- Mimics `MainWindowViewModel.ProcessLogLine()` behavior
- No dependency on Avalonia UI framework
- Tests the **exact same logic** the UI uses

### 3. Critical Validation
```csharp
// After processing all entries...

// Validation #1: Individual RPs sum to Total
summary.IndividualRPsSum.Should().Be(summary.TotalRealmPoints);
// If this fails → Bug in ProcessEntry switch statement

// Validation #2: Percentages sum to 100%
summary.TotalPercentage.Should().BeApproximately(100.0, 0.01);
// If this fails → Bug in percentage formulas
```

---

## Test Scenarios Explained

### Test 1: Mixed RP Sources
**Purpose**: Realistic scenario with all RP types

```csharp
[Fact]
public async Task FullFlow_WithMixedRPSources_PercentagesSumTo100()
{
    // Creates log with: kills, quests, ticks, siege, etc.
    var logContent = CreateRealisticLogFile();

    // Process entire log
    await foreach (var logLine in watcher.WatchAsync(cts.Token))
    {
        if (logLine.RealmPointEntry != null)
            summary.ProcessEntry(logLine.RealmPointEntry);
    }

    // Verify consistency
    summary.IndividualRPsSum.Should().Be(summary.TotalRealmPoints);
    summary.TotalPercentage.Should().BeApproximately(100.0, 0.01);
}
```

**What it catches**:
- ❌ Missing switch cases in ProcessEntry
- ❌ Parser failing to emit certain sources
- ❌ Accumulated total ≠ sum of categories

---

### Test 2: Single Source (100%)
**Purpose**: Edge case - only one category should be 100%

```csharp
var logContent = @"
[10:00:01] You get 1000 realm points!
[10:00:02] Random log line  // CRITICAL: Triggers parser
[10:00:05] You get 500 realm points!
[10:00:06] Random log line
";
```

**Why the "random" lines?**

The `RealmPointParser` uses a **state machine** for player kills:
1. Sees `"You get X realm points!"` (no reason)
2. **Waits** for next line to check for participation percentage
3. Next line doesn't match → Emits as `PlayerKill`

Without the trigger line, the last entry is **never emitted** (still pending)!

**What it catches**:
- ❌ Parser not emitting final entry
- ❌ Wrong percentage calculation for 100% scenario

---

### Test 3: Bonus Lines Exclusion
**Purpose**: Verify bonuses don't inflate totals

```csharp
var logContent = @"
[10:00:00] You get 100 realm points for Campaign Quest!
[10:00:00] You get an additional 20 realm points due to your realm rank!  // SKIP
[10:00:00] You get an additional 10 realm points due to your guild's buff! // SKIP
";

// Expected: Only 100 RPs counted
summary.TotalRealmPoints.Should().Be(100);
```

**What it catches**:
- ❌ Parser not filtering bonus lines
- ❌ Bonuses being added to totals (inflates percentages)

---

### Test 4: All RP Sources
**Purpose**: Every source type gets at least one entry

```csharp
// Forces coverage of ALL switch cases
[10:00:00] You get 1000 realm points!                     // PlayerKill
[10:00:05] You get 500 realm points for Campaign Quest!   // CampaignQuest
[10:00:10] You get 2000 realm points for Battle Tick!     // Tick
[10:00:15] You get 300 realm points for Tower Capture!    // Siege
[10:00:20] You get 100 realm points for Assault Order!    // AssaultOrder
[10:00:25] You get 50 realm points for support activity!  // SupportActivity
[10:00:30] Albion has stored the Strength of Hibernia     // Relic marker
[10:00:31] You get 5000 realm points!                     // RelicCapture
```

**What it catches**:
- ❌ Missing enum case in switch statement
- ❌ Parser not detecting specific sources

---

### Test 5: Real Log Sample
**Purpose**: Test with actual patterns from production log

```csharp
// Uses real lines from chat_example.log
var logContent = @"
[20:58:00] You get an additional 7 realm points for your support activity in battle!
[20:58:21] You get 6 realm points!
[21:08:59] You get 16 realm points for your efforts in the Tower Capture!
// ... etc
";
```

**What it catches**:
- ❌ Regex patterns don't match real logs
- ❌ Unexpected log formats cause parsing failures

---

## How to Diagnose Failures

### Failure: "Individual RPs sum to X, but Total is Y"

**Root Cause**: Bug in `ProcessEntry` logic

```csharp
// Example bug:
switch (entry.Source)
{
    case RealmPointSource.PlayerKill:
        PlayerKillsRP += entry.Points;
        break;
    case RealmPointSource.CampaignQuest:
        // BUG: Forgot to add to CampaignQuestsRP!
        break;
}
// Total gets incremented, but category doesn't → Mismatch!
```

**Fix**: Ensure every switch case adds to its category RP variable

---

### Failure: "Percentages sum to X%, expected 100%"

**Two possible causes:**

1. **Accumulation bug** (above) → Fix ProcessEntry
2. **Percentage formula bug**:
   ```csharp
   // Wrong:
   public double PlayerKillsPercentage => PlayerKillsRP * 100.0 / TotalEntries;

   // Correct:
   public double PlayerKillsPercentage => PlayerKillsRP * 100.0 / TotalRealmPoints;
   ```

---

### Failure: "Expected 2250 RPs, but found 1000"

**Root Cause**: Parser state machine not emitting all entries

```csharp
// Test log:
[10:00:01] You get 1000 realm points!
[10:00:05] You get 500 realm points!
[10:00:10] You get 750 realm points!  // LAST ENTRY PENDING!

// Total: 1000 + 500 + (not emitted) = 1500
```

**Fix**: Add trigger lines after player kills
```csharp
[10:00:01] You get 1000 realm points!
[10:00:02] Random line  // Triggers emission
[10:00:05] You get 500 realm points!
[10:00:06] Random line
[10:00:10] You get 750 realm points!
[10:00:11] Random line  // Emits last entry
```

---

## Benefits of Integration Tests

✅ **Catches bugs unit tests miss**
- State machine edge cases
- Component interaction issues
- End-to-end flow problems

✅ **Validates real-world scenarios**
- Mixed RP sources
- Bonus line filtering
- Parser + accumulation working together

✅ **Documents expected behavior**
- Tests serve as usage examples
- Shows how components should interact

✅ **Fast feedback**
- 54 tests run in ~25 seconds
- Catches percentage bugs immediately
- No need to run full UI to test calculations

---

## Running Integration Tests

```bash
# All integration tests
dotnet test --filter "FullyQualifiedName~EndToEndTests"

# Specific test
dotnet test --filter "FullFlow_WithMixedRPSources"

# All tests (unit + integration)
dotnet test
```

---

## Summary

**Integration tests verify the complete flow**:
1. ✅ LogWatcher reads file correctly
2. ✅ Parser handles all RP sources
3. ✅ State machine emits entries properly
4. ✅ Summary accumulates totals correctly
5. ✅ Percentages calculate accurately
6. ✅ **Percentages always sum to 100%**

If your UI shows percentages not summing to 100%, these tests will reveal exactly where the bug is!
