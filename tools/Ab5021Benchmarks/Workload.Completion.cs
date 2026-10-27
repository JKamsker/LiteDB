using System.Diagnostics;
using LiteDB;

namespace Ab5021Benchmarks;

internal sealed partial class Workload
{
    private void SmallTransactions()
    {
        SmallCompletion("small", _small, checkpointZero: false, sustained: false);
        SmallCompletion("small-cp0", _small, checkpointZero: true, sustained: false);
        SmallCompletion("sustained", _small * 4, checkpointZero: false, sustained: true);
    }

    private void SmallCompletion(string name, int count, bool checkpointZero, bool sustained)
    {
        var path = Path.Combine(_root, name + ".db");
        var db = Open(path);
        var tails = new long[count];
        if (checkpointZero) db.CheckpointSize = 0;
        var col = db.GetCollection<Document>("documents");
        col.EnsureIndex(x => x.Key, true);
        var probe = new CompletionProbe(db);
        var documents = _documents.Take(count).ToArray();
        double foreground, elapsed;
        try
        {
            var watch = Stopwatch.StartNew();
            for (var index = 0; index < documents.Length; index++)
            {
                var started = Stopwatch.GetTimestamp();
                col.Insert(documents[index]);
                tails[index] = Stopwatch.GetTimestamp() - started;
            }
            foreground = watch.Elapsed.TotalMilliseconds;
            // Contiguous completion: no correctness query, metric read, file stat,
            // idle gap or another workload between final insert and drain/close.
            if (checkpointZero) db.Checkpoint();
            db.Dispose();
            elapsed = watch.Elapsed.TotalMilliseconds;
        }
        finally { db.Dispose(); } // an error, including close failure, invalidates the round
        var log = Path.ChangeExtension(path, null) + "-log.db";
        var finalWal = File.Exists(log) ? new FileInfo(log).Length : 0;
        var completion = probe.Finish(foreground, elapsed - foreground, finalWal, tails);
        if (_record)
        {
            var dataBytes = new FileInfo(path).Length;
            var directoryBytes = Directory.GetFiles(_root).Sum(file => new FileInfo(file).Length);
            if (!checkpointZero && !sustained)
                Samples.Add(new Sample("single_insert_5k_tx", foreground, count, dataBytes, directoryBytes, completion));
            var scenario = checkpointZero ? "single_insert_5k_cp0_with_drain_close" :
                sustained ? "sustained_insert_20k_with_close" : "single_insert_5k_tx_with_close";
            Samples.Add(new Sample(scenario, elapsed, count, dataBytes, directoryBytes, completion));
            Console.WriteLine($"{_config} {scenario}: {elapsed:F3} ms; foreground={foreground:F3}, drain={elapsed - foreground:F3}, peak={completion.peakPhysicalWalBytes}, p99={completion.commitP99Milliseconds:F3}");
        }
        // Validate full payloads and persisted index results after the timed close.
        using var reopened = Open(path);
        var actual = reopened.GetCollection<Document>("documents").FindAll().OrderBy(d => d.Id).ToArray();
        Require(actual.Length == count, "completion reopen count");
        for (var index = 0; index < count; index++)
        {
            var expected = documents[index];
            var row = actual[index];
            Require(row.Id == expected.Id && row.Key == expected.Key && row.Value == expected.Value &&
                row.Payload == expected.Payload, "completion full payload");
        }
        foreach (var index in new[] { 0, count / 2, count - 1 })
            Require(reopened.GetCollection<Document>("documents").FindOne(Query.EQ("Key", documents[index].Key))?.Id == documents[index].Id,
                "completion persisted index");
    }
}
