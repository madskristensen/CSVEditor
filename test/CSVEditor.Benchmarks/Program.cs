using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace BenchmarkSuite1
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Summary[] _ = BenchmarkRunner.Run(typeof(Program).Assembly);
        }
    }
}
