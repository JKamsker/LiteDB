using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using JsonSerializer = System.Text.Json.JsonSerializer;
using LiteDB;
using LiteDB.Engine;

namespace WriteControllerExperiments;

internal static class Benchmark
{
    internal static void Run(string[] args)
    {
        // bench MODE CALLERS BATCH LINGER_MS WARMUP_SECONDS MEASURE_SECONDS DIRECTORY
        var mode = args[1];
        var callers = int.Parse(args[2]);
        var batch = int.Parse(args[3]);
        var linger = int.Parse(args[4]);
        var warmup = int.Parse(args[5]);
        var seconds = int.Parse(args[6]);
        Directory.CreateDirectory(args[7]);
        var file = Path.GetFullPath(Path.Combine(args[7], "writes-" + Guid.NewGuid().ToString("N") + ".db"));
        DatabaseFixture.Seed(file);
        using var direct = mode == "direct" ? DatabaseFixture.Open(file) : null;
        using var writer = mode == "queue" ? new WriteController(() => DatabaseFixture.Open(file), batch, linger) : null;
        var pipe = "ldb-exp-" + Guid.NewGuid().ToString("N");
        using var host = mode is "ipc" or "existing"
            ? new ChildProcess("host", file, mode == "existing" ? "existing" : pipe, batch.ToString(), linger.ToString()) : null;
        if (host != null && host.ReadLine() != "READY") throw new Exception("Host handshake failed.");
        var counts = new int[callers];
        var samples = Enumerable.Range(0, callers).Select(_ => new List<double>()).ToArray();
        var errors = new ConcurrentQueue<Exception>();
        using var phase = new Barrier(callers + 1);
        using var beginDrain = new ManualResetEventSlim();
        var deadline = 0L;
        var workers = Enumerable.Range(0, callers).Select(worker => new Thread(() =>
        {
            try
            {
                using var client = mode == "ipc" ? new PipeWriter(pipe) : null;
                using var local = mode == "shared" ? DatabaseFixture.Open(file, true) :
                    mode == "existing" ? new LiteDatabase(new CoordinatedEngine(file)) : null;
                for (var round = 0; round < 2; round++)
                {
                    phase.SignalAndWait(TimeSpan.FromSeconds(30));
                    do
                    {
                        var request = new WriteRequest(worker * 1000000000L + ++counts[worker], counts[worker]);
                        var start = Stopwatch.GetTimestamp();
                        if (writer != null) writer.Submit(request).GetAwaiter().GetResult();
                        else if (client != null) client.Write(request);
                        else (local ?? direct ?? throw new ArgumentException("Unknown mode")).GetCollection("rows").Insert(request.Document());
                        if (round == 1) samples[worker].Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds);
                    } while (Stopwatch.GetTimestamp() < Volatile.Read(ref deadline));
                    phase.SignalAndWait(TimeSpan.FromSeconds(60));
                }
                if (!beginDrain.Wait(TimeSpan.FromSeconds(60))) throw new TimeoutException("Drain was not released.");
            }
            catch (Exception error) { errors.Enqueue(error); }
        }) { IsBackground = true }).ToArray();
        foreach (var thread in workers) thread.Start();
        Volatile.Write(ref deadline, Stopwatch.GetTimestamp() + warmup * Stopwatch.Frequency);
        if (!phase.SignalAndWait(TimeSpan.FromSeconds(30)) || !phase.SignalAndWait(TimeSpan.FromSeconds(warmup + 60)))
            throw new Exception("Warmup stalled: " + string.Join(";", errors));
        var warmCounts = counts.ToArray();
        var beforeCommits = writer?.Commits;
        var bytes = GC.GetTotalAllocatedBytes(true);
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var remoteCpu = host?.Process.TotalProcessorTime;
        var peakWal = 0L;
        using var sampleStop = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!sampleStop.IsCancellationRequested)
            {
                foreach (var path in Directory.GetFiles(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + "*log*"))
                {
                    try { peakWal = Math.Max(peakWal, new FileInfo(path).Length); }
                    catch (FileNotFoundException) { }
                }
                await Task.Delay(25);
            }
        });
        var clock = Stopwatch.StartNew();
        Volatile.Write(ref deadline, Stopwatch.GetTimestamp() + seconds * Stopwatch.Frequency);
        if (!phase.SignalAndWait(TimeSpan.FromSeconds(30)) || !phase.SignalAndWait(TimeSpan.FromSeconds(seconds + 60)))
            throw new Exception("Measurement stalled: " + string.Join(";", errors));
        clock.Stop();
        bytes = GC.GetTotalAllocatedBytes(true) - bytes;
        process.Refresh();
        host?.Process.Refresh();
        var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        var serverCpuMs = host == null ? 0 : (host.Process.TotalProcessorTime - remoteCpu!.Value).TotalMilliseconds;
        sampleStop.Cancel();
        sampler.GetAwaiter().GetResult();
        var drain = Stopwatch.StartNew();
        beginDrain.Set();
        foreach (var thread in workers) if (!thread.Join(30000)) throw new TimeoutException("Caller did not drain.");
        if (!errors.IsEmpty) throw new AggregateException(errors);
        var commits = writer?.Commits - beforeCommits;
        var largestBatch = writer?.LargestBatch;
        writer?.Dispose();
        direct?.Dispose();
        var hostSummary = host?.Finish();
        drain.Stop();
        DatabaseFixture.Verify(file, counts);
        DatabaseFixture.Verify(file, counts);
        var latency = samples.SelectMany(values => values).Order().ToArray();
        var count = counts.Sum() - warmCounts.Sum();
        var dll = typeof(LiteDatabase).Assembly.Location;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            mode, callers, batch, linger, warmup, seconds, count, elapsed = clock.Elapsed.TotalSeconds,
            opsPerSecond = count / clock.Elapsed.TotalSeconds, p50us = latency[latency.Length / 2],
            p95us = latency[(int)(latency.Length * .95)], p99us = latency[(int)(latency.Length * .99)],
            clientBytesPerOp = bytes / (double)count, cpuMs, serverCpuMs, peakWal, commits, largestBatch,
            hostSummary, drainMs = drain.Elapsed.TotalMilliseconds, verified = true,
            runtime = Environment.Version.ToString(), dll, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll)))
        }));
        foreach (var path in Directory.GetFiles(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + "*")) File.Delete(path);
    }
}
