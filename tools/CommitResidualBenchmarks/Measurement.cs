using System.Runtime.CompilerServices;

namespace Ab5021Benchmarks;

// Stable measured-only boundary, inherited from P1-D.
internal static class Measurement
{
    public static void Run(string scenario, Action action) => Single(action);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Single(Action action) => action();
}
