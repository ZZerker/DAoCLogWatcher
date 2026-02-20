# Recent Changes

## Parser Logging Cleanup

### Changes Made
- **Replaced `Console.WriteLine` with `Debug.WriteLine`**
  - Only outputs in Debug builds
  - No console spam in Release/benchmarks
  - Still available for debugging when needed

- **Simplified debug messages**
  - Before: `"[Parser] Matched realm point line: [10:00:00] You get 1000 realm points!"`
  - After: `"[Parser] Relic: 5000 RP"` (only for special cases)
  - Removed redundant messages for common operations

- **Removed obvious comments**
  - Kept only non-obvious logic explanations
  - Code is more readable

### Impact
✅ **All 54 tests still pass**
✅ **Benchmarks now produce readable output**
✅ **Debug capability preserved** (visible in Visual Studio Output window)
✅ **Production code cleaner** (no console output)

### Debug Output Behavior

**Debug builds** (`dotnet run` or F5 in VS):
- `Debug.WriteLine()` outputs to Debug console
- Useful for troubleshooting

**Release builds** (`dotnet run -c Release` or benchmarks):
- `Debug.WriteLine()` is no-op (compiled out)
- No performance impact
- Clean output

---

## How to See Debug Output

### Visual Studio
1. Run in Debug mode (F5)
2. View → Output window
3. Show output from: Debug

### Rider
1. Run in Debug mode
2. Debug → Show Execution Point
3. View debug output in console

### Command Line
```bash
# Debug output goes to diagnostics, not visible in console
# Use Visual Studio/Rider to see it
```

---

## Summary of Improvements

| Before | After |
|--------|-------|
| `Console.WriteLine` everywhere | `Debug.WriteLine` only for special cases |
| Every RP line logged | Only relic captures and bonuses logged |
| Verbose messages | Concise messages |
| Benchmark output flooded | Clean benchmark results |
| Comments explaining obvious code | Minimal, focused comments |

**Result**: Cleaner code, faster benchmarks, same functionality! 🎉
