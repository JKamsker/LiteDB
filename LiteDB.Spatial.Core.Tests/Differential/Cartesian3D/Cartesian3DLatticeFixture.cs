#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LiteDB.Spatial;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian3D;

internal static class Cartesian3DLatticeFixtureLoader
{
    private const string FixtureName = "Differential/Cartesian3D/Fixtures/cartesian3d-lattice.json";

    public static Cartesian3DLatticeFixture Load()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var path = Path.Combine(baseDirectory, FixtureName.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Unable to locate fixture '{FixtureName}' relative to '{baseDirectory}'.", path);
        }

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var fixture = JsonSerializer.Deserialize<Cartesian3DLatticeFixture>(json, options);
        if (fixture == null)
        {
            throw new InvalidOperationException($"Fixture '{FixtureName}' could not be deserialized.");
        }

        return fixture;
    }
}

internal sealed record Cartesian3DLatticeFixture(
    LatticeDomain Domain,
    IReadOnlyList<LatticePoint> Points,
    IReadOnlyList<NearQuery> NearQueries,
    IReadOnlyList<BoxQuery> BoxQueries,
    int MaxCoveringCells,
    double DistanceTolerance)
{
    public BoundingBox DomainBoundingBox => Domain.ToBoundingBox();
}

internal sealed record LatticeDomain(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
{
    public BoundingBox ToBoundingBox() => BoundingBox.From3D(MinX, MinY, MinZ, MaxX, MaxY, MaxZ);
}

internal sealed record LatticePoint(int Id, double X, double Y, double Z)
{
    public GeoPoint3D ToGeoPoint() => new GeoPoint3D(X, Y, Z);
}

internal sealed record NearQuery(string Name, FixturePoint Center, double Radius, double DistanceTolerance);

internal sealed record BoxQuery(string Name, FixturePoint Min, FixturePoint Max)
{
    public BoundingBox ToBoundingBox() => BoundingBox.From3D(Min.X, Min.Y, Min.Z, Max.X, Max.Y, Max.Z);
}

internal sealed record FixturePoint(double X, double Y, double Z)
{
    public GeoPoint3D ToGeoPoint3D() => new GeoPoint3D(X, Y, Z);
}
