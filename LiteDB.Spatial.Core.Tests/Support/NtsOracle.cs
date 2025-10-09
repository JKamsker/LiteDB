using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial;
using NetTopologySuite.Geometries;
using NetTopologySuite;

namespace LiteDB.Spatial.Core.Tests.Support;

public static class NtsOracle
{
    private static readonly GeometryFactory Factory = NtsGeometryServices.Instance.CreateGeometryFactory();

    public static Polygon CreatePolygon(GeoPolygon polygon)
    {
        var shell = BuildRing(polygon.Outer);
        var holes = polygon.Holes.Select(BuildRing).ToArray();
        return Factory.CreatePolygon(shell, holes);
    }

    public static bool Contains(GeoPolygon polygon, GeoPoint point)
    {
        var ntsPolygon = CreatePolygon(polygon);
        var ntsPoint = Factory.CreatePoint(new Coordinate(point.Longitude, point.Latitude));
        return ntsPolygon.Covers(ntsPoint);
    }

    private static LinearRing BuildRing(IReadOnlyList<GeoPoint> ring)
    {
        var coordinates = ring.Select(p => new Coordinate(p.Longitude, p.Latitude)).ToArray();
        return Factory.CreateLinearRing(coordinates);
    }
}
