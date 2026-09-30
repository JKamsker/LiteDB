using LiteDB.ConcurrencyTesting;

namespace LiteDB.Fuzz.Targets;

internal sealed class TransactionInterleavingFuzzer : IFuzzTarget
{
    public string Name => "transaction-interleavings";
    public string Description => "Systematic forced actor schedules with committed-state and progress oracles.";

    public Task RunAsync(FuzzContext context)
    {
        while (context.Next())
        {
            // Enumerate the complete finite matrix, with the seed rotating its start.
            var index = (context.Steps - 1 + (context.Seed & int.MaxValue)) % (TransactionInterleavingExplorer.ScheduleCount * 8);
            var schedule = index / 8;
            var shared = (index & 1) != 0;
            var encrypted = (index / 2 & 1) != 0;
            var outcome = index / 4 % 2;
            var file = context.StepFile("interleaving-" + context.Steps + ".db");
            context.StepFile("interleaving-" + context.Steps + ".db.other");
            context.StepFile("interleaving-" + context.Steps + ".db.history");
            context.Trace("forced-schedule", new { schedule, shared, encrypted, outcome });
            TransactionInterleavingExplorer.Run(file, shared, encrypted, schedule, outcome);
            context.ObserveNovelty("interleaving", schedule, shared, encrypted, outcome);
            context.Metrics["completedActorSchedules"] = context.Steps;
        }
        return Task.CompletedTask;
    }
}
