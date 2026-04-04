using BenchmarkDotNet.Running;

namespace DAoCLogWatcher.Benchmarks;

class Program
{
	static void Main(string[] args)
	{
		var summary = BenchmarkRunner.Run<ParserBenchmarks>();
	}
}
