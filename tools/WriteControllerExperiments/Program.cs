using LiteDB;
using LiteDB.Engine;
using WriteControllerExperiments;

if (args.Length == 0) throw new ArgumentException("Expected bench, host, verify, or safety.");
switch (args[0])
{
    case "bench": Benchmark.Run(args); break;
    case "safety": Safety.Run(args[1]); break;
    case "crash": CrashProbe.Run(args); break;
    case "crash-ipc": CrashProbe.Ipc(args); break;
    case "verify": DatabaseFixture.Verify(args[1], args.Skip(2).Select(int.Parse).ToArray()); break;
    case "host":
    {
        var file = args[1];
        if (args[2] == "existing")
        {
            using var engine = new CoordinatedEngine(new EngineSettings { Filename = file, DurableCommits = true });
            if (!engine.IsCoordinator) throw new Exception("Expected dedicated coordinator host.");
            Console.WriteLine("READY");
            Console.ReadLine();
        }
        else
        {
            using var writer = new WriteController(() => DatabaseFixture.Open(file), int.Parse(args[3]), int.Parse(args[4]));
            using (var pipe = new PipeController(writer, args[2]))
            {
                Console.WriteLine("READY");
                Console.ReadLine();
            }
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { writer.Commits, writer.Completed, writer.LargestBatch }));
        }
        break;
    }
    default: throw new ArgumentException("Unknown experiment mode.");
}
