using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using LiteDB;

namespace Ab5021Benchmarks;

internal static class Program
{
    private static int Main(string[] args)
    {
        var config = args[0];
        var round = int.Parse(args[1]);
        var output = Path.GetFullPath(args[2]);
        var root = Path.Combine(Path.GetTempPath(), "litedb-ab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Every measured child gets the same untimed, scaled workload first.
            // Tiered compilation is disabled by the orchestrator for optimized first JITs.
            new Workload(config, Path.Combine(root, "warmup"), 2000, 100, false).Run();
            var workload = new Workload(config, Path.Combine(root, "measured"), 100000, 5000, true);
            workload.Run();
            var assembly = typeof(LiteDatabase).Assembly;
            var result = new
            {
                config, round,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                runtime = RuntimeInformation.FrameworkDescription,
                framework = AppContext.TargetFrameworkName,
                cpuCount = Environment.ProcessorCount,
                tempPath = Path.GetTempPath(),
                assembly = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
                sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                devCommit = "b3c4406231908c6ca29f6d4b2769aa08351d6fc2",
                seed = 5021,
                checkpointPages = 1000,
                samples = workload.Samples
            };
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public sealed class Document
{
    public int Id { get; set; }
    public int Key { get; set; }
    public int Value { get; set; }
    public string Payload { get; set; } = "";
}

internal sealed record Sample(string scenario, double milliseconds, int operations, long dataBytes, long directoryBytes);
