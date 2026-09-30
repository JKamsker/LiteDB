using System.IO.Pipes;
using LiteDB;

namespace WriteControllerExperiments;

internal static class Safety
{
    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Safety assertion failed: " + message);
    }

    internal static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var password in new string?[] { null, "experimental-secret" })
        {
            var tag = password == null ? "plain" : "encrypted";
            Group(Path.Combine(directory, tag + "-group.db"), password);
            Rollback(Path.Combine(directory, tag + "-rollback.db"), password);
            Backpressure(Path.Combine(directory, tag + "-backpressure.db"), password);
            foreach (var fault in new[] { "before", "after", "partial" })
                Failure(Path.Combine(directory, tag + "-" + fault + ".db"), password, fault);
        }
        Ipc(Path.Combine(directory, "ipc.db"));
        LostAcknowledgement(Path.Combine(directory, "ipc-lost-ack.db"));
        foreach (var boundary in new[] { "before", "after", "ack" })
            Crash(Path.Combine(directory, "crash-" + boundary + ".db"), boundary);
        Console.WriteLine("PASS all 17 controller safety scenarios");
    }

    private static Task[] Submit(WriteController writer, int first, int count) =>
        Enumerable.Range(first, count).Select(id => writer.Submit(new WriteRequest(id, id))).ToArray();

    private static void Wait(Task[] requests) => Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();

    private static void Group(string file, string? password)
    {
        using var storage = new FaultStorage(file, password);
        storage.Seed();
        using (var writer = new WriteController(storage.Open, 8, 100))
        {
            // Warm lazy encrypted stream preambles before counting commit syncs.
            Wait(Submit(writer, 1, 1));
            using var reached = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            storage.Log.BeforeSync = () => { reached.Set(); Check(release.Wait(10000), "release flush"); };
            var before = storage.Log.Syncs;
            var beforeCommit = storage.Log.CommitSyncs;
            var tasks = Submit(writer, 2, 8);
            Check(reached.Wait(10000), "reached shared flush");
            Check(tasks.All(task => !task.IsCompleted), "no acknowledgement before fsync");
            release.Set();
            Wait(tasks);
            storage.Log.BeforeSync = null;
            Check(storage.Log.Syncs - before == (password == null ? 1 : 2) && storage.Log.CommitSyncs - beforeCommit == 1 &&
                writer.Commits == 2 && writer.LargestBatch == 8,
                $"one sync for eight submissions: syncs={storage.Log.Syncs - before}, commits={writer.Commits}, max={writer.LargestBatch}; {storage.Log.LastSync}");
        }
        storage.Verify(9);
        storage.Verify(9);
        Console.WriteLine("PASS grouped durability " + password);
    }

    private static void Rollback(string file, string? password)
    {
        using var storage = new FaultStorage(file, password);
        storage.Seed();
        using (var writer = new WriteController(storage.Open, 8, 100))
        {
            Wait(Submit(writer, 1, 1));
            var duplicateBatch = new[] { writer.Submit(new WriteRequest(2, 2)), writer.Submit(new WriteRequest(1, 1)) };
            try { Wait(duplicateBatch); throw new Exception("Duplicate accepted"); }
            catch (BatchRejectedException) { }
            Check(duplicateBatch.All(task => task.IsFaulted && task.Exception!.InnerException is BatchRejectedException), "entire batch rejected");
            Wait(Submit(writer, 2, 2));
        }
        storage.Verify(3);
        storage.Verify(3);
        Console.WriteLine("PASS rollback and continuation " + password);
    }

    private static void Backpressure(string file, string? password)
    {
        using var storage = new FaultStorage(file, password);
        storage.Seed();
        var writer = new WriteController(storage.Open, 1, capacity: 1);
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        storage.Log.BeforeSync = () => { reached.Set(); Check(release.Wait(10000), "backpressure release"); };
        var first = writer.Submit(new WriteRequest(1, 1));
        Check(reached.Wait(10000), "blocked commit");
        var second = writer.Submit(new WriteRequest(2, 2));
        var rejected = writer.Submit(new WriteRequest(3, 3));
        Check(rejected.IsFaulted, "bounded queue rejects excess");
        var close = Task.Run(writer.Dispose);
        Check(!close.Wait(50), "close waits for accepted work");
        release.Set();
        Wait([first, second, close]);
        storage.Log.BeforeSync = null;
        Check(writer.Submit(new WriteRequest(3, 3)).IsFaulted, "closed rejects new requests");
        writer.Dispose();
        storage.Verify(2);
        Console.WriteLine("PASS backpressure and drain " + password);
    }

    private static void Failure(string file, string? password, string fault)
    {
        using var storage = new FaultStorage(file, password);
        storage.Seed();
        var writer = new WriteController(storage.Open, 8, 100);
        Wait(Submit(writer, 1, 1));
        var data = File.ReadAllBytes(file);
        storage.Log.Failure = fault;
        var tasks = Submit(writer, 2, 8);
        try { Wait(tasks); throw new Exception("Fault ignored"); }
        catch (OutcomeUnknownException) { }
        Check(storage.Log.Reached && tasks.All(task => task.IsFaulted), "failed batch never acknowledged");
        var durable = storage.Log.Durable.ToArray();
        var subsequent = writer.Submit(new WriteRequest(10, 10));
        try { subsequent.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); throw new Exception("Fatal writer accepted work"); }
        catch (InvalidOperationException) { }
        try { writer.Dispose(); throw new Exception("Fatal close hidden"); }
        catch (OutcomeUnknownException) { }
        storage.Log.Failure = null;
        // Simulated power loss: retain ONLY the last successfully synced log image.
        // Setup checkpointed the data; no checkpoint occurs during this small batch.
        var recovered = file + ".power.db";
        File.WriteAllBytes(recovered, data);
        File.WriteAllBytes(Path.ChangeExtension(recovered, null) + "-log.db", durable);
        using var cold = new FaultStorage(recovered, password);
        cold.Verify(fault == "after" ? 9 : 1);
        cold.Verify(fault == "after" ? 9 : 1);
        Console.WriteLine("PASS " + fault + " failure and durable-image recovery " + password);
    }

    private static void Ipc(string file)
    {
        DatabaseFixture.Seed(file);
        using (var writer = new WriteController(() => DatabaseFixture.Open(file), 16, 1))
        using (var host = new PipeController(writer, "ldb-safety-" + Guid.NewGuid().ToString("N")))
        {
            var clients = Enumerable.Range(0, 16).Select(worker => Task.Run(() =>
            {
                using var client = new PipeWriter(host.Name);
                for (var i = 1; i <= 16; i++) client.Write(new WriteRequest(worker * 1000000000L + i, i));
            })).ToArray();
            Wait(clients);
            using var truncated = new NamedPipeClientStream(".", host.Name, PipeDirection.InOut);
            truncated.Connect(10000);
            truncated.Write(new byte[5]); // EOF in the middle of a frame must submit nothing.
        }
        DatabaseFixture.Verify(file, Enumerable.Repeat(16, 16).ToArray());
        DatabaseFixture.Verify(file, Enumerable.Repeat(16, 16).ToArray());
        Console.WriteLine("PASS IPC concurrent model and truncated frame");
    }

    private static void Crash(string file, string boundary)
    {
        using var child = new ChildProcess("crash", file, boundary);
        Check(child.ReadLine() == "SEEDED", "crash child seed acknowledged");
        Check(child.ReadLine() == "BOUNDARY", "crash boundary reached");
        child.Process.Kill(true);
        child.Process.WaitForExit();
        using var cold = new FaultStorage(file);
        using (var db = cold.Open())
        {
            var count = db.GetCollection("rows").Count();
            Check(boundary == "before" ? count is 1 or 9 : count == 9, "all-or-none process recovery");
            db.Dispose();
            cold.Verify(count);
            cold.Verify(count);
        }
        Console.WriteLine("PASS process kill at " + boundary);
    }

    private static void LostAcknowledgement(string file)
    {
        var name = "ldb-lost-ack-" + Guid.NewGuid().ToString("N");
        using var child = new ChildProcess("crash-ipc", file, name);
        Check(child.ReadLine() == "READY", "IPC crash host ready");
        using var client = new PipeWriter(name);
        var request = Task.Run(() => client.Write(new WriteRequest(2, 2)));
        Check(child.ReadLine() == "SYNCED", "IPC write durable before reply");
        Check(!request.IsCompleted, "no early IPC success");
        child.Process.Kill(true);
        child.Process.WaitForExit();
        try { request.GetAwaiter().GetResult(); throw new Exception("Lost reply reported success"); }
        catch (OutcomeUnknownException) { }
        using var cold = new FaultStorage(file);
        cold.Verify(2);
        cold.Verify(2);
        Console.WriteLine("PASS durable IPC write with lost acknowledgement remains indeterminate");
    }
}
