using System.Collections.Generic;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

public sealed class NearFixture
{
    public NearFixtureCenter Center { get; set; } = new();

    public double RadiusMeters { get; set; }

    public List<NearFixturePoint> Points { get; set; } = new();
}

public sealed class NearFixtureCenter
{
    public double Lon { get; set; }

    public double Lat { get; set; }
}

public sealed class NearFixturePoint
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public double Lon { get; set; }

    public double Lat { get; set; }
}
