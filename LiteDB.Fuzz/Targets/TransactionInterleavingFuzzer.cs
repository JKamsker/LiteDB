using LiteDB.ConcurrencyTesting.Pr133;

namespace LiteDB.Fuzz.Targets;

internal sealed class TransactionInterleavingFuzzer : IFuzzTarget
{
    // Replay overlay: renamed from transaction-interleavings, which names the general explorer target here.
    public string Name => "pr133-transaction-interleavings";
    public string Description => "Systematic forced actor schedules with committed-state and progress oracles.";

    public Task RunAsync(FuzzContext context)
    {
        while (context.Next())
        {
            // Enumerate the complete finite matrix, with the seed rotating its start.
            var index = (int)(((long)context.Steps - 1 + (context.Seed & int.MaxValue)) % (TransactionInterleavingExplorer.ScheduleCount * 8));
            var schedule = index / 8;
            var shared = (index & 1) != 0;
            var encrypted = (index / 2 & 1) != 0;
            var outcome = index / 4 % 2;
            var file = context.StepFile("interleaving-" + context.Steps + ".db");
            context.StepFile("interleaving-" + context.Steps + ".db.other");
            context.StepFile("interleaving-" + context.Steps + ".db.history");
            context.Trace("forced-schedule", new { schedule, shared, encrypted, outcome });
            try { TransactionInterleavingExplorer.Run(file, shared, encrypted, schedule, outcome); }
            catch (Exception error)
            {
                if (error.Data["ExplorerLiveWorker"] is true)
                {
                    // The existing artifact writer snapshots registered files immediately.
                    // Keep originals in this isolated child's directory, but never copy a
                    // database while a retained actor could still be mutating its files.
                    context.Files.Clear();
                    File.WriteAllText(Path.Combine(context.DirectoryPath, "live-worker-retention.txt"),
                        "Original fixtures retained in place. No state-* snapshot is safe before process exit. " + file);
                }
                throw;
            }
            context.ObserveNovelty("interleaving", schedule, shared, encrypted, outcome);
            context.Metrics["completedActorSchedules"] = context.Steps;
        }
        return Task.CompletedTask;
    }
}
