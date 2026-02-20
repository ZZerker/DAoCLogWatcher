using BenchmarkDotNet.Running;

namespace DAoCLogWatcher.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        // Run all benchmarks
        var summary = BenchmarkRunner.Run<ParserBenchmarks>();

        // Uncomment to run specific benchmark:
        // var summary = BenchmarkRunner.Run<ParserBenchmarks>(
        //     BenchmarkDotNet.Configs.DefaultConfig.Instance
        //         .WithOptions(BenchmarkDotNet.Configs.ConfigOptions.DisableOptimizationsValidator));
    }
}
