using LiteDB;

internal static class TransactionHandleHarness
{
    internal static bool TryRun(string mode, string filename, string? password, string[] args)
    {
        if (!mode.StartsWith("handle-", StringComparison.Ordinal)) return false;
        using var db = new LiteDatabase(new ConnectionString { Filename = filename, Password = password,
            Connection = args[4] == "shared" ? ConnectionType.Shared : ConnectionType.Direct,
            TransactionPageLimit = 1 });
        if (mode == "handle-writer")
        {
            Console.WriteLine("attempting");
            using var writer = db.BeginTransaction();
            writer.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 3, ["value"] = 126 });
            writer.Commit();
            Console.WriteLine("done");
            return true;
        }
        ILiteTransaction? transaction = null;
        Exception? error = null;
        var creator = new Thread(() =>
        {
            try
            {
                transaction = db.BeginTransaction();
                transaction.GetCollection("rows").Insert(new BsonDocument
                { ["_id"] = 2, ["value"] = 84, ["payload"] = new string('x', 50000) });
            }
            catch (Exception failure) { error = failure; }
        });
        creator.Start();
        creator.Join();
        if (error != null) throw error;
        Console.WriteLine("ready");
        var command = Console.ReadLine();
        var completer = new Thread(() =>
        {
            try
            {
                if (command == "commit") transaction!.Commit();
                else transaction!.Rollback();
            }
            catch (Exception failure) { error = failure; }
        });
        completer.Start();
        completer.Join();
        if (error != null) throw error;
        Console.WriteLine("done");
        // Keep the completed handle and facade alive while a peer acquires writer ownership.
        Console.ReadLine();
        transaction!.Dispose();
        return true;
    }
}
