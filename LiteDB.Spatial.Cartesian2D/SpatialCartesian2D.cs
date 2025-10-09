#nullable enable

namespace LiteDB.Spatial;

public static class SpatialCartesian2D
{
    public static Cartesian2DEngine CreateEngine(
        SpatialIndexOptions? options = null,
        BoundingBox? coordinateSpace = null,
        string geometryFieldName = SpatialCollectionDescriptor.DefaultGeometryFieldName)
    {
        return new Cartesian2DEngine(options, coordinateSpace, geometryFieldName);
    }

    public static SpatialCollectionDescriptor CreateDescriptor(
        string collectionName,
        string geometryFieldName,
        SpatialIndexOptions? options = null,
        BoundingBox? coordinateSpace = null)
    {
        var engine = CreateEngine(options, coordinateSpace, geometryFieldName);
        return new SpatialCollectionDescriptor(collectionName, Cartesian2DEngine.EngineName, engine.Dimensions, geometryFieldName, engine.Options, engine);
    }
}
