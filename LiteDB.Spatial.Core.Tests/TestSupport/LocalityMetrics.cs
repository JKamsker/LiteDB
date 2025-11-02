using System;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

public readonly record struct LocalityMetrics(
    double AverageOverlap,
    double MinimumOverlap,
    double AverageWindow,
    double MaximumWindow,
    double AverageSpan,
    double WorstSpan)
{
    public LocalityMetrics ClampToPrecision(int decimals)
    {
        return new LocalityMetrics(
            Math.Round(AverageOverlap, decimals, MidpointRounding.AwayFromZero),
            Math.Round(MinimumOverlap, decimals, MidpointRounding.AwayFromZero),
            Math.Round(AverageWindow, decimals, MidpointRounding.AwayFromZero),
            Math.Round(MaximumWindow, decimals, MidpointRounding.AwayFromZero),
            Math.Round(AverageSpan, decimals, MidpointRounding.AwayFromZero),
            Math.Round(WorstSpan, decimals, MidpointRounding.AwayFromZero));
    }
}
