using System;
using System.Linq;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.CsProj;
using LiteDB.Benchmarks.Benchmarks;

namespace LiteDB.Benchmarks
{
    internal static class Program
    {
        private const string SpatialOnlyFlag = "--spatial-only";

        private static void Main(string[] args)
        {
            var spatialOnly = args?.Any(arg => string.Equals(arg, SpatialOnlyFlag, StringComparison.OrdinalIgnoreCase)) == true;
            var residualArgs = args?
                .Where(arg => !string.Equals(arg, SpatialOnlyFlag, StringComparison.OrdinalIgnoreCase))
                .ToArray() ?? Array.Empty<string>();

            var config = DefaultConfig.Instance
                .AddJob(Job.Default.WithRuntime(CoreRuntime.Core80)
                    .WithJit(Jit.RyuJit)
                    .WithToolchain(CsProjCoreToolchain.NetCoreApp80)
                    .WithGcForce(true))
                .AddDiagnoser(MemoryDiagnoser.Default)
                .AddExporter(BenchmarkReportExporter.Default, HtmlExporter.Default, MarkdownExporter.GitHub)
                .KeepBenchmarkFiles();

            if (spatialOnly)
            {
                config = config.AddFilter(new AnyCategoriesFilter(new[] { Constants.Categories.SPATIAL }));
                Console.WriteLine("Running LiteDB benchmarks with the SPATIAL category filter (--spatial-only).");
            }

            if (residualArgs.Length > 0)
            {
                BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(residualArgs, config);
                return;
            }

            BenchmarkRunner.Run(typeof(Program).Assembly, config);
        }
    }
}
