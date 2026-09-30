using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using LiteDB.Engine;
using LiteDB;
using LiteDB.ReproRunner.Shared;
using LiteDB.ReproRunner.Shared.Messaging;

namespace Issue_133_MaintenanceProgress;

/// <summary>
/// Repro of LiteDB issue #133. Exit code 0 means the bug reproduced, which the known-bad
/// LiteDB pinned in the .csproj must do; any other exit code means it did not, which the fixed
/// source must do. Catch the bug's own exception inside <see cref="Reproduce"/>: an escaping
/// exception counts as "did not reproduce".
/// </summary>
internal static class Program
{
    [ThreadStatic] private static int _readDelay;
    private static int Main()
    {
        var host = ReproHostClient.CreateDefault();
        ReproConfigurationReporter.SendConfiguration(host);
        var context = ReproContext.FromEnvironment();
        var directory = context.SharedDatabaseRoot
            ?? Path.Combine(Path.GetTempPath(), "Issue_133_MaintenanceProgress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var reproduced = Reproduce(Path.Combine(directory, "repro.db"));
            host.SendResult(reproduced, reproduced ? "The bug reproduced." : "The bug did not reproduce.");
            Console.WriteLine(reproduced ? "BUG_REPRODUCED" : "VERIFIED_FIXED");
            return reproduced ? 0 : 10;
        }
        catch (Exception error)
        {
            host.SendResult(false, "The repro failed.", new { Exception = error.ToString() });
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    /// <summary>Returns true only when the wrong behavior of issue #133 is observed.</summary>
    private static bool Reproduce(string databasePath)
    {
        var rebuild = Scenario(databasePath + "-rebuild", false);
        var close = Scenario(databasePath + "-close", true);
        if (rebuild != close) throw new Exception("Rebuild and close results disagree.");
        return rebuild && close;
    }

    private static bool Scenario(string databasePath, bool closing)
    {
        using var ready = new CountdownEvent(16);
        using var stop = new ManualResetEventSlim();
        using var engine = new LiteEngine(new EngineSettings
        {
            Filename = databasePath,
            ReadTransform = (collection, value) => { Thread.Sleep(_readDelay); return value; }
        });
        using var db = new LiteDatabase(engine, disposeOnClose: false);
        db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
        db.Timeout = TimeSpan.FromSeconds(3);
        var errors = new ConcurrentQueue<Exception>();
        var readers = new Thread[16];
        for (var i = 0; i < readers.Length; i++)
        {
            var delay = 100 + i * 7;
            readers[i] = new Thread(() =>
            {
                _readDelay = delay;
                try
                {
                    if (db.GetCollection("rows").FindById(1) == null) throw new Exception("Missing sentinel.");
                    ready.Signal();
                    while (!stop.IsSet)
                        if (db.GetCollection("rows").FindById(1) == null) throw new Exception("Missing sentinel.");
                }
                catch (LiteException ex) when (closing && ex.ErrorCode == LiteException.ENGINE_DISPOSED) { }
                catch (Exception ex) { errors.Enqueue(ex); }
            }) { IsBackground = true };
            readers[i].Start();
        }
        var reproduced = false;
        Task? disposal = null;
        try
        {
            if (!ready.Wait(TimeSpan.FromSeconds(10))) throw new Exception("Reader setup failed.");
            if (closing)
            {
                disposal = Task.Run(engine.Dispose);
                reproduced = !disposal.Wait(TimeSpan.FromSeconds(3));
            }
            else
            {
                try { db.Rebuild(); }
                catch (LiteException ex) when (ex.Message.Contains("operation/maintenance")) { reproduced = true; }
            }
        }
        finally
        {
            stop.Set();
            foreach (var reader in readers)
                if (!reader.Join(TimeSpan.FromSeconds(10))) throw new Exception("Reader failed to drain.");
        }
        if (disposal != null && !disposal.Wait(TimeSpan.FromSeconds(10)))
            throw new Exception("Close failed to drain after readers stopped.");
        Console.WriteLine($"{(closing ? "close" : "rebuild")}: reproduced={reproduced}");
        if (!errors.IsEmpty) throw new AggregateException(errors);
        engine.Dispose();
        using var cold = new LiteDatabase(databasePath);
        if (cold.GetCollection("rows").FindById(1) == null) throw new Exception("Lost committed sentinel.");
        return reproduced;
    }
}
