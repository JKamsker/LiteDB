using Geodesy;

namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// Provides distance calculations backed by a high-precision geodesic calculator on the WGS84 ellipsoid.
/// </summary>
public static class GeographicLibOracle
{
    private static readonly GeodeticCalculator EllipsoidalCalculator = new(Ellipsoid.WGS84);
    private static readonly GeodeticCalculator SphericalCalculator = new(Ellipsoid.FromAAndF(6378137d, 0d));

    /// <summary>
    /// Computes the geodesic distance between two lon/lat pairs in meters using the WGS84 ellipsoid.
    /// </summary>
    public static double Distance(double lon1, double lat1, double lon2, double lat2, bool ellipsoidal = true)
    {
        var start = new GlobalCoordinates(new Angle(lat1), new Angle(lon1));
        var end = new GlobalCoordinates(new Angle(lat2), new Angle(lon2));
        var calculator = ellipsoidal ? EllipsoidalCalculator : SphericalCalculator;
        var curve = calculator.CalculateGeodeticCurve(start, end);
        return curve.EllipsoidalDistance;
    }
}
