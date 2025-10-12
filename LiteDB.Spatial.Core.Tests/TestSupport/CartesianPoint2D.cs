extern alias LiteDbBase;

using BaseGeoPoint = LiteDbBase::LiteDB.Spatial.GeoPoint;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

public readonly record struct CartesianPoint2D(double X, double Y)
{
    public LiteDB.Spatial.GeoPoint ToGeoPointStruct()
    {
        return new LiteDB.Spatial.GeoPoint(X, Y);
    }

    public BaseGeoPoint ToGeoPointClass()
    {
        return new BaseGeoPoint(Y, X);
    }
}
