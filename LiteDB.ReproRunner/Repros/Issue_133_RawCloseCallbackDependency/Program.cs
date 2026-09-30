using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using LiteDB.Engine;
using LiteDB;
using LiteDB.ReproRunner.Shared;
using LiteDB.ReproRunner.Shared.Messaging;

namespace Issue_133_RawCloseCallbackDependency;

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
            ?? Path.Combine(Path.GetTempPath(), "Issue_133_RawCloseCallbackDependency-" + Guid.NewGuid().ToString("N"));
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

    private static bool Reproduce(string databasePath)
    {
        using var callbackEntered = new ManualResetEventSlim();
        using var startDependency = new ManualResetEventSlim();
        using var dependencyStarted = new ManualResetEventSlim();
        LiteDatabase? db = null;
        Thread? dependency = null;
        Exception? dependencyFailure = null;
        Exception? readFailure = null;
        Exception? closeFailure = null;
        var dependencyFinished = false;
        using var engine = new LiteEngine(new EngineSettings
        {
            Filename = databasePath,
            ReadTransform = (collection, value) =>
            {
                if (collection != "rows") return value;
                callbackEntered.Set();
                if (!startDependency.Wait(TimeSpan.FromSeconds(10)))
                    throw new Exception("Callback dependency was never admitted by the harness.");
                dependency = new Thread(() =>
                {
                    dependencyStarted.Set();
                    try { db!.GetCollection("untouched").Count(); }
                    catch (Exception error) { dependencyFailure = error; }
                }) { IsBackground = true };
                dependency.Start();
                if (!dependencyStarted.Wait(TimeSpan.FromSeconds(5)) || !SpinWait.SpinUntil(() =>
                    !dependency.IsAlive || (dependency.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(5)))
                    throw new Exception("Independent callback work never reached completion or a wait.");
                dependencyFinished = dependency.Join(TimeSpan.FromSeconds(2));
                return value;
            }
        });
        using (db = new LiteDatabase(engine, disposeOnClose: false))
        {
            db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = 10 });
            db.GetCollection("rows").EnsureIndex("value");
            db.GetCollection("untouched").Insert(new BsonDocument { ["_id"] = 42 });
            var read = new Thread(() =>
            {
                try { db.GetCollection("rows").FindById(1); }
                catch (Exception error) { readFailure = error; }
            }) { IsBackground = true };
            read.Start();
            if (!callbackEntered.Wait(TimeSpan.FromSeconds(5)))
                throw new Exception("Read callback did not start.");
            var close = new Thread(() =>
            {
                try { engine.Dispose(); }
                catch (Exception error) { closeFailure = error; }
            }) { IsBackground = true };
            close.Start();
            try
            {
                // This application thread performs only Dispose. WaitSleepJoin proves
                // it reached the drain rather than merely starting its thread.
                if (!SpinWait.SpinUntil(() => !close.IsAlive ||
                    (close.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(5)))
                    throw new Exception("Close did not reach its drain.");
            }
            finally { startDependency.Set(); }
            if (!read.Join(TimeSpan.FromSeconds(10)) || !close.Join(TimeSpan.FromSeconds(10)) ||
                dependency == null || !dependency.Join(TimeSpan.FromSeconds(10)))
                throw new Exception("Scenario did not clean up after the callback safety escape.");
            if (closeFailure != null) throw new Exception("Unexpected close failure.", closeFailure);
            if (readFailure != null && !IsClosed(readFailure))
                throw new Exception("Unexpected read failure.", readFailure);
            if (!IsClosed(dependencyFailure))
                throw new Exception("Independent work was not rejected by close.", dependencyFailure);
        }
        using var cold = new LiteDatabase(databasePath);
        if (cold.GetCollection("rows").Count(Query.EQ("value", 10)) != 1 ||
            cold.GetCollection("untouched").FindById(42) == null)
            throw new Exception("Cold committed-state/index/sentinel model failed.");
        return !dependencyFinished;
    }

    private static bool IsClosed(Exception? error) =>
        error is LiteException lite && lite.ErrorCode == LiteException.ENGINE_DISPOSED;
}
