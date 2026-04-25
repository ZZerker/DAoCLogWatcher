using BenchmarkDotNet.Running;

namespace DAoCLogWatcher.Benchmarks;

internal class Program
{
	private static void Main(string[] args)
	{
		var summary = BenchmarkRunner.Run<ParserBenchmarks>();
	}
}
