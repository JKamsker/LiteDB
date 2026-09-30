using System;
using System.IO;
using System.Linq;
using LiteDB;
using LiteDB.ReproRunner.Shared;
using LiteDB.ReproRunner.Shared.Messaging;

namespace Issue_3067_SharedNestedBegin;

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
        var directory = Path.Combine(Path.GetTempPath(), "p133-SharedNestedBegin-" + Guid.NewGuid().ToString("N"));
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
        using var db = new LiteDatabase(new ConnectionString { Filename = databasePath, Connection = ConnectionType.Shared });
        var rows = db.GetCollection("rows");
        rows.Insert(System.Linq.Enumerable.Range(0, 150).Select(i => new BsonDocument { ["_id"] = i }));
        var reproduced = false;
#pragma warning disable CS0618
        foreach (var legacy in new[] { false, true })
        {
            using var reader = db.Execute(legacy ? "SELECT $ FROM rows" : "SELECT $ FROM rows FOR UPDATE");
            if (!reader.Read()) throw new InvalidOperationException("Reader did not open.");
            if (legacy)
            {
                rows.Insert(new BsonDocument { ["_id"] = 200 });
                if (!db.BeginTrans()) throw new InvalidOperationException("Legacy transaction did not begin.");
                rows.Insert(new BsonDocument { ["_id"] = 201 });
            }
            try
            {
                using var handle = db.BeginTransaction(TimeSpan.Zero);
                throw new InvalidOperationException("Nested transaction unexpectedly admitted.");
            }
            catch (TimeoutException) { reproduced = true; }
            catch (InvalidOperationException error) when (error.Message.Contains("before opening a transaction handle")) { }
            if (!reader.Read()) throw new InvalidOperationException("Rejected begin damaged the reader.");
            if (legacy && !db.Commit()) throw new InvalidOperationException("Rejected begin damaged the transaction.");
        }
        using var retry = db.BeginTransaction(TimeSpan.FromSeconds(5));
        if (retry.GetCollection("rows").Count() != 152) throw new InvalidOperationException("Committed data was lost.");
        retry.Commit();
        return reproduced;
#pragma warning restore CS0618
    }
}
