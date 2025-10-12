using Geodesy;

namespace LiteDB.Spatial.Core.Tests.Oracles;

public static class GeographicLibOracle
{
    private static readonly GeodeticCalculator Calculator = new(Ellipsoid.WGS84);

    public static double Distance(double lon1, double lat1, double lon2, double lat2)
    {
        var start = new GlobalCoordinates(new Angle(lat1), new Angle(lon1));
        var end = new GlobalCoordinates(new Angle(lat2), new Angle(lon2));
        var curve = Calculator.CalculateGeodeticCurve(start, end);
        return curve.EllipsoidalDistance;
    }
}
