using System.Collections.Generic;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

public sealed class BoundingBoxFixture
{
    public string Id { get; set; } = string.Empty;

    public double[] Bounds { get; set; } = new double[4];

    public List<BoundingBoxFixturePoint> Points { get; set; } = new();
}

public sealed class BoundingBoxFixturePoint
{
    public string Id { get; set; } = string.Empty;

    public double Lon { get; set; }

    public double Lat { get; set; }
}
