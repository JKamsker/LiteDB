using System.Diagnostics;
using System.Reflection;
using LiteDB;

namespace Ab5021Benchmarks;

internal sealed record CompletionMeasurements(double foregroundMilliseconds, double closeMilliseconds,
    long finalWalBytes, long? peakPhysicalWalBytes, long? peakLogicalPages,
    long? admissionWaitCount, double? admissionWaitMilliseconds, long? requestCount,
    long? passCount, double? activeMilliseconds, bool? workerExited,
    double commitP50Milliseconds, double commitP95Milliseconds, double commitP99Milliseconds,
    double commitMaxMilliseconds, string peakAccounting);

// Reflection only captures/readbacks scalar metrics outside the timed interval;
// identical harness binaries also run against v5 and the pre-worker v6 library.
internal sealed class CompletionProbe
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly object? _metrics, _worker, _admission;

    internal CompletionProbe(LiteDatabase db)
    {
        var engine = typeof(LiteDatabase).GetField("_engine", Members)?.GetValue(db);
        var disk = engine?.GetType().GetField("_disk", Members)?.GetValue(engine);
        _metrics = Get(disk, "CheckpointMetrics");
        _worker = Get(disk, "AutomaticCheckpoint");
        _admission = Get(disk, "WriteAdmission");
        if (Environment.GetEnvironmentVariable("BENCHMARK_REQUIRE_BACKGROUND") == "true" && _worker == null)
            throw new InvalidOperationException("Scratch candidate failed to activate its production Direct worker");
    }

    internal CompletionMeasurements Finish(double foreground, double close, long finalWal, long[] tails)
    {
        var exited = Get(_worker, "HasExited") as bool?;
        if (Get(_worker, "LastFailure") is string failure) throw new InvalidOperationException(failure);
        if (exited == false) throw new InvalidOperationException("Close returned before worker exit");
        if (finalWal != 0) throw new InvalidOperationException("Unpinned completion retained a WAL");
        Array.Sort(tails);
        double Percentile(double p) => tails[Math.Max(0, (int)Math.Ceiling(tails.Length * p) - 1)] * 1000.0 / Stopwatch.Frequency;
        return new CompletionMeasurements(foreground, close, finalWal,
            Get(_metrics, "PeakPhysicalBytes") as long?, Get(_metrics, "PeakLogicalPages") as long?,
            Get(_admission, "WaitCount") as long?, Get(_admission, "WaitMilliseconds") as double?,
            Get(_metrics, "Requests") as long?, Get(_metrics, "Passes") as long?,
            Get(_metrics, "ActiveMilliseconds") as double?, exited,
            Percentile(.50), Percentile(.95), Percentile(.99), Percentile(1),
            _metrics == null ? "unavailable in released/before binary" : "exact successful write extents; includes footer/padding/preamble; excludes KEEP_SIZE reservations");
    }

    private static object? Get(object? value, string name) => value?.GetType().GetProperty(name, Members)?.GetValue(value);
}
