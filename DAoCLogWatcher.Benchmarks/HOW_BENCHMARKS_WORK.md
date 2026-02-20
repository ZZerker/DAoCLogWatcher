# How Performance Benchmarks Work - Complete Guide

## What is BenchmarkDotNet?

**BenchmarkDotNet** is the industry-standard .NET benchmarking library used by:
- Microsoft for .NET runtime performance
- Major open-source projects (Newtonsoft.Json, Dapper, etc.)
- Performance-critical applications

---

## How It Works Under the Hood

### 1. Benchmark Discovery
```csharp
[Benchmark]
public RealmPointEntry? ParseCampaignQuest()
{
    var parser = new RealmPointParser();
    parser.TryParse(campaignQuestLine, out var entry);
    return entry;
}
```

BenchmarkDotNet scans for methods with `[Benchmark]` attribute.

---

### 2. Execution Pipeline

```
┌────────────────────┐
│   Warmup Phase     │  Run benchmark multiple times to warm up JIT
│   (Default: 1s)    │  Eliminates "first run" bias
└────────┬───────────┘
         │
         ▼
┌────────────────────┐
│   Pilot Phase      │  Determine how many iterations fit in target time
│   (Adaptive)       │  Adapts to fast/slow benchmarks
└────────┬───────────┘
         │
         ▼
┌────────────────────┐
│   Measurement      │  Run benchmark N times (default: 15-100 iterations)
│   (Multiple runs)  │  Collect timing data for each iteration
└────────┬───────────┘
         │
         ▼
┌────────────────────┐
│   Statistics       │  Calculate Mean, Median, StdDev, Min, Max
│                    │  Detect outliers, calculate confidence intervals
└────────┬───────────┘
         │
         ▼
┌────────────────────┐
│   Results Table    │  Generate formatted output with rankings
└────────────────────┘
```

---

### 3. Timing Mechanism

**Stopwatch API**:
```csharp
var sw = Stopwatch.StartNew();
for (int i = 0; i < iterations; i++)
{
    ParseCampaignQuest();  // Your benchmark
}
sw.Stop();
var avgTime = sw.Elapsed / iterations;
```

**High-precision**: Uses `QueryPerformanceCounter` on Windows (nanosecond accuracy)

---

### 4. Memory Diagnostics

With `[MemoryDiagnoser]`:

```csharp
// Before benchmark
long before = GC.GetAllocatedBytesForCurrentThread();

ParseCampaignQuest();  // Your benchmark

// After benchmark
long after = GC.GetAllocatedBytesForCurrentThread();
long allocated = after - before;
```

Tracks:
- Total bytes allocated
- GC collections (Gen 0, Gen 1, Gen 2)
- Allocation rate

---

## Anatomy of Our Benchmarks

### Simple Benchmark

```csharp
[Benchmark(Description = "Parse campaign quest (immediate)")]
public RealmPointEntry? ParseCampaignQuest()
{
    var parser = new RealmPointParser();
    parser.TryParse(campaignQuestLine, out var entry);
    return entry;
}
```

**What it measures**:
- Parser instantiation cost
- Regex matching time
- TimeOnly parsing time
- RealmPointEntry allocation
- Total end-to-end time

---

### Stateful Benchmark (State Machine)

```csharp
[Benchmark(Description = "Parse player kill (state machine)")]
public RealmPointEntry? ParsePlayerKill()
{
    var parser = new RealmPointParser();
    parser.TryParse(playerKillLine, out _);  // Pending (line 1)
    parser.TryParse(invalidLine, out var entry);  // Emit (line 2)
    return entry;
}
```

**What it measures**:
- Two-line sequence overhead
- State machine field updates
- Pending entry allocation
- Emission logic

---

### Throughput Benchmark

```csharp
[Benchmark(Description = "Parse 1000 mixed lines (realistic workload)")]
public int ParseMixedWorkload()
{
    var parser = new RealmPointParser();
    int count = 0;

    foreach (var line in testLines)  // 1000 pre-generated lines
    {
        if (parser.TryParse(line, out var entry))
        {
            count++;
        }
    }

    return count;
}
```

**What it measures**:
- Bulk processing performance
- State machine behavior over many lines
- Memory pressure from accumulation
- **Real-world scenario**

---

## Key Attributes Explained

### `[MemoryDiagnoser]`
```csharp
[MemoryDiagnoser]
public class ParserBenchmarks { ... }
```

Adds memory columns to output:
- `Allocated`: Bytes allocated per operation
- `Gen0/Gen1/Gen2`: Garbage collections triggered

**Why it matters**: Memory allocations cause GC pauses, affecting UI responsiveness.

---

### `[Orderer]`
```csharp
[Orderer(OrdererKind.Method)]
```

Controls output order:
- `Method`: Alphabetical by method name
- `FastestToSlowest`: Performance ranking

---

### `[RankColumn]`
```csharp
[RankColumn]
```

Adds rank column (1 = fastest, 2 = second, etc.)

---

### `[GlobalSetup]`
```csharp
[GlobalSetup]
public void Setup()
{
    parser = new RealmPointParser();
    testLines = GenerateMixedWorkload(1000);
}
```

Runs **once** before all benchmarks. Use for:
- Pre-loading data
- Creating reusable objects
- Expensive initialization

**Not included** in benchmark timing.

---

## Understanding Output

### Example Results

```
| Method                  | Mean      | Error     | StdDev    | Median    | Allocated |
|------------------------ |----------:|----------:|----------:|----------:|----------:|
| ParseInvalidLine        |  82.34 ns |  1.234 ns |  1.154 ns |  82.10 ns |      48 B |
| ParseCampaignQuest      | 892.45 ns | 17.456 ns | 16.323 ns | 890.20 ns |     384 B |
| ParsePlayerKill         |   1.567 μs|  0.031 μs |  0.029 μs |   1.560 μs|     512 B |
| ParseMixedWorkload      |   1.234 ms|  0.023 ms |  0.021 ms |   1.230 ms|  25,000 B |
```

---

### Column Meanings

#### **Mean**
Average execution time across all iterations.

**Calculation**:
```
Mean = Sum(all execution times) / Number of iterations
```

**Example**: If 100 iterations took 89,245 ns total:
```
Mean = 89,245 ns / 100 = 892.45 ns
```

---

#### **Error**
Standard error of the mean (SEM) - measurement uncertainty.

**Formula**:
```
Error = StdDev / √(sample size)
```

**Interpretation**:
- Small error (< 5% of Mean) = reliable measurement
- Large error = unstable performance or insufficient iterations

---

#### **StdDev**
Standard deviation - how much times vary.

**Formula**:
```
StdDev = √(Σ(time - Mean)² / N)
```

**Interpretation**:
- Low StdDev = consistent performance
- High StdDev = inconsistent (caching, GC, background processes)

---

#### **Median**
Middle value when sorted. Less affected by outliers than Mean.

**Use case**: If Mean ≠ Median significantly, there are outliers.

---

#### **Allocated**
Total bytes allocated on managed heap per operation.

**Includes**:
- Object allocations (`new RealmPointEntry`)
- String allocations (if any)
- Boxing allocations
- Array allocations

**Excludes**:
- Stack allocations (structs, local variables)
- Unmanaged memory

---

## Time Units

```
1 second (s)      = 1,000 milliseconds (ms)
1 millisecond (ms) = 1,000 microseconds (μs)
1 microsecond (μs) = 1,000 nanoseconds (ns)
```

### Context

| Operation | Typical Time |
|-----------|--------------|
| CPU cycle (3 GHz) | ~0.3 ns |
| L1 cache access | ~1 ns |
| L2 cache access | ~3 ns |
| Main memory access | ~100 ns |
| String comparison (10 chars) | ~50 ns |
| Regex match (simple) | ~500 ns |
| Small object allocation | ~10 ns |
| File I/O (SSD) | ~100 μs |
| Network round-trip (local) | ~1 ms |

**Your parser**: ~1 μs per line = **FAST** ⚡

---

## Performance Analysis Workflow

### Step 1: Establish Baseline
```bash
dotnet run -c Release > baseline.txt
```

Save results before any optimizations.

---

### Step 2: Identify Bottlenecks

Look for:
- **Slowest benchmarks** (highest Mean)
- **High allocations** (> 1 KB per operation)
- **High variance** (StdDev > 10% of Mean)

Example findings:
```
ParsePlayerKill: 1.567 μs, 512 B  ← State machine overhead
ParseInvalidLine: 82 ns, 48 B     ← Fast rejection (good!)
```

**Analysis**: Player kills are 20x slower than invalid lines. Is state machine necessary?

---

### Step 3: Hypothesize Optimization

Examples:
- "Regex is slow" → Try compiled regex
- "Too many allocations" → Use `Span<char>`, object pooling
- "State machine costly" → Simplify state tracking

---

### Step 4: Implement & Measure

```csharp
[Benchmark(Baseline = true)]
public int Original() { /* ... */ }

[Benchmark]
public int Optimized() { /* ... */ }
```

BenchmarkDotNet shows **relative** performance:
```
| Method    | Mean    | Ratio |
|---------- |--------:|------:|
| Original  | 1.50 μs | 1.00  |
| Optimized | 0.75 μs | 0.50  | ← 2x faster!
```

---

### Step 5: Validate in Real Scenario

Micro-benchmarks don't always reflect real-world performance:
- Integration tests with full pipeline
- Profiling actual application
- User experience testing

---

## Common Pitfalls

### ❌ Benchmark Too Simple
```csharp
[Benchmark]
public int AddNumbers()
{
    return 5 + 3;  // Compiler optimizes away!
}
```

**Fix**: Use `[MethodImpl(MethodImplOptions.NoInlining)]` or consume result

---

### ❌ Shared State
```csharp
private int counter = 0;

[Benchmark]
public int IncrementCounter()
{
    return counter++;  // Changes state between runs!
}
```

**Fix**: Reset state in `[IterationSetup]` or avoid mutation

---

### ❌ I/O in Benchmark
```csharp
[Benchmark]
public string ReadFile()
{
    return File.ReadAllText("test.txt");  // File I/O dominates timing
}
```

**Fix**: Preload data in `[GlobalSetup]`, benchmark parsing only

---

### ❌ Running in Debug Mode
Debug builds have:
- No optimizations
- Extra checks
- Different JIT behavior

**Always use**: `dotnet run -c Release`

---

## Advanced Techniques

### Parameterized Benchmarks
```csharp
[Benchmark]
[Arguments(100)]
[Arguments(1000)]
[Arguments(10000)]
public int ParseNLines(int lineCount)
{
    // Benchmark with different input sizes
}
```

Generates separate results for each parameter.

---

### Multiple Baselines
```csharp
[Benchmark(Baseline = true, OperationsPerInvoke = 1000)]
public void ParseMixedWorkload_V1() { /* ... */ }

[Benchmark(OperationsPerInvoke = 1000)]
public void ParseMixedWorkload_V2() { /* ... */ }
```

Compare multiple implementations side-by-side.

---

### Custom Metrics
```csharp
[Benchmark]
public CustomResult ParseWithMetrics()
{
    var parser = new RealmPointParser();
    int parsed = 0, filtered = 0;

    foreach (var line in testLines)
    {
        if (parser.TryParse(line, out var entry))
            parsed++;
        else
            filtered++;
    }

    return new CustomResult { Parsed = parsed, Filtered = filtered };
}
```

---

## Real-World Application

### Your Parser Performance

**Measured**: ~1 μs per line average
**Throughput**: 1,000,000 lines/second

### Your Use Case

**Live watching**: ~1 line every 500ms
- Required: 2 lines/second
- Available: 1,000,000 lines/second
- **Headroom**: 500,000x ✅

**Startup bulk load**: 10,000 lines
- Time: 10 ms (10,000 × 1 μs)
- **Imperceptible to user** ✅

### Conclusion
Your parser is **vastly faster** than needed. Performance is NOT a bottleneck.

---

## Summary

**How benchmarks work**:
1. Warm up JIT compiler
2. Run code many times
3. Measure with high-precision timers
4. Calculate statistics (Mean, StdDev)
5. Track memory allocations
6. Generate comparison reports

**Why they're valuable**:
- ✅ Objective performance data
- ✅ Catch regressions
- ✅ Validate optimizations
- ✅ Guide optimization efforts

**Your parser**: Fast enough for any realistic DAoC log processing! 🚀
