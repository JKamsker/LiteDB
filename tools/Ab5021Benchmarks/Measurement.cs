using System.Runtime.CompilerServices;

namespace Ab5021Benchmarks;

// Stable, measured-only stack boundaries. Warmup, setup, verification and disposal
// never enter these methods. Keep wrappers out of the product assembly.
internal static class Measurement
{
    public static void Run(string scenario, Action action)
    {
        switch (scenario)
        {
            case "bulk_insert_100k": Bulk(action); break;
            case "indexed_point_10k": Point(action); break;
            case "full_scan_3x100k": Scan(action); break;
            case "update_10k_one_tx": Update(action); break;
            case "delete_10k_one_tx": Delete(action); break;
            case "single_insert_5k_tx": Single(action); break;
            case "checkpoint_100k": Checkpoint(action); break;
            case "open_close_20x": Open(action); break;
            default: throw new ArgumentException(scenario);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Bulk(Action action) => action();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Point(Action action) => action();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Scan(Action action) => action();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Update(Action action) => action();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Delete(Action action) => action();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Single(Action action) => action();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Checkpoint(Action action) => action();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Open(Action action) => action();
}
