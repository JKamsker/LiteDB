using System;
using System.IO;
using System.Linq;
using LiteDB;
using LiteDB.ReproRunner.Shared;
using LiteDB.ReproRunner.Shared.Messaging;

namespace Issue_3067_SharedCloseWait;

/// <summary>
/// Repro of LiteDB issue #3067. Exit code 0 means the bug reproduced, which the known-bad
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
        // Match the reported file-backed /tmp scenario; long checkout paths exceed native mutex name limits.
        var directory = Path.Combine(Path.GetTempPath(), "p133-SharedCloseWait-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var reproduced = Reproduce(Path.Combine(directory, "repro.db"));
            host.SendResult(reproduced, reproduced ? "The bug reproduced." : "The bug did not reproduce.");
            return reproduced ? 0 : 10;
        }
        catch (Exception error)
        {
            host.SendResult(false, "The repro failed.", new { Exception = error.ToString() });
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    /// <summary>Returns true only when the wrong behavior of issue #3067 is observed.</summary>
    private static bool Reproduce(string databasePath)
    {
        var connection = new ConnectionString { Filename = databasePath, Connection = ConnectionType.Shared };
        var db = new LiteDatabase(connection);
        var rows = db.GetCollection("rows");
        rows.EnsureIndex("value");
        rows.Insert(new BsonDocument { ["_id"] = 1, ["value"] = 11 });
#pragma warning disable CS0618
        if (!db.BeginTrans()) throw new InvalidOperationException("Legacy transaction did not begin.");
        rows.Insert(new BsonDocument { ["_id"] = 2, ["value"] = 22 });
        using var attempting = new System.Threading.ManualResetEventSlim();
        using var ended = new System.Threading.ManualResetEventSlim();
        Exception? peerError = null;
        var peer = new System.Threading.Thread(() =>
        {
            attempting.Set();
            try { rows.Insert(new BsonDocument { ["_id"] = 3, ["value"] = 33 }); }
            catch (Exception error) { peerError = error; }
            finally { ended.Set(); }
        }) { IsBackground = true };
        peer.Start();
        if (!attempting.Wait(TimeSpan.FromSeconds(5)) ||
            !System.Threading.SpinWait.SpinUntil(() => (peer.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5)) || ended.IsSet)
            throw new InvalidOperationException("Peer did not reach blocked native admission.");
        try { db.Dispose(); }
        catch (TimeoutException)
        {
            // Both symptoms are required: close cannot drain and its peer still cannot finish.
            if (!ended.Wait(TimeSpan.FromSeconds(1))) return true;
            throw new InvalidOperationException("Close timed out even though the waiting peer completed; this is not a verified fix.");
        }
        if (!ended.Wait(TimeSpan.FromSeconds(5)) || peerError is not OperationCanceledException)
            throw new InvalidOperationException("Close did not cancel and drain the waiting call.", peerError);
        using var reopen = new LiteDatabase(connection);
        if (reopen.GetCollection("rows").Count() != 1 ||
            reopen.GetCollection("rows").FindOne(Query.EQ("value", 11))?["_id"] != 1)
            throw new InvalidOperationException("Close failed to preserve the committed indexed model.");
        return false;
#pragma warning restore CS0618
    }
}
