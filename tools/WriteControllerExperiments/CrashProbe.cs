namespace WriteControllerExperiments;

internal static class CrashProbe
{
    internal static void Ipc(string[] args)
    {
        using var storage = new FaultStorage(args[1]);
        storage.Seed();
        using var writer = new WriteController(storage.Open, 8, 1);
        writer.Submit(new WriteRequest(1, 1)).GetAwaiter().GetResult();
        storage.Log.AfterSync = () => { Console.WriteLine("SYNCED"); Console.ReadLine(); };
        using var host = new PipeController(writer, args[2]);
        Console.WriteLine("READY");
        Thread.Sleep(Timeout.Infinite);
    }

    internal static void Run(string[] args)
    {
        using var storage = new FaultStorage(args[1]);
        storage.Seed();
        using var writer = new WriteController(storage.Open, 8, 100);
        writer.Submit(new WriteRequest(1, 1)).GetAwaiter().GetResult();
        Console.WriteLine("SEEDED");
        Action pause = () => { Console.WriteLine("BOUNDARY"); Console.ReadLine(); };
        if (args[2] == "before") storage.Log.BeforeSync = pause;
        if (args[2] == "after") storage.Log.AfterSync = pause;
        Task.WhenAll(Enumerable.Range(2, 8).Select(id => writer.Submit(new WriteRequest(id, id)))).GetAwaiter().GetResult();
        if (args[2] == "ack") pause();
    }
}
