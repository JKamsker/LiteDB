using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using LiteDB;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Ab5021Benchmarks;

internal static class Program
{
    private static readonly Type? Probe = typeof(LiteDatabase).Assembly.GetType("LiteDB.P1KProbe");
    private static readonly Action? BeginProbe = Probe?.GetMethod("Begin")?.CreateDelegate<Action>();
    private static readonly Func<string>? EndProbe = Probe?.GetMethod("End")?.CreateDelegate<Func<string>>();

    private static int Main(string[] args)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
            throw new InvalidOperationException("P1-K workloads run only on GitHub Actions.");
        var config = args[0];
        var root = Path.Combine(Path.GetTempPath(), "litedb-p1k-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var random = new Random(5021);
            var docs = Enumerable.Range(1, 5000).Select(id => new Document
            {
                Id = id, Key = id * 17, Value = random.Next(1000),
                Payload = Convert.ToHexString(Enumerable.Range(0, 48).Select(_ => (byte)random.Next(256)).ToArray())
            }).ToArray();
            Run(config, Path.Combine(root, "warmup.db"), docs, false);
            var repetitions = int.Parse(Environment.GetEnvironmentVariable("P1D_REPETITIONS") ?? "1");
            var samples = new List<object>();
            for (var i = 0; i < repetitions; i++)
                samples.Add(Run(config, Path.Combine(root, $"measured-{i}.db"), docs, true));
            var assembly = typeof(LiteDatabase).Assembly;
            File.WriteAllText(args[2], JsonSerializer.Serialize(new
            {
                config, round = int.Parse(args[1]), samples,
                os = RuntimeInformation.OSDescription, runtime = RuntimeInformation.FrameworkDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                libraryRef = Environment.GetEnvironmentVariable("P1D_LIBRARY_REF"),
                sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
                tempPath = Path.GetTempPath(), settings = Settings.Describe(config),
                drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => new
                    { d.Name, d.DriveFormat, driveType = d.DriveType.ToString() }).ToArray(),
                instrumentation = Probe != null, seed = 5021, repetitions
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        finally { Directory.Delete(root, true); }
    }

    private static object Run(string config, string path, Document[] docs, bool record)
    {
        var connection = new ConnectionString
        {
            Filename = path, Connection = ConnectionType.Direct,
            Collation = new Collation(127, System.Globalization.CompareOptions.Ordinal)
        };
#if DEV
        Settings.Configure(connection, config);
#endif
        double ms, cpuMs;
        long allocated;
        JsonElement? components = null;
        using (var db = new LiteDatabase(connection))
        {
            db.CheckpointSize = 0;
            Require(db.CheckpointSize == 0, "CP0");
            var col = db.GetCollection<Document>("documents");
            col.EnsureIndex(x => x.Key, true);
            using var process = Process.GetCurrentProcess();
            // Probe activation is outside the timer and stable stack boundary.
            if (record) Console.Error.WriteLine("P1D_BEGIN single_insert_5k_tx");
            var cpu = process.TotalProcessorTime;
            allocated = GC.GetTotalAllocatedBytes(true);
            if (record) BeginProbe?.Invoke();
            var watch = Stopwatch.StartNew();
            Measurement.Run("single_insert_5k_tx", () => Insert(col, docs));
            watch.Stop();
            if (record && EndProbe != null)
                components = JsonSerializer.Deserialize<JsonElement>(EndProbe());
            ms = watch.Elapsed.TotalMilliseconds;
            cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            if (record) Console.Error.WriteLine("P1D_END single_insert_5k_tx");
            Verify(col, docs);
            // Full verification, explicit checkpoint, close and reopen stay outside the timer.
            db.Checkpoint();
        }
        using (var db = new LiteDatabase(connection)) Verify(db.GetCollection<Document>("documents"), docs);
        return new { scenario = "single_insert_5k_tx", milliseconds = ms, operations = docs.Length,
            cpuMs, allocatedBytes = allocated, components };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Insert(ILiteCollection<Document> col, Document[] docs)
    {
        foreach (var doc in docs) col.Insert(doc);
    }

    private static void Verify(ILiteCollection<Document> col, Document[] expected)
    {
        var actual = col.FindAll().OrderBy(d => d.Id).ToArray();
        Require(actual.Length == expected.Length, "count");
        for (var i = 0; i < actual.Length; i++)
        {
            var a = actual[i]; var e = expected[i];
            Require(a.Id == e.Id && a.Key == e.Key && a.Value == e.Value && a.Payload == e.Payload, "contents");
        }
        foreach (var id in new[] { 1, 2500, 5000 })
            Require(col.FindOne(Query.EQ("Key", id * 17))?.Id == id, "index");
    }

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}

public sealed class Document
{
    public int Id { get; set; }
    public int Key { get; set; }
    public int Value { get; set; }
    public string Payload { get; set; } = "";
}
