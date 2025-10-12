using FluentAssertions;
using LiteDB.Spatial.Core.Tests.Infrastructure;
using LiteDB.Spatial.Testing.Oracles;
using LiteDB.Spatial.Testing.Oracles.Adapters;

namespace LiteDB.Spatial.Core.Tests.Engine;

public sealed class SpatialOracleParityTests
{
    private readonly GeographicDistance _vincenty = new(GeographicDistanceMode.Vincenty);
    private readonly CartesianDistance _cartesian3D = new(supports3D: true);
    private readonly MathNetOracle3D _mathNet = new();
    private readonly NtsOracle _nts = new();

    [OracleFact(feature: "geodesic_pairs", requiredOracles: new[] { "geographiclib" })]
    public void VincentyMatchesGeographicLibFixtures()
    {
        var fixtures = FixtureLoader.LoadCollection<GeodesicPairFixture>("geodesic_pairs.json");
        fixtures.Should().NotBeEmpty();

        var oracle = new GeographicLibOracle();
        foreach (var pair in fixtures)
        {
            var oracleMeters = oracle.DistanceInMeters(pair.From.ToCoordinate(), pair.To.ToCoordinate());
            SpatialTolerance.AssertEarthDistance(pair.Id + ":fixture", pair.DistanceM, oracleMeters);

            var actual = _vincenty.Distance(pair.From.ToGeoPoint(), pair.To.ToGeoPoint());
            SpatialTolerance.AssertEarthDistance(pair.Id, oracleMeters, actual);
        }
    }

    [OracleFact(feature: "point_clouds", requiredOracles: new[] { "mathnet" })]
    public void CartesianDistanceMatchesMathNet()
    {
        var cloud = FixtureLoader.Load<PointCloud3DFixture>("point_clouds/cube_3d.json");
        cloud.Points.Should().NotBeEmpty();

        foreach (var point in cloud.Points)
        {
            foreach (var other in cloud.Points)
            {
                var expected = _mathNet.Distance(point.ToCartesian(), other.ToCartesian());
                var actual = _cartesian3D.Distance(point.ToGeoPoint(), other.ToGeoPoint());
                SpatialTolerance.AssertCartesianDistance($"{cloud.Id}:{point.Id}->{other.Id}", expected, actual);
            }
        }
    }

    [OracleFact(feature: "geojson_polygons", requiredOracles: new[] { "nts" })]
    public void NtsOracleHonorsPolygonHoles()
    {
        var geoJson = FixtureLoader.LoadText("geojson_polygons/square_with_hole.json");
        var polygon = _nts.LoadFromGeoJson(geoJson);

        _nts.Contains(polygon, new PlanarCoordinate(0, 0)).Should().BeFalse("fixture hole should exclude the origin");
        _nts.Contains(polygon, new PlanarCoordinate(3, 3)).Should().BeTrue("point is inside the exterior ring but outside the hole");
        _nts.Within(_nts.LoadFromGeoJson(FixtureLoader.LoadText("geojson_polygons/square.json")), polygon).Should().BeFalse("unit square falls inside the interior hole");
    }
}
