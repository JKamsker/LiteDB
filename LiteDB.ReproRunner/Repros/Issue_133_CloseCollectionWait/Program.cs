using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using LiteDB.Engine;
using LiteDB;
using LiteDB.ReproRunner.Shared;
using LiteDB.ReproRunner.Shared.Messaging;

namespace Issue_133_CloseCollectionWait;

/// <summary>
/// Repro of LiteDB issue #133. Exit code 0 means the bug reproduced, which the known-bad
/// LiteDB pinned in the .csproj must do; any other exit code means it did not, which the fixed
/// source must do. Catch the bug's own exception inside <see cref="Reproduce"/>: an escaping
/// exception counts as "did not reproduce".
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var host = ReproHostClient.CreateDefault();
        ReproConfigurationReporter.SendConfiguration(host);
        var context = ReproContext.FromEnvironment();
        var directory = context.SharedDatabaseRoot
            ?? Path.Combine(Path.GetTempPath(), "Issue_133_CloseCollectionWait-" + Guid.NewGuid().ToString("N"));
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
        var engine = new LiteEngine(new EngineSettings { Filename = databasePath });
        var db = new LiteDatabase(engine, disposeOnClose: false);
        db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
        if (!engine.BeginTrans()) throw new Exception("Failed to begin owner transaction.");
        db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2 });
        using var attempting = new ManualResetEventSlim();
        Exception? peerFailure = null;
        var peer = new Thread(() =>
        {
            attempting.Set();
            try { db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 3 }); }
            catch (Exception ex) { peerFailure = ex; }
        }) { IsBackground = true };
        peer.Start();
        if (!attempting.Wait(TimeSpan.FromSeconds(5)) || !SpinWait.SpinUntil(() =>
            (peer.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)))
            throw new Exception("Peer failed to reach its lock wait.");
        var close = Task.Run(engine.Dispose);
        if (!close.Wait(TimeSpan.FromSeconds(3))) return true;
        if (!peer.Join(TimeSpan.FromSeconds(5)) || peerFailure is not LiteException)
            throw new Exception("Peer was not rejected by engine close.");
        using var cold = new LiteDatabase(databasePath);
        if (cold.GetCollection("rows").Count() != 1 || cold.GetCollection("rows").FindById(1) == null)
            throw new Exception("Cold committed-state model failed.");
        return false;
    }
}
