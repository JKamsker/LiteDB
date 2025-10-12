#nullable enable

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class SpatialTestTolerances
{
    /// <summary>
    /// Tolerance used when comparing Euclidean distances in tests.
    /// </summary>
    public const double Distance = 1e-6;

    /// <summary>
    /// Tolerance used when comparing bounding box coordinate calculations.
    /// </summary>
    public const double Bounding = 1e-7;
}
