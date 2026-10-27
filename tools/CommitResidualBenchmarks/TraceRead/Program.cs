using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System.Text.Json;

// Offline reader: no product execution, no allocation totals inferred from CPU samples.
foreach (var input in Directory.GetFiles(args[0], "trace-*.nettrace"))
{
    var etlx = Path.ChangeExtension(input, ".etlx");
    if (!File.Exists(etlx)) TraceLog.CreateFromEventPipeDataFile(input, etlx);
    using var log = new TraceLog(etlx);
    var types = new Dictionary<string, long>();
    var sites = new Dictionary<string, long>();
    var inclusive = new Dictionary<string, long>();
    var exclusive = new Dictionary<string, long>();
    long scopedSamples = 0, scopedAllocationTicks = 0;
    foreach (var ev in log.Events)
    {
        var allocation = ev as GCAllocationTickTraceData;
        var sample = ev.ProviderName == "Microsoft-DotNETCore-SampleProfiler";
        if (allocation == null && !sample) continue;
        var names = new List<string>();
        var stack = ev.CallStack();
        while (stack != null)
        {
            names.Add(stack.CodeAddress.FullMethodName);
            stack = stack.Caller;
        }
        if (!names.Any(n => n.Contains("Measurement.Single("))) continue;
        if (sample)
        {
            scopedSamples++;
            if (names.Count > 0) exclusive[names[0]] = exclusive.GetValueOrDefault(names[0]) + 1;
            foreach (var name in names.Distinct()) inclusive[name] = inclusive.GetValueOrDefault(name) + 1;
        }
        if (allocation != null)
        {
            scopedAllocationTicks++;
            types[allocation.TypeName] = types.GetValueOrDefault(allocation.TypeName) + allocation.AllocationAmount64;
            foreach (var name in names.Where(n => n.StartsWith("LiteDB.")).Distinct())
                sites[name] = sites.GetValueOrDefault(name) + allocation.AllocationAmount64;
        }
    }
    File.WriteAllText(Path.ChangeExtension(input, ".events.json"), JsonSerializer.Serialize(new
    {
        scopedSamples, scopedAllocationTicks, allocationTypes = types, inclusiveAllocationSites = sites,
        inclusiveSamples = inclusive, exclusiveSamples = exclusive,
        sampleMeaning = "EventPipe sampled thread time, including blocked intervals; not CPU-only time"
    }, new JsonSerializerOptions { WriteIndented = true }));
    if (scopedSamples == 0 || scopedAllocationTicks == 0)
        throw new InvalidOperationException("Missing measured Single stacks/allocation events: " + input);
}
