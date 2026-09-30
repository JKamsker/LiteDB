using System;
using System.IO;
using System.Linq;
using LiteDB;
using LiteDB.ReproRunner.Shared;
using LiteDB.ReproRunner.Shared.Messaging;

namespace Issue_3034_Pr133StaleSortCleanup;

/// <summary>
/// Repro of stale sort cleanup in PR #133, registered for issue #3034. Exit code 0 means the bug reproduced, which the known-bad
/// LiteDB pinned in the .csproj must do; any other exit code means it did not, which the fixed
/// source must do. Unexpected exceptions fail both variants.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var host = ReproHostClient.CreateDefault();
        ReproConfigurationReporter.SendConfiguration(host);
        var context = ReproContext.FromEnvironment();
        var directory = context.SharedDatabaseRoot
            ?? Path.Combine(Path.GetTempPath(), "Issue_3034_Pr133StaleSortCleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var reproduced = Reproduce(Path.Combine(directory, "repro.db"));
            host.SendResult(reproduced, reproduced ? "SORT_CLEANUP_REPRODUCED" : "SORT_CLEANUP_VERIFIED");
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
        using (var seed = new LiteDatabase(databasePath))
        {
            var rows = seed.GetCollection("rows");
            rows.Insert(new BsonDocument { ["_id"] = 1, ["value"] = 42 });
            rows.EnsureIndex("value");
            seed.GetCollection("untouched").Insert(new BsonDocument { ["_id"] = 1, ["value"] = "preserved" });
        }
        var temp = Path.Combine(Path.GetDirectoryName(databasePath)!,
            Path.GetFileNameWithoutExtension(databasePath) + "-tmp" + Path.GetExtension(databasePath));
        File.WriteAllBytes(temp, new byte[] { 9, 7, 5, 3 });
        using (var db = new LiteDatabase(databasePath)) Verify(db);
        var staleRemains = File.Exists(temp);
        using (var cold = new LiteDatabase(databasePath)) Verify(cold);
        File.Delete(temp);
        return staleRemains;
    }

    private static void Verify(LiteDatabase database)
    {
        var rows = database.GetCollection("rows");
        if (rows.Count() != 1 || rows.Find("value = 42").Single()["_id"].AsInt32 != 1 ||
            database.GetCollection("untouched").FindById(1)["value"].AsString != "preserved")
            throw new InvalidOperationException("Committed data changed.");
    }
}
