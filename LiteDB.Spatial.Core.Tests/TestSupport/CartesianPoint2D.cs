namespace LiteDB.Spatial.Core.Tests.TestSupport;

public readonly record struct CartesianPoint2D(double X, double Y)
{
    public LiteDB.Spatial.GeoPoint ToGeoPointStruct()
    {
        return new LiteDB.Spatial.GeoPoint(X, Y);
    }
}
