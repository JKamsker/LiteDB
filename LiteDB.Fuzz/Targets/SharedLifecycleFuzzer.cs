using System.Text.Json;

namespace LiteDB.Fuzz.Targets;

/// <summary>Bounded process-death schedules; OS caches survive, unlike the power-loss target.</summary>
internal sealed class SharedLifecycleFuzzer : IFuzzTarget
{
    internal static readonly string[] Boundaries =
    {
        "native-waiter", "uncommitted", "wal-before-durable-flush", "wal-after-durable-flush",
        "after-commit-acknowledgement", "after-refreshed-wrapper-reuse", "before-reader-dispose", "before-session-dispose",
        "checkpoint-before-data-flush", "checkpoint-after-data-flush"
    };
    public string Name => "shared-lifecycle";
    public string Description => "Observed Shared handle, native waiter, snapshot and checkpoint process-death schedules.";

    public async Task RunAsync(FuzzContext context)
    {
        var observed = new HashSet<string>();
        while (context.Next())
        {
            var boundary = Boundaries[(context.Steps - 1) % Boundaries.Length];
            // Every twenty cases covers every boundary in both encryption modes.
            var encrypted = ((context.Steps - 1) / Boundaries.Length + context.Seed) % 2 != 0;
            var alias = context.Random.Next(2) == 0;
            var competitorOrder = context.Random.Next(6);
            var path = context.StepFile("lifecycle.db");
            var password = encrypted ? "lifecycle-secret" : null;
            SharedLifecycleOracle.Seed(path, password);
            var directory = Path.Combine(context.DirectoryPath, "actors-" + context.Steps);
            Directory.CreateDirectory(directory);
            var actorPath = alias ? Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path)) : path;
            context.Trace("schedule", new { boundary, encrypted, alias, competitorOrder });
            File.WriteAllText(Path.Combine(directory, "schedule.json"), System.Text.Json.JsonSerializer.Serialize(new
            { context.Seed, context.Steps, boundary, encrypted, alias, competitorOrder, database = path, actorPath }));
            var actors = new List<SharedLifecycleProcess>();
            bool? committed = false;
            bool? peerCommitted = false;
            var quiescent = true;
            Exception failure = null;
            try
            {
                var owner = Start("owner");
                var writer = Start("writer");
                var reader = Start("reader");
                var opener = Start("opener");
                var maintenance = Start("maintenance");
                var verifier = Start("surviving-reader");
                Task[] competitors;
                await Task.WhenAll(actors.Select(actor => actor.Expect("ready")));
                if (boundary.StartsWith("checkpoint-"))
                {
                    peerCommitted = null;
                    owner.Send("prepare-checkpoint"); await owner.Expect("written");
                    peerCommitted = true; Acknowledge("peer");
                    competitors = StartCompetitors();
                    owner.Send("arm:" + boundary); await owner.Expect("armed");
                    owner.Send("checkpoint"); await owner.Expect("hook:" + boundary);
                    VerifyMarker("owner", "hook:" + boundary);
                    await owner.Kill();
                }
                else
                {
                    reader.Send("snapshot"); await reader.Expect("snapshot-held");
                    owner.Send("warm"); await owner.Expect("warmed");
                    owner.Send("begin"); await owner.Expect("begun");
                    peerCommitted = null;
                    writer.Send("write"); await writer.Expect("native-wait"); VerifyMarker("writer", "native-wait"); await writer.AssertBlocked();
                    competitors = StartCompetitors();
                    if (boundary == "native-waiter")
                    {
                        await writer.Kill();
                        committed = null;
                        owner.Send("commit"); await owner.Expect("committed");
                        committed = true; Acknowledge("owner");
                        writer = Start("replacement-writer"); await writer.Expect("ready"); writer.Send("write");
                    }
                    else if (boundary.StartsWith("wal-"))
                    {
                        owner.Send("arm:" + boundary); await owner.Expect("armed");
                        committed = null;
                        owner.Send("commit"); await owner.Expect("hook:" + boundary);
                        VerifyMarker("owner", "hook:" + boundary);
                        // The after-flush hook proves the confirmation and preceding pages were
                        // flushed. Even without an API acknowledgement, recovery must expose them.
                        committed = boundary == "wal-after-durable-flush" ? true : null;
                        await owner.Kill();
                    }
                    else if (boundary == "uncommitted")
                    {
                        owner.Send("stop:uncommitted"); await owner.Expect("stop:uncommitted");
                        VerifyMarker("owner", "stop:uncommitted"); await owner.Kill();
                    }
                    else
                    {
                        committed = null;
                        owner.Send("commit"); await owner.Expect("committed");
                        committed = true; Acknowledge("owner");
                        if (boundary == "after-refreshed-wrapper-reuse")
                        {
                            await writer.Expect("written"); peerCommitted = true; Acknowledge("peer");
                            owner.Send("refresh"); await owner.Expect("refreshed");
                        }
                        if (boundary == "before-reader-dispose")
                        {
                            reader.Send("stop:before-reader-dispose"); await reader.Expect("stop:before-reader-dispose");
                            VerifyMarker("reader", "stop:before-reader-dispose"); await reader.Kill();
                        }
                        else
                        {
                            owner.Send("stop:" + boundary); await owner.Expect("stop:" + boundary);
                            VerifyMarker("owner", "stop:" + boundary); await owner.Kill();
                        }
                    }
                    if (peerCommitted != true)
                    { await writer.Expect("written"); peerCommitted = true; Acknowledge("peer"); }
                    if (boundary != "before-reader-dispose")
                    { reader.Send("retire"); await reader.Expect("retired"); }
                }
                await Task.WhenAll(competitors);
                foreach (var actor in actors.Where(actor => !actor.HasExited)) await actor.Finish();
                observed.Add(boundary);
                context.Trace("completed", new { boundary, committed, peerCommitted });

                Task[] StartCompetitors()
                {
                    // Commands start while the native owner is alive. Each obligation has its own
                    // Send-based deadline; maintenance, opener and reader cannot mask one another.
                    var work = new[] { (Actor: opener, Command: "reopen", Done: "reopened"),
                        (Actor: maintenance, Command: "checkpoint", Done: "checkpointed"),
                        (Actor: verifier, Command: "read", Done: "read") };
                    var permutations = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
                        new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
                    return permutations[competitorOrder].Select(index =>
                    {
                        var item = work[index]; item.Actor.Send(item.Command); return item.Actor.Expect(item.Done);
                    }).ToArray();
                }
            }
            catch (Exception error) { failure = error; }
            finally
            {
                // Never copy/read the database while any actor may still mutate it.
                foreach (var actor in actors)
                {
                    try { await actor.DisposeAsync(); }
                    catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
                    quiescent &= actor.Stopped;
                }
            }
            if (quiescent)
            {
                try
                {
                    var resolved = SharedLifecycleOracle.Verify(context, path, password, committed, peerCommitted);
                    SharedLifecycleOracle.Verify(context, path, password, resolved.Owner, resolved.Peer);
                    DatabaseIntegrityVerifier.Verify(context, path, password);
                }
                catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            }
            else
            {
                // The generic failure writer copies registered files. Suppress that copy if a kill failed.
                context.Files.Remove(path);
                failure = new AggregateException(failure ?? new InvalidOperationException("Live child"),
                    new InvalidOperationException("Fixture retained in place at " + path + "; unsafe to reopen/copy while a child is alive."));
            }
            if (failure != null) throw failure;
            context.ObserveNovelty("shared-lifecycle", boundary, encrypted, alias);

            SharedLifecycleProcess Start(string name)
            {
                var selectedPath = name == "owner" ? path : actorPath;
                var actor = new SharedLifecycleProcess(selectedPath, directory, name, encrypted);
                actors.Add(actor); return actor;
            }
            void Acknowledge(string transaction)
            {
                // This ledger belongs to the supervisor, outside the killed process's failure domain.
                using var stream = new FileStream(Path.Combine(directory, "acknowledged.jsonl"), FileMode.Append,
                    FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream);
                writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { transaction, boundary }));
                writer.Flush(); stream.Flush(true);
            }
            void VerifyMarker(string actor, string marker) => context.Check(
                File.ReadAllText(Path.Combine(directory, actor + ".boundary")) == marker,
                "The selected process-death boundary was not durably observed.");
        }
        if (context.Steps >= Boundaries.Length)
            context.Check(observed.Count == Boundaries.Length, "A required process-death boundary was not exercised.");
        context.Metrics["observedBoundaries"] = observed.Count;
        context.Metrics["coldReopens"] = context.Steps * 2;
    }
}
